import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, pandas as pd
from opt5m_common import stats
pd.set_option('display.width', 250)
b = pd.read_pickle('data/R5M_prep.pkl')
slices = {'ALL': None, 'A': b.half == 'A', 'B': b.half == 'B', 'SepOct': b.regime == 'Sep/Oct', 'Nov': b.regime == 'Nov'}
def row(name, dirs):
    r = {'variant': name}
    for k, m in slices.items():
        st = stats(b, dirs, m, 3); r[f'{k}_n'] = st.get('n', 0); r[f'{k}_e'] = st.get('edge', np.nan)
        if k == 'ALL': r.update(win=st.get('win'), t=st.get('t'))
    return r
ratio = b.delta / b.volume
rng = (b.high - b.low).replace(0, np.nan); clv = (b.close - b.low) / rng
# past-only percentile rank of |delta/vol| and of volume within the previous 200 bars (~2.5 RTH sessions)
def past_rank(x, w=200):
    arr = x.values; out = np.full(len(arr), np.nan)
    for i in range(w, len(arr)):
        out[i] = (arr[i-w:i] < arr[i]).mean()
    return pd.Series(out, index=x.index)
pr_ratio = past_rank(ratio.abs()); pr_vol = past_rank(b.volume)
rows = []
for p in (0.80, 0.90):
    for vq in (0.5, 0.7):
        base = (pr_ratio >= p) & (pr_vol >= vq)
        buyers_abs = base & (ratio > 0) & (clv <= 0.4)      # strong buying, closed low
        sellers_abs = base & (ratio < 0) & (clv >= 0.6)     # strong selling, closed high
        rev = sellers_abs.astype(int) - buyers_abs.astype(int)          # reversal reading
        rows.append(row(f"A reversal  |d%| pct>={p:.2f} vol pct>={vq:.1f}", rev))
        rows.append(row(f"A follow-delta  (same bars)", -rev))
for n in (6, 12, 24):
    hh = b.high.rolling(n).max().shift(1); ll = b.low.rolling(n).min().shift(1)
    same = b.sess_i == b.sess_i.shift(n)                     # lookback inside the session only
    for q in (0.3, 0.5):
        weak_up = (b.high > hh) & same & (pr_ratio.where(ratio > 0, 0) <= q)   # new high, buyers weak (or delta <= 0)
        weak_dn = (b.low < ll) & same & (pr_ratio.where(ratio < 0, 0) <= q)
        e = weak_dn.astype(int) - weak_up.astype(int)
        rows.append(row(f"E new {n}-bar extreme, supporting delta pct<={q}", e))
# current DSB E for reference
s = pd.read_csv('data/R5M_signals.csv', sep=';', keep_default_na=False).set_index('bar').reindex(b.index)
rows.append(row("E current DSB default", (s.dsb == 'DownExhaustion').astype(int) - (s.dsb == 'UpExhaustion').astype(int)))
print(pd.DataFrame(rows).round(2).to_string(index=False))
print("\nshare of bars flagged: current E %.1f %%" % (100 * s.dsb.str.contains('Exhaustion').mean()))
