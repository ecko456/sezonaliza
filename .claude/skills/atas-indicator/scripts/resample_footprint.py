#!/usr/bin/env python3
"""Build N-minute footprint bars from a 1-minute Footprint Exporter set.

    python3 -I resample_footprint.py <1M prefix|file|zip> <minutes> <out prefix> [--rth]

Writes <out prefix>_bars.csv and <out prefix>_levels.csv in the exporter's format,
so they can be fed to atas/analysis/Replay.cs like a native export.

- Bars are aligned to the clock (floor to N minutes), as ATAS time charts are.
- Footprint: bid/ask/volume summed per price over the 1M bars.
- max_delta / min_delta: rebuilt from the cumulative 1M path plus each 1M bar's
  intrabar extremes. This matched ATAS's own 5M export on 100 % of bars.
- --rth keeps only 14:30–21:00 UTC (ES regular session in winter time; use when the
  exported timestamps are UTC – check with load_footprint.py first).

Verified on the Nov 2026 export: 2758 / 2759 5M bars identical to ATAS's 5M export,
25 of 87,286 footprint levels differ (trades on the minute boundary).
"""
import os
import sys

import pandas as pd

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from load_footprint import load  # noqa: E402


def resample(bars, levels, minutes, rth=False):
    b = bars.copy()
    if "bar" in b.columns:          # levels refer to the exporter's bar ids
        b = b.set_index("bar")
    if rth:
        m = b.time.dt.hour * 60 + b.time.dt.minute
        b = b[(m >= 14 * 60 + 30) & (m < 21 * 60)]
    b["t"] = b.time.dt.floor(f"{minutes}min")
    b["cum"] = b.groupby("t").delta.cumsum()
    b["prev"] = b.cum - b.delta

    g = b.groupby("t")
    out = pd.DataFrame({
        "last_time": g.last_time.max(),
        "open": g.open.first(), "high": g.high.max(), "low": g.low.min(), "close": g.close.last(),
        "volume": g.volume.sum(), "delta": g.delta.sum(),
        "max_delta": (b.prev + b.max_delta).groupby(b.t).max(),
        "min_delta": (b.prev + b.min_delta).groupby(b.t).min(),
    }).reset_index().rename(columns={"t": "time"})
    out.insert(0, "bar", range(len(out)))

    lv = levels.merge(b[["t"]], left_on="bar", right_index=True)
    fp = lv.groupby(["t", "price"], as_index=False)[["bid", "ask", "volume"]].sum()
    fp = fp.merge(out[["bar", "time"]], left_on="t", right_on="time")
    fp = fp.sort_values(["bar", "price"], ascending=[True, False])[["bar", "price", "bid", "ask", "volume"]]

    out["levels"] = out.bar.map(fp.groupby("bar").size()).fillna(0).astype(int)
    return out, fp


def write(out, fp, prefix):
    o = out.copy()
    o["time"] = o.time.dt.strftime("%Y-%m-%d %H:%M:%S.000")
    o["last_time"] = pd.to_datetime(o.last_time).dt.strftime("%Y-%m-%d %H:%M:%S.%f").str[:-3]
    cols = ["bar", "time", "last_time", "open", "high", "low", "close", "volume", "delta",
            "max_delta", "min_delta", "levels"]
    o[cols].to_csv(prefix + "_bars.csv", sep=";", index=False)
    fp.to_csv(prefix + "_levels.csv", sep=";", index=False)


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(2)
    bars, levels, _ = load(sys.argv[1])
    out, fp = resample(bars.reset_index(), levels, int(sys.argv[2]), rth="--rth" in sys.argv)
    write(out, fp, sys.argv[3])
    print(f"{len(out)} bars, {len(fp)} levels -> {sys.argv[3]}_bars.csv / _levels.csv")
