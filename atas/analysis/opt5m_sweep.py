import os, sys, itertools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, pandas as pd
from opt5m_common import stats
from wa_model import wa_signals
pd.set_option('display.width', 250); pd.set_option('display.max_columns', 30)
b = pd.read_pickle('data/R5M_prep.pkl'); f = pd.read_pickle('data/R5M_wickfeat.pkl')
NATIVE = {'BullAbsorption': 1, 'BullInitiative': 1, 'BearAbsorption': -1, 'BearInitiative': -1}
slices = {'A': b.half == 'A', 'B': b.half == 'B', 'SO': b.regime == 'Sep/Oct', 'NV': b.regime == 'Nov'}
grid = list(itertools.product(['level', 'zone1', 'zone2', 'whole'], [100, 200, 300, 500, 800], [0.10, 0.15, 0.25, 0.35], [0.25, 0.35, 0.5], [0.1, 0.2, 0.3]))
rows = []
for src, md, mr, wr, wv in grid:
    sig = wa_signals(b, f, source=src, min_delta=md, min_ratio=mr, min_wick_ratio=wr, min_wick_vol_share=wv)
    for typ, d in NATIVE.items():
        dirs = (sig == typ).astype(int) * d
        r = dict(src=src, min_delta=md, ratio=mr, wick=wr, wickvol=wv, type=typ)
        for k, m in slices.items():
            st = stats(b, dirs, m, 3); r[f'n_{k}'] = st.get('n', 0); r[f'e_{k}'] = st.get('edge', np.nan)
        rows.append(r)
t = pd.DataFrame(rows); t.to_pickle('data/R5M_sweep.pkl')
print(f"{len(grid)} configs x 4 signal types")
for typ in NATIVE:
    x = t[(t.type == typ) & (t.n_A >= 10) & (t.n_B >= 10)].drop_duplicates(['n_A', 'n_B', 'e_A', 'e_B'])
    corr = x[['e_A', 'e_B']].corr().iloc[0, 1]
    both = ((x.e_A > 0) & (x.e_B > 0)).mean() * 100; neg = ((x.e_A < 0) & (x.e_B < 0)).mean() * 100
    print(f"\n### {typ} (native direction {'long' if NATIVE[typ] > 0 else 'short'}): {len(x)} distinct configs with n>=10 in both halves")
    print(f"    corr(edge A, edge B) = {corr:+.2f};  positive in both halves: {both:.0f} %;  negative in both: {neg:.0f} %")
    top = x.sort_values('e_A', ascending=False).head(5)
    print("    top 5 by half A ->", top[['src', 'min_delta', 'ratio', 'wick', 'wickvol', 'n_A', 'e_A', 'n_B', 'e_B', 'e_SO', 'e_NV']].round(2).to_string(index=False).replace('\n', '\n    '))
