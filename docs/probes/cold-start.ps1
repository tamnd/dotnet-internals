# Build the book on a machine that has never seen it. Windows. Use cold-start.sh on Linux and
# macOS.
#
# There is no container here. Windows containers are a different operating system image rather
# than a different directory tree, and pulling one is slower than the thing being measured. What
# this does instead is take away everything a warm machine would have given the build: the SDK on
# the path, the NuGet packages, the artifact cache, the temporary directory and the home
# directory. That is the part that hides defects. The part a container would add on top is the
# missing system library, and Windows does not have that problem because .NET on Windows uses the
# ICU that ships with the operating system.
#
# Usage: cold-start.ps1 [-Source <path to a checkout>]

param([string]$Source = (Resolve-Path "$PSScriptRoot\..\.."))

$ErrorActionPreference = "Stop"

# A directory nothing has ever used, which is what the process id is doing in the name. Reusing one
# directory looks tidier and is not cold, and running this twice at the same path is how that got
# found: see "running it twice" in cold-start.md. Set XRAY_COLD_WORK to override it, and expect the
# second run there to be a different run from the first.
$work = if ($env:XRAY_COLD_WORK) { $env:XRAY_COLD_WORK } else { Join-Path $env:TEMP "xray-cold-$PID" }
$started = Get-Date

function Phase([string]$name) {
    Write-Host ""
    Write-Host "=== $name ==="
    $script:phaseStarted = Get-Date
}
function Took {
    Write-Host ("-- {0}s" -f [int]((Get-Date) - $script:phaseStarted).TotalSeconds)
}
function Gate([string[]]$arguments) {
    dotnet run --project tools/xray -- @arguments
    if ($LASTEXITCODE -ne 0) { throw "xray $($arguments -join ' ') exited with $LASTEXITCODE" }
}

Write-Host "source    $Source"
Write-Host "work      $work"
Write-Host "platform  $env:PROCESSOR_ARCHITECTURE"

if (Test-Path $work) { Remove-Item -Recurse -Force $work }
foreach ($d in "repo", "home", "nuget", "cache", "tmp") { New-Item -ItemType Directory -Force -Path (Join-Path $work $d) | Out-Null }

# Everything the build could otherwise pick up from the machine, pointed somewhere empty. The path
# is rebuilt rather than prepended to, because a dotnet already on it is the whole thing being
# taken away. The temporary directory is in the list because restore does not do all of its work
# inside the packages folder: it takes its locks and unpacks through a NuGetScratch directory in
# the system temporary directory, and MSBuild keeps one there as well.
$root = Join-Path $work "dotnet"
$env:DOTNET_ROOT = $root
$env:DOTNET_CLI_HOME = Join-Path $work "home"
$env:USERPROFILE = Join-Path $work "home"
$env:NUGET_PACKAGES = Join-Path $work "nuget"
$env:XRAY_CACHE = Join-Path $work "cache"
$env:TEMP = Join-Path $work "tmp"
$env:TMP = $env:TEMP
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:PATH = "$root;$env:SystemRoot\system32;$env:SystemRoot"

$failure = $null

try {
    Phase "a copy of the checkout with none of its build output"
    $repo = Join-Path $work "repo"
    robocopy $Source $repo /E /NFL /NDL /NJH /NJS /NP /XD .git obj bin | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "the copy failed with $LASTEXITCODE" }
    Set-Location $repo
    Write-Host ("{0} files" -f (Get-ChildItem -Recurse -File).Count)
    Took

    # --jsonfile global.json, so the version that gets installed is the version global.json
    # literally names. This is not the same as what a machine with an SDK already on it resolves:
    # global.json rolls forward to the latest feature band, so a developer machine gets a 10.0.4xx
    # and a reader following the README gets 10.0.100. Those two disagreed about the working
    # directory of a file run with dotnet run, and nothing here had ever run on the second one.
    Phase "the SDK named by global.json"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "$work\dotnet-install.ps1" -UseBasicParsing
    & "$work\dotnet-install.ps1" -JSonFile "$repo\global.json" -InstallDir $root -NoPath | Out-Null
    Write-Host "sdk $(dotnet --version)"
    Took

    Phase "the six step build, offline"
    Gate @("check", "--offline")
    Took

    # The self test is the part of this that a warm machine cannot do for you. It builds small
    # broken copies of a lesson and requires the build to refuse each one, and five of its cases
    # only ever failed on the SDK that global.json names, in a directory nobody had a copy of.
    Phase "the regeneration gate against itself"
    Gate @("check", "--selftest")
    Took

    Phase "the rest of the gates"
    Gate @("lint")
    Gate @("numbers", "lessons")
    Gate @("assert", "--selftest")
    Gate @("cache", "--selftest")
    Took

    Phase "what the machine turned out to be"
    Gate @("env")
    Took
}
catch {
    $failure = $_
}
finally {
    # A run that passed takes its directory with it, and a run that failed leaves everything where
    # it is, because the first thing anybody wants after a failure here is to go and look. The
    # build servers are stopped either way. They outlive this script otherwise, and on Windows they
    # hold their files open, so the directory cannot be deleted until they are gone.
    dotnet build-server shutdown | Out-Null
    Set-Location $PSScriptRoot

    if ($failure) {
        Write-Host ""
        Write-Host $failure.Exception.Message
        Write-Host "left where it is, to be looked at: $work"
    }
    else {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
        Write-Host ""
        Write-Host ("cold start: {0}s in total" -f [int]((Get-Date) - $started).TotalSeconds)
    }
}

if ($failure) { exit 1 }
