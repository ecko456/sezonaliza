using System; using System.Collections.Generic; using System.Globalization; using System.IO; using System.Linq;
using ATAS.Indicators; using DeltaStrength; using WickAbsorptionIndicator;
static class R {
  static decimal Dm(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);
  static void Main(string[] a) {
    string prefix = a[0], outp = a[1];
    var bars = File.ReadLines(prefix + "_bars.csv").Skip(1).Select(l => l.Split(';')).ToList();
    var byBar = new Dictionary<int, IndicatorCandle>(); var order = new List<int>();
    foreach (var f in bars) {
      int id = int.Parse(f[0]);
      byBar[id] = new IndicatorCandle { Time = DateTime.Parse(f[1], CultureInfo.InvariantCulture), Open = Dm(f[3]), High = Dm(f[4]), Low = Dm(f[5]), Close = Dm(f[6]), Volume = Dm(f[7]), Delta = Dm(f[8]) };
      order.Add(id); }
    foreach (var l in File.ReadLines(prefix + "_levels.csv").Skip(1)) {
      var f = l.Split(';'); var c = byBar[int.Parse(f[0])];
      decimal bid = Dm(f[2]), ask = Dm(f[3]);
      c.StubLevels.Add(new PriceVolumeInfo { Price = Dm(f[1]), Bid = bid, Ask = ask, Volume = Dm(f[4]) });
      c.Ask += ask; c.Bid += bid; }
    var candles = order.Select(i => byBar[i]).ToList();
    var cols = new List<(string name, Func<int, string> get)>();
    void Dsb(string name, Action<DeltaStrengthBar> cfg) { var x = new DeltaStrengthBar(); cfg(x); x.StubRun(candles, 0, 0); cols.Add((name, i => x.GetSignal(i).ToString())); }
    void Wa(string name, Action<WickAbsorption> cfg) { var x = new WickAbsorption(); cfg(x); x.StubRun(candles, 0, 0); cols.Add((name, i => x.GetSignal(i).ToString())); }
    Dsb("dsb", x => { });
    Dsb("dsb_vpoc50", x => { x.WickMinVpocDelta = 50; });
    Dsb("dsb_vpoc200", x => { x.WickMinVpocDelta = 200; });
    Dsb("dsb_abs30", x => { x.AbsMinRatio = 0.30m; x.AbsMinRelVolume = 1.5m; });
    Wa("wa", x => { });
    Wa("wa_whole", x => { x.DeltaSource = DeltaSource.WholeWick; });
    Wa("wa_zone", x => { x.DeltaSource = DeltaSource.VpocZone; });
    Wa("wa_d50", x => { x.MinWickDelta = 50; });
    Wa("wa_d200", x => { x.MinWickDelta = 200; });
    Wa("wa_r25", x => { x.MinWickDeltaRatio = 0.25m; });
    Wa("wa_novpoc", x => { x.RequireVpocInWick = false; });
    using var w = new StreamWriter(outp);
    w.WriteLine("bar;" + string.Join(";", cols.Select(c => c.name)));
    for (int i = 0; i < candles.Count; i++) w.WriteLine(order[i] + ";" + string.Join(";", cols.Select(c => c.get(i))));
    Console.WriteLine($"{prefix}: {candles.Count} bars, ask/bid rebuilt from levels; signals written to {outp}");
  } }
