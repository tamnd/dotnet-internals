# Drop a nightly checked JIT into the stock release runtime you already have, prove it really
# loaded, and measure what it costs. Windows. Nothing is overwritten: the checked JIT goes next
# to the runtime under a name of its own and is deleted again at the end.
#
# This is the script behind nightly-checked-jit.md. It is not run by CI, because it downloads a
# large binary from outside the pin.

$ErrorActionPreference = "Continue"
$container = "https://clrjit2.blob.core.windows.net/jitrollingbuild/builds"
$work = Join-Path $env:TEMP "checked-jit-probe"
$localName = "clrjit_checked.dll"

# Which runtime is installed, and therefore which release branch the JIT has to come from. A JIT
# built from main will not load into a released runtime, so this is not a detail.
$line = (& dotnet --list-runtimes | Select-String "Microsoft.NETCore.App" | Select-Object -Last 1).ToString()
$version = ($line -split ' ')[1]
$root = [regex]::Match($line, '\[(.*)\]').Groups[1].Value
$runtimeDir = Join-Path $root $version
$branch = "release/" + (($version -split '\.')[0..1] -join '.')
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }

Write-Output "runtime   $version at $runtimeDir"
Write-Output "branch    $branch"
Write-Output "platform  windows-$arch"

# Not every commit gets a JIT build, so walk the branch until the container has one. This is the
# same walk superpmi does, and it is why the commit is discovered rather than written down.
Write-Output "looking for a commit with a published checked JIT"
$commits = (Invoke-RestMethod -UseBasicParsing `
    "https://api.github.com/repos/dotnet/runtime/commits?sha=$branch&per_page=40" `
    -Headers @{ "User-Agent" = "dotnet-internals-probe" }) | ForEach-Object { $_.sha }

$sha = $null
foreach ($candidate in $commits) {
    $url = "$container/$candidate/windows/$arch/Checked/clrjit.dll"
    try {
        Invoke-WebRequest -UseBasicParsing -Method Head -Uri $url -TimeoutSec 20 | Out-Null
        $sha = $candidate
        break
    } catch { }
}

if (-not $sha) {
    Write-Output "no commit in the last forty on $branch has a checked JIT for windows-$arch"
    exit 1
}
Write-Output "commit    $sha"

New-Item -ItemType Directory -Force -Path "$work\app" | Out-Null
Write-Output "=== download ==="
Invoke-WebRequest -UseBasicParsing "$container/$sha/windows/$arch/Checked/clrjit.dll" -OutFile "$work\$localName"
Write-Output ("size " + (Get-Item "$work\$localName").Length)
Copy-Item "$work\$localName" "$runtimeDir\$localName" -Force

try {
    Set-Location "$work\app"
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@ | Set-Content -Encoding UTF8 app.csproj

    @'
namespace JitProbe;

public static class Program
{
    // A real named method rather than a local function, so a JitDump filter can name it without
    // anybody having to know how the compiler mangles a local function's name.
    public static long Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);

    public static void Main()
    {
        var total = 0L;
        for (var i = 0; i < 32; i++)
        {
            total += Fib(i);
        }

        Console.WriteLine($"total {total}");
        Console.WriteLine($"runtime {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
    }
}
'@ | Set-Content -Encoding UTF8 Program.cs

    & dotnet build -c Release -v q --nologo | Out-Null
    $app = "bin\Release\net10.0\app.dll"

    Write-Output "=== control 1, a name that does not exist must be fatal ==="
    $env:DOTNET_JitName = "clrjit_does_not_exist.dll"
    # Let cmd do the redirection. PowerShell would otherwise turn the runtime's stderr into an
    # error record and print its own report on top, which reads like two failures when it is one,
    # and suppressing that report suppresses the message with it.
    $control = & cmd /c "dotnet $app 2>&1"
    $exit = $LASTEXITCODE
    $control | Select-Object -First 2
    Write-Output "exit code: $exit"
    Remove-Item Env:\DOTNET_JitName

    Write-Output "=== control 2, JitDump against the shipped JIT, which has nothing behind it ==="
    $env:DOTNET_JitDump = "Fib"
    $env:DOTNET_TieredCompilation = "0"
    $shipped = & dotnet $app 2>&1
    Write-Output ("lines: " + ($shipped | Measure-Object -Line).Lines)

    Write-Output "=== control 3, JitDump against the checked JIT ==="
    $env:DOTNET_JitName = $localName
    $dump = & dotnet $app 2>&1
    Write-Output ("lines: " + ($dump | Measure-Object -Line).Lines)
    $dump | Select-Object -First 2

    Remove-Item Env:\DOTNET_JitDump
    Remove-Item Env:\DOTNET_JitName

    Write-Output "=== cost, with tiering and ReadyToRun off so the JIT compiles everything ==="
    $env:DOTNET_TieredCompilation = "0"
    $env:DOTNET_ReadyToRun = "0"

    function Bench($label) {
        $runs = @()
        for ($i = 0; $i -lt 7; $i++) {
            $sw = [Diagnostics.Stopwatch]::StartNew()
            & dotnet $app | Out-Null
            $sw.Stop()
            $runs += [int]$sw.ElapsedMilliseconds
        }
        $median = ($runs | Sort-Object)[3]
        Write-Output "$label median ${median}ms  runs: $($runs -join ' ')"
    }

    Bench "shipped JIT"
    $env:DOTNET_JitName = $localName
    Bench "checked JIT"
}
finally {
    Remove-Item "$runtimeDir\$localName" -Force -ErrorAction SilentlyContinue
    Write-Output "=== done, the checked JIT was removed from the runtime directory ==="
}
