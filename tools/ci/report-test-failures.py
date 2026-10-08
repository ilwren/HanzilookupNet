#!/usr/bin/env python3
"""Mirror failing xUnit tests into check-run annotations.

The GitHub Actions test log lives on blob storage, which is not reachable from every
environment (an agent sandbox, for instance).  Check-run annotations, on the other hand,
are readable through the REST API, so this helper converts a failed test run into a
handful of ``::error`` workflow commands that GitHub turns into annotations.

Usage:  python3 tools/ci/report-test-failures.py [results-directory]
"""

from __future__ import annotations

import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
MAX_TESTS = 24
MAX_STACKS = 6
MAX_MESSAGE = 900


def escape(text: str) -> str:
    """Escape a workflow-command argument (see GitHub's workflow command syntax)."""
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def collect(results_directory: str):
    failures = []
    stacks = []
    totals = {"total": 0, "Passed": 0, "Failed": 0, "NotExecuted": 0, "Skipped": 0, "other": 0}
    for path in sorted(glob.glob(os.path.join(results_directory, "**", "*.trx"), recursive=True)):
        try:
            root = ET.parse(path).getroot()
        except Exception as error:  # pragma: no cover - diagnostics only
            print(f"::error::could not parse {path}: {escape(str(error))}")
            continue
        for result in root.iter(NS + "UnitTestResult"):
            outcome = result.get("outcome") or "other"
            totals["total"] += 1
            totals[outcome if outcome in totals else "other"] = (
                (totals[outcome] if outcome in totals else totals["other"]) + 1
            )
            if outcome != "Failed":
                continue
            name = result.get("testName") or "<unknown>"
            message = result.findtext(f"{NS}Output/{NS}ErrorInfo/{NS}Message") or ""
            message = re.sub(r"\s+", " ", message).strip()
            failures.append((name, message[:MAX_MESSAGE]))
            trace = result.findtext(f"{NS}Output/{NS}ErrorInfo/{NS}StackTrace") or ""
            if trace:
                lines = [line.strip() for line in trace.splitlines() if line.strip()]
                stacks.append((name, " | ".join(lines[:6])[:MAX_MESSAGE]))
    return failures, stacks, totals


def main() -> int:
    results_directory = sys.argv[1] if len(sys.argv) > 1 else "TestResults"
    failures, stacks, totals = collect(results_directory)

    if totals["total"]:
        breakdown = ", ".join(
            f"{count} {name.lower()}"
            for name, count in totals.items()
            if name != "total" and count
        )
        print(f"::notice::{totals['total']} tests: {breakdown}")

    if not failures:
        if os.environ.get("JOB_STATUS", "success") not in ("success", ""):
            print(
                "::error::the test step failed but no failing test was recorded in "
                f"{results_directory}"
            )
        return 0

    # One compact annotation with every failing test name first: annotation counts can be
    # capped, and the names alone are usually enough to work out what went wrong.
    names = ", ".join(name.split(".")[-1] for name, _ in failures)
    print(f"::error title={len(failures)} failing test(s)::{escape(names[:900])}")

    for name, message in failures[:MAX_TESTS]:
        print(f"::error title={escape(name)}::{escape(message)}")
    for name, trace in stacks[:MAX_STACKS]:
        print(f"::error title=stack {escape(name)}::{escape(trace)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
