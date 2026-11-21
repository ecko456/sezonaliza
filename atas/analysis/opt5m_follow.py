import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, pandas as pd
from opt5m_common import stats
from wa_model import wa_signals
pd.set_option('display.width', 250)
b = pd.read_pickle('data/R5M_prep.pkl'); f = pd.read_pickle('data/R5M_wickfeat.pkl')
slices = {'ALL': None, 'A': b.half == 'A', 'B': b.half == 'B', 'SepOct': b.regime == 'Sep/Oct', 'Nov': b.regime == 'Nov'}
def row(name, dirs):
    r = {'variant': name}
    for k, m in slices.items():
        st = stats(b, dirs, m, 3); r[f'{k}_n'] = st.get('n', 0); r[f'{k}_edge'] = st.get('edge', np.nan)
        if k == 'ALL': r.update(win=st.get('win'), t=st.get('t'), mfe=st.get('mfe'), mae=st.get('mae'))
    return r
rows = []
# --- follow the delta at the wick VPOC (all four WA types, direction = sign of the measured delta)
def follow_vpoc(src, **kw):
    sig = wa_signals(b, f, source=src, **kw)
    z = {'level': 0, 'zone1': 1, 'zone2': 2}.get(src)
    lw_d = f['lw_delta'] if src == 'whole' else f[f'lw_z{z}_delta']; uw_d = f['uw_delta'] if src == 'whole' else f[f'uw_z{z}_delta']
    d = np.where(sig.str.startswith('Bull'), np.sign(lw_d), np.where(sig.str.startswith('Bear'), np.sign(uw_d), 0))
    return pd.Series(d, index=b.index).astype(int)
for src, kw in [('level', {}), ('level', dict(min_delta=200)), ('level', dict(min_delta=300, min_ratio=0.25)),
                ('zone1', {}), ('zone1', dict(min_delta=200)), ('zone1', dict(min_delta=300, min_ratio=0.25))]:
    rows.append(row(f"FOLLOW wick-VPOC delta [{src} {kw or 'default'}]", follow_vpoc(src, **kw)))
# --- controls: follow the bar's own delta, no wick / VPOC condition, matched frequency
ratio = b.delta / b.volume
for q in (0.90, 0.95, 0.97):
    thr = ratio.abs().quantile(q)
    rows.append(row(f"control: follow BAR delta, |delta/vol| top {100-q*100:.0f}% (>= {thr:.3f})", (np.sign(ratio) * (ratio.abs() >= thr)).astype(int)))
# --- control: follow VPOC-level delta of ANY bar (VPOC anywhere) with the same thresholds as default
L = pd.read_csv('data/R5M_levels.csv', sep=';'); L['delta'] = L.ask - L.bid
L = L.sort_values(['bar', 'price'], ascending=[True, False]); vp = L.loc[L.groupby('bar').volume.idxmax()].set_index('bar').reindex(b.index)
ok = (vp.delta.abs() >= 100) & (vp.delta.abs() / vp.volume >= 0.15)
rows.append(row("control: follow VPOC delta of ANY bar (|d|>=100, >=15%)", (np.sign(vp.delta) * ok).astype(int)))
ok2 = ok & ~((f.vpoc < f.body_lo) | (f.vpoc > f.body_hi))
rows.append(row("control: same, but VPOC inside the BODY", (np.sign(vp.delta) * ok2).astype(int)))
print("edge = pts in signal direction after 15 min minus same-direction random entry; t is rough (overlap ignored)")
print(pd.DataFrame(rows).round(2).to_string(index=False))
