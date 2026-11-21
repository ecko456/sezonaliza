// Delta Strength Bar – ATAS / ATAS X indicator (built for ES / MES)
//
// A two-lane strip below the price action:
//
//   SIGNAL lane   – order-flow events as bright cells with a glyph
//                   ▲ / ▼  Ignition    – move confirmed after a wick setup
//                   •      Wick setup  – heavy activity at the VPOC inside a rejected wick
//                   A      Absorption  – strong delta + high volume, but price closed against it
//                   E      Exhaustion  – new N-bar high/low without delta support or on thin volume
//                   Color = direction (bullish / bearish), glyph = type.
//
//   STRENGTH lane – bid/ask delta strength, Bid ↔ Neutral ↔ Ask, drawn in three clear
//                   steps (weak / medium / strong) or as a smooth gradient.
//
// Strength modes:
//   * DeltaPercent   – Delta / Volume (how one-sided the bar was)
//   * RelativeDelta  – Delta / max |Delta| over the lookback window
//   * ZScore         – (Delta - mean) / stdev over the lookback window

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Globalization;

using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

// FontStyle is only an enum value passed to RenderFont (no GDI+ call), so the
// "Windows-only" analyzer warning does not apply on ATAS X.
#pragma warning disable CA1416

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

    public enum ColorMode
    {
        [Display(Name = "Steps (weak / medium / strong)")]
        Steps,

        [Display(Name = "Smooth gradient")]
        Gradient,
    }

    public enum DeltaSignal
    {
        None = 0,
        BullWickSetup,    // heavy activity at the VPOC in a rejected LOWER wick – watch for a move up
        BearWickSetup,    // heavy activity at the VPOC in a rejected UPPER wick – watch for a move down
        BullIgnition,     // move up confirmed after a bull wick setup
        BearIgnition,     // move down confirmed after a bear wick setup
        SellersAbsorbed,  // strong selling, high volume, but the bar closed high (bullish)
        BuyersAbsorbed,   // strong buying, high volume, but the bar closed low (bearish)
        UpExhaustion,     // new high without buyers' support (bearish)
        DownExhaustion,   // new low without sellers' support (bullish)
    }

    [DisplayName("Delta Strength Bar")]
    [Category("Order Flow")]
    public class DeltaStrengthBar : Indicator
    {
        #region Fields

        // Per-bar calculated values (index = bar number).
        private readonly List<decimal> _raw = new();
        private readonly List<decimal> _smoothed = new();
        private readonly List<decimal> _strength = new();   // final value in [-1, 1]
        private readonly List<DeltaSignal> _signal = new();
        private readonly List<int> _setup = new();          // +1 bull wick setup, -1 bear, 0 none

        // calculation
        private StrengthMode _mode = StrengthMode.DeltaPercent;
        private int _lookback = 50;
        private int _smoothing = 1;
        private decimal _saturation; // 0 = auto per mode
        private decimal _deadZone = 0.10m;
        private decimal _gamma = 1.0m;

        // strength colors
        private ColorMode _colorMode = ColorMode.Steps;
        private Color _bidColor = Color.FromArgb(255, 255, 40, 80);
        private Color _neutralColor = Color.FromArgb(255, 80, 86, 100);
        private Color _askColor = Color.FromArgb(255, 0, 210, 120);
        private int _opacity = 100;

        // signals – general
        private bool _showSignals = true;
        private bool _onlyClosedBars = true;
        private int _volumeLookback = 20;

        // signals – absorption
        private bool _useAbsorption = true;
        private decimal _absMinRatio = 0.20m;
        private decimal _absMinRelVolume = 1.2m;
        private decimal _absClosePosition = 0.40m;

        // signals – exhaustion
        private bool _useExhaustion = true;
        private int _exhLookback = 10;
        private bool _exhDivergence = true;
        private decimal _exhMaxSupport = 0.05m;
        private bool _exhLowVolume = true;
        private decimal _exhLowVolRatio = 0.60m;

        // signals – wick setup & ignition
        private bool _useWick = true;
        private bool _showSetups = true;
        private decimal _wickMinRatio = 0.35m;
        private int _wickMinTicks = 3;
        private int _wickMinVpocDelta = 100;
        private decimal _wickMinVpocRatio = 0.15m;
        private int _ignitionWindow = 3;
        private decimal _ignitionMinRatio = 0.15m;

        // signal colors
        private Color _bullIgnitionColor = Color.FromArgb(255, 0, 255, 140);
        private Color _bearIgnitionColor = Color.FromArgb(255, 255, 30, 60);
        private Color _sellersAbsorbedColor = Color.FromArgb(255, 0, 190, 255);
        private Color _buyersAbsorbedColor = Color.FromArgb(255, 255, 150, 0);
        private Color _exhaustionColor = Color.FromArgb(255, 255, 225, 0);

        // layout
        private StripPosition _position = StripPosition.ChartBottom;
        private int _height = 12;
        private int _signalHeight = 16;
        private int _offset = 4;
        private int _gap = 1;
        private bool _drawBorder;
        private Color _borderColor = Color.FromArgb(160, 30, 30, 35);

        // labels
        private bool _showValues;
        private int _minWidthForText = 24;
        private Color _textColor = Color.White;
        private readonly RenderFont _font = new("Arial", 7);
        private RenderFont _glyphFont = new("Arial", 9, FontStyle.Bold);

        private decimal[] _volBuffer = new decimal[64];

        #endregion

        #region Settings – 1. Calculation

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
                          "Delta% 0.30 (30 % one-sided), Relative 0.70, Z-score 2.0 σ.")]
        [Range(0, 100)]
        public decimal Saturation
        {
            get => _saturation;
            set { _saturation = Math.Max(0m, value); RecalculateValues(); }
        }

        [Display(Name = "Neutral dead zone (0–0.9)", GroupName = "1. Calculation", Order = 50,
            Description = "Strength below this (after scaling) is drawn as pure Neutral.")]
        [Range(0, 0.9)]
        public decimal DeadZone
        {
            get => _deadZone;
            set { _deadZone = Clamp(value, 0m, 0.9m); RecalculateValues(); }
        }

        [Display(Name = "Gradient curve (gamma)", GroupName = "1. Calculation", Order = 60,
            Description = "Smooth gradient only. 1 = linear, <1 colors faster near neutral.")]
        [Range(0.2, 5)]
        public decimal Gamma
        {
            get => _gamma;
            set { _gamma = Clamp(value, 0.2m, 5m); RecalculateValues(); }
        }

        private decimal EffectiveSaturation =>
            _saturation > 0m
                ? _saturation
                : _mode switch
                {
                    StrengthMode.DeltaPercent => 0.30m,
                    StrengthMode.RelativeDelta => 0.70m,
                    StrengthMode.ZScore => 2.0m,
                    _ => 1m,
                };

        #endregion

        #region Settings – 2. Strength colors

        [Display(Name = "Color mode", GroupName = "2. Strength colors", Order = 5,
            Description = "Steps = 3 clear intensity levels per side. Gradient = continuous.")]
        public ColorMode ColorMode
        {
            get => _colorMode;
            set { _colorMode = value; RedrawChart(); }
        }

        [Display(Name = "Bid color (sellers)", GroupName = "2. Strength colors", Order = 10)]
        public Color BidColor
        {
            get => _bidColor;
            set { _bidColor = value; RedrawChart(); }
        }

        [Display(Name = "Neutral color", GroupName = "2. Strength colors", Order = 20)]
        public Color NeutralColor
        {
            get => _neutralColor;
            set { _neutralColor = value; RedrawChart(); }
        }

        [Display(Name = "Ask color (buyers)", GroupName = "2. Strength colors", Order = 30)]
        public Color AskColor
        {
            get => _askColor;
            set { _askColor = value; RedrawChart(); }
        }

        [Display(Name = "Opacity %", GroupName = "2. Strength colors", Order = 40)]
        [Range(0, 100)]
        public int Opacity
        {
            get => _opacity;
            set { _opacity = Math.Clamp(value, 0, 100); RedrawChart(); }
        }

        #endregion

        #region Settings – 3. Signals

        [Display(Name = "Show signal lane", GroupName = "3. Signals", Order = 10)]
        public bool ShowSignals
        {
            get => _showSignals;
            set { _showSignals = value; RedrawChart(); }
        }

        [Display(Name = "Only closed bars", GroupName = "3. Signals", Order = 20,
            Description = "Hide signals on the live bar (they can still change before it closes).")]
        public bool OnlyClosedBars
        {
            get => _onlyClosedBars;
            set { _onlyClosedBars = value; RedrawChart(); }
        }

        [Display(Name = "Average volume lookback", GroupName = "3. Signals", Order = 30,
            Description = "Bars used for relative volume (volume / MEDIAN volume of the previous bars).")]
        [Range(2, 500)]
        public int VolumeLookback
        {
            get => _volumeLookback;
            set { _volumeLookback = Math.Max(2, value); RecalculateValues(); }
        }

        // --- Absorption

        [Display(Name = "A · Absorption", GroupName = "3. Signals", Order = 100,
            Description = "Strong delta on above-average volume, but the bar closed against it (effort without result).")]
        public bool UseAbsorption
        {
            get => _useAbsorption;
            set { _useAbsorption = value; RecalculateValues(); }
        }

        [Display(Name = "A · Min |delta| / volume", GroupName = "3. Signals", Order = 110)]
        [Range(0.01, 1)]
        public decimal AbsMinRatio
        {
            get => _absMinRatio;
            set { _absMinRatio = Clamp(value, 0.01m, 1m); RecalculateValues(); }
        }

        [Display(Name = "A · Min relative volume", GroupName = "3. Signals", Order = 120,
            Description = "1.2 = volume at least 120 % of the average.")]
        [Range(0, 20)]
        public decimal AbsMinRelVolume
        {
            get => _absMinRelVolume;
            set { _absMinRelVolume = Clamp(value, 0m, 20m); RecalculateValues(); }
        }

        [Display(Name = "A · Close position limit", GroupName = "3. Signals", Order = 130,
            Description = "Buyers absorbed: close in the lowest X of the range (0.40 = bottom 40 %). Mirror for sellers.")]
        [Range(0.05, 0.5)]
        public decimal AbsClosePosition
        {
            get => _absClosePosition;
            set { _absClosePosition = Clamp(value, 0.05m, 0.5m); RecalculateValues(); }
        }

        // --- Exhaustion

        [Display(Name = "E · Exhaustion", GroupName = "3. Signals", Order = 200,
            Description = "New N-bar high/low that the delta does not support, or made on thin volume.")]
        public bool UseExhaustion
        {
            get => _useExhaustion;
            set { _useExhaustion = value; RecalculateValues(); }
        }

        [Display(Name = "E · New high/low lookback (bars)", GroupName = "3. Signals", Order = 210)]
        [Range(2, 500)]
        public int ExhLookback
        {
            get => _exhLookback;
            set { _exhLookback = Math.Max(2, value); RecalculateValues(); }
        }

        [Display(Name = "E · Delta divergence", GroupName = "3. Signals", Order = 220,
            Description = "New high with delta/volume ≤ limit (buyers missing). Mirror for lows.")]
        public bool ExhDivergence
        {
            get => _exhDivergence;
            set { _exhDivergence = value; RecalculateValues(); }
        }

        [Display(Name = "E · Max supporting delta / volume", GroupName = "3. Signals", Order = 230)]
        [Range(-1, 1)]
        public decimal ExhMaxSupport
        {
            get => _exhMaxSupport;
            set { _exhMaxSupport = Clamp(value, -1m, 1m); RecalculateValues(); }
        }

        [Display(Name = "E · Thin volume", GroupName = "3. Signals", Order = 240,
            Description = "New high/low on relative volume ≤ limit.")]
        public bool ExhLowVolume
        {
            get => _exhLowVolume;
            set { _exhLowVolume = value; RecalculateValues(); }
        }

        [Display(Name = "E · Max relative volume", GroupName = "3. Signals", Order = 250)]
        [Range(0, 5)]
        public decimal ExhLowVolRatio
        {
            get => _exhLowVolRatio;
            set { _exhLowVolRatio = Clamp(value, 0m, 5m); RecalculateValues(); }
        }

        // --- Wick setup & ignition

        [Display(Name = "▲▼ Wick setup & ignition", GroupName = "3. Signals", Order = 300,
            Description = "Setup: VPOC inside a rejected wick with strong delta at the VPOC. " +
                          "Ignition: a following bar closes beyond the setup bar's body with delta in that direction.")]
        public bool UseWick
        {
            get => _useWick;
            set { _useWick = value; RecalculateValues(); }
        }

        [Display(Name = "▲▼ Show setup markers (•)", GroupName = "3. Signals", Order = 305)]
        public bool ShowSetups
        {
            get => _showSetups;
            set { _showSetups = value; RedrawChart(); }
        }

        [Display(Name = "▲▼ Min wick / range", GroupName = "3. Signals", Order = 310)]
        [Range(0.05, 0.95)]
        public decimal WickMinRatio
        {
            get => _wickMinRatio;
            set { _wickMinRatio = Clamp(value, 0.05m, 0.95m); RecalculateValues(); }
        }

        [Display(Name = "▲▼ Min wick (ticks)", GroupName = "3. Signals", Order = 320)]
        [Range(1, 200)]
        public int WickMinTicks
        {
            get => _wickMinTicks;
            set { _wickMinTicks = Math.Max(1, value); RecalculateValues(); }
        }

        [Display(Name = "▲▼ Min |delta| at VPOC", GroupName = "3. Signals", Order = 330,
            Description = "Contracts. ES RTH ~100+. Overnight / MES: lower.")]
        [Range(0, 100000)]
        public int WickMinVpocDelta
        {
            get => _wickMinVpocDelta;
            set { _wickMinVpocDelta = Math.Max(0, value); RecalculateValues(); }
        }

        [Display(Name = "▲▼ Min |delta| / volume at VPOC", GroupName = "3. Signals", Order = 340)]
        [Range(0, 1)]
        public decimal WickMinVpocRatio
        {
            get => _wickMinVpocRatio;
            set { _wickMinVpocRatio = Clamp(value, 0m, 1m); RecalculateValues(); }
        }

        [Display(Name = "▲▼ Ignition window (bars)", GroupName = "3. Signals", Order = 350,
            Description = "How many bars after the setup the move must start.")]
        [Range(1, 50)]
        public int IgnitionWindow
        {
            get => _ignitionWindow;
            set { _ignitionWindow = Math.Max(1, value); RecalculateValues(); }
        }

        [Display(Name = "▲▼ Ignition min delta / volume", GroupName = "3. Signals", Order = 360)]
        [Range(0, 1)]
        public decimal IgnitionMinRatio
        {
            get => _ignitionMinRatio;
            set { _ignitionMinRatio = Clamp(value, 0m, 1m); RecalculateValues(); }
        }

        #endregion

        #region Settings – 4. Signal colors

        [Display(Name = "▲ Bull ignition / • setup", GroupName = "4. Signal colors", Order = 10)]
        public Color BullIgnitionColor
        {
            get => _bullIgnitionColor;
            set { _bullIgnitionColor = value; RedrawChart(); }
        }

        [Display(Name = "▼ Bear ignition / • setup", GroupName = "4. Signal colors", Order = 20)]
        public Color BearIgnitionColor
        {
            get => _bearIgnitionColor;
            set { _bearIgnitionColor = value; RedrawChart(); }
        }

        [Display(Name = "A Sellers absorbed (bullish)", GroupName = "4. Signal colors", Order = 30)]
        public Color SellersAbsorbedColor
        {
            get => _sellersAbsorbedColor;
            set { _sellersAbsorbedColor = value; RedrawChart(); }
        }

        [Display(Name = "A Buyers absorbed (bearish)", GroupName = "4. Signal colors", Order = 40)]
        public Color BuyersAbsorbedColor
        {
            get => _buyersAbsorbedColor;
            set { _buyersAbsorbedColor = value; RedrawChart(); }
        }

        [Display(Name = "E Exhaustion", GroupName = "4. Signal colors", Order = 50)]
        public Color ExhaustionColor
        {
            get => _exhaustionColor;
            set { _exhaustionColor = value; RedrawChart(); }
        }

        #endregion

        #region Settings – 5. Layout

        [Display(Name = "Position", GroupName = "5. Layout", Order = 10)]
        public StripPosition Position
        {
            get => _position;
            set { _position = value; RedrawChart(); }
        }

        [Display(Name = "Strength lane height (px)", GroupName = "5. Layout", Order = 20)]
        [Range(1, 200)]
        public int StripHeight
        {
            get => _height;
            set { _height = Math.Max(1, value); RedrawChart(); }
        }

        [Display(Name = "Signal lane height (px)", GroupName = "5. Layout", Order = 25)]
        [Range(6, 60)]
        public int SignalHeight
        {
            get => _signalHeight;
            set
            {
                _signalHeight = Math.Clamp(value, 6, 60);
                _glyphFont = new RenderFont("Arial", Math.Max(6, _signalHeight - 7), FontStyle.Bold);
                RedrawChart();
            }
        }

        [Display(Name = "Offset (px)", GroupName = "5. Layout", Order = 30,
            Description = "Distance from the chart edge or from the candle low.")]
        [Range(0, 500)]
        public int Offset
        {
            get => _offset;
            set { _offset = Math.Max(0, value); RedrawChart(); }
        }

        [Display(Name = "Gap between cells (px)", GroupName = "5. Layout", Order = 40,
            Description = "Applied only when bars are at least 5 px wide.")]
        [Range(0, 20)]
        public int Gap
        {
            get => _gap;
            set { _gap = Math.Max(0, value); RedrawChart(); }
        }

        [Display(Name = "Draw border", GroupName = "5. Layout", Order = 50)]
        public bool DrawBorder
        {
            get => _drawBorder;
            set { _drawBorder = value; RedrawChart(); }
        }

        [Display(Name = "Border color", GroupName = "5. Layout", Order = 60)]
        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; RedrawChart(); }
        }

        #endregion

        #region Settings – 6. Labels

        [Display(Name = "Show delta values", GroupName = "6. Labels", Order = 10)]
        public bool ShowValues
        {
            get => _showValues;
            set { _showValues = value; RedrawChart(); }
        }

        [Display(Name = "Min cell width for text (px)", GroupName = "6. Labels", Order = 20)]
        [Range(5, 200)]
        public int MinWidthForText
        {
            get => _minWidthForText;
            set { _minWidthForText = Math.Max(5, value); RedrawChart(); }
        }

        [Display(Name = "Text color", GroupName = "6. Labels", Order = 30)]
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

        /// <summary>Order-flow event on a bar (for strategies / other indicators).</summary>
        public DeltaSignal GetSignal(int bar) =>
            bar >= 0 && bar < _signal.Count ? _signal[bar] : DeltaSignal.None;

        #region Calculation

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == 0)
            {
                _raw.Clear();
                _smoothed.Clear();
                _strength.Clear();
                _signal.Clear();
                _setup.Clear();
            }

            EnsureSize(bar);

            var candle = GetCandle(bar);

            // --- strength
            var raw = CalcRaw(bar, candle);
            _raw[bar] = raw;

            // EMA uses only the previous bar's stored value, so repeated calls
            // on the live bar stay correct.
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

            // --- signals (setup first: ignition on later bars depends on it)
            _setup[bar] = _useWick ? DetectWickSetup(candle) : 0;
            _signal[bar] = DetectSignal(bar, candle);
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

        private DeltaSignal DetectSignal(int bar, IndicatorCandle candle)
        {
            if (candle.Volume <= 0)
                return DeltaSignal.None;

            var ratio = candle.Delta / candle.Volume;
            var range = candle.High - candle.Low;
            var closePos = range > 0 ? (candle.Close - candle.Low) / range : 0.5m;
            var relVol = RelativeVolume(bar, candle.Volume);

            // 1. Ignition – a move confirmed after a wick setup (highest priority).
            if (_useWick)
            {
                var ignition = DetectIgnition(bar, candle, ratio, closePos);
                if (ignition != DeltaSignal.None)
                    return ignition;
            }

            // 2. Absorption – effort (delta + volume) without result (close against it).
            if (_useAbsorption && range > 0 && relVol >= _absMinRelVolume)
            {
                if (ratio >= _absMinRatio && closePos <= _absClosePosition)
                    return DeltaSignal.BuyersAbsorbed;

                if (ratio <= -_absMinRatio && closePos >= 1m - _absClosePosition)
                    return DeltaSignal.SellersAbsorbed;
            }

            // 3. Exhaustion – new N-bar extreme without support.
            if (_useExhaustion && bar >= _exhLookback)
            {
                decimal prevHigh = decimal.MinValue, prevLow = decimal.MaxValue;
                for (var i = bar - _exhLookback; i < bar; i++)
                {
                    var c = GetCandle(i);
                    prevHigh = Math.Max(prevHigh, c.High);
                    prevLow = Math.Min(prevLow, c.Low);
                }

                var thin = _exhLowVolume && relVol <= _exhLowVolRatio;

                if (candle.High > prevHigh && ((_exhDivergence && ratio <= _exhMaxSupport) || thin))
                    return DeltaSignal.UpExhaustion;

                if (candle.Low < prevLow && ((_exhDivergence && ratio >= -_exhMaxSupport) || thin))
                    return DeltaSignal.DownExhaustion;
            }

            // 4. Wick setup on this bar.
            if (_setup[bar] > 0)
                return DeltaSignal.BullWickSetup;

            if (_setup[bar] < 0)
                return DeltaSignal.BearWickSetup;

            return DeltaSignal.None;
        }

        /// <summary>+1 / -1 when the VPOC sits in a rejected lower / upper wick with strong delta at the VPOC.</summary>
        private int DetectWickSetup(IndicatorCandle candle)
        {
            var range = candle.High - candle.Low;
            if (range <= 0 || candle.Volume <= 0)
                return 0;

            var tick = Tick;
            var bodyLow = Math.Min(candle.Open, candle.Close);
            var bodyHigh = Math.Max(candle.Open, candle.Close);

            var levels = candle.GetAllPriceLevels();
            if (levels == null)
                return 0;

            PriceVolumeInfo vpoc = null;
            foreach (var lvl in levels)
            {
                if (vpoc == null || lvl.Volume > vpoc.Volume)
                    vpoc = lvl;
            }

            if (vpoc == null || vpoc.Volume <= 0)
                return 0;

            var vpocDelta = vpoc.Ask - vpoc.Bid;
            var strong = Math.Abs(vpocDelta) >= _wickMinVpocDelta
                         && Math.Abs(vpocDelta) / vpoc.Volume >= _wickMinVpocRatio
                         && vpocDelta != 0;

            if (!strong)
                return 0;

            var lowerWick = bodyLow - candle.Low;
            if (vpoc.Price < bodyLow && lowerWick >= _wickMinTicks * tick && lowerWick / range >= _wickMinRatio)
                return 1;

            var upperWick = candle.High - bodyHigh;
            if (vpoc.Price > bodyHigh && upperWick >= _wickMinTicks * tick && upperWick / range >= _wickMinRatio)
                return -1;

            return 0;
        }

        private DeltaSignal DetectIgnition(int bar, IndicatorCandle candle, decimal ratio, decimal closePos)
        {
            for (var k = 1; k <= _ignitionWindow; k++)
            {
                var b = bar - k;
                if (b < 0)
                    break;

                var dir = _setup[b];
                if (dir == 0)
                    continue;

                var setup = GetCandle(b);
                var fired = dir > 0 ? DeltaSignal.BullIgnition : DeltaSignal.BearIgnition;

                // Setup already used, or its wick extreme broken (failed rejection)?
                var dead = false;
                for (var j = b + 1; j < bar && !dead; j++)
                {
                    var cj = GetCandle(j);
                    dead = _signal[j] == fired || (dir > 0 ? cj.Low < setup.Low : cj.High > setup.High);
                }

                if (dead || (dir > 0 ? candle.Low < setup.Low : candle.High > setup.High))
                    continue;

                if (dir > 0
                    && candle.Close > Math.Max(setup.Open, setup.Close)
                    && ratio >= _ignitionMinRatio
                    && closePos >= 0.6m)
                    return DeltaSignal.BullIgnition;

                if (dir < 0
                    && candle.Close < Math.Min(setup.Open, setup.Close)
                    && ratio <= -_ignitionMinRatio
                    && closePos <= 0.4m)
                    return DeltaSignal.BearIgnition;
            }

            return DeltaSignal.None;
        }

        /// <summary>
        /// Volume / MEDIAN volume of the previous bars. The median is used on purpose: one
        /// huge bar (RTH open, news, a big wick) would inflate a mean for the whole window
        /// and make the following normal bars look "thin".
        /// </summary>
        private decimal RelativeVolume(int bar, decimal volume)
        {
            var start = Math.Max(0, bar - _volumeLookback);
            var n = bar - start;
            if (n <= 0)
                return 1m;

            if (_volBuffer.Length < n)
                _volBuffer = new decimal[Math.Max(n, _volumeLookback)];

            for (var i = 0; i < n; i++)
                _volBuffer[i] = GetCandle(start + i).Volume;

            Array.Sort(_volBuffer, 0, n);
            var median = n % 2 == 1
                ? _volBuffer[n / 2]
                : (_volBuffer[n / 2 - 1] + _volBuffer[n / 2]) / 2m;

            return median > 0 ? volume / median : 1m;
        }

        private decimal Tick
        {
            get
            {
                var t = InstrumentInfo?.TickSize ?? 0.25m;
                return t > 0 ? t : 0.25m;
            }
        }

        private void EnsureSize(int bar)
        {
            while (_raw.Count <= bar)
            {
                _raw.Add(0m);
                _smoothed.Add(0m);
                _strength.Add(0m);
                _signal.Add(DeltaSignal.None);
                _setup.Add(0);
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

            var lastSignalBar = _onlyClosedBars ? Math.Min(last, CurrentBar - 2) : last;
            var borderPen = _drawBorder ? new RenderPen(_borderColor, 1f) : null;

            // Two lanes: signals sit on the side closer to the price.
            const int laneGap = 2;
            var sigH = _showSignals ? _signalHeight : 0;
            var total = _height + (sigH > 0 ? sigH + laneGap : 0);
            var signalsFirst = _position != StripPosition.ChartTop;

            var fixedTop = _position == StripPosition.ChartTop
                ? region.Top + _offset
                : region.Bottom - _offset - total;

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

                var top = _position == StripPosition.BelowCandleLow
                    ? ChartInfo.GetYByPrice(GetCandle(bar).Low, false) + _offset
                    : fixedTop;

                var strengthY = signalsFirst && sigH > 0 ? top + sigH + laneGap : top;
                var signalY = signalsFirst ? top : top + _height + laneGap;

                var gap = width >= 5 ? _gap : 0;
                var cellWidth = Math.Max(1, width - gap);

                // --- strength lane
                var rect = new Rectangle(x, strengthY, cellWidth, _height);
                context.FillRectangle(StrengthColor(_strength[bar]), rect);

                if (borderPen != null)
                    context.DrawRectangle(borderPen, rect);

                if (_showValues && cellWidth >= _minWidthForText)
                    DrawValue(context, bar, rect);

                // --- signal lane
                if (sigH > 0 && bar <= lastSignalBar && _signal[bar] != DeltaSignal.None)
                {
                    var cell = new Rectangle(x, signalY, cellWidth, sigH);
                    DrawSignal(context, _signal[bar], cell);

                    // The bar is also a wick setup but shows another signal (A / E):
                    // keep the wick visible as a small corner dot.
                    if (_showSetups && _setup[bar] != 0 && IsAbsorptionOrExhaustion(_signal[bar]))
                        DrawCornerDot(context, _setup[bar] > 0 ? _bullIgnitionColor : _bearIgnitionColor, cell);
                }
            }
        }

        private void DrawSignal(RenderContext context, DeltaSignal signal, Rectangle cell)
        {
            switch (signal)
            {
                case DeltaSignal.BullWickSetup:
                case DeltaSignal.BearWickSetup:
                {
                    if (!_showSetups)
                        return;

                    // Small solid dot (square) – a "first sign", deliberately quieter than ignition.
                    var c = signal == DeltaSignal.BullWickSetup ? _bullIgnitionColor : _bearIgnitionColor;
                    var size = Math.Max(3, Math.Min(cell.Width, cell.Height) / 2);
                    context.FillRectangle(c, new Rectangle(
                        cell.X + (cell.Width - size) / 2, cell.Y + (cell.Height - size) / 2, size, size));
                    return;
                }

                case DeltaSignal.BullIgnition:
                case DeltaSignal.BearIgnition:
                {
                    var up = signal == DeltaSignal.BullIgnition;
                    var c = up ? _bullIgnitionColor : _bearIgnitionColor;
                    context.FillRectangle(c, cell);
                    FillTriangle(context, ContrastText(c), cell, up);
                    return;
                }

                default:
                {
                    var c = signal switch
                    {
                        DeltaSignal.SellersAbsorbed => _sellersAbsorbedColor,
                        DeltaSignal.BuyersAbsorbed => _buyersAbsorbedColor,
                        _ => _exhaustionColor,
                    };

                    context.FillRectangle(c, cell);

                    var glyph = signal is DeltaSignal.SellersAbsorbed or DeltaSignal.BuyersAbsorbed ? "A" : "E";
                    var size = context.MeasureString(glyph, _glyphFont);
                    if (size.Width <= cell.Width + 2)
                    {
                        context.DrawString(glyph, _glyphFont, ContrastText(c),
                            cell.X + (cell.Width - size.Width) / 2,
                            cell.Y + (cell.Height - size.Height) / 2);
                    }

                    return;
                }
            }
        }

        private static bool IsAbsorptionOrExhaustion(DeltaSignal s) =>
            s is DeltaSignal.SellersAbsorbed or DeltaSignal.BuyersAbsorbed
                or DeltaSignal.UpExhaustion or DeltaSignal.DownExhaustion;

        private static void DrawCornerDot(RenderContext context, Color color, Rectangle cell)
        {
            var size = Math.Max(3, Math.Min(cell.Width, cell.Height) / 3);
            var dot = new Rectangle(cell.Right - size, cell.Y, size, size);
            context.FillRectangle(Color.FromArgb(255, 20, 20, 24), new Rectangle(dot.X - 1, dot.Y, size + 1, size + 1));
            context.FillRectangle(color, dot);
        }

        /// <summary>Filled triangle drawn line by line (works on every renderer).</summary>
        private static void FillTriangle(RenderContext context, Color color, Rectangle cell, bool up)
        {
            var h = Math.Min(cell.Height - 4, cell.Width - 2);
            if (h < 3)
                return;

            var pen = new RenderPen(color, 1f);
            var cx = cell.X + cell.Width / 2;
            var top = cell.Y + (cell.Height - h) / 2;
            var halfBase = Math.Min(h * 0.6, (cell.Width - 3) / 2.0);

            for (var i = 0; i < h; i++)
            {
                var half = (int)Math.Round(halfBase * i / (h - 1));
                var y = up ? top + i : top + h - 1 - i;
                context.DrawLine(pen, cx - half, y, cx + half + 1, y);
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

        /// <summary>Bid ↔ Neutral ↔ Ask, in 3 steps or as a smooth gradient.</summary>
        private Color StrengthColor(decimal strength)
        {
            var t = (double)Clamp(strength, -1m, 1m);
            var a = Math.Abs(t);

            if (_colorMode == ColorMode.Steps && a > 0)
                a = a < 1.0 / 3 ? 0.55 : a < 2.0 / 3 ? 0.80 : 1.0;

            var c = t < 0
                ? Lerp(_neutralColor, _bidColor, a)
                : Lerp(_neutralColor, _askColor, a);

            var alpha = (int)Math.Round(c.A * (_opacity / 100.0));
            return Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);
        }

        private static Color ContrastText(Color background)
        {
            var luminance = 0.299 * background.R + 0.587 * background.G + 0.114 * background.B;
            return luminance > 150 ? Color.Black : Color.White;
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
