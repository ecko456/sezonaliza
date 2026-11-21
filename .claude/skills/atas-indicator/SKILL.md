---
name: atas-indicator
description: Write, fix or extend custom C# indicators for the ATAS / ATAS X order-flow trading platform, tuned for ES (E-mini S&P 500) and MES futures. Use this skill whenever the user wants an ATAS indicator, an order-flow/footprint/delta/volume/CVD study, a colored strip, levels, markers or any custom drawing on an ATAS chart, or mentions ATAS, ATAS X, OnCalculate/OnRender, RenderContext, IndicatorCandle — even if they only describe the visual ("barevný pruh pod cenou", "zvýrazni absorpci na ES") without naming ATAS. Also use it to port an indicator from TradingView/Pine, NinjaTrader or Sierra Chart into ATAS.
---

# ATAS / ATAS X indicator (ES futures)

The user trades **ES futures only** (CME E-mini S&P 500; sometimes MES). Write indicators with that market in mind: order-flow first (delta, bid/ask volume), intraday timeframes, RTH vs. overnight sessions. The user writes in Czech. Write the code, comments and setting names in English and the README and chat replies in Czech.

## Workflow

1. **Pin down the spec** from the request. Decide these, ask only when it's genuinely ambiguous:
   - *What is computed*: the formula, the inputs (candle fields), the lookback and smoothing.
   - *Where it draws*: an overlay on the price panel, or its own panel.
   - *How it draws*: built-in `ValueDataSeries` (lines or histograms, cheap, shows in the data window) or custom `OnRender` (strips, boxes, text, gradients).
   - *Session behaviour on ES*: should the normalisation/accumulation reset at the RTH open or at the Globex open? See `references/es-futures.md`.
2. **Start from the template**: copy `assets/template/Indicator.cs` and `Indicator.csproj`. The csproj targets net10.0 and references System.Drawing.Common. Keep both: they were confirmed against the user's real ATAS X install, into `atas/<IndicatorName>/` in the repo and rename. For a worked example with custom rendering, gradients, settings groups and labels, read `assets/examples/DeltaStrengthBar.cs`.
3. **Implement** following the rules below and `references/api.md`.
4. **Compile-check**: run `scripts/check_compile.sh atas/<Name>/<Name>.cs`. It builds against API stubs and runs a small smoke test. If `dotnet` is missing, install it first (`apt-get install -y dotnet-sdk-8.0`).
5. **Deliver**:
   - `<Name>.cs`, `<Name>.csproj` and a short Czech `README.md` (what it shows, the settings, build and install steps).
   - Add an `atas/.gitignore` with `bin/` and `obj/` if it doesn't exist yet.
   - In the reply, list honestly which API calls were only stub-checked, so the user knows what to report back if the real build fails.

## Rules that prevent the usual bugs

**`OnCalculate(bar, value)` runs many times for the live bar.** Every tick re-invokes it for the last bar. Any state must therefore be indexed by bar and derived from `bar - 1` values, never accumulated in a plain field like `_sum += delta`. That would double-count on every tick. Reset per-bar storage when `bar == 0`, because ATAS calls a full recalculation starting at bar 0 after settings change or data reload.

**Settings setters decide what happens next.** If a parameter changes the math, call `RecalculateValues()`. If it is visual only (colors, sizes, positions), call `RedrawChart()`. Use backing fields with defaults and don't assign properties in the constructor, so these calls don't fire before the indicator is attached.

**Setting attributes**: use `[Display(Name = "...", GroupName = "1. Calculation", Order = 10, Description = "...")]` and `[Range(min, max)]` from `System.ComponentModel.DataAnnotations`. Numbered group names keep the groups ordered in the dialog. Enums show their `[Display(Name)]` in drop-downs.

**Colors are `System.Drawing.Color`.** That's what ATAS X community indicators use and it works cross-platform. Avoid WPF types (`System.Windows.Media.*`): ATAS X also runs on macOS.

**Numbers**: candle values are `decimal`. Cast to `double` only for `Math.Sqrt`, `Math.Pow` or `Math.Log`, then cast back. Guard against division by zero, because ES does have zero-volume bars in thin overnight periods and on some chart types.

**Rendering (`OnRender`) must be cheap.** It runs on every repaint.
- Return early unless the layout matches what you subscribed to, and also when `ChartInfo`, `ChartInfo.PriceChartContainer` or `Container` is null.
- Loop only over `FirstVisibleBarNumber..LastVisibleBarNumber`, clamped to the computed range.
- Create fonts and pens once (as fields, or before the loop), not per bar. No LINQ and no string formatting unless a label is actually drawn.
- For contiguous per-bar cells, take the width from `GetXByBar(bar + 1) - GetXByBar(bar)` instead of `BarsWidth`. That stays correct at any zoom.
- Pixel heights from settings don't scale with the price axis. Use `ChartInfo.GetYByPrice(price, false)` for anything tied to a price level.

**Overlay vs. own panel**:
- Price-panel overlay: `base(true)`, then `Panel = IndicatorDataProvider.CandlesPanel; DenyToChangePanel = true;`
- Own panel: `Panel = IndicatorDataProvider.NewPanel;`
- If you draw everything yourself, hide `DataSeries[0]` (`IsHidden = true` and `VisualType = VisualMode.Hide`). Otherwise a stray zero line appears.

**Relative volume: use the median, not the mean.** One huge bar (RTH open, news, a capitulation wick) inflates a mean for the whole window. The following normal bars then look "thin" and trigger false low-volume signals. This happened in the Delta Strength Bar preview.

