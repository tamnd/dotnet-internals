#!/usr/bin/env bash
# Drop a nightly checked JIT into the stock release runtime you already have, prove it really
# loaded, and measure what it costs. Linux and macOS. Nothing is overwritten: the checked JIT
# goes next to the runtime under a name of its own and is deleted again at the end.
#
# This is the script behind nightly-checked-jit.md. It is not run by CI, because it downloads a
# large binary from outside the pin.

set -uo pipefail

CONTAINER="https://clrjit2.blob.core.windows.net/jitrollingbuild/builds"
WORK="${TMPDIR:-/tmp}/checked-jit-probe"

# Which runtime is installed, and therefore which release branch the JIT has to come from. A JIT
# built from main will not load into a released runtime, so this is not a detail.
line=$(dotnet --list-runtimes | grep 'Microsoft.NETCore.App' | tail -1)
version=$(echo "$line" | awk '{print $2}')
root=$(echo "$line" | sed 's/.*\[\(.*\)\]/\1/')
runtime_dir="$root/$version"
branch="release/$(echo "$version" | cut -d. -f1,2)"

case "$(uname -s)" in
  Darwin) os=osx;   jit=libclrjit.dylib; local_name=libclrjit_checked.dylib ;;
  Linux)  os=linux; jit=libclrjit.so;    local_name=libclrjit_checked.so ;;
  *) echo "this script is for Linux and macOS, use nightly-checked-jit.ps1 on Windows"; exit 2 ;;
esac

case "$(uname -m)" in
  x86_64|amd64) arch=x64 ;;
  arm64|aarch64) arch=arm64 ;;
  *) echo "no checked JIT is published for $(uname -m)"; exit 2 ;;
esac

echo "runtime   $version at $runtime_dir"
echo "branch    $branch"
echo "platform  $os-$arch"

# Not every commit gets a JIT build, so walk the branch until the container has one. This is the
# same walk superpmi does, and it is why the commit is discovered rather than written down.
echo "looking for a commit with a published checked JIT"
sha=""
for candidate in $(curl -sS --max-time 60 \
    "https://api.github.com/repos/dotnet/runtime/commits?sha=$branch&per_page=40" \
    | grep '"sha"' | head -40 | sed 's/.*"sha": *"\([0-9a-f]*\)".*/\1/'); do
  if [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 -I \
      "$CONTAINER/$candidate/$os/$arch/Checked/$jit")" = "200" ]; then
    sha="$candidate"
    break
  fi
done

if [ -z "$sha" ]; then
  echo "no commit in the last forty on $branch has a checked JIT for $os-$arch"
  exit 1
fi
echo "commit    $sha"

mkdir -p "$WORK/app"
echo "=== download ==="
curl -sS --max-time 300 -o "$WORK/$local_name" "$CONTAINER/$sha/$os/$arch/Checked/$jit" || exit 1
ls -l "$WORK/$local_name"

if [ "$os" = "osx" ]; then
  echo "=== how it is signed, which is the thing said to be awkward here ==="
  codesign -dv "$WORK/$local_name" 2>&1 | grep -E 'Format|CodeDirectory|Signature'
  echo "quarantine attributes: $(xattr "$WORK/$local_name" | grep -c quarantine)"
fi

cp "$WORK/$local_name" "$runtime_dir/$local_name"
trap 'rm -f "$runtime_dir/$local_name"' EXIT

cd "$WORK/app"
cat > app.csproj <<'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
PROJ

cat > Program.cs <<'CODE'
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
CODE

dotnet build -c Release -v q --nologo > /dev/null 2>&1 || { echo "the probe app did not build"; exit 1; }
app=bin/Release/net10.0/app.dll

echo "=== control 1, a name that does not exist must be fatal ==="
# Captured rather than piped, because the runtime aborts here and a pipeline would print the
# shell's own report of the abort on top of the runtime's, which reads like two failures.
control=$(DOTNET_JitName=libclrjit_does_not_exist dotnet $app 2>&1); code=$?
echo "$control" | head -2
echo "exit code: $code"

echo "=== control 2, JitDump against the shipped JIT, which has nothing behind it ==="
echo "lines: $(DOTNET_JitDump=Fib DOTNET_TieredCompilation=0 dotnet $app 2>&1 | wc -l | tr -d ' ')"

echo "=== control 3, JitDump against the checked JIT ==="
DOTNET_JitName=$local_name DOTNET_JitDump=Fib DOTNET_TieredCompilation=0 dotnet $app > "$WORK/dump.txt" 2>&1
echo "lines: $(wc -l < "$WORK/dump.txt" | tr -d ' ')"
head -2 "$WORK/dump.txt"

echo "=== cost, with tiering and ReadyToRun off so the JIT compiles everything ==="

# There is no millisecond clock that exists everywhere this script runs. GNU date has one, macOS
# date does not, and a container image may have neither perl nor python. So pick one once, and
# say so rather than silently timing everything as zero.
if [ "$(date +%s%N 2>/dev/null | tr -dc 0-9 | wc -c | tr -d ' ')" -ge 15 ]; then
  now_ms() { echo $(( $(date +%s%N) / 1000000 )); }
elif command -v perl > /dev/null 2>&1; then
  now_ms() { perl -MTime::HiRes -e 'printf "%d", Time::HiRes::time() * 1000'; }
elif command -v python3 > /dev/null 2>&1; then
  now_ms() { python3 -c 'import time; print(int(time.time() * 1000))'; }
else
  echo "no millisecond clock here, so the timings are skipped and the load result above stands"
  exit 0
fi

bench() {
  label="$1"; shift
  runs=""
  for _ in 1 2 3 4 5 6 7; do
    start=$(now_ms)
    env "$@" dotnet $app > /dev/null 2>&1
    runs="$runs $(( $(now_ms) - start ))"
  done
  median=$(echo "$runs" | tr ' ' '\n' | grep -v '^$' | sort -n | awk '{a[NR]=$1} END {print a[int((NR+1)/2)]}')
  echo "$label median ${median}ms  runs:$runs"
}
bench "shipped JIT" DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0
bench "checked JIT" DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 DOTNET_JitName=$local_name

echo "=== done, the checked JIT is being removed from the runtime directory ==="
