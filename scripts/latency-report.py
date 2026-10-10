#!/usr/bin/env python3
"""Turns Dictate's metrics log into a latency report and a CSV file.

Usage: scripts/latency-report.py [metrics.jsonl] [output directory]
Defaults: ~/Library/Logs/Dictate/metrics.jsonl, written next to it as latency.html and latency.csv.
Only rows with the timeline fields (at_*_ms) are used. Older rows are skipped.
"""
import csv
import html
import json
import os
import statistics
import sys

LOG = os.path.expanduser(sys.argv[1] if len(sys.argv) > 1 else "~/Library/Logs/Dictate/metrics.jsonl")
OUT = os.path.expanduser(sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(LOG))

# Stages after fn release, in order. Each is the time between two timeline marks.
AFTER_RELEASE = [
    ("Stop recording", "release", "recorder_stopped"),
    ("Speech recognition", "recorder_stopped", "transcript"),
    ("Rules and dictionary", "transcript", "rules_done"),
    ("Wait for the model", "rules_done", "text_ready"),
    ("Typing", "text_ready", "events_posted"),
    ("Check the field", "events_posted", "text_verified"),
]
# Stages before release. These do not add to the wait after release, but show slow starts.
BEFORE_RELEASE = [
    ("fn press to microphone ready", "press", "mic_ready"),
    ("Microphone ready to first audio", "mic_ready", "first_audio"),
    ("fn press to first speech", "press", "speech_start"),
    ("Last speech to fn release", "speech_end", "release"),
]
# The parts of one model request, from URLSession's measurements.
MODEL_PARTS = [
    ("Network round trip", "llm_round_trip_ms"),
    ("Model and server", "llm_server_ms"),
    ("Download", "llm_download_ms"),
]
COLORS = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300"]
DARK = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300"]


def mark(row, name):
    return 0 if name == "press" else row.get("at_%s_ms" % name)


def span(row, start, end):
    # Dictations without a model request have no model wait. A zero would pull the median down.
    if (start, end) == ("rules_done", "text_ready") and row.get("smart") in (None, "off", "not needed"):
        return None
    a, b = mark(row, start), mark(row, end)
    return None if a is None or b is None else max(0, b - a)


def load():
    rows = []
    with open(LOG) as f:
        for line in f:
            try:
                row = json.loads(line)
            except ValueError:
                continue
            if "at_release_ms" not in row:
                continue
            # Server time: the first response byte minus one network round trip.
            if row.get("llm_first_byte_ms") is not None and row.get("llm_round_trip_ms") is not None:
                row["llm_server_ms"] = max(0, row["llm_first_byte_ms"] - row["llm_round_trip_ms"])
            if mark(row, "llm_start") is not None and mark(row, "llm_end") is not None:
                row["llm_total_ms"] = mark(row, "llm_end") - mark(row, "llm_start")
                row["llm_head_start_ms"] = max(0, mark(row, "release") - mark(row, "llm_start"))
            end = mark(row, "text_verified") or mark(row, "events_posted")
            row["release_to_text_ms"] = None if end is None else end - mark(row, "release")
            rows.append(row)
    return rows


def percentile(values, p):
    values = sorted(v for v in values if v is not None)
    if not values:
        return None
    return values[min(len(values) - 1, int(round(p / 100 * (len(values) - 1))))]


