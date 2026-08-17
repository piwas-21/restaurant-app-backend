#!/usr/bin/env python3
"""Merge Cobertura reports from the sharded integration-test jobs and enforce the coverage floors.

Why this exists: the integration suite runs as a matrix of shards (see .github/workflows/ci.yml),
so each shard's coverlet report only sees the part of the code ITS tests exercised. Enforcing
coverlet's own -p:Threshold inside a shard would compare a floor for the whole suite against a
fraction of it. The floors therefore move here, onto the union of every shard's report.

Merge semantics (validated against coverlet itself, see below):
  line    - a line is covered if ANY shard hit it; the denominator is coverlet's own lines-valid.
  branch  - per (file, class, line) take the highest covered-condition count any shard reported.
            The denominator is coverlet's own branches-valid attribute, which is 50 branches
            LARGER than what the per-line condition-coverage strings add up to; using the
            attribute keeps the percentage identical to what coverlet would print, instead of
            flattering it by ~1.6% relative with a smaller denominator.
  method  - a method is covered if any of its lines was hit in any shard.

Validated 2026-08-17 on three real runs of this suite: coverlet's own totals for a run of set A,
of set B, and of A+B in one process were 7.74/1.11/1.60, 7.73/1.39/1.72 and 7.78/1.58/1.74
(line/branch/method %). This script fed A and B reports reproduces the A+B run exactly:
7.78/1.58/1.74. Merging is therefore neither optimistic nor lossy at the totals level.

The `max` on branches is conservative (two shards covering DIFFERENT conditions on one line report
as one, not two), so the merged branch number can only UNDER-state reality - it can never let a
regression through.

Usage:
  python3 scripts/merge-coverage.py --line 27 --branch 18 --method 32 report1.xml report2.xml ...
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET

CONDITION_COUNTS = re.compile(r"\((\d+)/(\d+)\)")


def parse(path):
    """-> (line hits by (file, line), branch (covered, total) by (file, class, line),
    method covered-flag by (file, class, signature), coverlet's own totals)."""
    root = ET.parse(path).getroot()
    lines, branches, methods = {}, {}, {}
    for cls in root.findall("packages/package/classes/class"):
        filename, class_name = cls.get("filename"), cls.get("name")
        for line in cls.findall("lines/line"):
            number, hits = int(line.get("number")), int(line.get("hits"))
            key = (filename, number)
            lines[key] = max(lines.get(key, 0), hits)
            counts = CONDITION_COUNTS.search(line.get("condition-coverage") or "")
            if line.get("branch") == "True" and counts:
                covered, total = int(counts.group(1)), int(counts.group(2))
                key = (filename, class_name, number)
                was_covered, was_total = branches.get(key, (0, 0))
                branches[key] = (max(was_covered, covered), max(was_total, total))
        for method in cls.findall("methods/method"):
            key = (filename, class_name, method.get("name") + method.get("signature"))
            covered = any(int(line.get("hits")) > 0 for line in method.findall("lines/line"))
            methods[key] = methods.get(key, False) or covered
    totals = {k: int(root.get(k, 0)) for k in ("lines-valid", "branches-valid")}
    return lines, branches, methods, totals


def merge(paths):
    lines, branches, methods = {}, {}, {}
    totals = {"lines-valid": 0, "branches-valid": 0}
    for path in paths:
        shard_lines, shard_branches, shard_methods, shard_totals = parse(path)
        for key, hits in shard_lines.items():
            lines[key] = max(lines.get(key, 0), hits)
        for key, (covered, total) in shard_branches.items():
            was_covered, was_total = branches.get(key, (0, 0))
            branches[key] = (max(was_covered, covered), max(was_total, total))
        for key, covered in shard_methods.items():
            methods[key] = methods.get(key, False) or covered
        for key in totals:
            totals[key] = max(totals[key], shard_totals.get(key, 0))
    return lines, branches, methods, totals


def percentage(covered, valid):
    return 100.0 * covered / valid if valid else 100.0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--line", type=float, required=True, help="line coverage floor, percent")
    parser.add_argument("--branch", type=float, required=True, help="branch coverage floor, percent")
    parser.add_argument("--method", type=float, required=True, help="method coverage floor, percent")
    parser.add_argument("reports", nargs="+", help="Cobertura XML files, one per shard")
    args = parser.parse_args()

    lines, branches, methods, totals = merge(args.reports)

    lines_valid = max(len(lines), totals["lines-valid"])
    lines_covered = sum(1 for hits in lines.values() if hits > 0)
    branches_valid = max(sum(total for _, total in branches.values()), totals["branches-valid"])
    branches_covered = sum(covered for covered, _ in branches.values())
    methods_valid = len(methods)
    methods_covered = sum(1 for covered in methods.values() if covered)

    measured = {
        "line": (percentage(lines_covered, lines_valid), lines_covered, lines_valid, args.line),
        "branch": (
            percentage(branches_covered, branches_valid),
            branches_covered,
            branches_valid,
            args.branch,
        ),
        "method": (
            percentage(methods_covered, methods_valid),
            methods_covered,
            methods_valid,
            args.method,
        ),
    }

    print(f"merged {len(args.reports)} shard coverage report(s)")
    failed = []
    for name, (value, covered, valid, floor) in measured.items():
        verdict = "OK" if value >= floor else "BELOW FLOOR"
        print(f"  {name:7} {value:6.2f}%  ({covered}/{valid})  floor {floor}%  {verdict}")
        if value < floor:
            failed.append(name)

    if failed:
        print(f"::error::merged coverage below floor: {', '.join(failed)}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
