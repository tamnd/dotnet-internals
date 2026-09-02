#!/usr/bin/env bash
# Run the same fifty one checks twice, once on a desktop runtime and once in a browser.
#
# The desktop column prints on standard output and this script waits for you to read it. The
# browser column prints in the page's console, so the last thing this does is serve the app and
# stop, and you open the address it prints and look at the console. Every line of both columns
# starts with PROBE| so a filter is one word.
#
# The Blazor app is a stock template with two edits: Probe.cs dropped in, and one call added to
# Program.cs. It is scaffolded here rather than committed because a template full of Razor pages
# nobody reads is not worth reviewing, and because the point of the measurement is that a plain
# app behaves this way with nothing special done to it.
#
# This is the script behind browser-tier.md. It is not run by CI, because the browser half needs
# a browser and the result is a comparison a person has to look at.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${1:-${TMPDIR:-/tmp}/browser-tier}"
PORT="${PORT:-5199}"

command -v dotnet > /dev/null 2>&1 || { echo "this script needs dotnet on PATH"; exit 2; }
command -v python3 > /dev/null 2>&1 || { echo "this script needs python3"; exit 2; }

mkdir -p "$WORK"
cp -R "$HERE/Sample" "$HERE/DesktopProbe" "$WORK/"
cp "$HERE/Probe.cs" "$WORK/Probe.cs"

# Everything below runs from the work directory rather than from the repository. The repository
# has a global.json and a Directory.Build.props, and both of them apply to whatever is built from
# inside it. The whole point of this probe is a stock app on a stock SDK, so it is built somewhere
# the project's own settings cannot reach it. Running the build from the repository root instead
# fails on the first attempt with a Razor namespace error and succeeds on the second, which is a
# good sign you are building something other than what you meant to.
cd "$WORK"

echo "building the sample library, which is the PE the metadata checks read"
dotnet build Sample/Sample.csproj -c Release -v q --nologo

echo
echo "=== desktop column ==="
dotnet run --project DesktopProbe/DesktopProbe.csproj -c Release -v q --nologo

if [ ! -d "$WORK/BrowserProbe" ]; then
  echo
  echo "scaffolding a stock Blazor WebAssembly app"
  dotnet new blazorwasm -n BrowserProbe -f net10.0 > /dev/null

  python3 - "$WORK" <<'PY'
import sys

work = sys.argv[1]

# Carry the sample as a resource. Blazor serves its own assemblies as Webcil rather than as plain
# PE, so a lesson that wants to show a reader a PE has to ship one, and so does this.
path = f"{work}/BrowserProbe/BrowserProbe.csproj"
text = open(path).read()
text = text.replace("</Project>", """  <ItemGroup>
    <EmbeddedResource Include="../Sample/bin/Release/netstandard2.0/Sample.dll" LogicalName="BrowserProbe.Sample.dll" />
  </ItemGroup>

</Project>""")
open(path, "w").write(text)

# Run the checks before the app starts rendering, so nothing else is competing for the console.
path = f"{work}/BrowserProbe/Program.cs"
text = open(path).read()
old = "await builder.Build().RunAsync();"
assert old in text, "the template changed, this patch needs updating"
text = text.replace(old, "var host = builder.Build();\nBrowserProbe.Probe.RunAll();\nawait host.RunAsync();")
open(path, "w").write(text)
PY
fi

cp "$HERE/Probe.cs" "$WORK/BrowserProbe/Probe.cs"

# Build before serving rather than letting dotnet run do it, so that the analyzer's verdict on
# which APIs are unsupported is a line you read rather than something buried in startup output.
echo
echo "building the browser app, which reports the two APIs the analyzer knows are unsupported"
dotnet build BrowserProbe/BrowserProbe.csproj -c Release --nologo 2>&1 | grep -E "CA1416|error|Build" | sort -u

echo
echo "=== browser column ==="
echo "open http://127.0.0.1:$PORT and read the console, filtering on PROBE"
echo
dotnet run --project BrowserProbe/BrowserProbe.csproj -c Release --no-build --urls "http://127.0.0.1:$PORT"
