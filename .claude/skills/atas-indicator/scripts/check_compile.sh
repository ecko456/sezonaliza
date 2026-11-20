#!/usr/bin/env bash
# Compile-check and smoke-test ATAS indicator source(s) against API stubs.
#
#   scripts/check_compile.sh path/to/MyIndicator.cs [more.cs ...]
#
# What it does:
#   1. builds the given .cs files together with scripts/stubs/AtasStubs.cs
#   2. instantiates every non-abstract Indicator subclass, feeds it ~600 synthetic
#      ES-like 1-min bars (session break every 390 bars, live bar re-calculated
#      several times), calls OnRender once, and reports exceptions.
#
# It cannot prove compatibility with the real ATAS DLLs – it catches C# errors,
# wrong usage of the API as described in references/api.md, and runtime crashes
# (division by zero, index errors) in OnCalculate/OnRender.

set -euo pipefail

if [ $# -lt 1 ]; then
  echo "usage: $0 Indicator.cs [more.cs ...]" >&2
  exit 2
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet not found. Install: apt-get install -y dotnet-sdk-8.0  (or 10.0)" >&2
  exit 3
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/atas-check.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

TFM="net$(dotnet --version | cut -d. -f1).0"

{
  echo '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
  echo "<OutputType>Exe</OutputType><TargetFramework>$TFM</TargetFramework>"
  echo '<LangVersion>latest</LangVersion><Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings>'
  echo '<NoWarn>CS0649;CS0169;CS0414</NoWarn></PropertyGroup><ItemGroup>'
  echo "<Compile Include=\"$HERE/stubs/AtasStubs.cs\" />"
  for f in "$@"; do
    echo "<Compile Include=\"$(cd "$(dirname "$f")" && pwd)/$(basename "$f")\" />"
  done
  echo '</ItemGroup><ItemGroup><PackageReference Include="System.Drawing.Common" Version="8.0.10" /></ItemGroup></Project>'
} > "$WORK/check.csproj"

cat > "$WORK/Harness.cs" <<'EOF'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ATAS.Indicators;

static class Harness
{
    static List<IndicatorCandle> Candles(int n)
    {
        var rnd = new Random(42);
        var list = new List<IndicatorCandle>();
        decimal price = 5800m;
        for (int i = 0; i < n; i++)
        {
            bool rth = (i % 390) >= 60 && (i % 390) < 300;     // crude RTH vs ETH volume regime
            bool empty = i % 50 == 7;                            // zero-volume bar
            decimal open = price;
            price += rnd.Next(-8, 9) * 0.25m;
            var c = new IndicatorCandle
            {
                Open = open, Close = price,
                High = Math.Max(open, price) + rnd.Next(0, 9) * 0.25m,
                Low = Math.Min(open, price) - rnd.Next(0, 9) * 0.25m,
                Time = new DateTime(2026, 11, 2, 0, 0, 0).AddMinutes(i),
            };

            // Footprint: volume per 0.25 level; candle totals are derived from it.
            if (!empty)
            {
                for (var p = c.Low; p <= c.High; p += 0.25m)
                {
                    int v = rnd.Next(rth ? 50 : 2, rth ? 900 : 60);
                    int a = rnd.Next(0, v + 1);
                    c.StubLevels.Add(new PriceVolumeInfo { Price = p, Volume = v, Ask = a, Bid = v - a });
                    c.Volume += v; c.Ask += a; c.Bid += v - a;
                }
            }
            c.Delta = c.Ask - c.Bid;
            c.Ticks = c.Volume / 3;
            list.Add(c);
        }
        return list;
    }

    static int Main()
    {
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(Indicator).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .ToList();

        if (types.Count == 0) { Console.WriteLine("No Indicator subclasses found."); return 1; }

        int failures = 0;
        foreach (var t in types)
        {
            try
            {
                var ind = (Indicator)Activator.CreateInstance(t);
                ind.StubRun(Candles(600), sessionLength: 390);
                int draws = ind.StubRender();
                Console.WriteLine($"OK   {t.FullName}: calculated 600 bars, OnRender issued {draws} draw calls");
            }
            catch (Exception e)
            {
                failures++;
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                Console.WriteLine($"FAIL {t.FullName}: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
            }
        }
        return failures == 0 ? 0 : 1;
    }
}
EOF

cd "$WORK"
if ! dotnet build -nologo -v q -clp:NoSummary > build.log 2>&1; then
  grep -E "error|warning CS" build.log | sed "s#$WORK/##" | sort -u
  echo "BUILD FAILED"
  exit 1
fi
grep -E "warning CS" build.log | sort -u || true
dotnet bin/Debug/$TFM/check.dll
