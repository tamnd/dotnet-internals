# Probe: building the book on a machine that has never seen it

**Question.** Does the whole pipeline work on a machine with nothing on it, on all four platforms, or does it only work on the machines it was written on?

**Answer. It did not, and three separate things were wrong.** All three are now fixed, all three are now gated, and the cold run is a job in CI on all four platforms rather than something a person remembers to do.

**Measured on 6 September 2026.** Linux in a bare `ubuntu:24.04` image with nothing added, macOS and Windows on real machines with everything the build could inherit taken away.

## Why this was worth measuring

Every other job in CI starts from a runner image that has an SDK on it, a NuGet folder, a home directory the SDK has already written to, and a package manager somebody has already used. Every machine this project has been written on is the same, only more so. A reader has none of that, and neither does a container, which is what the one click environment is.

The failure mode this is looking for is not a slow first run. It is a build that passes everywhere it is tried and is wrong, because the thing that would have made it fail is sitting on every machine that tried it. That is not a hypothetical, and this probe found three of them.

## What is taken away

| Thing | Warm machine | Here |
|---|---|---|
| The SDK | on the path already | installed into an empty directory, and the path has no other one on it |
| Which SDK | whatever `global.json` rolls forward to | the version `global.json` literally names |
| NuGet packages | a populated folder in the home directory | an empty folder |
| The artifact cache | whatever previous runs left | an empty directory |
| The home directory | years of state | created a minute ago |
| System libraries | an image with development tools on it | `ubuntu:24.04` with nothing added |
| The checkout | the working tree, with its build output in it | a copy with no `obj`, no `bin` and no `.git`, and on Linux the original is mounted read only |

## The first thing: a bare Ubuntu image cannot start .NET at all

`dotnet --version` in a fresh `ubuntu:24.04` does not print a version. It aborts before reaching any of this project's code.

```
Process terminated. Couldn't find a valid ICU package installed on the system.
Please install libicu using your package manager and try again.
   at System.Globalization.GlobalizationMode.GetInvariantSwitchValue()
   at System.Globalization.GlobalizationMode+Settings..cctor()
```

.NET links against the system ICU on Linux, and the image does not ship one. The installer will not help, and says so in its own help text: `dotnet-install.sh` does not resolve dependencies. So the first thing a container needs is not the SDK, it is `ca-certificates`, `curl` and `libicu`, and none of the four hosted runners would ever have told us that, because all four have them.

The package is named after the ICU release rather than the distribution, so it is `libicu74` on 24.04 and something else on the next one. The script looks the name up instead of writing it down.

## The second thing: `global.json` names an SDK nothing had ever run on

```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

`rollForward: latestFeature` means a machine that already has a 10.0.4xx uses it. Every developer machine and every hosted runner has one, so every run anybody had ever done was on the 400 band. A reader who follows the installer with `--jsonfile global.json` gets exactly 10.0.100, because the installer takes the version and not the roll forward rule.

Those two bands disagree about something this repository was relying on. `dotnet run some/file.cs` does not promise the working directory of the file it runs. On 10.0.400 the file inherits the working directory the SDK was started with. On 10.0.100 it can get the directory the file itself is in.

Four files were reading a path relative to the working directory. A boss fight lives in `boss/` and reads `../lesson.cs` under one of those and `lesson.cs` under the other, and which one was right was decided by whichever SDK the machine had resolved. Nothing was wrong with the code. Nothing was wrong with the test. The two had only ever met on one of the two SDKs this repository accepts.

## The third thing: the self test was building with the wrong settings

The regeneration gate proves itself by copying a lesson somewhere temporary, breaking the copy on purpose, and requiring the build to refuse it. The copy took the lesson directory and nothing above it, and what is above it is two `Directory.Build.props` files.

So the copy was compiled with the SDK's defaults rather than this repository's: a different target framework rule, no invariant globalization, warnings not treated as errors. The self test was testing a build the build never does. On a machine where that mattered it produced five failures whose text was a stack trace where a verdict should be, which is the least useful shape a failure can have.

## Running it twice, which is not explained

This one is a defect in the probe rather than in the repository, it is not solved, and it is written down rather than quietly worked around because the next person to see it should not have to find it again.

Run the script twice at the same work directory and the second run fails. Three lessons and a blueprint generator die with the same two errors, an analyzer assembly missing out of the packages folder:

```
error CS0006: Metadata file '.../nuget/microsoft.net.illink.tasks/10.0.0/analyzers/dotnet/cs/ILLink.RoslynAnalyzer.dll' could not be found
```

The packages folder is empty on those runs. Restore fetched three packages on the run that passed and none at all on the run that failed, without saying anything about it.

What is known. It alternates exactly, and it did so for thirteen runs in a row. It is per work directory: four runs at four different paths all passed, then a second run at one of those paths failed. It is not the build servers, because the failing run had none running and disabling them changes nothing. It is not the home directory, the packages folder or the temporary directory, because all three are inside the work directory and get deleted with it. Re-running the same build by hand in the failed directory passes. So there is something outside the work directory that is keyed by its path, and the thing that would tell us what it is has not been found yet.

The script therefore uses a directory nothing has ever used, with the process id in the name, and deletes it when the run passes. That is not a fix, it is the honest version of what cold means, and it makes the probe reliable: a directory the machine has used before is not cold, whatever is going on.

The failure also went from useless to readable along the way. The tool used to report only the error stream of a lesson that failed, and the SDK puts "The build failed. Fix the build errors and run again." there and the compiler's actual errors on the output stream, so the report was a sentence with the reason discarded. It now prints both streams, which is the only reason the two lines above are in this page.

## What the build does about it now

The working directory contract is written down and handed over rather than assumed. Every process the build starts gets `XRAY_HERE`, set to the absolute path of the directory the file being run belongs to. A lesson or a boss fight starts from that.

```csharp
// The build says which directory this lesson is in. It has to, because dotnet run does not
// promise the working directory of a file you point it at.
var here = Environment.GetEnvironmentVariable("XRAY_HERE") ?? ".";