**Readability**: the user finds dark or muddy colors hard to read.
- Prefer saturated colors on a dark-gray neutral.
- Use discrete intensity steps rather than long gradients.
- Use a glyph per signal type (▲▼ A E •), with color for direction and glyph for type.
- Text contrast is picked automatically from the cell luminance.
- Draw triangles line by line with `DrawLine`; that works on every renderer.
- Preview renders: the stub `RenderContext` records draw ops (`Ops`), and `ChartInfo.XByBar`/`YByPrice` are settable. Render the ops to a PNG with PIL to check the look before shipping (see `atas/DeltaStrengthBar/preview.png`).

**Lookback loops**: an O(lookback) loop per bar is fine up to a few hundred bars. For larger windows, or several of them, keep rolling sums indexed by bar.

**Use instrument data, not hardcoded ES constants.** Use `InstrumentInfo.TickSize` rather than `0.25`, so the indicator also works on MES and on other tick-size settings.

## ES-specific defaults (details in `references/es-futures.md`)

- Volume and delta differ by an order of magnitude between the RTH (15:30–22:00 CET) and overnight sessions. Any normalisation over a lookback window (max |delta|, z-score, average volume) is distorted around the RTH open. Offer a *session-aware* option: reset or separate the statistics at the session start (`IsNewSession(bar)`).
- Ratios like `Delta / Volume` are session-independent and make the safest default.
- Typical "strong" thresholds on 1–5 min ES bars: |delta|/volume ≥ 0.3–0.4. Treat these as starting points to expose as settings, not truths.

## Working with the user's real data

The user can export footprint history from ATAS with **Footprint Exporter** (`atas/FootprintExporter`, also `assets/examples/FootprintExporter.cs`). It produces three files:
- `…_bars.csv` with `bar;time;last_time;open;high;low;close;volume;delta;max_delta;min_delta;levels`
- `…_levels.csv` with `bar;price;bid;ask;volume`, one row per traded price
- `…_meta.txt`

When they upload them (usually zipped):
1. Run `python3 -I scripts/load_footprint.py <zip or file>`. It prints counts, checks that the level sums match the bar totals, and **detects the time zone** from the RTH-open volume jump (14:30 = UTC winter, 15:30 = CET).
2. Import `load()` for analysis. It gives `bars` with VPOC columns, `levels` with per-level delta, and `meta`.
3. Replay the indicator logic in Python, or feed the bars into the C# stub harness (`StubRun`) to evaluate signals. Measure forward returns at 5/15/30 min against a same-period baseline, split RTH and overnight, and report the sample size honestly.

Lessons from the first real export (11 sessions, Nov 2026, see `atas/analysis/README.md`):
- ATAS candle `Time` is **UTC** (RTH open 14:30, MOC spike 20:59, break 22:00–23:00).
- A fixed delta/volume threshold is meaningless across sessions: the RTH 99th percentile is about 0.20, while in ETH 0.20 is the 75th percentile. Use thresholds relative to RTH/ETH separately.
- The VPOC + delta wick filter removed the worst counter-trend hammers. No signal family had a proven edge on 11 sessions, so ask for more data before claiming one.
- **1M export is enough for any higher timeframe.** `scripts/resample_footprint.py` rebuilds N-minute footprint bars. 5M rebuilt from 1M matched ATAS's native 5M export on 100 % of indicator signals.
- RTH-only vs full-session data: export from the chart the user actually trades. Indicators with lookbacks (median volume, N-bar high/low) differ in the first ~30 min of RTH without the overnight bars. Wick Absorption has no lookback and is unaffected.
- `pd.read_csv` turns the string "None" into NaN. Read signal files with `keep_default_na=False`.
- Reusable pipeline: `atas/analysis/Replay.cs` runs the real indicator code on exported data, and `evaluate.py` measures forward returns against a same-session random-entry baseline.

A chart OHLC export (`Chart.csv`, `yyyy-dd-MM HH:mm:ss;O;H;L;C`, Prague time) has **no volume**. Only price structure can be studied from it.

## References

- `references/api.md`: ATAS API cheat sheet with a confidence level per item. Read it when unsure about a signature.
- `references/es-futures.md`: ES contract specs, sessions in CET, roll calendar, order-flow notes.
- `assets/examples/DeltaStrengthBar.cs`: complete custom-render indicator (3-point gradient strip from delta strength).
- `assets/examples/DeltaStrengthBar.cs` also contains a two-lane signal strip: ignition after a wick setup, absorption, exhaustion, median relative volume.
- `assets/examples/WickAbsorption.cs`: footprint-based reversal detector (wick, VPOC in wick, wick delta: absorption vs. initiative). Use it as the pattern for anything that works per price level.

## The user's trading style (keep in mind when proposing ideas)

The user looks for **reversal situations in order flow**: a wick forms, the bar's VPOC sits in the wick, and the delta inside the wick is strongly one-sided. That means absorption (delta against the rejection) or initiative/"pohlcení" (delta with the rejection). When they say "gamma" in this context they most likely mean delta in the wick, not options gamma. Confirm if it matters.

They read the footprint **at the VPOC level**, e.g. `483 x 157` on the VPOC means sellers. Don't judge such setups only on the aggregate delta of a whole wick or bar. Mixed levels around the VPOC dilute it: on their real example the whole-wick delta was 2.7 % while the VPOC level was 51 %. Default to measuring at the VPOC level and offer zone or whole-wick as options.
- `scripts/check_compile.sh`: stub compile and smoke test.
