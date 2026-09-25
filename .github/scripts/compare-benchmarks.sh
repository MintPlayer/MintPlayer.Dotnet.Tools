#!/usr/bin/env bash
# Compares two BenchmarkDotNet JSON reports: the pull request's head against its base.
#
# Usage: compare-benchmarks.sh <base-results-dir> <head-results-dir> <summary-file>
#
# Only ALLOCATED BYTES are gated. They are a property of the emitted IL and the object graph, so they
# reproduce on a loaded shared runner; wall-clock does not (see assertions-benchmark.yml for the measured
# spread), so time is reported but never fails the job.
#
# A benchmark fails when its head allocation exceeds the base by more than BYTES_TOLERANCE_PERCENT
# (default 5%), or when a row that allocated 0 B on the base allocates anything on the head: 0 B is a
# design property of the generated equality, not a number that drifts.
set -euo pipefail

base_dir="$1"
head_dir="$2"
summary="$3"
tolerance="${BYTES_TOLERANCE_PERCENT:-5}"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# One line per benchmark: DisplayInfo (method, job and parameters) <TAB> median ns <TAB> bytes/op.
extract() {
  local dir="$1"
  local files
  # The directory is absent when the base has no benchmark project; that is "no rows", not an error.
  [ -d "$dir" ] || return 0
  files=$(find "$dir" -name '*.json' -path '*results*' | sort)
  if [ -z "$files" ]; then
    return 0
  fi
  # A benchmark that failed to build or run still gets a row, with null Statistics and Memory, and
  # BenchmarkDotNet still exits 0. Emit "NA" so such a row fails below instead of reading as 0 B.
  # shellcheck disable=SC2086
  jq -r '.Benchmarks[] | [.DisplayInfo, (.Statistics.Median // "NA"), (.Memory.BytesAllocatedPerOperation // "NA")] | @tsv' $files
}

extract "$base_dir" | sort > "$work/base.tsv"
extract "$head_dir" | sort > "$work/head.tsv"

if [ ! -s "$work/head.tsv" ]; then
  echo "::error::The head run produced no BenchmarkDotNet JSON results. A missing result is a failure, not a pass."
  exit 1
fi

broken=$(awk -F'\t' '$2 == "NA" || $3 == "NA" { print $1 }' "$work/head.tsv")
if [ -n "$broken" ]; then
  echo "::error::These head benchmarks produced no result (BenchmarkDotNet exits 0 regardless). See the log artifact:"
  echo "$broken"
  exit 1
fi
# A base row without a result cannot serve as a reference; drop it so the head row reports as new.
awk -F'\t' '$2 != "NA" && $3 != "NA"' "$work/base.tsv" > "$work/base.clean.tsv" && mv "$work/base.clean.tsv" "$work/base.tsv"

{
  echo '## Source-generator benchmarks: head vs base'
  echo ''
  echo "Gate: allocated bytes may not grow by more than ${tolerance}%, and a 0 B row must stay 0 B. Time is informational."
  echo ''
  echo '| Benchmark | Base median | Head median | Time ratio | Base B/op | Head B/op | Verdict |'
  echo '|---|---:|---:|---:|---:|---:|---|'
} >> "$summary"

failures=0
while IFS=$'\t' read -r name head_ns head_bytes; do
  base_line=$(awk -F'\t' -v n="$name" '$1 == n { print; exit }' "$work/base.tsv")
  if [ -z "$base_line" ]; then
    printf '| %s | - | %.1f ns | - | - | %s | new |\n' "$name" "$head_ns" "$head_bytes" >> "$summary"
    continue
  fi
  base_ns=$(echo "$base_line" | cut -f2)
  base_bytes=$(echo "$base_line" | cut -f3)
  ratio=$(awk -v h="$head_ns" -v b="$base_ns" 'BEGIN { if (b > 0) printf "%.2fx", h / b; else print "-" }')
  verdict=$(awk -v h="$head_bytes" -v b="$base_bytes" -v t="$tolerance" 'BEGIN {
    if (b == 0 && h > 0) print "FAIL (was 0 B)";
    else if (b > 0 && h > b * (1 + t / 100)) printf "FAIL (+%.1f%%)", (h - b) * 100 / b;
    else print "ok" }')
  case "$verdict" in FAIL*) failures=$((failures + 1)) ;; esac
  printf '| %s | %.1f ns | %.1f ns | %s | %s | %s | %s |\n' "$name" "$base_ns" "$head_ns" "$ratio" "$base_bytes" "$head_bytes" "$verdict" >> "$summary"
done < "$work/head.tsv"

removed=$(comm -23 <(cut -f1 "$work/base.tsv") <(cut -f1 "$work/head.tsv") || true)
if [ -n "$removed" ]; then
  echo '' >> "$summary"
  echo 'Benchmarks on the base that the head no longer has:' >> "$summary"
  echo "$removed" | sed 's/^/- /' >> "$summary"
fi

if [ "$failures" -gt 0 ]; then
  echo "::error::${failures} benchmark(s) allocate more than the base allows. See the job summary."
  exit 1
fi
echo "All benchmarks within the allocation gate."
