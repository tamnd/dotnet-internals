---
id: t01-one-line-ten-stages
title: One line, ten stages
part: tour
env: E0
platforms: [linux-x64, linux-arm64, win-x64, osx-arm64]
---

# One line, ten stages

One line of C#, followed all the way down.

This is the first lesson of the tour, and the tour is one pass over the whole runtime at low resolution. Everything here gets its own chapter later. The point of doing it once quickly first is that the later chapters make a lot more sense when you already know roughly where they sit.

We are going to take a single line of C#, the shortest useful one I could think of, and watch what happens to it. It is this.

```csharp
public static int Count(string line) => line.Split(' ').Length;
```

Ten things happen to that line between you typing it and a processor doing anything about it. Five of them happen on the machine that runs the build, and they happen once. The other five happen on the machine that runs the program, and they happen every time it starts. Almost everything people find confusing about .NET lives in the gap between those two halves.

{{needs}}

Nothing in this lesson needs a debugger, a build of the runtime, or a privilege you do not already have. It is all done with `System.Reflection.Metadata` and `System.Reflection`, both of which ship in the box.

![One line, ten stages](../../docs/diagrams/ten-stages.svg)

## Stage one. It is text

Before anything else, the line is bytes in a file. That sounds too obvious to say, but it is worth saying once, because it is the last point at which the thing you wrote and the thing on disk are the same thing.

{{block:usings}}

{{block:source}}

{{output:source}}

That is the entire input to everything below. The compiler reads those bytes and never looks at them again, and no part of what follows can recover them. The line is not stored anywhere in what comes out of the build. What is stored is what the compiler concluded from it, which is a different thing, and the rest of this lesson is mostly about how different.

## Stage two. A name becomes one specific method

`line.Split(' ')` is a name and an argument. There are several methods called `Split` on `String`, and picking which one you meant is a job with rules that take a chapter of the language specification to state. That job happens once, at build time, and the answer is written down.

Here is the answer, read back out of the file.

{{block:open}}

{{block:overload}}

{{output:overload}}

The source passed one argument and the call passes two. That is not a mistake in either place.

{{gate:arguments}}

Everything about that resolution is now frozen. If somebody ships a new version of the library tomorrow with a better overload of `Split`, this file goes on calling the one that was picked today, until somebody rebuilds it. Overload resolution is a build time act with a build time answer, and that is true of a great deal that people assume is decided later.

## Stage three. It becomes a Windows executable, whatever machine you are on

The compiler produces a file. The format of that file is the Portable Executable format, which is the Windows executable format, and it is that on Linux and on macOS too.

{{block:pe}}

{{output:pe}}

Every line of that is worth a second look.

The file opens with the two bytes `MZ`. Those are the initials of Mark Zbikowski, who worked on MS-DOS in the early eighties, and they are followed by a tiny MS-DOS program that prints a message saying this program cannot be run in DOS. Your .NET assembly, on your Linux server, in a container, contains an MS-DOS program. Nothing has run it in thirty years.

The header has a field saying which processor the code is for.

{{gate:machine}}

The flag that makes the processor field meaningless is on the fourth line, `ILOnly`, and the line under it is the other half of the same statement. There is a directory in the header reserved for precompiled machine code, and its size is zero. There is no machine code in this file for any processor. Copy it to a machine with a completely different instruction set and it works, because there was never anything in it that depended on the instruction set in the first place.

## Stage four. The method becomes a row in a table

Inside the file, past the headers, is a set of tables. Not a tree, not a stream of records, tables, with fixed width rows and columns. Every type is a row, every method is a row, every parameter is a row.

{{block:row}}

{{output:row}}

The number at the top is a metadata token, and the way to read it is to split it in two. The top byte says which table, the bottom three bytes say which row. So a token is nothing more exotic than a table number and a row number packed into a machine word, and the whole of metadata is a set of arrays indexed by those.

