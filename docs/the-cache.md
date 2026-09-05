# The cache

One place on this machine for everything the tool fetches, with a record beside each thing saying where it came from.

There was a cache before this one. It held source files the citation checker had pulled out of the two pinned repositories, it had its own directory, its own environment variable that appeared in no document, no way to look inside it, no way to empty it, and nothing recording what any of it was. It also lived for the length of one CI job, so every pull request fetched every cited file again. This replaces it.

## What goes in it

Two kinds of thing, and the second one is the reason the design is what it is.

Source files, fetched by `xray cite` from `dotnet/runtime` and `dotnet/roslyn` at the commit in `pin.json`. These are text, they are small, and they are the same bytes for everybody.

Binaries, which is where this is heading. A checked `clrjit` is one file of about thirty megabytes and it is what E1 means. A built runtime is the output of hours. Neither of those is something a reader should download twice, and neither is something a reader should download once and then be unable to say anything about.

## The key

A name, and the four ways two things with the same name can differ. Every fetched thing declares all five.

| Part | What it is | Example |
|---|---|---|
| Repository | Where it came from | `dotnet/runtime` |
| Tag | The commit or tag inside that repository, which is a pin and never a branch | `60629d1...` |
| Platform | The runtime identifier, or `any` for something that does not vary | `linux-x64` |
| Configuration | Release, Checked, Debug, or `any` | `checked` |
| Name | What the thing is called, slashes allowed, so a fetched source file keeps its shape | `src/coreclr/vm/methodtable.h` |

The only thing in the cache today varies along two of those four. A source file is the same text whatever machine asked for it and whatever configuration that machine builds in, so it says `any` twice rather than leaving the axes out.

The other two are there for the binaries, and they are there now rather than later on purpose. A checked and a release `libclrjit.so`, from the same commit, on the same platform, have the same file name, roughly the same size, and are different programs. A cache that mixed them up would hand a lesson the wrong JIT and the lesson would run, print output, and pin numbers that are correct for a runtime nobody was using. Adding an axis to a key after the fact means invalidating every cache anybody has, so it is worth being right about while the cache holds four text files.

The key turns into a path under the cache root, one directory per part, with the file last. Anything in a part that is not a letter, a digit, a dot, an underscore or a hyphen becomes a hyphen. A part that is empty, or that is `.` or `..`, or that is an absolute path, is refused rather than escaped, because a cache is a set of directories a program writes into while nobody is watching and that is the wrong place to be relaxed about paths.

`xray cache key` prints the flat form of a key, which is what a workflow hands to whatever restores the cache between runs. The layout version is on the front, so changing the layout abandons an old cache rather than restoring it into a tool that would read it wrong.

```
$ xray cache key --repository dotnet/runtime --tag v10.0.0 --platform linux-x64 --configuration checked --name libclrjit.so
xray1-dotnet-runtime-v10.0.0-linux-x64-checked-libclrjit.so
```

## Why every entry has a hash

The interesting part of a cache is not the speed.

Everything else in this repository is checked by regenerating it and comparing, and a cache exists precisely so that the thing does not get regenerated. That makes it the one set of files here that is read back and believed. So each entry has a small JSON file beside it recording the address it was fetched from, its size, the time it arrived, and the sha256 of the bytes that arrived. The hash is checked every time the entry is read. An entry that no longer matches is deleted and fetched again, with a line on standard error saying so, rather than used.

This is not about an attacker, although it covers one. It is mostly about the ordinary way a cache goes wrong, which is that somebody edits a file in it to try something out, forgets, and then every run on that machine reads the edit for the next six months and nothing anywhere goes red.

The provenance record matters for the same reason a citation carries a commit. When the cache holds a thirty megabyte binary that a lesson's output depends on, the question of where that binary came from has to have an answer that is written down rather than remembered.

## Where it lives

`XRAY_CACHE`, if it is set. Otherwise the local application data directory for the account, with `xray/cache` under it, which is `~/.local/share/xray/cache` on Linux, `~/Library/Application Support/xray/cache` on macOS and `%LOCALAPPDATA%\xray\cache` on Windows.

It is outside the repository on purpose. A cache inside a clone is a cache that a clean checkout throws away and a clean checkout is the thing CI does on every run.

## The commands

| Command | What it does |
|---|---|
| `xray cache path` | Prints the directory, which is what a workflow needs before it can restore anything into it |
| `xray cache key` | Prints the key for a set of parts |
| `xray cache list` | Every entry, with its size, when it arrived and the address it came from |
| `xray cache clear` | Empties it, and says how much it removed |
| `xray cache --selftest` | Sixteen assertions about the paragraphs above |

## Proving it works

A cache that lost everything and refetched every time would pass a test that only checks the answers are right, and would show up as nothing worse than a slow build. A cache that handed back the wrong entry would show up as a lesson with confident, wrong numbers. Neither of those turns anything red on its own, so the self test goes at them directly.

It stores bytes made up on the spot in a directory under the temporary folder, so it touches no network and runs on all four platforms. The control comes first, because a cache that stored nothing would pass every case after it. Then one case per axis: store something that differs from an earlier entry by only the repository, only the tag, only the platform, only the configuration, and check the earlier entry is still there and still itself. Then the quiet one, which edits a cached file on disk and requires the cache to refuse it, to say why, and to throw it away so the next run refetches instead of refusing again. Then the record, which has to name the address and carry a hash of the right length. Then three names that try to climb out of the cache root. Then emptying it.

The citation self test uses this cache rather than one of its own. Until the pin lands there are no citations in the repository, so those four files are the only things anything here ever fetches, and pointing them at a private directory would leave the cache exercised by nothing. The cases in that test that expect a refusal are not affected, because nothing stores a 404.
