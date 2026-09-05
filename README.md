# dotnet-internals

A complete teardown of .NET 11, taught from zero, where every claim is something you watch happen on a runtime you installed with one command. The same work produces a second artifact: a specification precise enough to write a garbage collector the real CLR loads, or your own runtime from scratch, with a conformance scorecard that says how far you got.

**Status: M0, under way.** The plan is in the [milestones](https://github.com/tamnd/dotnet-internals/milestones), the decisions that have not been made yet are in the [open questions](https://github.com/tamnd/dotnet-internals/issues?q=is%3Aissue+label%3Akind%2Fopen-question), and M0 exists to try to kill the project cheaply before anything expensive is built.

The pilot lesson is [M03, the four heaps](lessons/m03-four-heaps/lesson.md). Every listing on that page is a region of a program that runs, every output under it is what that program printed, and CI regenerates the whole page on four platforms and fails if what it produces is not what is committed.

## Who this is for

Two people, and pretending they are one person is how projects like this fail.

The first writes C# for a living, has never opened a runtime source file, and does not read C++. They need a path with no cliff in it, where every claim is something they can watch on their own screen. They will quit at the first paragraph that says "the JIT then generates efficient code" without showing them.

The second wants to write a collector, a compiler or a runtime of their own. They do not need motivation. They need exact layouts, exact algorithms, exact invariants, exact failure behaviour and a harness that grades them.

Every chapter therefore produces two things. The chapter teaches, in prose and pictures and code you run. The blueprint specifies, with structures, algorithms, invariants, edge cases and port notes, and no motivation at all. CI checks that the two agree.

## What is different about it

**The observation surface ships in the product.** `DOTNET_JitDisasm` has worked on the release runtime since .NET 7. `DOTNET_Interpreter` runs a method under the new IL interpreter. The event pipe, the counters and the data contracts are all in the binary you already have. A reader can watch the JIT compile their own method without building anything, and almost nobody teaches this. The one switch that is not in the release binary, `DOTNET_JitDump`, turns out to be [one download away rather than a build away](docs/probes/nightly-checked-jit.md).

**The build lesson is number 97 of 99.** Ninety six lessons run on an SDK you install with one command. One bet held that number up, and it has now been measured rather than assumed: a nightly checked JIT does drop into a stock release runtime with `DOTNET_JitName=`, on all four platforms, at about twice the JIT time. [The probe is written up here](docs/probes/nightly-checked-jit.md), including the condition nobody predicted, which is that the JIT has to come from the same release branch as the runtime.

**You can replace subsystems of the shipping runtime without forking it.** There are three supported plug in points: a standalone collector behind `IGCHeap`, an alternative compiler behind `ICorJitCompiler`, and a profiler behind `ICorProfilerCallback`. The first capstone is a garbage collector, a JIT and a profiler that the production runtime loads, graded by the production runtime and by SuperPMI replay. This is not a toy runtime that pretends. It is your code inside the real one.

**The blueprints are generated, not transcribed.** The runtime publishes versioned struct layouts and globals in the binary through the cDAC data contracts, officially supported from .NET 11. Twelve blueprints are generated from those contracts and from `opcode.def`, the ECMA grammar, the metadata table schema and the event manifests. When the pin moves, they move. How far those contracts actually reach has been [counted rather than assumed](docs/probes/cdac-coverage.md): they name seventy four per cent of what the runtime side blueprints need, they cover the fixed part of a structure almost completely, and they never describe its variable length tail.

**There is a real standard, and it is out of date.** ECMA-335 was last revised in 2012 and no seventh edition is planned. The runtime augments it in a file in its own tree. So conformance here is not a single question, it is three answers that sometimes disagree, and the project publishes a drift ledger of every place they do. That ledger is the most useful thing this book can produce for anyone writing a runtime, and nobody maintains one today.

**Every mechanism has a switch on the same binary.** Legacy exception handling against the new managed one, DATAS on and off, Server and Workstation, tiering on and off, interpreter against JIT. A lesson that can turn a mechanism off and show you the difference does not have to ask you to take its word for anything.

**Some lessons run in the browser, and the limit is not that things throw.** Fifty one checks were [run in a real page](docs/probes/browser-tier.md). Two throw. Sixteen more answer, and answer about Mono rather than about the runtime this book describes, and four of those do it without any sign that anything is wrong. Reading metadata, signatures and IL in the browser is exact, down to the byte. Reading an object header there gives you a different runtime's header with the two words in the other order, which is why the no install tier stops at Part III rather than Part IV.

**The one click environment is built and its cost is measured, except for the part GitHub owns.** The [E0 image](docs/probes/codespaces-cost.md) carries the pinned SDK, lldb, the diagnostics tools and a checked JIT that produces a real `JitDump` with nothing installed by the reader. It rebuilds from nothing in 51 seconds, it is 0.70 GB to download, a reader's first command in it takes 4.5 seconds and the pilot lesson takes 11.8. What is not measured is how long a real Codespace takes to appear, so the question of whether the local path should be the recommended default is still open rather than answered.

## What "compatible" is allowed to mean

Nobody has ever built a fully compatible CLI that is not CoreCLR, and a project that claims otherwise gets taken apart in an afternoon, so the tiers are here rather than in a footnote.

| Tier | What it covers | Reachable |
|---|---|---|
| A1 | A collector the real runtime loads and runs ASP.NET on | Yes, and the runtime is the grader |
| A2 | A JIT the real runtime loads, graded by SuperPMI replay | Yes, to a documented rung |
| A3 | A profiler that rewrites IL at load time | Yes |
| B | Your own metadata reader, type loader and IL interpreter, on a corelib you wrote | Yes |
| C | The real `System.Private.CoreLib` on your own runtime | No, and the project says why |
| D | Everything above plus interop, threading and AOT | No |

Tier C is where every independent CLI has stopped, and the reason is worth stating plainly. `System.Private.CoreLib` is not a library that ships with a runtime. It is one half of a single product, bound to the other half by FCalls, QCalls, JIT intrinsics and layout assumptions that are a private contract between the two. The second capstone ends by enumerating that contract rather than defeating it, and the count is published.

## What this is not

It is not a specification of .NET. ECMA-335 is the standard, and this project specifies what it has examined against a pinned implementation and records where the two differ.

It is not authoritative on performance. Every timing here is one measurement on one machine, labelled as such, and no lesson asserts on a timing.

It is not stable across versions. The book is pinned to one runtime commit, one Roslyn commit and one SDK, the pin prints on every page, and there is a branch per minor version.

It is not complete. Forty seven blueprints do not cover the runtime. The largest gap is the corelib as a body of code in its own right, and the coverage ledger names it rather than hiding it.

## The pin

| Thing | Pinned to |
|---|---|
| Runtime | `dotnet/runtime` at the .NET 11 tag, by commit |
| Compiler | `dotnet/roslyn` at the matching C# 15 tag, by commit |
| SDK | The `11.0.1xx` feature band |
| Standard | ECMA-335 sixth edition, June 2012 |
| Augments | `docs/design/specs/Ecma-335-Augments.md`, by commit |

The machine readable copy is [pin.json](pin.json), and the commits in it are null until the .NET 11 release candidate is tagged. Authoring runs against the release candidate, the pin moves to the release tag at general availability on 10 November 2026, and every number in the book is regenerated at that flip with the diff published as a page.

Two repositories are pinned rather than one, which costs something. Every citation carries a repository prefix, the drift bot has two heads, and a version bump is two bumps that have to land together.

Citations are checked by a machine rather than by a reviewer. `xray cite` takes each one apart, refuses anything pinned to a tag or a branch instead of a commit, fetches the file from the repository it names at the commit it names, checks the line is there and says what the citation claims it says, and then prints that line so a reviewer reading the log sees what was pointed at rather than a tick. The [format is here](docs/citation-format.md). Because the pin has not landed, the honest count of citations in this repository today is zero, which is also what a broken checker would report, so the gate proves it can go red: a self test resolves against two real commits in the two pinned repositories and checks that every citation that should be refused is refused for the right reason.

## Layout

| Path | What is in it |
|---|---|
| `tools/xray` | The command line tool. Prints the environment banner, lints the prose rules, builds and checks lessons, diagrams and blueprints |
| `lessons` | One directory per lesson. The page in each one is generated, never edited |
| `blueprints` | One directory per blueprint. The document in each one is generated, and so are the sections that can be |
| `docs` | How the machinery works. The [lesson format](docs/lesson-format.md), the [diagram format](docs/diagram-format.md), the [blueprint format](docs/blueprint-format.md) and the [citation format](docs/citation-format.md) |
| `docs/probes` | The measurements that decided something, with the script that produced each one |
| `.github/workflows` | CI. Everything in this repository is checked by a machine or it is not checked |

The first blueprint is [BP-METADATA](blueprints/bp-metadata/blueprint.md), a draft with two of its subsections generated rather than typed. The animations and the site arrive with the milestones that build them. Nothing is committed here as a placeholder.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. The short version is that no number in this repository is typed by a human, no citation is trusted because somebody read it once, and a hand edited generated file is rejected on sight.

Issues labelled [good first issue](https://github.com/tamnd/dotnet-internals/labels/good%20first%20issue) are the place to start.

## License

MIT. See [LICENSE](LICENSE).
