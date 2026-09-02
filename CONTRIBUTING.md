# Contributing

Everything here is checked by a machine or it is not checked. That is the whole design, and most of the rules below exist to keep it true.

## Before you write anything

Open an issue first if the change is a lesson, a blueprint or a tool. A lesson written before there is anything to check it with is a lesson nobody can trust, and the ordering in the milestones is deliberate.

Fixes to prose, citations and broken links do not need an issue. Send the pull request.

## The rules that get a pull request rejected

These are mechanical and CI enforces most of them. A rejection is not a comment thread, it comes back as a new pull request.

A hand edited generated file. Expected outputs, captured runs and generated blueprint sections are outputs. If one of them is wrong, the generator is wrong.

A citation without a repository prefix or without a commit. Two repositories are pinned and both of them move. `runtime:src/coreclr/vm/methodtable.h:1420@abc1234` resolves. `methodtable.h line 1420` does not.

A number typed into prose. If you want to say a structure is twenty four bytes, transclude the block that printed twenty four.

A codegen or disassembly claim with no environment banner. Generated code is a function of the runtime version, the build configuration, the platform, the instruction set and the tier. A listing without all five is not evidence of anything.

A blueprint section that says "see the chapter". A blueprint is read by somebody implementing from scratch who has not read the chapter and does not have to.

A boss fight a human has to grade.

A performance claim asserted as a test. Measure it, print it, discuss it. Never assert on a timing, because a reader on a slow laptop must not see a red failure that means their computer is slow.

The words simply, just, obviously, of course and trivially. They are not stylistic tics. Every one of them is a small lie about how hard the thing is, addressed to the reader least able to detect it. <!-- xray-lint: allow -->

The linter has to be told to ignore the line above, which is the only exemption in this repository and is meant to be visible in the diff.

A lesson that needs a runtime build without saying so in the second block, in bold, with the time and the disk cost.

## Working on a lesson

Read [the lesson format](docs/lesson-format.md) once. It is short and it answers most of what you are about to ask.

Edit `lesson.cs` and `lesson.src.md`, then regenerate.

```
dotnet run --project tools/xray -- build lessons
dotnet run --project tools/xray -- lint
```

Commit what that wrote, including `lesson.md` and everything under `expected/`. CI runs the same build on four platforms with `check` instead of `build`, so a generated file that is out of date fails the pull request rather than reaching a reader.

If a lesson prints something that differs between two machines, mark the block `capture=drop` and describe the output in prose. Do not go looking for a way to make the expected file match on your laptop.

## Prose rules

Run `dotnet run --project tools/xray -- lint` before you push. It checks four things and CI runs the same command. The fourth is the banned word list above.

No em dashes. Use a comma, a full stop or a pair of brackets.

No horizontal rules. If a document needs a page break it needs a heading.

One line per paragraph. Do not hard wrap. A sentence split across two lines makes every later diff wider than the change inside it.

Beyond what the linter can see: write to one competent adult who does not yet know this thing, use they for a person whose pronouns you do not know, and say "I do not know" and "this is undocumented" in those words. This subject has a lot of both, and the reader will find out.

## Review

Two reviewers, always, and the pairing is the point.

One at or below the target reader's level, asked a single question: where did you get lost. Not whether it is correct, because they cannot know. Their confusion is data and it outranks the author's opinion about clarity.

One at or above core contributor level, asked what is wrong, what is stale, and what has been implied that is not true. They check citations against current upstream and catch the claim that was true in .NET 8.

## Commits and pull requests

One logical change per pull request. A lesson and the tool change it needed are two pull requests unless the tool change is unusable on its own.

Write the commit message for somebody reading `git log` in two years with no context. Say what changed and why it changed, not what file you touched.

Do not force push after a review has started.

## Filing upstream

Citing a BOTR chapter means checking it against the current source. When it no longer matches, filing that upstream is part of the definition of done for the lesson. This project does not get to complain about the state of the documentation while making it no better.

## License

By contributing you agree that your contribution is licensed under the MIT license in this repository.
