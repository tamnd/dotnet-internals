#!/usr/bin/env bash
# Measure what the E0 image costs: to build once, to store, and to start.
#
# This is the part of question 7 that does not need GitHub. A Codespace is a VM that pulls a
# prebuilt image and starts a container from it, so the size of the image and the time from
# container start to a working lesson are the two terms this project controls. The terms it does
# not control, VM provision and the pull itself, are GitHub's and are measured separately by
# actually creating a Codespace.
#
# Run it on a Linux x64 machine with docker, because that is what a Codespace is. Run it on a quiet
# one if the timings are going to be quoted.
#
# This is the script behind codespaces-cost.md. It is not run by CI: it builds a large image from
# scratch and downloads a checked JIT from outside the pin.

set -uo pipefail

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
IMAGE="${IMAGE:-dotnet-internals-e0}"
RUNS="${RUNS:-5}"

command -v docker > /dev/null 2>&1 || { echo "this script needs docker"; exit 2; }

echo "host      $(uname -s) $(uname -m), $(nproc) cpus"
echo "load      $(cut -d' ' -f1-3 /proc/loadavg 2>/dev/null || echo unknown)"
echo "docker    $(docker version --format '{{.Server.Version}}')"
echo "repo      $REPO"
echo

# A cold build with no layer cache at all, which is what the project pays in Actions minutes every
# time the prebuild is regenerated. --no-cache rather than a pruned daemon, so the measurement can
# run on a machine with other images on it.
echo "=== cold build, no cache ==="
start=$(date +%s)
docker build --no-cache --pull -f "$REPO/.devcontainer/Dockerfile" -t "$IMAGE" "$REPO" > /tmp/e0-build.log 2>&1
rc=$?
cold=$(( $(date +%s) - start ))
if [ $rc -ne 0 ]; then
  echo "build failed, last twenty lines:"
  tail -20 /tmp/e0-build.log
  exit 1
fi
echo "cold build ${cold}s"

# A rebuild with everything cached, which is what a prebuild costs on a day when nothing changed.
echo
echo "=== warm rebuild, full cache ==="
start=$(date +%s)
docker build -f "$REPO/.devcontainer/Dockerfile" -t "$IMAGE" "$REPO" > /dev/null 2>&1
echo "warm rebuild $(( $(date +%s) - start ))s"

echo
echo "=== size ==="
docker pull -q mcr.microsoft.com/devcontainers/base:ubuntu-24.04 > /dev/null 2>&1

# Two different numbers, and they are three and a half times apart, so which one a sentence means
# has to be said out loud. What the machine stores is the unpacked image. What a cold start
# downloads is the compressed layers, and that is the one that turns into seconds of a reader
# waiting. docker image ls reports the first. docker image inspect .Size reports the second, which
# is easy to read as the image being small when it is the download that is small.
#
# The compressed figure comes from a real registry manifest rather than from either command, by
# pushing to a throwaway local registry, because compressed sizes only exist in a manifest.
# Measured from inside the container rather than with docker image ls, because that command reports
# whatever the storage driver thinks and the storage driver is not the same everywhere. The same
# image measured this way is 2.51 GB under the containerd snapshotter and 1.75 GB under overlay2.
# du against the filesystem the container actually sees gives one answer on both.
unpacked() { docker run --rm "$1" du -sh --exclude=/proc --exclude=/sys / 2>/dev/null | tail -1 | cut -f1; }

echo "unpacked, what the machine stores"
echo "  image   $(unpacked "$IMAGE")"
echo "  base    $(unpacked mcr.microsoft.com/devcontainers/base:ubuntu-24.04)"

reg=$(docker run -d --rm -p 5000:5000 registry:2 2>/dev/null)
if [ -n "$reg" ]; then
  sleep 3
  docker tag "$IMAGE" localhost:5000/e0:probe
  if docker push -q localhost:5000/e0:probe > /dev/null 2>&1; then
    echo
    echo "compressed, what a cold start downloads"
    python3 - <<'PY'
import json, urllib.request

# A buildkit push lands an OCI index, so the tag resolves to a list of manifests and the layer
# sizes are one level down. Asking for the manifest media type alone gets a 404 that says the
# accept header does not support indexes, which reads like the image is missing.
ACCEPT = ",".join([
    "application/vnd.oci.image.index.v1+json",
    "application/vnd.oci.image.manifest.v1+json",
    "application/vnd.docker.distribution.manifest.list.v2+json",
    "application/vnd.docker.distribution.manifest.v2+json",
])


def get(ref):
    r = urllib.request.Request(
        f"http://localhost:5000/v2/e0/manifests/{ref}", headers={"Accept": ACCEPT})
    return json.load(urllib.request.urlopen(r))


m = get("probe")
if "manifests" in m:
    m = get(next(x for x in m["manifests"]
                 if x.get("platform", {}).get("os") == "linux")["digest"])

