---
id: m03-four-heaps
title: The four heaps
part: metadata
env: E0
platforms: [linux-x64, linux-arm64, win-x64, osx-arm64]
---

# The four heaps

Where do the strings and the blobs live?

You have seen the metadata tables. A type is a row, a method is a row, a parameter is a row, and every row has a fixed set of columns. What you have not seen is where the actual text goes, because a row cannot hold text. This lesson is about the four places it goes instead, and about the fact that two of those places both hold strings and are not the same place.

Everything here runs on a stock SDK. There is no runtime built from source, no debugger, and no privilege you do not already have. `System.Reflection.Metadata` ships in the box.

## A row cannot hold a name

A metadata table is an array of rows and every row in one table is the same width. That is not a style choice. Finding row four hundred has to be one multiplication and one addition, because a metadata reader does that millions of times during a startup and cannot afford to walk from the beginning counting.

A fixed width row cannot contain a name, because names are not all the same length. So the row contains a number, and the number is an offset into a byte array that sits elsewhere in the file. Those byte arrays are the heaps.

![What a metadata row actually holds](../../docs/diagrams/metadata-streams.svg)

There are five streams in a metadata section and four of them are heaps. `#~` holds the tables. `#Strings` holds names. `#US` holds the string literals from your program. `#Blob` holds signatures and anything else that is a length prefixed run of bytes. `#GUID` holds sixteen byte values, and in practice holds exactly one.

Here is the fixture assembly, opened.

{{block:usings}}

{{block:open}}

{{output:open}}

The metadata version string is worth a moment. It says `v4.0.30319`, which is the build number of .NET Framework 4.0, released in 2010. Every .NET assembly ever produced since then says that, including the one you built ten seconds ago on .NET 10. It is a version number that stopped meaning anything and stayed anyway, because too much code compares it against that exact string. <!-- literal: product versions and the year one of them shipped, neither of which this lesson measured -->

## Where the heaps are, and why that is not on this page

Each heap has a position in the metadata section and a length.

{{block:sizes}}

Those numbers are positions in a file the compiler wrote, and they move when the compiler moves. A newer Roslyn that emits one more attribute makes every one of them different. Nothing would be learned by pinning them, and the build would break every time the SDK moved, which is the sort of red build that teaches people to ignore red builds. So the block is marked `capture=drop` and this page cannot show you what it printed.

That does not mean nothing is checked. It means the checking moves from the numbers to the shape.

{{asserts:sizes}}

Read the middle one again, because it is the interesting one. It pins the size of `#GUID` and says nothing whatever about the other three, which is the shape of most honest claims about a file format: one part is fixed by the specification and the rest is up to whoever wrote the compiler that day.

## A name is an offset, and you can go and look

Take the type `Sample.Catalogue`. Its row has a `Name` column, and the value in that column is not the text `Catalogue`. It is a number. Here is that number used the hard way, by walking into the byte array ourselves and reading until the terminator.

{{block:name}}

{{output:name}}

Nine bytes of UTF-8, then a zero. Read that hex two characters at a time and the first three pairs are `C`, `a` and `t`, one byte each, because every character in this name happens to be ASCII. The reader agrees with us because the reader is doing the same thing with better error handling.

![Two heaps, two ways of ending a string](../../docs/diagrams/heap-entries.svg)

That is the whole of `#Strings`. It is one long byte array of UTF-8 strings, each ended by a zero byte, and an offset into it is an index into that array. The first byte is a zero, so an offset of zero means the empty string, which is how a row says it has no name.

{{gate:suffix}}

{{block:suffix}}

{{output:suffix}}

Four bytes apart, and the second run of bytes is the tail of the first one. There is one string in the heap and two names pointing into it, one at the start and one four bytes in. The compiler sorts the names it is about to write by their reversed text, which puts every name next to the names that end the same way, and then it can spot that `Greeting` is already present as the tail of `get_Greeting`.

This is worth carrying with you for the rest of Part II. A name in `#Strings` can begin in the middle of another name, so a program that walks the heap from the start and cuts at every zero byte does not see every name that is in there. It sees a subset. The names it misses are exactly the shared tails, which for a typical assembly means most of the property and event names.

