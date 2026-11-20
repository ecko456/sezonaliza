# ATAS / ATAS X indicator API – cheat sheet

The official docs live at docs.atas.net (Indicator class, Graphics, IndicatorExamples). They were not reachable from the cloud sandbox when this was written, so this sheet comes from public ATAS X indicators on GitHub (2025–2026, .NET 10) and from widespread usage.

Confidence levels:
- ✅ seen verbatim in ATAS X community code
- 🟡 widely used, signature from memory
- ❓ uncertain, so verify against the user's ATAS build or the docs

## Project / build

| Item | Value | |
|---|---|---|
| Target framework | `net10.0` (ATAS X). Older ATAS builds use `net8.0` | ✅ |
| References (Private=false) | `ATAS.Indicators.dll`, `ATAS.DataFeedsCore.dll`, `OFT.Rendering.dll`, `OFT.Attributes.dll`, `Utils.Common.dll` | ✅ |
| DLL folder, Windows | `C:\Program Files (x86)\ATAS Platform\` | ✅ |
| DLL folder, macOS | `/Applications/ATAS X.app/Contents/MonoBundle/` | ✅ |
| Install DLL, Windows | `%APPDATA%\ATAS\Indicators\` | ✅ |
| Install DLL, macOS | `~/Library/Application Support/ATAS/Indicators/` | ✅ |

## Usings

```csharp
using System.ComponentModel;                 // DisplayName, Category
using System.ComponentModel.DataAnnotations; // Display, Range
using System.Drawing;                        // Color, Rectangle, Size
using ATAS.Indicators;                       // Indicator, IndicatorCandle, ValueDataSeries, DrawingLayouts
using OFT.Rendering.Context;                 // RenderContext
using OFT.Rendering.Tools;                   // RenderFont, RenderPen
```
✅ All of these appear together in community code.

## Indicator skeleton

```csharp
[DisplayName("My Indicator")]          // 🟡 name in the indicator list
[Category("Order Flow")]               // 🟡
public class MyIndicator : Indicator
{
    public MyIndicator() : base(true)  // ✅ true = indicator uses candle data
    {
        Panel = IndicatorDataProvider.CandlesPanel; // 🟡 overlay; NewPanel = own panel
        DenyToChangePanel = true;                   // ✅
        EnableCustomDrawing = true;                 // ✅
        SubscribeToDrawingEvents(DrawingLayouts.Final);  // ✅ also: LatestBar | Final
        DataSeries[0].IsHidden = true;                   // ✅
        ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide; // ✅
    }

    protected override void OnCalculate(int bar, decimal value) { }               // ✅
    protected override void OnRender(RenderContext context, DrawingLayouts layout) { } // ✅
}
```

Series ✅:
```csharp
private readonly ValueDataSeries _line = new("Line") { Color = Color.DodgerBlue, VisualType = VisualMode.Line };
DataSeries[0] = _line;     // replace the default series
DataSeries.Add(other);     // add more
_line[bar] = 1.23m;        // write value
```
🟡 The constructor `ValueDataSeries(string name)` exists in older builds. Newer builds may prefer `(string id, string name)`. If the build complains, switch to the overload it offers.

`VisualMode` values 🟡: `Line`, `Histogram`, `Dots`, `Block`, `Hide`, `UpArrow`, `DownArrow`, `Square`.

## Indicator members

| Member | | Notes |
|---|---|---|
| `GetCandle(int bar)` → `IndicatorCandle` | ✅ | |
| `CurrentBar` | 🟡 | count of bars (last index = `CurrentBar - 1`) |
| `FirstVisibleBarNumber`, `LastVisibleBarNumber` | 🟡 | |
| `RecalculateValues()` | 🟡 | full recalculation from bar 0 |
| `RedrawChart()` | 🟡 | repaint only |
| `IsNewSession(int bar)` | 🟡 | true on the first bar of a trading session (per chart session settings) |
| `InstrumentInfo.TickSize` | 🟡 | 0.25 for ES / MES |
| `ChartInfo`, `Container` | ✅ | |
| `AddAlert(soundFile, instrument, message, background, foreground)` | ❓ | background/foreground may be `CrossColor` in ATAS X |

## IndicatorCandle

✅/🟡: `Open`, `High`, `Low`, `Close`, `Volume`, `Delta`, `Ask` (buy volume at ask), `Bid` (sell volume at bid), `MaxDelta`, `MinDelta`, `Ticks`, `Time`, `LastTime`. All prices and volumes are `decimal`.

Footprint (volume per price level) 🟡 – used by `assets/examples/WickAbsorption.cs`:
```csharp
foreach (PriceVolumeInfo lvl in candle.GetAllPriceLevels())   // every traded price of the bar
{
    lvl.Price; lvl.Volume; lvl.Ask; lvl.Bid;                   // level delta = Ask - Bid
}
candle.GetPriceVolumeInfo(price);   // one level (null if not traded)
candle.MaxVolumePriceInfo;          // the VPOC level of the bar
```
Prefer computing VPOC yourself during the `GetAllPriceLevels()` pass: one API dependency and one loop.
Wick/body split: wick levels are `Price < min(Open, Close)` (lower) and `Price > max(Open, Close)` (upper).

## Chart geometry

| Member | | |
|---|---|---|
| `Container.Region` (`Rectangle`) | ✅ | price panel area; use `.Top/.Bottom/.Width` |
| `ChartInfo.PriceChartContainer` | ✅ | null check before rendering |
| `ChartInfo.GetYByPrice(decimal price, bool isStartOfTick)` → `int` | ✅ | called as `(price, false)` |
| `ChartInfo.GetXByBar(int bar, bool isStartOfBar = true)` → `int` | 🟡 | left edge of bar |
| `ChartInfo.PriceChartContainer.BarsWidth` | 🟡 | prefer an x-difference between bars |

## RenderContext

```csharp
context.FillRectangle(Color color, Rectangle rect);                    // ✅
context.DrawRectangle(RenderPen pen, Rectangle rect);                  // ✅
context.DrawLine(RenderPen pen, int x1, int y1, int x2, int y2);       // ✅
context.DrawString(string text, RenderFont font, Color color, int x, int y); // ✅
Size size = context.MeasureString(string text, RenderFont font);       // ✅
```
- 🟡 There is also a `FillRectangle` overload with two colors for a gradient fill. The exact signature is ❓.
- `new RenderFont("Arial", 8)` and `new RenderFont("Arial", 12, FontStyle.Bold)` ✅
- `new RenderPen(Color color, float width) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }` ✅

## Settings

```csharp
[Display(Name = "Lookback", GroupName = "1. Calculation", Order = 10, Description = "...")] // ✅
[Range(2, 5000)]                                                                           // 🟡
public int Lookback { get => _lookback; set { _lookback = Math.Max(2, value); RecalculateValues(); } }
```
- Supported property types: `int`, `decimal`, `bool`, `string`, `enum`, `System.Drawing.Color` ✅, and `DashStyle` ✅.