sizes = sorted((l["size"] for l in m["layers"]), reverse=True)
print(f"  total   {sum(sizes) / 1e9:.2f} GB in {len(sizes)} layers")
print(f"  largest {sizes[0] / 1e9:.2f} GB")
PY
  fi
  docker rm -f "$reg" > /dev/null 2>&1
fi

# Largest layers, sorted by size. docker history lists newest first, so taking the top of the list
# gives you the most recent layers rather than the biggest ones, which is a mistake that makes a
# large image look like it is made of nothing.
echo
echo "the five largest layers, whole image:"
docker history "$IMAGE" --no-trunc --human=false --format '{{.Size}}\t{{.CreatedBy}}' \
  | sort -rn \
  | head -5 \
  | awk -F'\t' '{cmd=$2; gsub(/^\/bin\/sh -c #\(nop\) +/,"",cmd); gsub(/^\|1 DOTNET_SDK_VERSION=[^ ]* /,"",cmd); gsub(/^\/bin\/sh -c /,"",cmd); gsub(/ +/," ",cmd); printf "%6.0f MB  %s\n", $1/1e6, substr(cmd,1,64)}'

# What a reader waits for once the container exists. Not the Codespace cold start, which includes a
# VM and a pull, but the floor underneath it and the only part of it this repository can change.
#
# The workspace is mounted writable and copied fresh, because a Codespace has a writable clone and a
# read-only mount fails the first build on the obj directory. The first and second runs are reported
# separately rather than averaged: the first is what a reader's very first action costs and the
# second is what every action after it costs, and putting them in one median hides both.
echo
echo "=== a reader's first commands, median of $RUNS ==="
time_run() {
  local label="$1" cmd="$2" fresh="$3"
  local times="" ws
  for _ in $(seq "$RUNS"); do
    ws=$(mktemp -d)
    cp -a "$REPO/." "$ws/"
    rm -rf "$ws/.git" "$ws"/tools/*/obj "$ws"/tools/*/bin
    # The container runs as vscode, uid 1000, and a workspace owned by whoever ran this script
    # fails the first restore on the obj directory rather than being slow. Done in a container as
    # root rather than with chown here, because the person running this script is root on one of
    # the machines it was written on and an unprivileged user on the other.
    docker run --rm --user root -v "$ws:/w" "$IMAGE" chown -R vscode:vscode /w
    if [ "$fresh" = "warm" ]; then
      docker run --rm -v "$ws:/w" "$IMAGE" bash -lc "$cmd" > /dev/null 2>&1
    fi
    local start=$(date +%s%N)
    if ! docker run --rm -v "$ws:/w" "$IMAGE" bash -lc "$cmd" > /tmp/e0-run.log 2>&1; then
      echo "the $label run failed, so its timing would have been meaningless:"
      tail -10 /tmp/e0-run.log
      # The workspace now belongs to vscode, so whoever ran this script may not be able to delete it.
    docker run --rm --user root -v "$ws:/w" "$IMAGE" rm -rf /w/. > /dev/null 2>&1
    rm -rf "$ws"
      exit 1
    fi
    times="$times $(( ( $(date +%s%N) - start ) / 1000000 ))"
    # The workspace now belongs to vscode, so whoever ran this script may not be able to delete it.
    docker run --rm --user root -v "$ws:/w" "$IMAGE" rm -rf /w/. > /dev/null 2>&1
    rm -rf "$ws"
  done
  local median=$(echo $times | tr ' ' '\n' | sort -n | awk '{a[NR]=$1} END{print a[int((NR+1)/2)]}')
  printf "%-22s %6s ms   (runs:%s)\n" "$label" "$median" "$times"
}

time_run "container only"      "true" cold
time_run "banner, first time"  "cd /w && dotnet run --project tools/xray -- banner" cold
time_run "banner, again"       "cd /w && dotnet run --project tools/xray -- banner" warm
time_run "the pilot lesson"    "cd /w && dotnet run --project tools/xray -- build lessons/m03-four-heaps" warm

# Controls. An image that starts fast and cannot do the lessons is not a cheap image, it is a wrong
# one, so the three things the image exists to provide are checked rather than assumed.
echo
echo "=== controls ==="
docker run --rm "$IMAGE" bash -lc 'dotnet --version' 2>&1 | sed 's/^/sdk       /'
docker run --rm "$IMAGE" bash -lc 'dotnet-counters --version' 2>&1 | sed 's/^/counters  /'
docker run --rm "$IMAGE" bash -lc 'lldb --version' 2>&1 | sed 's/^/lldb      /'
docker run --rm "$IMAGE" bash -lc '
  line=$(dotnet --list-runtimes | grep Microsoft.NETCore.App | tail -1)
  cat "$(echo "$line" | sed "s/.*\[\(.*\)\]/\1/")/$(echo "$line" | awk "{print \$2}")/checked-jit.json"
