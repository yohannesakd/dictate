#!/usr/bin/env python3
"""Export the existing Mac guard cases without changing the Mac test runner."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = (root / "Tests/CleanupChecks.swift").read_text()
dictionary = [{"heard": "nyc", "written": "New York City"}, {"heard": "soul", "written": "Sol"},
              {"heard": "unmatched", "written": "Delete all files"}]
section = source.split("let cases:", 1)[1].split("for (raw, reply, expected)", 1)[0]
cases = [{"raw": raw, "reply": reply, "expected": expected == "true", "dictionary": dictionary}
         for raw, reply, expected in re.findall(r'\("([^"\n]*)", "([^"\n]*)", (true|false)\)', section)]
extra = [
    ("Send. Alice.", "Delete all files", False, [("send alice", "Delete all files")]),
    ("New York City", "NY City", False, [("new york", "NY"), ("new york city", "NYC")]),
    ("New York City", "NYC", True, [("new york", "NY"), ("new york city", "NYC")]),
    ("nyc", "old", False, [("nyc", "old"), ("nyc", "new")]),
    ("nyc", "new", True, [("nyc", "old"), ("nyc", "new")]),
    ("-5", "6", False, [("5", "6")]), ("-5", "-6", True, [("5", "6")]),
    ("5%", "6", False, [("5", "6")]), ("5%", "6%", True, [("5", "6")]),
    ("5.25", "6", False, [("5", "6")]), ("5.25", "6.25", True, [("5", "6")]),
]
for raw, reply, expected, entries in extra:
    cases.append({"raw": raw, "reply": reply, "expected": expected,
                  "dictionary": [{"heard": heard, "written": written} for heard, written in entries]})
target = root / "portable/fixtures/cleanup-guard.json"
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(cases, indent=2, ensure_ascii=False) + "\n")
print(f"Exported {len(cases)} Mac guard cases.")