The last line is the part to hold on to. The row for a method does not contain the method. It contains a name, some flags, and an address of somewhere else in the file where the code is. Part II is the chapter about these tables and it goes into all of this properly. For now the useful shape is: fixed width rows, and anything that is not fixed width lives somewhere else and is pointed at.

## Stage five. The line becomes twelve bytes of a different language

Follow that address and you get the code. It is not machine code. It is a stack machine language called IL, and your line of C# is now this.

{{block:il}}

{{output:il}}

Read that from the top and you can see the whole line in it. Push the argument. Push the space character as a number. Push zero, which is the default nobody wrote. Call the method the token names. Take the length of the array that came back. Convert it and return it.

Two things in there were not in the source. The zero is the default argument, decided at stage two and baked in here. And the string, which the source treats as an object with methods on it, has become an argument in position zero of a call, because that is what an instance method call is once the sugar is off.

Nothing in those twelve bytes is specific to any processor. There are no registers, because there are no registers in this language, only a stack. There is no stack layout either, because the stack in question is imaginary and no real one gets described until much later. That is the whole reason this file works on four platforms.

## Stage six. Four other files, and none of them is your code

Building this program produced five files. One of them is the assembly we have been reading. The other four are worth knowing about, because the biggest of them by a wide margin is the one most people think of as their program.

{{block:files}}

{{asserts:files}}

The exact sizes are dropped from this page on purpose, because they change with every version of the SDK and pinning them would mean a red build every time somebody upgraded. What matters is which five things are there and how big they are relative to one another. The assembly with all your code in it is a few kilobytes. The launcher next to it is a hundred kilobytes and more.

So what is in the launcher?

{{block:launcher}}

{{output:launcher}}

Nothing of yours. The launcher is a native executable, built by Microsoft, shipped in the SDK, and copied next to your assembly with your assembly's name written into a fixed spot inside it. Its entire job is to find a runtime, start it, and hand it the name of the assembly to run. That is why it is native, why it is large, and why it is different on every platform while your assembly is identical on all of them.

The file that tells it which runtime to find is `Sample.runtimeconfig.json`, and it is text. You can open it in an editor. The version of .NET that your program demands at startup is a string in a JSON file sitting next to it, not anything compiled into anything.

That is the boundary the diagram at the top draws. Everything up to here happened once, on a build machine, and produced files. Everything after here happens on startup, on the machine actually running the thing, and produces nothing you can keep.

## Stage seven. Somebody has to load it

Reading a file is not running it. Up to this point we have opened `Sample.dll` the way you would open any file, with a reader, as bytes. The runtime has not been involved and the code in there has not been anywhere near being executed.

Loading is a separate act, and you can watch it happen.

{{block:load}}

{{output:load}}

Before the request, the assembly is not in the process. After it, it is. Nothing about having the file on disk, or even having it open, put it there.

The last two lines are the interesting ones. `System.Private.CoreLib` is the library that `String` and `Object` and everything else foundational live in, and it was already loaded before we asked for anything, because nothing can run without it. Notice also that the name is not `System.Runtime`, which is what the compiler thought it was compiling against. There is a whole layer of indirection between the assembly names a compiler sees and the assembly names a runtime loads, and Part III is where that gets untangled.

## Stage eight. The type gets built, and it was not in the file

Here is the stage that most people have never thought about, and it is the one that explains the most.

Asking the loaded assembly for a type does not hand you something that was sitting in the file. It builds a structure in memory: the layout of the fields, the table of methods, a pointer to the parent, and a good deal more. That structure is what the rest of the runtime actually works with, and no part of it exists on disk.

{{block:type}}

{{output:type}}

That handle is an address of a live structure in this process. Ask for the same type twice and you get the same address, because the runtime built it once and remembered. Ask for a different type and you get a different one. Run the program again and every one of those numbers is different, which is why this page shows you that they are not zero and not equal and shows you nothing else about them.

