// Wick Absorption – ATAS / ATAS X indicator (built for ES / MES)
//
// Finds reversal candles where the action happened in the wick:
//
//   1. a meaningful wick (share of the range + minimum ticks),
//   2. the candle's VPOC (price level with the most volume) sits inside that wick,
//   3. the delta traded *inside the wick* is strong:
//
//   Lower wick (bullish rejection)
//     • Absorption  – wick delta strongly NEGATIVE: aggressive sellers hit the bid
//                     into the low, passive buyers absorbed it, price closed back up.
//     • Initiative  – wick delta strongly POSITIVE: buyers stepped in aggressively
//                     at the low and took it back ("pohlcení").
//
//   Upper wick (bearish rejection) – mirror image
//     • Absorption  – wick delta strongly POSITIVE (buyers absorbed by passive sellers)
//     • Initiative  – wick delta strongly NEGATIVE (sellers took it back)
//
// Drawing: shaded wick zone, VPOC line in the wick and the wick delta label.
// Uses footprint data per price level (IndicatorCandle.GetAllPriceLevels()).

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Globalization;

using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace WickAbsorptionIndicator
{
    public enum DeltaSource
    {
        [Display(Name = "VPOC level")]
        VpocLevel,

        [Display(Name = "VPOC zone (± ticks)")]
        VpocZone,

        [Display(Name = "Whole wick")]
        WholeWick,
    }

    public enum WickSignal
    {
        None = 0,
        BullAbsorption,
        BullInitiative,
        BearAbsorption,
        BearInitiative,
    }

    [DisplayName("Wick Absorption")]
    [Category("Order Flow")]
    public class WickAbsorption : Indicator
    {
        private struct BarInfo
        {
            public WickSignal Signal;
            public decimal WickTop;     // price
            public decimal WickBottom;  // price
            public decimal Vpoc;
            public decimal WickDelta;   // whole wick
            public decimal SignalDelta; // delta the signal was judged on (per DeltaSource)
            public decimal WickVolume;
        }

        #region Fields

        private readonly List<BarInfo> _bars = new();

        // wick
        private decimal _minWickRatio = 0.35m;
        private int _minWickTicks = 3;
        private bool _requireVpocInWick = true;
        private decimal _minWickVolumeShare = 0.20m;

        // delta
        private bool _detectAbsorption = true;
        private bool _detectInitiative = true;
        private DeltaSource _deltaSource = DeltaSource.VpocLevel;
        private int _vpocZoneTicks = 1;
        private int _minWickDelta = 100;
        private decimal _minWickDeltaRatio = 0.15m;

        // filters
        private bool _onlyClosedBars = true;

        private readonly List<PriceVolumeInfo> _levels = new();

        // visuals
        private Color _bullAbsorptionColor = Color.FromArgb(255, 0, 190, 255);
        private Color _bullInitiativeColor = Color.FromArgb(255, 40, 210, 90);
        private Color _bearAbsorptionColor = Color.FromArgb(255, 255, 140, 0);
        private Color _bearInitiativeColor = Color.FromArgb(255, 235, 45, 70);
        private int _zoneOpacity = 35;
        private bool _showVpoc = true;
        private int _vpocWidth = 2;
        private bool _showLabels = true;
        private Color _labelColor = Color.White;
        private RenderFont _font = new("Arial", 8);
        private int _fontSize = 8;

        #endregion

        #region Settings – 1. Wick

        [Display(Name = "Min wick / range", GroupName = "1. Wick", Order = 10,
            Description = "Wick length as a share of the candle range (0.35 = 35 %).")]
        [Range(0.05, 0.95)]
        public decimal MinWickRatio
        {
            get => _minWickRatio;
            set { _minWickRatio = Clamp(value, 0.05m, 0.95m); RecalculateValues(); }
        }

        [Display(Name = "Min wick (ticks)", GroupName = "1. Wick", Order = 20)]
        [Range(1, 200)]
        public int MinWickTicks
        {
            get => _minWickTicks;
            set { _minWickTicks = Math.Max(1, value); RecalculateValues(); }
        }

        [Display(Name = "Require VPOC in wick", GroupName = "1. Wick", Order = 30)]
        public bool RequireVpocInWick
        {
            get => _requireVpocInWick;
            set { _requireVpocInWick = value; RecalculateValues(); }
        }

        [Display(Name = "Min wick volume share", GroupName = "1. Wick", Order = 40,
            Description = "Volume traded in the wick / candle volume. 0 = off.")]
        [Range(0, 1)]
        public decimal MinWickVolumeShare
        {
            get => _minWickVolumeShare;
            set { _minWickVolumeShare = Clamp(value, 0m, 1m); RecalculateValues(); }
        }

        #endregion

        #region Settings – 2. Delta in wick

        [Display(Name = "Measure delta at", GroupName = "2. Delta in wick", Order = 5,
            Description = "VPOC level = only the VPOC price (how you read the footprint). " +
                          "VPOC zone = VPOC ± N ticks inside the wick. Whole wick = sum of all wick levels.")]
        public DeltaSource DeltaSource
        {
            get => _deltaSource;
            set { _deltaSource = value; RecalculateValues(); }
        }

        [Display(Name = "VPOC zone (± ticks)", GroupName = "2. Delta in wick", Order = 6,
            Description = "Used with 'VPOC zone'.")]
        [Range(1, 20)]
        public int VpocZoneTicks
        {
            get => _vpocZoneTicks;
            set { _vpocZoneTicks = Math.Clamp(value, 1, 20); RecalculateValues(); }
        }

        [Display(Name = "Detect absorption", GroupName = "2. Delta in wick", Order = 10,
            Description = "Wick delta against the rejection (e.g. negative in a lower wick).")]
        public bool DetectAbsorption
        {
            get => _detectAbsorption;
            set { _detectAbsorption = value; RecalculateValues(); }
        }

        [Display(Name = "Detect initiative (pohlcení)", GroupName = "2. Delta in wick", Order = 20,
            Description = "Wick delta with the rejection (e.g. positive in a lower wick).")]
        public bool DetectInitiative
        {
            get => _detectInitiative;
            set { _detectInitiative = value; RecalculateValues(); }
        }

        [Display(Name = "Min |delta| (contracts)", GroupName = "2. Delta in wick", Order = 30,
            Description = "Measured where 'Measure delta at' says. ES RTH VPOC level: ~100+. Overnight / MES: lower. 0 = off.")]
        [Range(0, 100000)]
        public int MinWickDelta
        {
            get => _minWickDelta;
            set { _minWickDelta = Math.Max(0, value); RecalculateValues(); }
        }

        [Display(Name = "Min |delta| / volume", GroupName = "2. Delta in wick", Order = 40,
            Description = "How one-sided the measured levels were (0.15 = 15 %). Works in any session. 0 = off.")]
        [Range(0, 1)]
        public decimal MinWickDeltaRatio
        {
            get => _minWickDeltaRatio;
            set { _minWickDeltaRatio = Clamp(value, 0m, 1m); RecalculateValues(); }
        }

        #endregion

        #region Settings – 3. Filters

        [Display(Name = "Only closed bars", GroupName = "3. Filters", Order = 10,
            Description = "Do not show signals on the live bar (they can disappear before close).")]
        public bool OnlyClosedBars
        {
            get => _onlyClosedBars;
            set { _onlyClosedBars = value; RedrawChart(); }
        }

        #endregion

        #region Settings – 4. Visual

        [Display(Name = "Bull absorption", GroupName = "4. Visual", Order = 10)]
        public Color BullAbsorptionColor
        {
            get => _bullAbsorptionColor;
            set { _bullAbsorptionColor = value; RedrawChart(); }
        }

        [Display(Name = "Bull initiative", GroupName = "4. Visual", Order = 20)]
        public Color BullInitiativeColor
        {
            get => _bullInitiativeColor;
            set { _bullInitiativeColor = value; RedrawChart(); }
        }

        [Display(Name = "Bear absorption", GroupName = "4. Visual", Order = 30)]
        public Color BearAbsorptionColor
        {
            get => _bearAbsorptionColor;
            set { _bearAbsorptionColor = value; RedrawChart(); }
        }

        [Display(Name = "Bear initiative", GroupName = "4. Visual", Order = 40)]
        public Color BearInitiativeColor
        {
            get => _bearInitiativeColor;
            set { _bearInitiativeColor = value; RedrawChart(); }
        }

        [Display(Name = "Wick zone opacity %", GroupName = "4. Visual", Order = 50)]
        [Range(0, 100)]
        public int ZoneOpacity
        {
            get => _zoneOpacity;
            set { _zoneOpacity = Math.Clamp(value, 0, 100); RedrawChart(); }
        }

        [Display(Name = "Show VPOC line", GroupName = "4. Visual", Order = 60)]
        public bool ShowVpoc
        {
            get => _showVpoc;
            set { _showVpoc = value; RedrawChart(); }
        }

        [Display(Name = "VPOC line width", GroupName = "4. Visual", Order = 70)]
        [Range(1, 10)]
        public int VpocWidth
        {
            get => _vpocWidth;
            set { _vpocWidth = Math.Clamp(value, 1, 10); RedrawChart(); }
        }

        [Display(Name = "Show wick delta label", GroupName = "4. Visual", Order = 80)]
        public bool ShowLabels
        {
            get => _showLabels;
            set { _showLabels = value; RedrawChart(); }
        }

        [Display(Name = "Label color", GroupName = "4. Visual", Order = 90)]
        public Color LabelColor
        {
            get => _labelColor;
            set { _labelColor = value; RedrawChart(); }
        }

        [Display(Name = "Label font size", GroupName = "4. Visual", Order = 100)]
        [Range(6, 20)]
        public int FontSize
        {
            get => _fontSize;
            set { _fontSize = Math.Clamp(value, 6, 20); _font = new RenderFont("Arial", _fontSize); RedrawChart(); }
        }

        #endregion

        public WickAbsorption() : base(true)
        {
            Panel = IndicatorDataProvider.CandlesPanel;
            DenyToChangePanel = true;

            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final);

            DataSeries[0].IsHidden = true;
            ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
        }

        /// <summary>Signal on a bar (for strategies / other indicators).</summary>
        public WickSignal GetSignal(int bar) =>
            bar >= 0 && bar < _bars.Count ? _bars[bar].Signal : WickSignal.None;

        #region Calculation

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == 0)
                _bars.Clear();

            while (_bars.Count <= bar)
                _bars.Add(default);

            _bars[bar] = Analyze(GetCandle(bar));
        }

        private BarInfo Analyze(IndicatorCandle candle)
        {
            var none = default(BarInfo);

            var tick = InstrumentInfo?.TickSize ?? 0.25m;
            if (tick <= 0)
                tick = 0.25m;

            var range = candle.High - candle.Low;
            if (range <= 0 || candle.Volume <= 0)
                return none;

            var bodyLow = Math.Min(candle.Open, candle.Close);
            var bodyHigh = Math.Max(candle.Open, candle.Close);
            var lowerWick = bodyLow - candle.Low;
            var upperWick = candle.High - bodyHigh;

            var levels = candle.GetAllPriceLevels();
            if (levels == null)
                return none;

            // Pass 1: copy levels, find VPOC, sum volume/delta of each wick.
            _levels.Clear();
            decimal vpoc = 0, vpocVol = -1;
            decimal lowVol = 0, lowDelta = 0, upVol = 0, upDelta = 0;

            foreach (var lvl in levels)
            {
                _levels.Add(lvl);

                if (lvl.Volume > vpocVol)
                {
                    vpocVol = lvl.Volume;
                    vpoc = lvl.Price;
                }

                if (lvl.Price < bodyLow)
                {
                    lowVol += lvl.Volume;
                    lowDelta += lvl.Ask - lvl.Bid;
                }
                else if (lvl.Price > bodyHigh)
                {
                    upVol += lvl.Volume;
                    upDelta += lvl.Ask - lvl.Bid;
                }
            }

            if (vpocVol <= 0)
                return none;

            // Pass 2: delta/volume the signal is judged on, per wick.
            var (lowSigDelta, lowSigVol) = SignalDelta(true, vpoc, bodyLow, bodyHigh, tick, lowDelta, lowVol);
            var (upSigDelta, upSigVol) = SignalDelta(false, vpoc, bodyLow, bodyHigh, tick, upDelta, upVol);

            var bull = Evaluate(true, lowerWick, range, tick, vpoc < bodyLow, lowVol, candle.Volume, lowSigDelta, lowSigVol);
            var bear = Evaluate(false, upperWick, range, tick, vpoc > bodyHigh, upVol, candle.Volume, upSigDelta, upSigVol);

            // Doji-like candles can qualify on both sides – keep the wick with more volume.
            if (bull != WickSignal.None && (bear == WickSignal.None || lowVol >= upVol))
            {
                return new BarInfo
                {
                    Signal = bull, WickTop = bodyLow - tick, WickBottom = candle.Low,
                    Vpoc = vpoc, WickDelta = lowDelta, WickVolume = lowVol, SignalDelta = lowSigDelta,
                };
            }

            if (bear != WickSignal.None)
            {
                return new BarInfo
                {
                    Signal = bear, WickTop = candle.High, WickBottom = bodyHigh + tick,
                    Vpoc = vpoc, WickDelta = upDelta, WickVolume = upVol, SignalDelta = upSigDelta,
                };
            }

            return none;
        }

        /// <summary>Delta and volume used for the signal, according to DeltaSource.</summary>
        private (decimal delta, decimal volume) SignalDelta(bool lowerWick, decimal vpoc,
            decimal bodyLow, decimal bodyHigh, decimal tick, decimal wickDelta, decimal wickVol)
        {
            if (_deltaSource == DeltaSource.WholeWick)
                return (wickDelta, wickVol);

            var half = _deltaSource == DeltaSource.VpocZone ? _vpocZoneTicks * tick : 0m;
            decimal d = 0, v = 0;

            foreach (var lvl in _levels)
            {
                var inWick = lowerWick ? lvl.Price < bodyLow : lvl.Price > bodyHigh;
                if (!inWick || Math.Abs(lvl.Price - vpoc) > half)
                    continue;

                d += lvl.Ask - lvl.Bid;
                v += lvl.Volume;
            }

            return (d, v);
        }

        private WickSignal Evaluate(bool lowerWick, decimal wick, decimal range, decimal tick,
            bool vpocInWick, decimal wickVol, decimal candleVol, decimal sigDelta, decimal sigVol)
        {
            if (wick < _minWickTicks * tick || wick / range < _minWickRatio)
                return WickSignal.None;

            if (_requireVpocInWick && !vpocInWick)
                return WickSignal.None;

            if (wickVol <= 0 || wickVol / candleVol < _minWickVolumeShare)
                return WickSignal.None;

            var absDelta = Math.Abs(sigDelta);
            if (absDelta == 0 || sigVol <= 0 || absDelta < _minWickDelta || absDelta / sigVol < _minWickDeltaRatio)
                return WickSignal.None;

            // Lower wick: negative delta = sellers absorbed; positive = buyers' initiative.
            // Upper wick: the mirror image.
            var againstRejection = lowerWick ? sigDelta < 0 : sigDelta > 0;

            if (againstRejection && _detectAbsorption)
                return lowerWick ? WickSignal.BullAbsorption : WickSignal.BearAbsorption;

            if (!againstRejection && _detectInitiative)
                return lowerWick ? WickSignal.BullInitiative : WickSignal.BearInitiative;

            return WickSignal.None;
        }

        #endregion

        #region Rendering

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (layout != DrawingLayouts.Final)
                return;

            if (ChartInfo?.PriceChartContainer == null || Container == null || _bars.Count == 0)
                return;

            var first = Math.Max(0, FirstVisibleBarNumber);
            var last = Math.Min(LastVisibleBarNumber, _bars.Count - 1);
            if (_onlyClosedBars)
                last = Math.Min(last, CurrentBar - 2);

            if (last < first)
                return;

            var tick = InstrumentInfo?.TickSize ?? 0.25m;
            if (tick <= 0)
                tick = 0.25m;

            var prevWidth = 3;

            for (var bar = first; bar <= last; bar++)
            {
                var info = _bars[bar];
                if (info.Signal == WickSignal.None)
                    continue;

                var x = ChartInfo.GetXByBar(bar);
                var width = bar < CurrentBar - 1 ? ChartInfo.GetXByBar(bar + 1) - x : prevWidth;
                if (width <= 0)
                    width = prevWidth;
                prevWidth = width;

                var baseColor = SignalColor(info.Signal);

                // Pixel height of one tick, to cover whole price levels.
                var yA = ChartInfo.GetYByPrice(info.WickTop, false);
                var yB = ChartInfo.GetYByPrice(info.WickBottom, false);
                var tickPx = Math.Abs(ChartInfo.GetYByPrice(info.WickTop + tick, false) - yA);
                var half = Math.Max(1, tickPx / 2);

                var top = Math.Min(yA, yB) - half;
                var bottom = Math.Max(yA, yB) + half;
                var zone = new Rectangle(x, top, Math.Max(2, width - 1), Math.Max(2, bottom - top));

                context.FillRectangle(WithOpacity(baseColor, _zoneOpacity), zone);
                context.DrawRectangle(new RenderPen(baseColor, 1f), zone);

                if (_showVpoc)
                {
                    var yv = ChartInfo.GetYByPrice(info.Vpoc, false);
                    var pen = new RenderPen(baseColor, _vpocWidth);
                    context.DrawLine(pen, x - 2, yv, x + width + 1, yv);
                }

                if (_showLabels)
                {
                    var text = FormatDelta(info.SignalDelta);
                    var size = context.MeasureString(text, _font);
                    var tx = x + (width - size.Width) / 2;
                    var isBull = info.Signal == WickSignal.BullAbsorption || info.Signal == WickSignal.BullInitiative;
                    var ty = isBull ? bottom + 2 : top - size.Height - 2;

                    context.FillRectangle(WithOpacity(baseColor, 80),
                        new Rectangle(tx - 2, ty, size.Width + 4, size.Height));
                    context.DrawString(text, _font, _labelColor, tx, ty);
                }
            }
        }

        private Color SignalColor(WickSignal s) => s switch
        {
            WickSignal.BullAbsorption => _bullAbsorptionColor,
            WickSignal.BullInitiative => _bullInitiativeColor,
            WickSignal.BearAbsorption => _bearAbsorptionColor,
            WickSignal.BearInitiative => _bearInitiativeColor,
            _ => Color.Gray,
        };

        private static Color WithOpacity(Color c, int percent) =>
            Color.FromArgb(Math.Clamp((int)Math.Round(c.A * percent / 100.0), 0, 255), c.R, c.G, c.B);

        private static string FormatDelta(decimal delta)
        {
            var sign = delta > 0 ? "+" : delta < 0 ? "-" : "";
            var abs = Math.Abs(delta);
            return abs >= 1000m
                ? sign + (abs / 1000m).ToString("0.0", CultureInfo.InvariantCulture) + "K"
                : sign + abs.ToString("0", CultureInfo.InvariantCulture);
        }

        #endregion

        private static decimal Clamp(decimal v, decimal min, decimal max) =>
            v < min ? min : v > max ? max : v;
    }
}