## Your string literals are somewhere else entirely

This is the part that surprises people. `#Strings` holds names. The literals you wrote in your code are not names, and they are in a different heap, `#US`, in a different encoding, with a different way of saying where a string stops.

{{gate:duplicate}}

{{block:us}}

{{output:us}}

Read the first line as arithmetic. `the quick brown fox` is nineteen characters. UTF-16 stores each of them in two bytes, which is thirty eight. The stored length is thirty nine, and the extra byte is a flag on the end.

The flag is one when any character in the string needs more than the bottom seven bits, and zero otherwise. It exists so that a reader deciding whether a string is plain ASCII does not have to look at the string. It is a single byte answering a question that would otherwise cost a scan, and it is the sort of thing you find all through this format once you start looking.

And the literal written twice appears once, which is the answer to the gate above. That is also the reason two identical literals in your program are reference equal at runtime. Most C# developers know that as a rule and have never seen why.

The two heaps really are separate. Nothing is shared between them, not even a string that appears in both.

{{block:separate}}

{{output:separate}}

Four searches over raw bytes. The literal is in `#US` and not in `#Strings`. The type name is in `#Strings` and not in `#US`. They are separate byte arrays with separate offsets, and nothing in the format connects them.

## #Blob, which is everything else that varies

A signature is not text, and it does not go in either string heap. It goes in `#Blob`, which holds length prefixed runs of bytes: signatures, constant values, custom attribute arguments, public keys, and marshalling descriptors.

{{block:blob}}

{{output:blob}}

Five bytes for a whole method signature. That is worth sitting with. `int Count(string, int)` is a return type, two parameter types and the knowledge that it is an instance method, and the entire thing costs five bytes because every part of it is a number from a fixed table rather than a name.

The last line is the same deduplication you saw in `#US`, doing something more useful. Three property getters with three different names all have the shape "instance method, no parameters, returns String", and the heap stores that shape once. In a real assembly with hundreds of properties, that is hundreds of pointers at one blob.

Signatures get a lesson of their own, M05, which is also where the ECMA drift ledger first appears, because the signature grammar is one of the things the augments document actually changed.

## #GUID, the small one

{{block:guid}}

{{output:guid}}

`#GUID` is an array of sixteen byte values with no length prefix and no terminator, because every entry is the same size. It is the only heap indexed by entry number rather than by byte offset, and it counts from one, so a row holding zero is a row saying it has no GUID.

Almost every assembly has exactly one entry, the module version id. The Mvid is a fresh value for every distinct build, which is what makes it useful for matching an assembly to its PDB and useless as anything you could put on this page.

{{block:mvid}}

That is marked `capture=drop` for the reason that is now familiar. Run it twice against two builds and you get two different values, which is the point of it. What survives from one build to the next is the shape, so the shape is what gets checked.

{{asserts:mvid}}

## What to take away

A row is fixed width, so anything of variable length is an offset and the thing itself is in a heap.

There are two heaps holding strings and they are not interchangeable. `#Strings` holds names, in UTF-8, ended by a zero byte. `#US` holds your literals, in UTF-16, with the length in front and a flag on the end.

Every heap deduplicates, and `#Strings` goes further and shares tails, so walking it from the front does not enumerate it.

`#Blob` holds anything else that is a run of bytes, and a whole method signature fits in five of them.

`#GUID` counts from one, and entry zero means none.

{{boss}}

## Sources

ECMA-335, 6th edition, June 2012. <!-- literal: the edition and the month it was published -->

II.24.2.1 for the metadata root, II.24.2.2 for the stream headers, II.24.2.3 for `#Strings`, II.24.2.4 for `#US` and `#Blob` including the flag byte on the end of a user string, II.24.2.5 for `#GUID`, and II.24.2.6 for `#~`.

II.23.2 for compressed integers, which is the four line rule the boss fight is about.

II.22 for the table layouts, and II.23.2.1 for the method signature decoded above.

There are no `runtime:` citations in this lesson, because the runtime pin in `pin.json` has not landed yet and a citation without a commit is one this repository does not accept. The claims above are claims about the format and are checkable against the standard and against the output on this page. When the pin lands, the reader side of this in `src/coreclr/md/` gets cited here properly.
