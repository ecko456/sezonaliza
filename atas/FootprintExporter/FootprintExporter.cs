// Footprint Exporter – ATAS / ATAS X indicator
//
// Writes the chart's loaded history to CSV so it can be analysed outside ATAS:
//
//   <instrument>_<yyyyMMdd_HHmm>_bars.csv    one row per bar
//       bar;time;last_time;open;high;low;close;volume;delta;max_delta;min_delta;levels
//   <instrument>_<yyyyMMdd_HHmm>_levels.csv  one row per traded price of each bar (footprint)
//       bar;price;bid;ask;volume
//   <instrument>_<yyyyMMdd_HHmm>_meta.txt    instrument, tick size, time zone info, range
//
// The export starts automatically once the history is calculated (only CLOSED bars).
// With "Keep appending" on, every newly closed bar is added to the same files.
// "Export now" re-exports on demand (works on weekends too).
// Status is shown in the top-left corner of the chart.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace FootprintExport
{
    [DisplayName("Footprint Exporter")]
    [Category("Order Flow")]
    public class FootprintExporter : Indicator
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

        #region Fields

        private string _folder = DefaultFolder();
        private int _days = 14;
        private bool _appendLive = true;
        private bool _showStatus = true;

        private bool _started;
        private int _lastWritten = -1;
        private string _barsPath, _levelsPath, _metaPath;
        private int _exportedBars;
        private DateTime _firstTime, _lastTime;
        private string _status = "Footprint Exporter: waiting for history…";

        private readonly List<PriceVolumeInfo> _levelBuf = new();
        private readonly RenderFont _font = new("Arial", 9);

        #endregion

        #region Settings

        [Display(Name = "Output folder", GroupName = "1. Export", Order = 10,
            Description = "Folder for the CSV files. Default: Documents\\ATAS_Export.")]
        public string Folder
        {
            get => _folder;
            set { _folder = string.IsNullOrWhiteSpace(value) ? DefaultFolder() : value.Trim(); Restart(); }
        }

        [Display(Name = "Days to export (0 = all loaded)", GroupName = "1. Export", Order = 20,
            Description = "Counted back from the last bar. The chart must have at least this many days loaded.")]
        [Range(0, 3650)]
        public int Days
        {
            get => _days;
            set { _days = Math.Max(0, value); Restart(); }
        }

        [Display(Name = "Keep appending new bars", GroupName = "1. Export", Order = 30,
            Description = "Add every newly closed bar to the same files while the chart is open.")]
        public bool AppendLive
        {
            get => _appendLive;
            set => _appendLive = value;
        }

        [Display(Name = "Export now (tick to run)", GroupName = "1. Export", Order = 40,
            Description = "Re-export immediately into new files. Works with a closed market too.")]
        public bool ExportNow
        {
            get => false; // never stored as true, so loading a template does not re-export
            set
            {
                if (!value)
                    return;

                Restart();
                if (CurrentBar >= 2)
                    StartExport(CurrentBar - 2);
            }
        }

        [Display(Name = "Show status on chart", GroupName = "2. Display", Order = 10)]
        public bool ShowStatus
        {
            get => _showStatus;
            set { _showStatus = value; RedrawChart(); }
        }

        #endregion

        public FootprintExporter() : base(true)
        {
            Panel = IndicatorDataProvider.CandlesPanel;
            DenyToChangePanel = true;

            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final);

            DataSeries[0].IsHidden = true;
            ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
        }

        #region Calculation / export

        protected override void OnCalculate(int bar, decimal value)
        {
            if (!_started)
            {
                // History is complete when the last bar is calculated; export all CLOSED bars.
                if (bar >= 1 && bar == CurrentBar - 1)
                    StartExport(bar - 1);

                return;
            }

            // A new bar started -> the previous one(s) are closed: append them.
            if (_appendLive && bar - 1 > _lastWritten)
                AppendBars(_lastWritten + 1, bar - 1);
        }

        private void Restart()
        {
            _started = false;
            _lastWritten = -1;
            _exportedBars = 0;
            _status = "Footprint Exporter: waiting for history…";
            RedrawChart();
        }

        private void StartExport(int lastClosedBar)
        {
            try
            {
                Directory.CreateDirectory(_folder);

                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm", Inv);
                var name = SafeFileName(InstrumentName());
                var basePath = Path.Combine(_folder, $"{name}_{stamp}");
                _barsPath = basePath + "_bars.csv";
                _levelsPath = basePath + "_levels.csv";
                _metaPath = basePath + "_meta.txt";

                File.WriteAllText(_barsPath,
                    "bar;time;last_time;open;high;low;close;volume;delta;max_delta;min_delta;levels\n", Utf8);
                File.WriteAllText(_levelsPath, "bar;price;bid;ask;volume\n", Utf8);

                // First bar inside the requested day range.
                var first = 0;
                if (_days > 0)
                {
                    var cutoff = GetCandle(lastClosedBar).Time.AddDays(-_days);
                    while (first < lastClosedBar && GetCandle(first).Time < cutoff)
                        first++;
                }

                _exportedBars = 0;
                _started = true;
                _lastWritten = first - 1;
                AppendBars(first, lastClosedBar);
                WriteMeta();
            }
            catch (Exception e)
            {
                _started = true; // do not retry on every tick
                _status = "Footprint Exporter: EXPORT FAILED – " + e.Message;
                RedrawChart();
            }
        }

        private void AppendBars(int from, int to)
        {
            if (to < from || _barsPath == null)
                return;

            try
            {
                var bars = new StringBuilder(256 * (to - from + 1));
                var levels = new StringBuilder(1024 * (to - from + 1));

                for (var bar = from; bar <= to; bar++)
                {
                    var c = GetCandle(bar);

                    _levelBuf.Clear();
                    var all = c.GetAllPriceLevels();
                    if (all != null)
                        _levelBuf.AddRange(all);
                    _levelBuf.Sort((a, b) => b.Price.CompareTo(a.Price)); // high -> low, like the footprint

                    bars.Append(bar).Append(';')
                        .Append(c.Time.ToString(TimeFormat, Inv)).Append(';')
                        .Append(FormatTime(Prop(c, "LastTime"))).Append(';')
                        .Append(D(c.Open)).Append(';').Append(D(c.High)).Append(';')
                        .Append(D(c.Low)).Append(';').Append(D(c.Close)).Append(';')
                        .Append(D(c.Volume)).Append(';').Append(D(c.Delta)).Append(';')
                        .Append(FormatDecimal(Prop(c, "MaxDelta"))).Append(';')
                        .Append(FormatDecimal(Prop(c, "MinDelta"))).Append(';')
                        .Append(_levelBuf.Count).Append('\n');

                    foreach (var l in _levelBuf)
                    {
                        levels.Append(bar).Append(';').Append(D(l.Price)).Append(';')
                            .Append(D(l.Bid)).Append(';').Append(D(l.Ask)).Append(';')
                            .Append(D(l.Volume)).Append('\n');
                    }

                    if (_exportedBars == 0)
                        _firstTime = c.Time;
                    _lastTime = c.Time;
                    _exportedBars++;
                }

                File.AppendAllText(_barsPath, bars.ToString(), Utf8);
                File.AppendAllText(_levelsPath, levels.ToString(), Utf8);
                _lastWritten = to;

                _status = $"Footprint Exporter: {_exportedBars:N0} bars " +
                          $"({_firstTime:yyyy-MM-dd HH:mm} → {_lastTime:yyyy-MM-dd HH:mm}) → {_barsPath}";
            }
            catch (Exception e)
            {
                _status = "Footprint Exporter: WRITE FAILED – " + e.Message;
            }

            RedrawChart();
        }

        private void WriteMeta()
        {
            try
            {
                var last = CurrentBar >= 1 ? GetCandle(CurrentBar - 1).Time : DateTime.MinValue;
                var sb = new StringBuilder();
                sb.AppendLine("exporter=FootprintExporter 1.0");
                sb.AppendLine("instrument=" + InstrumentName());
                sb.AppendLine("tick_size=" + D(InstrumentInfo?.TickSize ?? 0m));
                sb.AppendLine("exported_at_local=" + DateTime.Now.ToString(TimeFormat, Inv));
                sb.AppendLine("exported_at_utc=" + DateTime.UtcNow.ToString(TimeFormat, Inv));
                sb.AppendLine("local_time_zone=" + TimeZoneInfo.Local.Id);
                sb.AppendLine("local_utc_offset=" + TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));
                sb.AppendLine("candle_time_kind=" + last.Kind);
                sb.AppendLine("days_requested=" + _days);
                sb.AppendLine("bars_exported=" + _exportedBars);
                sb.AppendLine("first_bar_time=" + _firstTime.ToString(TimeFormat, Inv));
                sb.AppendLine("last_closed_bar_time=" + _lastTime.ToString(TimeFormat, Inv));
                sb.AppendLine("chart_bars_loaded=" + CurrentBar);
                sb.AppendLine("append_live=" + _appendLive);
                File.WriteAllText(_metaPath, sb.ToString(), Utf8);
            }
            catch
            {
                // meta is optional
            }
        }

        #endregion

        #region Rendering (status line)

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (!_showStatus || layout != DrawingLayouts.Final || Container == null)
                return;

            var text = _status;
            var size = context.MeasureString(text, _font);
            var x = Container.Region.Left + 6;
            var y = Container.Region.Top + 6;

            var failed = text.Contains("FAILED");
            context.FillRectangle(failed ? Color.FromArgb(220, 150, 20, 30) : Color.FromArgb(200, 20, 24, 32),
                new Rectangle(x - 4, y - 2, size.Width + 8, size.Height + 4));
            context.DrawString(text, _font, Color.White, x, y);
        }

        #endregion

        #region Helpers

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private static string DefaultFolder() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ATAS_Export");

        private static string D(decimal v) => v.ToString(Inv);

        private string InstrumentName()
        {
            // Read via reflection so the build does not depend on the exact property name.
            var name = Prop(InstrumentInfo, "Instrument") as string;
            return string.IsNullOrWhiteSpace(name) ? "instrument" : name;
        }

        private static string SafeFileName(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');
            return s;
        }

        // Cached reflection for optional members (MaxDelta, MinDelta, LastTime, Instrument).
        private static readonly Dictionary<(Type, string), MemberInfo> MemberCache = new();

        private static object Prop(object obj, string name)
        {
            if (obj == null)
                return null;

            var key = (obj.GetType(), name);
            if (!MemberCache.TryGetValue(key, out var m))
            {
                m = (MemberInfo)obj.GetType().GetProperty(name) ?? obj.GetType().GetField(name);
                MemberCache[key] = m;
            }

            try
            {
                return m switch
                {
                    PropertyInfo p => p.GetValue(obj),
                    FieldInfo f => f.GetValue(obj),
                    _ => null,
                };
            }
            catch
            {
                return null;
            }
        }

        private static string FormatDecimal(object v) =>
            v switch
            {
                decimal d => d.ToString(Inv),
                double d => d.ToString(Inv),
                int i => i.ToString(Inv),
                long l => l.ToString(Inv),
                _ => "",
            };

        private static string FormatTime(object v) =>
            v is DateTime t ? t.ToString(TimeFormat, Inv) : "";

        #endregion
    }
}