def write_csv(rows):
    stage_columns = [label for label, _, _ in AFTER_RELEASE + BEFORE_RELEASE]
    columns = ["date", "audio_s", "chars", "hands_free", "smart", "delivery_outcome", "release_to_text_ms",
               "llm_total_ms", "llm_head_start_ms", "llm_round_trip_ms", "llm_server_ms", "llm_download_ms",
               "llm_input_tokens", "llm_output_tokens", "llm_reused_connection", "snapshots", "speculative_requests"]
    path = os.path.join(OUT, "latency.csv")
    with open(path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(columns + [label.lower().replace(" ", "_") + "_ms" for label in stage_columns])
        for row in rows:
            stages = [span(row, a, b) for _, a, b in AFTER_RELEASE + BEFORE_RELEASE]
            writer.writerow([row.get(c) for c in columns] + stages)
    return path


def stacked_bars(rows, parts, title, unit_note):
    """One horizontal stacked bar per row. parts: (label, function(row) -> ms or None)."""
    if not rows:
        return "<p class=muted>No data yet.</p>"
    width, label_w, bar_h, gap = 720, 120, 14, 6
    totals = [sum(fn(r) or 0 for _, fn in parts) for r in rows]
    scale = (width - label_w - 60) / max(max(totals), 1)
    height = len(rows) * (bar_h + gap) + 24
    out = ['<svg viewBox="0 0 %d %d" role="img" aria-label="%s">' % (width, height, html.escape(title))]
    for i, (row, total) in enumerate(zip(rows, totals)):
        y = i * (bar_h + gap)
        out.append('<text x="%d" y="%d" class="tick" text-anchor="end">%s</text>'
                   % (label_w - 8, y + bar_h - 3, html.escape(row.get("_label", ""))))
        x = label_w
        for slot, (label, fn) in enumerate(parts):
            value = fn(row) or 0
            w = value * scale
            if w >= 0.5:
                # A 2-pixel gap separates neighboring segments.
                out.append('<rect x="%.1f" y="%d" width="%.1f" height="%d" rx="2" class="s%d"><title>%s: %d ms</title></rect>'
                           % (x, y, max(0.5, w - 2), bar_h, slot, html.escape(label), value))
            x += w
        out.append('<text x="%.1f" y="%d" class="value">%d ms</text>' % (x + 4, y + bar_h - 3, total))
    out.append('<text x="%d" y="%d" class="tick">%s</text>' % (label_w, height - 4, html.escape(unit_note)))
    out.append("</svg>")
    legend = "".join('<span><i class="s%d"></i>%s</span>' % (i, html.escape(label)) for i, (label, _) in enumerate(parts))
    return '<div class=legend>%s</div>%s' % (legend, "".join(out))


def table(rows, items):
    lines = ["<table><thead><tr><th>Stage</th><th>Median</th><th>90th percentile</th><th>Maximum</th><th>Dictations</th></tr></thead><tbody>"]
    for label, fn in items:
        values = [fn(r) for r in rows]
        present = [v for v in values if v is not None]
        if not present:
            continue
        lines.append("<tr><td>%s</td><td>%d ms</td><td>%d ms</td><td>%d ms</td><td>%d</td></tr>"
                     % (html.escape(label), statistics.median(present), percentile(present, 90), max(present), len(present)))
    lines.append("</tbody></table>")
    return "".join(lines)


def main():
    rows = load()
    csv_path = write_csv(rows)
    recent = rows[-40:]
    for row in recent:
        row["_label"] = row.get("date", "")[5:16].replace("T", " ")

    after = [(label, (lambda a, b: lambda r: span(r, a, b))(a, b)) for label, a, b in AFTER_RELEASE]
    before = [(label, (lambda a, b: lambda r: span(r, a, b))(a, b)) for label, a, b in BEFORE_RELEASE]
    model_rows = [r for r in recent if r.get("llm_first_byte_ms") is not None]
    model = [(label, (lambda k: lambda r: r.get(k))(key)) for label, key in MODEL_PARTS]
    extra = [("fn release to text", lambda r: r.get("release_to_text_ms")),
             ("Model request, total", lambda r: r.get("llm_total_ms")),
             ("Model request started before release by", lambda r: r.get("llm_head_start_ms"))]

    style_light = "".join(".s%d{fill:%s}" % (i, c) for i, c in enumerate(COLORS))
    style_dark = "".join(".s%d{fill:%s}" % (i, c) for i, c in enumerate(DARK))
    page = """<!doctype html><meta charset=utf-8><title>Dictate latency</title>
<style>
body{font:14px -apple-system,system-ui,sans-serif;margin:32px auto;max-width:780px;padding:0 16px;background:#fcfcfb;color:#0b0b0b}
h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:32px 0 8px}.muted,.tick{color:#52514e;fill:#52514e;font-size:11px}
.value{fill:#0b0b0b;font-size:11px}svg{width:100%%;height:auto}
.legend{display:flex;flex-wrap:wrap;gap:12px;margin:0 0 8px;font-size:12px}.legend i{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:5px;vertical-align:-1px}
.legend i.s0{background:%s}.legend i.s1{background:%s}.legend i.s2{background:%s}.legend i.s3{background:%s}.legend i.s4{background:%s}.legend i.s5{background:%s}
table{border-collapse:collapse;width:100%%;font-variant-numeric:tabular-nums}td,th{text-align:right;padding:4px 8px;border-bottom:1px solid #e4e3df}td:first-child,th:first-child{text-align:left}
%s
@media (prefers-color-scheme:dark){body{background:#1a1a19;color:#fff}.muted,.tick{color:#c3c2b7;fill:#c3c2b7}.value{fill:#fff}td,th{border-color:#383835}%s
.legend i.s0{background:%s}.legend i.s1{background:%s}.legend i.s2{background:%s}.legend i.s3{background:%s}.legend i.s4{background:%s}.legend i.s5{background:%s}}
</style>
<h1>Dictate latency</h1>
<p class=muted>%d dictations with a timeline. The charts show the last %d. Hover a segment for its time. Data: %s</p>
<h2>After fn release, by stage</h2>%s
<h2>Model requests</h2><p class=muted>The round trip is the TCP connection time to the provider. Model and server time is the time to the first response byte minus that round trip.</p>%s
<h2>All stages</h2>%s
""" % (tuple(COLORS) + (style_light, style_dark) + tuple(DARK) + (
        len(rows), len(recent), html.escape(csv_path),
        stacked_bars(recent, after, "Time after fn release, by stage", "Each bar is one dictation, oldest at the top."),
        stacked_bars(model_rows, model, "Model request time, by part", "Each bar is one model request."),
        table(rows, after + extra + before + model)))
    path = os.path.join(OUT, "latency.html")
    with open(path, "w") as f:
        f.write(page)
    print(path)
    print(csv_path)


if __name__ == "__main__":
    main()
