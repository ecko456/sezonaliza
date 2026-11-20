// Minimal stand-ins for the ATAS API, used ONLY to compile-check and smoke-test
// indicator code when the real ATAS DLLs are not available (e.g. in a cloud sandbox).
// Signatures mirror references/api.md. Passing this check proves C# correctness and
// that the code uses the API *as documented in the cheat sheet* – not that the real
// ATAS build accepts it.

using System;
using System.Collections.Generic;
using System.Drawing;

namespace OFT.Rendering.Tools
{
    public class RenderFont
    {
        public RenderFont(string family, float size) { }
        public RenderFont(string family, float size, FontStyle style) { }
    }

    public class RenderPen
    {
        public RenderPen(Color color, float width = 1f) { }
        public System.Drawing.Drawing2D.DashStyle DashStyle { get; set; }
    }
}

namespace OFT.Rendering.Context
{
    using OFT.Rendering.Tools;

    public class RenderContext
    {
        public int Calls;
        public void FillRectangle(Color color, Rectangle rect) => Calls++;
        public void DrawRectangle(RenderPen pen, Rectangle rect) => Calls++;
        public void DrawLine(RenderPen pen, int x1, int y1, int x2, int y2) => Calls++;
        public void DrawString(string text, RenderFont font, Color color, int x, int y) => Calls++;
        public Size MeasureString(string text, RenderFont font) => new(text.Length * 5, 9);
    }
}

namespace ATAS.Indicators
{
    [Flags]
    public enum DrawingLayouts { None = 0, Historical = 1, LatestBar = 2, Final = 4 }

    public enum VisualMode { Line, Histogram, Dots, Block, Hide, UpArrow, DownArrow, Square }

    public class PriceVolumeInfo
    {
        public decimal Price { get; set; }
        public decimal Volume { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
    }

    public class IndicatorCandle
    {
        public decimal Open, High, Low, Close, Volume, Delta, Ask, Bid, MaxDelta, MinDelta, Ticks;
        public DateTime Time, LastTime;

        // footprint
        public List<PriceVolumeInfo> StubLevels = new();
        public IEnumerable<PriceVolumeInfo> GetAllPriceLevels() => StubLevels;
        public PriceVolumeInfo GetPriceVolumeInfo(decimal price) => StubLevels.Find(l => l.Price == price);

        public PriceVolumeInfo MaxVolumePriceInfo
        {
            get
            {
                PriceVolumeInfo best = null;
                foreach (var l in StubLevels)
                    if (best == null || l.Volume > best.Volume) best = l;
                return best;
            }
        }
    }

    public abstract class IDataSeries
    {
        public string Name { get; set; }
        public bool IsHidden { get; set; }
    }

    public class ValueDataSeries : IDataSeries
    {
        private readonly List<decimal> _v = new();
        public ValueDataSeries(string name) { Name = name; }
        public ValueDataSeries(string id, string name) { Name = name; }
        public Color Color { get; set; }
        public VisualMode VisualType { get; set; }
        public int Width { get; set; }

        public decimal this[int bar]
        {
            get => bar >= 0 && bar < _v.Count ? _v[bar] : 0m;
            set { while (_v.Count <= bar) _v.Add(0m); _v[bar] = value; }
        }
    }

    public class ChartContainer
    {
        public Rectangle Region { get; set; }
    }

    public class PriceChartContainer
    {
        public int BarsWidth { get; set; } = 8;
    }

    public class ChartInfo
    {
        public PriceChartContainer PriceChartContainer { get; set; } = new();
        public int GetXByBar(int bar, bool isStartOfBar = true) => bar * 10;
        public int GetYByPrice(decimal price, bool isStartOfTick = true) => 400 - (int)(price % 400);
    }

    public class InstrumentInfo
    {
        public decimal TickSize { get; set; } = 0.25m;
    }

    public static class IndicatorDataProvider
    {
        public const string CandlesPanel = "Chart";
        public const string NewPanel = "NewPanel";
    }

    public abstract class Indicator
    {
        protected Indicator(bool useCandles = false)
        {
            DataSeries.Add(new ValueDataSeries("Default"));
        }

        public List<IDataSeries> DataSeries { get; } = new();
        public string Panel { get; set; }
        public bool DenyToChangePanel { get; set; }
        public bool EnableCustomDrawing { get; set; }
        public ChartInfo ChartInfo { get; set; }
        public ChartContainer Container { get; set; }
        public InstrumentInfo InstrumentInfo { get; set; } = new();
        public int FirstVisibleBarNumber { get; set; }
        public int LastVisibleBarNumber { get; set; }
        public int CurrentBar { get; protected set; }

        protected void SubscribeToDrawingEvents(DrawingLayouts layouts) { }
        protected abstract void OnCalculate(int bar, decimal value);
        protected virtual void OnRender(OFT.Rendering.Context.RenderContext context, DrawingLayouts layout) { }

        public void RecalculateValues() { }
        public void RedrawChart() { }
        public bool IsNewSession(int bar) => StubSessionLength > 0 && bar % StubSessionLength == 0;

        // ---- stub harness (not ATAS API) ----
        public List<IndicatorCandle> StubCandles = new();
        public int StubSessionLength = 0;
        public IndicatorCandle GetCandle(int bar) => StubCandles[bar];

        public void StubRun(List<IndicatorCandle> candles, int sessionLength, int liveTicks = 3)
        {
            StubCandles = candles;
            StubSessionLength = sessionLength;
            for (var i = 0; i < candles.Count; i++)
            {
                CurrentBar = i + 1;
                OnCalculate(i, candles[i].Close);
            }

            // Simulate repeated calls on the live bar – results must not change
            // (state accumulated in plain fields would double-count here).
            var last = candles.Count - 1;
            var before = SnapshotSeries(last);
            for (var t = 0; t < liveTicks; t++)
                OnCalculate(last, candles[^1].Close);
            var after = SnapshotSeries(last);

            for (var s = 0; s < before.Count; s++)
            {
                if (before[s] != after[s])
                    throw new InvalidOperationException(
                        $"Series '{DataSeries[s].Name}' changed from {before[s]} to {after[s]} when the live bar " +
                        "was recalculated again. OnCalculate is not idempotent – index state by bar.");
            }
        }

        private List<decimal> SnapshotSeries(int bar)
        {
            var list = new List<decimal>();
            foreach (var s in DataSeries)
                list.Add(s is ValueDataSeries v ? v[bar] : 0m);
            return list;
        }

        public int StubRender()
        {
            ChartInfo ??= new ChartInfo();
            Container ??= new ChartContainer { Region = new Rectangle(0, 0, 1200, 400) };
            FirstVisibleBarNumber = Math.Max(0, CurrentBar - 120);
            LastVisibleBarNumber = CurrentBar - 1;
            var ctx = new OFT.Rendering.Context.RenderContext();
            OnRender(ctx, DrawingLayouts.Final);
            return ctx.Calls;
        }
    }
}
