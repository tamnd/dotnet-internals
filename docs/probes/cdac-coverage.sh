#!/usr/bin/env bash
# Measure how much of what the blueprints need is published by the runtime's cDAC data contracts.
#
# The published side is read from dotnet/runtime, from the three datadescriptor.inc files and the
# contract specification directory. The needs side is read from cdac-coverage-needs.json next to
# this script. Nothing here runs the runtime, so this works anywhere with bash, curl and python3.
#
# This is the script behind cdac-coverage.md. It is not run by CI, because it reads a moving
# branch rather than the pin. When the pin has commits in it, pass that commit as the first
# argument and the numbers become reproducible.

set -uo pipefail

REF="${1:-release/11.0}"
RAW="https://raw.githubusercontent.com/dotnet/runtime/$REF/src/coreclr"
API="https://api.github.com/repos/dotnet/runtime/contents/docs/design/datacontracts?ref=$REF"
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${TMPDIR:-/tmp}/cdac-coverage"

command -v python3 > /dev/null 2>&1 || { echo "this script needs python3"; exit 2; }

mkdir -p "$WORK"
echo "ref       $REF"

for pair in "vm:vm" "gc:gc" "nativeaot:nativeaot/Runtime"; do
  name="${pair%%:*}"; path="${pair##*:}"
  url="$RAW/$path/datadescriptor/datadescriptor.inc"
  curl -sS --fail --max-time 60 -o "$WORK/$name.inc" "$url" || {
    echo "could not read $url"; exit 1; }
done

curl -sS --fail --max-time 60 -o "$WORK/contracts.json" "$API" || {
  echo "could not list the contract specifications"; exit 1; }

python3 - "$WORK" "$HERE/cdac-coverage-needs.json" <<'PY'
import json, re, sys

work, needs_path = sys.argv[1], sys.argv[2]

# The published side. A type counts as published if the descriptor names it at all, and the field
# count is carried along so that shallow coverage shows up in the output instead of hiding inside
# a tick. A global counts the same way, because a constant section 2 has to state is satisfied
# just as well by a published global as by a published field.
types, globals_, fields = {}, set(), {}
for src in ("vm", "gc", "nativeaot"):
    current = None
    for line in open(f"{work}/{src}.inc"):
        m = re.match(r"\s*CDAC_TYPE_BEGIN\((\w+)\)", line)
        if m:
            current = m.group(1)
            types.setdefault(current, set()).add(src)
            fields.setdefault(current, 0)
            continue
        if re.match(r"\s*CDAC_TYPE_END", line):
            current = None
            continue
        if current and re.match(r"\s*CDAC_TYPE_FIELD\(", line):
            fields[current] += 1
        m = re.match(r"\s*CDAC_GLOBAL\w*\((\w+)", line)
        if m:
            globals_.add(m.group(1))

specs = sorted(
    e["name"][:-3]
    for e in json.load(open(f"{work}/contracts.json"))
    if e["name"].endswith(".md") and e["name"][0].isupper()
)

lower_types = {t.lower(): t for t in types}
lower_globals = {g.lower(): g for g in globals_}

print(f"published  {len(types)} types, {sum(fields.values())} fields, "
      f"{len(globals_)} globals, {len(specs)} contract specifications")
print()

needs = json.load(open(needs_path))
rows, aliases, gaps = [], [], []
for bp in needs["blueprints"]:
    hit = 0
    for s in bp["structures"]:
        wanted = s.get("published_as", s["name"])
        key = wanted.lower()
        found = lower_types.get(key) or lower_globals.get(key)
        if found:
            hit += 1
            if "published_as" in s:
                aliases.append(f"{bp['id']}: {s['name']} matched as {found}")
        else:
            gaps.append((bp["id"], bp["section2_source"], s["name"], s["kind"], s["why"]))
    rows.append((bp["id"], bp["part"], bp["section2_source"], hit, len(bp["structures"])))

def pct(a, b):
    return f"{100.0 * a / b:.0f}%" if b else "n/a"

print(f"{'blueprint':<18}{'part':<6}{'source':<10}{'covered':>9}")
for bid, part, source, hit, total in rows:
    print(f"{bid:<18}{part:<6}{source:<10}{f'{hit}/{total}':>9}  {pct(hit, total)}")

hit_all = sum(r[3] for r in rows)
tot_all = sum(r[4] for r in rows)
cdac = [r for r in rows if r[2] == "cdac"]
other = [r for r in rows if r[2] == "none"]

print()
print(f"all runtime side blueprints      {hit_all}/{tot_all}  {pct(hit_all, tot_all)}")
print(f"the three betting on cDAC        {sum(r[3] for r in cdac)}/{sum(r[4] for r in cdac)}"
      f"  {pct(sum(r[3] for r in cdac), sum(r[4] for r in cdac))}")
print(f"those with no other generator    {sum(r[3] for r in other)}/{sum(r[4] for r in other)}"
      f"  {pct(sum(r[3] for r in other), sum(r[4] for r in other))}")

print()
print(f"=== how deep the covered types go, worst first ===")
seen = set()
depth = []
for bp in needs["blueprints"]:
    for s in bp["structures"]:
        wanted = s.get("published_as", s["name"])
        t = lower_types.get(wanted.lower())
        if t and t not in seen:
            seen.add(t)
            depth.append((fields[t], t))
for n, t in sorted(depth)[:15]:
    print(f"  {n:>3} field(s)  {t}")

print()
print(f"=== the gap, {len(gaps)} of {tot_all} ===")
for source in ("cdac", "none", "header", "manifest"):
    these = [g for g in gaps if g[1] == source]
    if not these:
        continue
    print(f"-- section 2 source {source}")
    for bid, _, name, kind, why in these:
        print(f"  {bid:<16}{name:<34}{kind:<10}{why}")

if aliases:
    print()
    print("=== names matched through an alias, check these by hand ===")
    for a in aliases:
        print(f"  {a}")

print()
print("=== contract specifications published ===")
print("  " + "  ".join(specs))
PY
