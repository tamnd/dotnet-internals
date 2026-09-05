---
id: smoke-pipeline
title: The lesson that proves the pipeline
part: tooling
env: E0
platforms: [linux-x64, linux-arm64, win-x64, osx-arm64]
---

# The lesson that proves the pipeline

This is not a lesson about .NET. It is the smallest thing the lesson pipeline can build, and it is here so that when a real lesson breaks, you can tell whether the lesson broke or the machinery under it did.

Everything below is generated. The code came out of `lesson.cs`, the output came out of running `lesson.cs`, and the page you are reading was assembled from `lesson.src.md`. Nobody typed the output, and nobody can, because CI runs the same build on four platforms and fails if what it produces is not what is committed.

## A block is a region of a file

A lesson is one program. The book quotes pieces of it, and each piece is called a block.

{{block:blocks}}

The two comment lines around that region are directives. They are comments, so the file still compiles and runs on its own. If you clone this repository and type `dotnet run lesson.cs` in this directory, you get the whole program, and the block boundaries are invisible to you.

Here is what that block printed.

{{output:blocks}}

The tool did not run the block by itself. It ran the whole program once with one extra line inserted at the top of each block, a line that prints a marker nobody sees. Afterwards it cut the output at the markers. That sounds like a detail and it is a decision: a lesson is a sequence, and the fourth block usually depends on what the second one allocated, so running blocks in isolation would produce output that is tidy and wrong.

## Some output can be an expected file and some cannot

{{block:invariant}}

{{output:invariant}}

That output is committed to the repository, and CI on linux-x64, linux-arm64, win-x64 and osx-arm64 all have to produce it byte for byte. Three lines of arithmetic can carry that promise. Most things cannot.

{{block:machine}}

Those two lines are different on every machine that runs them, so this block is marked `capture=drop`. It runs, no expected file exists for it, and the page is not allowed to quote its output, which the tool enforces rather than trusting.

That used to be the end of it, and it was the one hole in this whole arrangement. A dropped block could print anything at all, or stop printing, and nothing would notice. So a dropped block now has to say what is true of its output whatever machine produced it, and the build checks that on all four.

{{asserts:machine}}

The second of those is the one worth pointing at. This page claims in several places that the book runs on four platforms. That claim is now made by the runtime itself, once per platform, in a job that fails if any of them disagrees.

{{gate:capture}}

## What this file guarantees

Four things, and they are the four that every later lesson leans on.

The code on the page is the code that ran, because it is the same region of the same file rather than a copy of it.

The output on the page is the output that region produced, on a machine you can look at, on four platforms rather than one.

Changing the code and leaving the numbers alone is not possible, because the check that regenerates the page is the check that fails the build.

Output the page cannot show you is still output somebody has made a promise about, and the promise is on the page next to the code that has to keep it.

{{boss}}

## What it does not guarantee

It says nothing about whether the lesson is true, useful or well written. A pipeline can only promise that the page and the program agree with each other. Everything else is the reviewer's job, and the rules for that are in [CONTRIBUTING.md](../../CONTRIBUTING.md).
