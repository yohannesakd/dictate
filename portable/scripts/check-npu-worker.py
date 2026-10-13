#!/usr/bin/env python3
"""Verify the published OpenVINO worker with the public JFK reference."""
import array
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import time
import wave

executable, models, reference, output = map(Path, sys.argv[1:5])
device = sys.argv[5] if len(sys.argv) > 5 else "CPU"
environment = dict(os.environ)
search = "PATH" if os.name == "nt" else "LD_LIBRARY_PATH"
environment[search] = str(executable.parent.resolve()) + os.pathsep + environment.get(search, "")

with wave.open(str(reference)) as audio:
    assert audio.getnchannels() == 1 and audio.getframerate() == 16000 and audio.getsampwidth() == 2
    samples = array.array("h", audio.readframes(audio.getnframes()))
pcm = array.array("f", (sample / 32768 for sample in samples))

def start():
    process = subprocess.Popen([str(executable.resolve()), str(models.resolve()),
                               str((models / "ggml-base.en-q5_1.bin").resolve()), device],
                              stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                              stderr=subprocess.DEVNULL, env=environment)
    assert reply(process).get("available"), "The requested device is unavailable."
    process.stdin.write(b"\x01")
    process.stdin.flush()
    assert reply(process).get("ready"), "The encoder did not prepare."
    return process

def reply(process):
    while True:
        line = process.stdout.readline()
        assert line, "The worker stopped without a response."
        if line.startswith(b"DICTATE_NPU "):
            result = json.loads(line[len(b"DICTATE_NPU "):])
            assert "error" not in result, result
            return result

def send(process, samples):
    process.stdin.write(b"\x02" + struct.pack("<i", len(samples)) + samples.tobytes())
    process.stdin.flush()

rows = []
process = start()
try:
    for iteration in range(3):
        started = time.monotonic()
        send(process, pcm)
        text = reply(process)["text"]
        assert "country" in text.lower() and "fellow" in text.lower(), text
        rows.append({"text": text, "recognition_ms": (time.monotonic() - started) * 1000})
    send(process, pcm * min(20, 300 * 16000 // len(pcm)))
    time.sleep(0.1)
finally:
    process.kill()
    process.wait()

process = start()
try:
    send(process, pcm)
    assert "country" in reply(process)["text"].lower()
finally:
    process.kill()
    process.wait()

output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"requested_device": device, "results": rows,
                              "termination_and_restart": "passed"}, indent=2) + "\n")
print("OpenVINO encoding, recognition, worker termination, and restart passed on " + device + ".")
