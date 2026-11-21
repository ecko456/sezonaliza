"""Forward-return evaluation of indicator signals on Footprint Exporter data (see README)."""
import sys, numpy as np, pandas as pd
import os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '.claude', 'skills', 'atas-indicator', 'scripts'))
from load_footprint import load
pd.set_option('display.width', 250); pd.set_option('display.max_rows', 200)
BULL = {'BullAbsorption','BullInitiative','BullIgnition','SellersAbsorbed','DownExhaustion','BullWickSetup'}
BEAR = {'BearAbsorption','BearInitiative','BearIgnition','BuyersAbsorbed','UpExhaustion','BearWickSetup'}

def prep(prefix, step_min):
    b,_,_ = load(prefix + '_bars.csv')
    s = pd.read_csv(prefix + '_signals.csv', sep=';', keep_default_na=False).set_index('bar')
    b = b.join(s)
    b['sess'] = (b.time + pd.Timedelta(hours=1)).dt.date          # Globex day (starts 23:00 UTC)
    m = b.time.dt.hour*60 + b.time.dt.minute
    b['rth'] = (m >= 14*60+30) & (m < 21*60)
    pos = np.arange(len(b)); sess = b.sess.values; c = b.close.values; h = b.high.values; l = b.low.values
    for mins in (5,15,30):
        k = mins // step_min
        j = pos + k; ok = (j < len(b))
        jj = np.where(ok, j, 0); ok &= sess[jj] == sess
        b[f'f{mins}'] = np.where(ok, c[jj] - c, np.nan)
    k = 15 // step_min
    mfe = np.full(len(b), np.nan); mae = np.full(len(b), np.nan)
    for i in range(len(b)-k):
        if sess[i+k] != sess[i]: continue
        mfe[i] = h[i+1:i+k+1].max() - c[i]; mae[i] = c[i] - l[i+1:i+k+1].min()
    b['up15'] = mfe; b['dn15'] = mae
    # price-only wick bars for comparison (what the OHLC export showed)
    rng = b.high - b.low; lw = b[['open','close']].min(axis=1) - b.low; uw = b.high - b[['open','close']].max(axis=1)
    p75 = rng.groupby(b.rth).transform(lambda x: x.quantile(.75))
    b['price_wick'] = np.where((lw >= .5*rng) & (rng >= p75) & (rng > 0), 'BullPriceWick', np.where((uw >= .5*rng) & (rng >= p75) & (rng > 0), 'BearPriceWick', 'None'))
    return b

def table(b, col, label):
    rows = []
    base = {r: {m: b.loc[b.rth==r, f'f{m}'].mean() for m in (5,15,30)} for r in (True, False)}
    for sig, g in b[b[col] != 'None'].groupby(col):
        d = 1 if (sig in BULL or sig.startswith('Bull')) else -1
        for sesname, gg in (('ALL', g), ('RTH', g[g.rth]), ('ETH', g[~g.rth])):
            gg = gg.dropna(subset=['f15'])
            if len(gg) == 0: continue
            r = {'signal': sig, 'sess': sesname, 'n': len(gg)}
            for mm in (5,15,30):
                v = d*gg[f'f{mm}']; bl = np.where(gg.rth, d*base[True][mm], d*base[False][mm])
                r[f'avg{mm}'] = v.mean(); r[f'edge{mm}'] = (v - bl).mean()
            v15 = d*gg['f15']; r['win15%'] = 100*(v15 > 0).mean()
            r['t15'] = (v15 - np.where(gg.rth, d*base[True][15], d*base[False][15])).mean() / (v15.std(ddof=1)/np.sqrt(len(gg))) if len(gg) > 2 else np.nan
            fav = gg.up15 if d > 0 else gg.dn15; adv = gg.dn15 if d > 0 else gg.up15
            r['MFE15'] = fav.mean(); r['MAE15'] = adv.mean()
            rows.append(r)
    t = pd.DataFrame(rows)
    print(f"\n### {label} [{col}]   (points; avg = move in signal direction; edge = avg minus random entry same direction & session)")
    if len(t): print(t.round(2).to_string(index=False))

# usage: python3 -I evaluate.py data/ES1M:1 data/ES5M:5   (prefix:bar minutes; needs <prefix>_signals.csv from Replay)
for arg in sys.argv[1:]:
    prefix, step = arg.rsplit(':', 1); step = int(step)
    b = prep(prefix, step)
    print(f"\n================ {prefix}: {len(b)} bars, {b.sess.nunique()} sessions, close {b.close.iloc[0]} -> {b.close.iloc[-1]}")
    print("random-entry drift (pts, long): RTH f15 %.2f f30 %.2f | ETH f15 %.2f f30 %.2f" % (b.loc[b.rth,'f15'].mean(), b.loc[b.rth,'f30'].mean(), b.loc[~b.rth,'f15'].mean(), b.loc[~b.rth,'f30'].mean()))
    print("avg bar range RTH %.2f ETH %.2f; median volume RTH %d ETH %d" % ((b.high-b.low)[b.rth].mean(), (b.high-b.low)[~b.rth].mean(), b.volume[b.rth].median(), b.volume[~b.rth].median()))
    for col, lab in (('wa','Wick Absorption – default (VPOC level, |d|>=100, 15%)'), ('dsb','Delta Strength Bar – default'), ('price_wick','Price-only wick (no order flow)')):
        table(b, col, lab)
    b.to_pickle(prefix + '_eval.pkl')
