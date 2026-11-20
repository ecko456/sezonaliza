# ES / MES futures – what an indicator needs to know

## Contract specs (CME)

| | ES (E-mini S&P 500) | MES (Micro E-mini) |
|---|---|---|
| Tick size | 0.25 index points | 0.25 |
| Tick value | $12.50 | $1.25 |
| Point value | $50 | $5 |
| Contract months | Mar (H), Jun (M), Sep (U), Dec (Z) | same |
| Expiry | 3rd Friday of the contract month (09:30 ET open) | same |

Read the tick size from `InstrumentInfo.TickSize` rather than hardcoding it. To express a value in dollars, compute `ticks * tickValue` and expose tick value as a setting (default 12.5 for ES).

## Sessions (shown in CET/CEST, the user is in the Czech Republic)

| Session | US Eastern | Prague (normal) |
|---|---|---|
| Globex open (new trading day) | 18:00 ET (Sun–Thu) | 00:00 |
| RTH (cash session) | 09:30–16:00 ET | 15:30–22:00 |
| Close / daily maintenance break | 17:00–18:00 ET | 23:00–00:00 |

- **DST mismatch**: for about 2–3 weeks in March (the US switches first) and about 1 week around late October/early November (Europe switches first), everything is one hour earlier in Prague, e.g. RTH runs 14:30–21:00. Never hardcode Prague times. Use ET times converted with `TimeZoneInfo`, or ATAS's session settings and `IsNewSession`.
- Candle `Time` in ATAS is UTC 🟡. Convert with `TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")` on Windows, or `"America/New_York"` on macOS/Linux. Try both and fall back to the other.

## Order-flow behaviour that matters for indicators

- **RTH vs. overnight**: RTH volume per bar is many times the overnight volume. The first 5–15 minutes after 15:30 CET and the 22:00 close carry extreme volume and delta.
  - Lookback normalisations (max |delta|, z-score, average volume, percentile) that span the session boundary will saturate at the RTH open and look dead overnight.
  - Fix it with session-aware statistics: reset at `IsNewSession`, or keep separate RTH and ETH baselines.
- **Ratios travel well**: `Delta/Volume` and `Ask/(Ask+Bid)` stay comparable across sessions. Prefer them as defaults.
- **News spikes**: 14:30 CET (CPI, NFP, …) and 20:00 CET on FOMC days. Settings that cap or winsorise outliers (clamp z-scores, saturation) keep the colors readable.
- **Roll**: liquidity moves to the next quarter's contract about 8 days before expiry, around the 2nd Thursday of Mar/Jun/Sep/Dec. Indicators with long lookbacks see a gap or volume shift across the roll on non-continuous charts.
- **Big lots**: ES trades in 1-lot increments, and iceberg or large-participant activity shows up as large single prints. A "big trade" filter threshold is typically 20–100+ contracts on ES and much lower on MES. Expose it as a setting.

## Sensible defaults for ES intraday (1–5 min, or 500–2000 volume/range charts)

| Concept | Default to expose |
|---|---|
| Strong one-sided bar | \|Delta\|/Volume ≥ 0.35 |
| Lookback for relative stats | 50–100 bars, session-aware |
| Z-score saturation | 2.0 σ |
| EMA smoothing of delta strength | 1 (off) for scalping, 3–5 for context |
