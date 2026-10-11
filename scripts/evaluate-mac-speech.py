#!/usr/bin/env python3
"""Evaluate the app's real Engine without changing preferences, history, or cleanup providers."""
import hashlib
import json
import os
from pathlib import Path
import platform
import signal
import subprocess
import sys

app, fixtures, destination = map(Path, sys.argv[1:])
samples = json.loads((fixtures / "samples.json").read_text()) + json.loads((fixtures / "german-samples.json").read_text())
rows = []
result = {"platform": platform.platform(), "logicalCores": os.cpu_count(), "passes": 5,
          "samples": samples, "measurements": rows,
          "timing": "Native app Engine callback time includes helper startup and model loading. Field delivery and cleanup are excluded."}
for sample in samples:
    assert hashlib.sha256((fixtures / (sample["name"] + ".wav")).read_bytes()).hexdigest() == sample["sha256"]

def save():
    destination.write_text(json.dumps(result, indent=2) + "\n")

def run(engine, sample, extra=()):
    command = [str(app / "Contents/MacOS/Dictate"), "--benchmark", engine,
               str(fixtures / (sample["name"] + ".wav")), *extra]
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=True)
    try:
        stdout, stderr = process.communicate(timeout=360)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        stdout, stderr = process.communicate()
        return {"error": "Native evaluation exceeded six minutes.", "stderr": stderr[-400:]}
    try:
        row = json.loads(stdout.strip().splitlines()[-1])
    except (ValueError, IndexError):
        row = {"error": "Native evaluation returned no JSON.", "stderr": stderr[-400:]}
    row["exitCode"] = process.returncode
    return row

for pass_index in range(5):
    for sample in samples:
        engines = ["whistle", "whisper"] if pass_index % 2 == 0 else ["whisper", "whistle"]
        for engine in engines:
            row = run(engine, sample)
            row.update(sample=sample["name"], engine=engine, passNumber=pass_index + 1)
            rows.append(row)
            save()
            print(sample["name"], engine, pass_index + 1, row.get("wall_ms"), row.get("error", row.get("text")), flush=True)
result["phononRegression"] = [dict(run("phonon", sample), sample=sample["name"]) for sample in samples if sample["language"] == "en"]
long_sample = next(sample for sample in samples if sample["name"] == "dictate-long")
result["cancellation"] = {engine: run(engine, long_sample, ["--cancel-after-ms", "100"])
                          for engine in ["whistle", "whisper", "phonon"]}
save()
if any(row.get("error") or row.get("exitCode") != 0 for row in rows):
    sys.exit(1)
if any(row.get("error") or row.get("cancelled_callback") or row.get("cancel_missed")
       for engine, row in result["cancellation"].items() if engine != "phonon"):
    sys.exit(1)
print("Native Mac evaluation and cancellation completed.", flush=True)
