// TEMPLATE – rename the namespace, class, DisplayName and file, then replace the calculation.
//
// Shows the patterns that matter on ES:
//   * per-bar indexed state (OnCalculate re-runs on every tick of the live bar)
//   * optional session reset (RTH vs overnight volume differ by an order of magnitude)
//   * built-in series output + optional custom OnRender drawing
//   * settings with groups, ranges, Recalculate vs Redraw

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;

using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace MyIndicators
{
    [DisplayName("My ES Indicator")]
    [Category("Order Flow")]
    public class MyEsIndicator : Indicator
    {
        #region Fields

        // Output series (shown in the data window). Index = bar number.
        private readonly ValueDataSeries _value = new("Value")
        {
            Color = Color.DodgerBlue,
            VisualType = VisualMode.Histogram,
        };

        private int _period = 20;
        private bool _resetOnSession = true;
        private bool _drawMarkers = true;
        private decimal _markerThreshold = 0.35m;
        private Color _markerColor = Color.Gold;

        private readonly RenderFont _font = new("Arial", 8);

        #endregion

        #region Settings

        [Display(Name = "Period", GroupName = "1. Calculation", Order = 10)]
        [Range(1, 5000)]
        public int Period
        {
            get => _period;
            set { _period = Math.Max(1, value); RecalculateValues(); }
        }

        [Display(Name = "Reset on new session", GroupName = "1. Calculation", Order = 20,
            Description = "Restart the statistics at each session start (RTH/Globex per chart settings).")]
        public bool ResetOnSession
        {
            get => _resetOnSession;
            set { _resetOnSession = value; RecalculateValues(); }
        }

        [Display(Name = "Draw markers", GroupName = "2. Visual", Order = 10)]
        public bool DrawMarkers
        {
            get => _drawMarkers;
            set { _drawMarkers = value; RedrawChart(); }
        }

        [Display(Name = "Marker threshold", GroupName = "2. Visual", Order = 20)]
        [Range(0, 1)]
        public decimal MarkerThreshold
        {
            get => _markerThreshold;
            set { _markerThreshold = value; RedrawChart(); }
        }

        [Display(Name = "Marker color", GroupName = "2. Visual", Order = 30)]
        public Color MarkerColor
        {
            get => _markerColor;
            set { _markerColor = value; RedrawChart(); }
        }

        #endregion

        public MyEsIndicator() : base(true)
        {
            // Own panel for the series. For a price overlay use CandlesPanel.
            Panel = IndicatorDataProvider.NewPanel;

            DataSeries[0] = _value;

            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);

            // Example metric: delta as a fraction of volume, averaged over Period bars
            // (restarting at the session start if enabled).
            var ratio = candle.Volume > 0 ? candle.Delta / candle.Volume : 0m;

            var sum = ratio;
            var n = 1;
            for (var i = bar - 1; i >= 0 && n < _period; i--)
            {
                if (_resetOnSession && IsNewSession(i + 1))
                    break;

                var c = GetCandle(i);
                sum += c.Volume > 0 ? c.Delta / c.Volume : 0m;
                n++;
            }

            // Written by index, so re-running for the live bar just overwrites it.
            _value[bar] = sum / n;
        }

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (!_drawMarkers || layout != DrawingLayouts.Final)
                return;

            if (ChartInfo?.PriceChartContainer == null || Container == null)
                return;

            var first = Math.Max(0, FirstVisibleBarNumber);
            var last = Math.Min(LastVisibleBarNumber, CurrentBar - 1);

            for (var bar = first; bar <= last; bar++)
            {
                var v = _value[bar];
                if (Math.Abs(v) < _markerThreshold)
                    continue;

                // Small square at the top of this indicator's panel. For a price overlay
                // (Panel = CandlesPanel) anchor on price instead:
                //   var y = ChartInfo.GetYByPrice(GetCandle(bar).High, false) - 8;
                var x = ChartInfo.GetXByBar(bar);
                var y = Container.Region.Top + 2;
                context.FillRectangle(_markerColor, new Rectangle(x, y, 4, 4));
            }
        }
    }
}