var lines = File.ReadAllLines(Path.Combine(here, "lesson.cs"));
```

Writing it down is not enough on its own, because the next person to write a lesson will write `File.ReadAllLines("lesson.cs")` and it will work on their machine. So the build refuses it. Every lesson file, every generator block and both files of every boss fight are scanned, and a call to `File`, `Directory` or `Path.Combine` whose first argument is a string literal is a build failure with the file and line named.

The gate has two cases in the regeneration self test, one that breaks a boss fight and one that breaks a lesson, so the rule is proved to go red rather than assumed to. That self test now copies both `Directory.Build.props` files along with the lesson, and it has sixteen cases where it had fourteen.

## The results

Every gate passes on all four, from nothing, on the SDK `global.json` names rather than the one a developer machine resolves.

| Platform | Machine | What cold meant | SDK | Gates | Cold start |
|---|---|---|---|---|---|
| linux-x64 | Ubuntu 24.04 server, 8 cpus, shared with other work | `ubuntu:24.04` container, checkout mounted read only | 10.0.100 | all pass | 252 s |
| linux-arm64 | container on a laptop | `ubuntu:24.04` container, checkout mounted read only | 10.0.100 | all pass | 181 s |
| osx-arm64 | laptop, shared with other work | no container, everything inheritable emptied | 10.0.100 | all pass | 141 s |
| win-x64 | Windows 11, bare metal, idle, on a slow link that afternoon | no container, everything inheritable emptied | 10.0.100 | all pass | 394 s |

The gates are the six step build offline, the regeneration self test with its sixteen cases, the prose lint, the numbers gate, the assertion self test and the cache self test. The two Linux rows also had to install `ca-certificates`, `curl` and `libicu74` before .NET would start.

## Where the time goes

| Phase | linux-x64 | linux-arm64 | osx-arm64 | win-x64 |
|---|---|---|---|---|
| the packages Ubuntu does not have | 29 s | 36 s | not needed | not needed |
| a copy of the checkout with none of its build output | 0 s | 0 s | 0 s | 0 s |
| the SDK named by `global.json` | 28 s | 49 s | 41 s | 349 s |
| the six step build, offline | 93 s | 45 s | 48 s | 21 s |
| the regeneration gate against itself | 68 s | 33 s | 37 s | 16 s |
| the rest of the gates | 28 s | 14 s | 12 s | 6 s |
| what the machine turned out to be | 6 s | 4 s | 3 s | 2 s |

One measurement per machine, on four machines that are not comparable with each other. The Linux server was carrying other work and its two build phases show it. The download phases are a function of somebody's network on one afternoon and not of anything in this repository, and the 349 s on Windows is that and nothing else: the same machine did the rest of the run faster than any of the other three.

The right way to read this table is by column and not by row. Within a column the shape is the same everywhere once the download is set aside, and that shape is the point.

Nothing here is a benchmark. The point of the numbers is the shape: the SDK install and the first build of the tool are most of a cold run, and both of them are things a warm machine has already paid for and a reader pays once.

## Running it yourself

On Linux, in a container with nothing in it, with the checkout mounted read only so the build cannot write into it:

```
docker run --rm -v "$PWD:/src:ro" ubuntu:24.04 bash /src/docs/probes/cold-start.sh /src
```

On macOS, where there is no container and the cold part is an empty home directory, an empty NuGet folder, an empty cache and a path with no `dotnet` on it:

```
bash docs/probes/cold-start.sh
```

On Windows, the same, with [cold-start.ps1](cold-start.ps1):

```
pwsh -File docs/probes/cold-start.ps1
```

This is the same script CI runs, on all four platforms, on every pull request. A copy of it kept in a YAML file would drift from this page within a month, so there is one script and both the page and the job point at it.