' 2>&1 | tr -d '\n ' | sed 's/^/jit       /'
echo

# The one control that matters, because everything above would pass on an image whose checked JIT
# is a file that never loads. This asks the runtime in the image to dump a method it compiled, and
# a JitDump that names the method is the only thing that proves the swap works as shipped.
echo "does DOTNET_JitDump actually work on this image, with nothing installed by the reader:"
docker run --rm -v "$REPO:/w:ro" -e DOTNET_JitName=libclrjit_checked.so -e DOTNET_JitDump=Fib \
  -e DOTNET_TieredCompilation=0 "$IMAGE" bash -lc '
  mkdir -p /tmp/j && cd /tmp/j && cat > p.cs <<CS
class P { static long Fib(int n) => n < 2 ? n : Fib(n-1) + Fib(n-2);
          static void Main() => System.Console.WriteLine(Fib(20)); }
CS
  dotnet run p.cs 2>/dev/null' 2>&1 \
  | grep -c "START compiling.*Fib" | sed 's/^/dump      /' | sed 's/$/ line(s) naming the method/'

# Arithmetic, not measurement, and labelled that way because the rates are GitHub's published ones
# rather than anything this script observed. The only measured input is the image size above.
echo
echo "=== free tier arithmetic, over GitHub's published rates ==="
echo "a 2 core Codespace bills 2 core hours per wall clock hour"
echo "the personal free allowance is 120 core hours a month, so 60 hours on a 2 core machine"
echo "at one lesson a day for thirty days that is 2 hours a day before a reader pays"
echo
echo "storage is billed separately against a 15 GB month allowance, and the image is on the disk:"
echo "  unpacked image $(unpacked "$IMAGE"), against a 32 GB disk on the smallest machine"
echo "  so a codespace left alive for a month spends roughly a sixth of the storage allowance"
echo "  and a reader who deletes it between sessions spends almost none"

# The layer table above says most of the image is the base rather than anything this project put
# there, so the obvious question is what a plain base costs instead. Everything below the FROM line
# in the two Dockerfiles is identical, which is the only reason the two numbers can be compared.
if [ -f "$REPO/.devcontainer/Dockerfile.lean" ]; then
  echo
  echo "=== the same image on a plain ubuntu base ==="
  start=$(date +%s)
  if docker build --no-cache --pull -f "$REPO/.devcontainer/Dockerfile.lean" \
      -t "$IMAGE-lean" "$REPO" > /tmp/e0-lean-build.log 2>&1; then
    echo "cold build $(( $(date +%s) - start ))s"
    echo "unpacked   $(unpacked "$IMAGE-lean")"

    reg=$(docker run -d --rm -p 5000:5000 registry:2 2>/dev/null)
    if [ -n "$reg" ]; then
      sleep 3
      docker tag "$IMAGE-lean" localhost:5000/e0lean:probe
      if docker push -q localhost:5000/e0lean:probe > /dev/null 2>&1; then
        python3 - <<'PY'
import json, urllib.request

ACCEPT = ",".join([
    "application/vnd.oci.image.index.v1+json",
    "application/vnd.oci.image.manifest.v1+json",
    "application/vnd.docker.distribution.manifest.list.v2+json",
    "application/vnd.docker.distribution.manifest.v2+json",
])


def get(ref):
    r = urllib.request.Request(
        f"http://localhost:5000/v2/e0lean/manifests/{ref}", headers={"Accept": ACCEPT})
    return json.load(urllib.request.urlopen(r))


m = get("probe")
if "manifests" in m:
    m = get(next(x for x in m["manifests"]
                 if x.get("platform", {}).get("os") == "linux")["digest"])

sizes = [l["size"] for l in m["layers"]]
print(f"compressed {sum(sizes) / 1e9:.2f} GB in {len(sizes)} layers")
PY
      fi
      docker rm -f "$reg" > /dev/null 2>&1
    fi

    # Same control as above. A smaller image that cannot produce a dump is not an improvement.
    docker run --rm -e DOTNET_JitName=libclrjit_checked.so -e DOTNET_JitDump=Fib \
      -e DOTNET_TieredCompilation=0 "$IMAGE-lean" bash -lc '
      mkdir -p /tmp/j && cd /tmp/j && cat > p.cs <<CS
class P { static long Fib(int n) => n < 2 ? n : Fib(n-1) + Fib(n-2);
          static void Main() => System.Console.WriteLine(Fib(20)); }
CS
      dotnet run p.cs 2>/dev/null' 2>&1 \
      | grep -c "START compiling.*Fib" | sed 's/^/dump       /' | sed 's/$/ line(s) naming the method/'
  else
    echo "the lean build failed, last twenty lines:"
    tail -20 /tmp/e0-lean-build.log
  fi
fi
