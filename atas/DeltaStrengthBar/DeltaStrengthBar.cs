// Delta Strength Bar – ATAS / ATAS X indicator
//
// Horizontal color strip drawn below the price action. Each bar's cell is
// colored by bid/ask delta strength using a 3-point gradient:
//
//     Bid (selling)  <-->  Neutral  <-->  Ask (buying)
//      strength -1           0            +1
//
// Strength modes:
//   * DeltaPercent   – Delta / Volume (how one-sided the bar was)
//   * RelativeDelta  – Delta / max |Delta| over the lookback window
//   * ZScore         – (Delta - mean) / stdev over the lookback window
//
// The raw value is optionally EMA-smoothed, scaled by "Saturation" (value at
// which the color is fully saturated), and passed through a neutral dead zone.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Globalization;

using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace DeltaStrength
{
    public enum StrengthMode
    {
        [Display(Name = "Delta % of volume")]
        DeltaPercent,

        [Display(Name = "Delta / max |delta| (lookback)")]
        RelativeDelta,

        [Display(Name = "Delta Z-score (lookback)")]
        ZScore,
    }

    public enum StripPosition
    {
        [Display(Name = "Bottom of chart")]
        ChartBottom,

        [Display(Name = "Top of chart")]
        ChartTop,

        [Display(Name = "Below each candle low")]
        BelowCandleLow,
    }

    [DisplayName("Delta Strength Bar")]
    [Category("Order Flow")]
    public class DeltaStrengthBar : Indicator
    {
        #region Fields

        // Per-bar calculated values (index = bar number).
        private readonly List<decimal> _raw = new();       // unsmoothed raw strength
        private readonly List<decimal> _smoothed = new();  // EMA of raw
        private readonly List<decimal> _strength = new();  // final value in [-1, 1]

        private StrengthMode _mode = StrengthMode.DeltaPercent;
        private int _lookback = 50;
        private int _smoothing = 1;
        private decimal _saturation; // 0 = auto per mode
        private decimal _deadZone = 0.10m;
        private decimal _gamma = 1.0m;

        private Color _bidColor = Color.FromArgb(255, 220, 40, 60);
        private Color _neutralColor = Color.FromArgb(255, 90, 90, 100);
        private Color _askColor = Color.FromArgb(255, 30, 190, 90);
        private int _opacity = 100;

        private StripPosition _position = StripPosition.ChartBottom;
        private int _height = 10;
        private int _offset = 4;
        private int _gap = 0;
        private bool _drawBorder;
        private Color _borderColor = Color.FromArgb(160, 30, 30, 35);

        private bool _showValues;
        private int _minWidthForText = 24;
        private Color _textColor = Color.White;
        private readonly RenderFont _font = new("Arial", 7);

        #endregion

        #region Settings – Calculation

        [Display(Name = "Strength mode", GroupName = "1. Calculation", Order = 10,
            Description = "How the per-bar delta is turned into a strength value.")]
        public StrengthMode Mode
        {
            get => _mode;
            set { _mode = value; RecalculateValues(); }
        }

        [Display(Name = "Lookback (bars)", GroupName = "1. Calculation", Order = 20,
            Description = "Window for Relative and Z-score modes.")]
        [Range(2, 5000)]
        public int Lookback
        {
            get => _lookback;
            set { _lookback = Math.Max(2, value); RecalculateValues(); }
        }

        [Display(Name = "Smoothing (EMA period)", GroupName = "1. Calculation", Order = 30,
            Description = "1 = no smoothing.")]
        [Range(1, 500)]
        public int Smoothing
        {
            get => _smoothing;
            set { _smoothing = Math.Max(1, value); RecalculateValues(); }
        }

        [Display(Name = "Saturation (0 = auto)", GroupName = "1. Calculation", Order = 40,
            Description = "Raw value at which the color is fully Bid/Ask. 0 = auto: " +
                          "Delta% 0.40 (40 % one-sided), Relative 0.80, Z-score 2.0 σ.")]
        [Range(0, 100)]
        public decimal Saturation
        {
            get => _saturation;
            set { _saturation = Math.Max(0m, value); RecalculateValues(); }
        }

        private decimal EffectiveSaturation =>
            _saturation > 0m
                ? _saturation
                : _mode switch
                {
                    StrengthMode.DeltaPercent => 0.40m,
                    StrengthMode.RelativeDelta => 0.80m,
                    StrengthMode.ZScore => 2.0m,
                    _ => 1m,
                };

        [Display(Name = "Neutral dead zone (0–0.9)", GroupName = "1. Calculation", Order = 50,
            Description = "Strength below this (after scaling) is drawn as pure Neutral.")]
        [Range(0, 0.9)]
        public decimal DeadZone
        {
            get => _deadZone;
            set { _deadZone = Clamp(value, 0m, 0.9m); RecalculateValues(); }
        }

        [Display(Name = "Gradient curve (gamma)", GroupName = "1. Calculation", Order = 60,
            Description = "1 = linear. <1 colors faster near neutral, >1 emphasises extremes.")]
        [Range(0.2, 5)]
        public decimal Gamma
        {
            get => _gamma;
            set { _gamma = Clamp(value, 0.2m, 5m); RecalculateValues(); }
        }

        #endregion

        #region Settings – Colors

        [Display(Name = "Bid color (sellers)", GroupName = "2. Colors", Order = 10)]
        public Color BidColor
        {
            get => _bidColor;
            set { _bidColor = value; RedrawChart(); }
        }

        [Display(Name = "Neutral color", GroupName = "2. Colors", Order = 20)]
        public Color NeutralColor
        {
            get => _neutralColor;
            set { _neutralColor = value; RedrawChart(); }
        }

        [Display(Name = "Ask color (buyers)", GroupName = "2. Colors", Order = 30)]
        public Color AskColor
        {
            get => _askColor;
            set { _askColor = value; RedrawChart(); }
        }

        [Display(Name = "Opacity %", GroupName = "2. Colors", Order = 40)]
        [Range(0, 100)]
        public int Opacity
        {
            get => _opacity;
            set { _opacity = Math.Clamp(value, 0, 100); RedrawChart(); }
        }

        #endregion

        #region Settings – Layout

        [Display(Name = "Position", GroupName = "3. Layout", Order = 10)]
        public StripPosition Position
        {
            get => _position;
            set { _position = value; RedrawChart(); }
        }

        [Display(Name = "Height (px)", GroupName = "3. Layout", Order = 20)]
        [Range(1, 200)]
        public int StripHeight
        {
            get => _height;
            set { _height = Math.Max(1, value); RedrawChart(); }
        }

        [Display(Name = "Offset (px)", GroupName = "3. Layout", Order = 30,
            Description = "Distance from the chart edge or from the candle low.")]
        [Range(0, 500)]
        public int Offset
        {
            get => _offset;
            set { _offset = Math.Max(0, value); RedrawChart(); }
        }

        [Display(Name = "Gap between cells (px)", GroupName = "3. Layout", Order = 40)]
        [Range(0, 20)]
        public int Gap
        {
            get => _gap;
            set { _gap = Math.Max(0, value); RedrawChart(); }
        }

        [Display(Name = "Draw border", GroupName = "3. Layout", Order = 50)]
        public bool DrawBorder
        {
            get => _drawBorder;
            set { _drawBorder = value; RedrawChart(); }
        }

        [Display(Name = "Border color", GroupName = "3. Layout", Order = 60)]
        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; RedrawChart(); }
        }

        #endregion

        #region Settings – Labels

        [Display(Name = "Show delta values", GroupName = "4. Labels", Order = 10)]
        public bool ShowValues
        {
            get => _showValues;
            set { _showValues = value; RedrawChart(); }
        }

        [Display(Name = "Min cell width for text (px)", GroupName = "4. Labels", Order = 20)]
        [Range(5, 200)]
        public int MinWidthForText
        {
            get => _minWidthForText;
            set { _minWidthForText = Math.Max(5, value); RedrawChart(); }
        }

        [Display(Name = "Text color", GroupName = "4. Labels", Order = 30)]
        public Color TextColor
        {
            get => _textColor;
            set { _textColor = value; RedrawChart(); }
        }

        #endregion

        public DeltaStrengthBar() : base(true)
        {
            Panel = IndicatorDataProvider.CandlesPanel;
            DenyToChangePanel = true;

            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final);

            // The indicator draws everything itself; hide the default series.
            DataSeries[0].IsHidden = true;
            ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
        }

        /// <summary>Final strength of a bar in [-1, 1] (negative = Bid, positive = Ask).</summary>
        public decimal GetStrength(int bar) =>
            bar >= 0 && bar < _strength.Count ? _strength[bar] : 0m;

        #region Calculation

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == 0)
            {
                _raw.Clear();
                _smoothed.Clear();
                _strength.Clear();
            }

            EnsureSize(bar);

            var candle = GetCandle(bar);
            var raw = CalcRaw(bar, candle);
            _raw[bar] = raw;

            // EMA smoothing. Uses only the previous bar's stored value, so
            // repeated calls on the live bar stay correct.
            decimal smoothed;
            if (_smoothing <= 1 || bar == 0)
            {
                smoothed = raw;
            }
            else
            {
                var k = 2m / (_smoothing + 1);
                smoothed = _smoothed[bar - 1] + k * (raw - _smoothed[bar - 1]);
            }

            _smoothed[bar] = smoothed;
            _strength[bar] = Shape(smoothed / EffectiveSaturation);
        }

        private decimal CalcRaw(int bar, IndicatorCandle candle)
        {
            switch (_mode)
            {
                case StrengthMode.DeltaPercent:
                    return candle.Volume > 0 ? candle.Delta / candle.Volume : 0m;

                case StrengthMode.RelativeDelta:
                {
                    var maxAbs = 0m;
                    var start = Math.Max(0, bar - _lookback + 1);
                    for (var i = start; i <= bar; i++)
                        maxAbs = Math.Max(maxAbs, Math.Abs(GetCandle(i).Delta));

                    return maxAbs > 0 ? candle.Delta / maxAbs : 0m;
                }

                case StrengthMode.ZScore:
                {
                    var start = Math.Max(0, bar - _lookback + 1);
                    var n = bar - start + 1;
                    if (n < 2)
                        return 0m;

                    double sum = 0, sumSq = 0;
                    for (var i = start; i <= bar; i++)
                    {
                        var d = (double)GetCandle(i).Delta;
                        sum += d;
                        sumSq += d * d;
                    }

                    var mean = sum / n;
                    var variance = Math.Max(0, sumSq / n - mean * mean);
                    var std = Math.Sqrt(variance);
                    if (std <= double.Epsilon)
                        return 0m;

                    var z = ((double)candle.Delta - mean) / std;
                    return (decimal)Math.Clamp(z, -50, 50);
                }

                default:
                    return 0m;
            }
        }

        /// <summary>Clamp to [-1,1], apply the neutral dead zone and the gamma curve.</summary>
        private decimal Shape(decimal x)
        {
            var sign = Math.Sign(x);
            var a = Math.Min(1m, Math.Abs(x));

            if (a <= _deadZone)
                return 0m;

            a = (a - _deadZone) / (1m - _deadZone);

            if (_gamma != 1m)
                a = (decimal)Math.Pow((double)a, (double)_gamma);

            return sign * a;
        }

        private void EnsureSize(int bar)
        {
            while (_raw.Count <= bar)
            {
                _raw.Add(0m);
                _smoothed.Add(0m);
                _strength.Add(0m);
            }
        }

        #endregion

        #region Rendering

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (layout != DrawingLayouts.Final)
                return;

            if (ChartInfo?.PriceChartContainer == null || Container == null || _strength.Count == 0)
                return;

            var region = Container.Region;
            var first = Math.Max(0, FirstVisibleBarNumber);
            var last = Math.Min(LastVisibleBarNumber, _strength.Count - 1);
            if (last < first)
                return;

            var borderPen = _drawBorder ? new RenderPen(_borderColor, 1f) : null;

            // Fixed Y for edge-anchored strips.
            var fixedY = _position == StripPosition.ChartTop
                ? region.Top + _offset
                : region.Bottom - _offset - _height;

            var prevWidth = 1;

            for (var bar = first; bar <= last; bar++)
            {
                var x = ChartInfo.GetXByBar(bar);

                // Cell width = distance to next bar, so the strip is continuous.
                var width = bar < CurrentBar - 1
                    ? ChartInfo.GetXByBar(bar + 1) - x
                    : prevWidth;
                if (width <= 0)
                    width = prevWidth;
                prevWidth = width;

                var y = fixedY;
                if (_position == StripPosition.BelowCandleLow)
                    y = ChartInfo.GetYByPrice(GetCandle(bar).Low, false) + _offset;

                var cellWidth = Math.Max(1, width - _gap);
                var rect = new Rectangle(x, y, cellWidth, _height);

                var color = GradientColor(_strength[bar]);
                context.FillRectangle(color, rect);

                if (borderPen != null)
                    context.DrawRectangle(borderPen, rect);

                if (_showValues && cellWidth >= _minWidthForText)
                    DrawValue(context, bar, rect);
            }
        }

        private void DrawValue(RenderContext context, int bar, Rectangle rect)
        {
            var text = FormatDelta(GetCandle(bar).Delta);
            var size = context.MeasureString(text, _font);

            if (size.Width > rect.Width - 2 || size.Height > rect.Height + 4)
                return;

            var tx = rect.X + (rect.Width - size.Width) / 2;
            var ty = rect.Y + (rect.Height - size.Height) / 2;
            context.DrawString(text, _font, _textColor, tx, ty);
        }

        private static string FormatDelta(decimal delta)
        {
            var abs = Math.Abs(delta);
            var sign = delta < 0 ? "-" : "";

            if (abs >= 1_000_000m)
                return sign + (abs / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (abs >= 10_000m)
                return sign + (abs / 1_000m).ToString("0", CultureInfo.InvariantCulture) + "K";
            if (abs >= 1_000m)
                return sign + (abs / 1_000m).ToString("0.#", CultureInfo.InvariantCulture) + "K";

            return sign + abs.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>3-point gradient: -1 = Bid, 0 = Neutral, +1 = Ask.</summary>
        private Color GradientColor(decimal strength)
        {
            var t = (double)Clamp(strength, -1m, 1m);

            var c = t < 0
                ? Lerp(_neutralColor, _bidColor, -t)
                : Lerp(_neutralColor, _askColor, t);

            var alpha = (int)Math.Round(c.A * (_opacity / 100.0));
            return Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);
        }

        private static Color Lerp(Color a, Color b, double t)
        {
            static int Mix(int x, int y, double k) => (int)Math.Round(x + (y - x) * k);

            return Color.FromArgb(
                Mix(a.A, b.A, t),
                Mix(a.R, b.R, t),
                Mix(a.G, b.G, t),
                Mix(a.B, b.B, t));
        }

        #endregion

        private static decimal Clamp(decimal v, decimal min, decimal max) =>
            v < min ? min : v > max ? max : v;
    }
}
