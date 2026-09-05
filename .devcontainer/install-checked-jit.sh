#!/usr/bin/env bash
# Put a checked clrjit next to the release runtime in this image, so DOTNET_JitDump works on the
# image as shipped and a reader never builds the runtime to see a dump.
#
# The discovery walk is the same one docs/probes/nightly-checked-jit.sh uses, and it is a walk
# rather than a hardcoded commit for two reasons. Not every commit gets a JIT build, so a written
# down commit goes stale on its own. And the JIT has to come from the same release branch as the
# runtime, because a JIT from main fails to load into a released runtime, which the probe measured.
#
# Nothing is overwritten. The file lands under a name of its own and DOTNET_JitName= selects it.

set -euo pipefail

CONTAINER="https://clrjit2.blob.core.windows.net/jitrollingbuild/builds"

line=$(dotnet --list-runtimes | grep 'Microsoft.NETCore.App' | tail -1)
version=$(echo "$line" | awk '{print $2}')
root=$(echo "$line" | sed 's/.*\[\(.*\)\]/\1/')
runtime_dir="$root/$version"
branch="release/$(echo "$version" | cut -d. -f1,2)"

case "$(uname -m)" in
  x86_64|amd64) arch=x64 ;;
  aarch64|arm64) arch=arm64 ;;
  *) echo "no checked JIT is published for $(uname -m)"; exit 1 ;;
esac

echo "runtime $version, branch $branch, linux-$arch"

sha=""
for candidate in $(curl -sS --max-time 60 \
    "https://api.github.com/repos/dotnet/runtime/commits?sha=$branch&per_page=40" \
    | grep '"sha"' | head -40 | sed 's/.*"sha": *"\([0-9a-f]*\)".*/\1/'); do
  if [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 -I \
      "$CONTAINER/$candidate/linux/$arch/Checked/libclrjit.so")" = "200" ]; then
    sha="$candidate"
    break
  fi
done

if [ -z "$sha" ]; then
  echo "no commit in the last forty on $branch has a checked JIT for linux-$arch"
  exit 1
fi

echo "checked JIT from $sha"
curl -fsSL --max-time 300 -o "$runtime_dir/libclrjit_checked.so" \
  "$CONTAINER/$sha/linux/$arch/Checked/libclrjit.so"

# Record where it came from, because a reader who asks "which JIT is this" deserves an answer that
# is a file rather than a guess, and because the lesson that explains the swap cites this.
cat > "$runtime_dir/checked-jit.json" <<EOF
{
  "repo": "dotnet/runtime",
  "branch": "$branch",
  "commit": "$sha",
  "runtime": "$version",
  "file": "libclrjit_checked.so",
  "switch": "DOTNET_JitName=libclrjit_checked.so"
}
EOF

ls -l "$runtime_dir/libclrjit_checked.so"