Part V is the chapter about that structure, which is called a MethodTable, and I will say now that it is the single most useful thing in this book to understand. Almost every question that starts "why is .NET doing this" ends up being a question about it.

## Stage nine. The IL becomes machine code, once

We have established there is no machine code in the file. There is machine code by the time the processor is running anything, or nothing would run at all. Something has to make it, and the something is the JIT, and it does its work the first time a method is called.

That is a measurable claim.

{{block:jit}}

{{asserts:jit}}

The numbers are dropped, because they are a function of your machine and the weather. The relationship is not. The first call costs hundreds of times what the later ones cost, and it costs that because the first call is where a compiler ran. What you are timing on that first line is a compiler compiling, in your process, while your program waits.

That cost is once per method, not once per call, which is why the second line is what it is. It is also the entire reason a .NET service is slow for its first few seconds and then is not. Part VII is the chapter about the JIT, and Part IX is about the ways of not paying this at all.

## Stage ten. It runs, and the heap is different afterwards

The line runs. It returns a number. It also leaves something behind.

{{gate:allocation}}

{{block:heap}}

{{output:heap}}

Look at what that line does not say. There is no `new` in it. It reads like it is looking at the string rather than copying it. And it puts five objects on the heap, one array and four strings, none of which existed a moment ago and all of which are now the garbage collector's problem.

The arithmetic at the bottom is measured rather than reasoned, one allocation at a time, so you can see where every byte went. A string of three characters and a string of five characters cost the same, which is a hint about how object sizes get rounded, and Part VIII is where that gets explained.

This is what most allocation in a real program looks like. Not `new` in a loop, which people notice, but ordinary looking library calls that hand back objects, in code nobody thinks of as allocating at all.

## What your machine says

Everything on this page is the same on all four supported platforms, which is why the page can exist. Here is the part that is not.

{{block:machine}}

{{asserts:machine}}

## What to take away

The build and the run are two different events on two different machines, and the file in between them contains no machine code.

Overload resolution, default arguments and the choice of which method to call happen at build time and are frozen into the file.

What comes out of the build is tables and IL. A method is a row, and the row points at bytes in a made up instruction set for a stack machine that does not exist.

Most of what runs your program is not your program. The launcher is a hundred kilobytes of somebody else's native code, and the runtime it starts is a great deal more.

Loading an assembly, building a type and compiling a method are three separate things that happen at three separate moments, and all three leave nothing behind when the process exits.

The first call to a method costs hundreds of times what the second one does, and that is a compiler running inside your process.

## Where the rest of this goes

Each of those ten stages is a chapter.

Stages three, four and five are Part II, metadata and IL. Stage seven is Part III, assemblies and loading. Stage eight is Part V, the type loader. Stage nine is Part VII, the JIT. Stage ten is Part VIII, the garbage collector. Stage six, the host and the launcher, turns up properly in Part IX along with the ways of getting rid of both.

The next lesson in this part, T02, takes the file apart properly rather than at a glance.

## Sources

ECMA-335, sixth edition.

II.25.2.2 for the COFF header and the machine field. II.25.3.3 for the CLI header, the `ILOnly` flag and the directory that would hold precompiled code. II.24 for the metadata root and the streams. II.22.26 for the MethodDef table and what a row of it holds. II.23.1.9 for how a metadata token splits into a table and a row.

III.3 for the instructions decoded above, one section each: `ldarg`, `ldc.i4`, `callvirt`, `ldlen`, `conv.i4` and `ret`.

There are no `runtime:` citations in this lesson. The runtime pin in `pin.json` has no commit in it yet, and this repository refuses a citation that cannot be resolved to one. Everything claimed above is either a claim about the file format, checkable against the standard, or a claim about behaviour, checkable against the output on this page on any of the four platforms. The lessons that need to point at the runtime source start in Part II, and they will point at a pinned commit when there is one.
