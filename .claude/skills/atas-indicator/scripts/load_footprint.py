#!/usr/bin/env python3
"""Load a Footprint Exporter set (…_bars.csv, …_levels.csv, …_meta.txt) into pandas.

Usage (prints a sanity summary):
    python3 -I load_footprint.py <any of the three files, their common prefix, or a .zip>

As a module:
    from load_footprint import load
    bars, levels, meta = load(path)

bars   – one row per bar, index = bar number; columns time, last_time, open..close,
         volume, delta, max_delta, min_delta, levels, plus vpoc, vpoc_bid, vpoc_ask
levels – one row per (bar, price): bid, ask, volume, delta
meta   – dict from the _meta.txt file

Timestamps are exported exactly as ATAS gives them. ATAS candle times are
believed to be UTC, which is not verified. `guess_time_zone()` checks it from
the data: the largest volume jump of the day is the ES RTH open (09:30 New York).
"""
import io
import os
import sys
import zipfile

import pandas as pd


def _open_set(path):
    """Return (bars_text, levels_text, meta_text) for a prefix, one of the files, or a zip."""
    if path.lower().endswith(".zip"):
        with zipfile.ZipFile(path) as z:
            names = z.namelist()

            def pick(suffix):
                hits = [n for n in names if n.endswith(suffix)]
                return z.read(hits[0]).decode("utf-8") if hits else ""

            return pick("_bars.csv"), pick("_levels.csv"), pick("_meta.txt")

    prefix = path
    for suffix in ("_bars.csv", "_levels.csv", "_meta.txt"):
        if prefix.endswith(suffix):
            prefix = prefix[: -len(suffix)]

    def read(suffix):
        p = prefix + suffix
        return open(p, encoding="utf-8").read() if os.path.exists(p) else ""

    return read("_bars.csv"), read("_levels.csv"), read("_meta.txt")


def load(path):
    bars_txt, levels_txt, meta_txt = _open_set(path)
    if not bars_txt or not levels_txt:
        raise FileNotFoundError("need both …_bars.csv and …_levels.csv")

    bars = pd.read_csv(io.StringIO(bars_txt), sep=";")
    bars["time"] = pd.to_datetime(bars["time"])
    bars["last_time"] = pd.to_datetime(bars["last_time"], errors="coerce")
    bars = bars.drop_duplicates("bar", keep="last").set_index("bar").sort_index()

    levels = pd.read_csv(io.StringIO(levels_txt), sep=";")
    levels = levels.drop_duplicates(["bar", "price"], keep="last")
    levels["delta"] = levels["ask"] - levels["bid"]

    # VPOC per bar (highest volume price; ties -> first, i.e. the higher price)
    idx = levels.groupby("bar")["volume"].idxmax()
    vp = levels.loc[idx, ["bar", "price", "bid", "ask"]].set_index("bar")
    bars = bars.join(vp.rename(columns={"price": "vpoc", "bid": "vpoc_bid", "ask": "vpoc_ask"}))

    meta = {}
    for line in meta_txt.splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            meta[k.strip()] = v.strip()

    return bars, levels, meta


def guess_time_zone(bars):
    """Return the clock time (HH:MM) of the biggest average volume jump, i.e. the ES RTH open."""
    t = bars["time"]
    minute = t.dt.hour * 60 + t.dt.minute
    prof = bars.groupby(minute)["volume"].mean().reindex(range(1440)).fillna(0)
    # mean of the next 15 minutes vs the previous 15 – robust to single noisy minutes
    after = prof[::-1].rolling(15, min_periods=1).mean()[::-1]
    before = prof.shift(1).rolling(15, min_periods=1).mean()
    jump = (after - before).idxmax()
    hhmm = f"{int(jump) // 60:02d}:{int(jump) % 60:02d}"
    hints = {
        "14:30": "UTC (winter)",
        "13:30": "UTC (US summer time)",
        "15:30": "Prague / CET (winter)",
        "09:30": "New York",
    }
    return hhmm, hints.get(hhmm, "unknown – inspect the volume profile")


def summary(path):
    bars, levels, meta = load(path)
    print("meta:", {k: meta[k] for k in list(meta)[:8]})
    print(f"bars: {len(bars):,}  levels: {len(levels):,}  "
          f"range: {bars.time.min()} -> {bars.time.max()}")
    per_day = bars.groupby(bars.time.dt.date).size()
    print(f"days: {len(per_day)}  bars/day min {per_day.min()} max {per_day.max()}")
    print(f"levels per bar: median {bars['levels'].median():.0f}, max {bars['levels'].max()}")

    # consistency: footprint totals vs bar totals
    agg = levels.groupby("bar").agg(v=("volume", "sum"), d=("delta", "sum"))
    j = bars.join(agg)
    bad_v = (j["v"] != j["volume"]).sum()
    bad_d = (j["d"] != j["delta"]).sum()
    print(f"bars where level volume != bar volume: {bad_v}; level delta != bar delta: {bad_d}")

    hhmm, zone = guess_time_zone(bars)
    print(f"biggest volume jump at {hhmm} -> timestamps look like {zone}")
    return bars, levels, meta


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    summary(sys.argv[1])
