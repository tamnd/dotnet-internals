#!/usr/bin/env bash
# Build the book on a machine that has never seen it. No SDK on the path, no NuGet packages, no
# artifact cache, no home directory to inherit anything from. Linux and macOS. Use
# cold-start.ps1 on Windows.
#
# This is the script behind cold-start.md, and unlike the other probes here it is also what the
# cold job in CI runs, on all four platforms. A cold machine is not a slower warm machine. It is
# a different machine, and three of the defects this repository has had were only visible on one.
#
# Usage: cold-start.sh [path to a checkout, default the one this script is in]

set -uo pipefail

source="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

# A directory nothing has ever used, which is what the process id is doing in the name. Reusing one
# directory looks tidier and is not cold, and running this twice at the same path is how that got
# found: see "running it twice" in cold-start.md. Point XRAY_COLD_WORK somewhere to override it,
# and expect the second run there to be a different run from the first.
WORK="${XRAY_COLD_WORK:-${TMPDIR:-/tmp}/xray-cold-$$}"

started=$SECONDS
phase() {
  echo
  echo "=== $1 ==="
  phase_started=$SECONDS
}
took() {
  echo "-- $(($SECONDS - phase_started))s"
}

echo "source    $source"
echo "work      $WORK"
echo "platform  $(uname -s) $(uname -m)"

# A run that passed takes its directory with it, and a run that failed leaves everything where it
# is, because the first thing anybody wants after a failure here is to go and look. The build
# servers are stopped either way. They outlive this script otherwise, holding open an SDK and a
# home directory that are about to stop existing, and on Windows they stop the directory being
# deleted at all.
finish() {
  code=$?
  dotnet build-server shutdown >/dev/null 2>&1
  cd /
  if [ "$code" = "0" ]; then
    rm -rf "$WORK"
  else
    echo
    echo "left where it is, to be looked at: $WORK"
  fi
}
trap finish EXIT

rm -rf "$WORK"
mkdir -p "$WORK/repo" "$WORK/home" "$WORK/nuget" "$WORK/cache" "$WORK/tmp"

# Everything the build could otherwise pick up from the machine, pointed somewhere empty. HOME is
# in the list because that is where the SDK keeps its own state, and a first run and a second run
# of the same command are different runs until it is.
export HOME="$WORK/home"
# The temporary directory too. Restore does not do all of its work inside the packages folder: it
# takes its locks and unpacks through a NuGetScratch directory in the system temporary directory,
# and MSBuild keeps one there as well. Leave those where they are and the run shares a directory
# with every previous run and with everything else on the machine, which is not what this script
# is for.
export TMPDIR="$WORK/tmp"
export DOTNET_ROOT="$WORK/dotnet"
export DOTNET_CLI_HOME="$WORK/home"
export NUGET_PACKAGES="$WORK/nuget"
export XRAY_CACHE="$WORK/cache"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$DOTNET_ROOT:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"

# A bare Ubuntu image cannot run .NET at all. It has no curl to fetch the SDK with, and once the
# SDK is there it aborts on startup with "Couldn't find a valid ICU package installed on the
# system", because .NET links against the system ICU and the image does not ship one.
# dotnet-install.sh says in its own help that it does not resolve dependencies, and this is what
# that sentence means in practice. The package is numbered after the ICU release, so the name
# changes with the distribution version and is looked up rather than written down.
if [ -r /etc/debian_version ] && [ "$(id -u)" = "0" ]; then
  phase "the packages Ubuntu does not have"
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  icu=$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)
  echo "icu package: $icu"
  apt-get install -y -qq --no-install-recommends ca-certificates curl "$icu" >/dev/null
  took
fi

phase "a copy of the checkout with none of its build output"
tar -cf - --exclude=.git --exclude=obj --exclude=bin -C "$source" . | tar -xf - -C "$WORK/repo" 2>/dev/null
cd "$WORK/repo" || exit 1
echo "$(find . -type f | wc -l | tr -d ' ') files"
took

# --jsonfile global.json, so the version that gets installed is the version global.json literally
# names. This is not the same as what a machine with an SDK already on it resolves: global.json
# rolls forward to the latest feature band, so a developer machine gets a 10.0.4xx and a reader
# following the README gets 10.0.100. Those two disagreed about the working directory of a file
# run with dotnet run, and nothing in this repository had ever run on the second one.
phase "the SDK named by global.json"
curl -sSL --max-time 300 https://dot.net/v1/dotnet-install.sh -o "$WORK/dotnet-install.sh" || exit 1
bash "$WORK/dotnet-install.sh" --jsonfile global.json --install-dir "$DOTNET_ROOT" --no-path >/dev/null || exit 1
echo "sdk $(dotnet --version)"
took

phase "the six step build, offline"
dotnet run --project tools/xray -- check --offline || exit 1
took

# The self test is the part of this that a warm machine cannot do for you. It builds small broken
# copies of a lesson and requires the build to refuse each one, and five of its cases only ever
# failed on the SDK that global.json names, in a directory nobody had a copy of.
phase "the regeneration gate against itself"
dotnet run --project tools/xray -- check --selftest || exit 1
took

phase "the rest of the gates"
dotnet run --project tools/xray -- lint || exit 1
dotnet run --project tools/xray -- numbers lessons || exit 1
dotnet run --project tools/xray -- assert --selftest || exit 1
dotnet run --project tools/xray -- cache --selftest || exit 1
took

phase "what the machine turned out to be"
dotnet run --project tools/xray -- env
took

echo
echo "cold start: $(($SECONDS - started))s in total"
