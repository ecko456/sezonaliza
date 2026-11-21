"""Check the C# defaults against the Python study and report the final 5M numbers."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, pandas as pd
from opt5m_common import stats
from wa_model import wa_signals
pd.set_option('display.width', 250)
b = pd.read_pickle('data/R5M_prep.pkl'); f = pd.read_pickle('data/R5M_wickfeat.pkl')
s = pd.read_csv('data/R5M_signals.csv', sep=';', keep_default_na=False).set_index('bar').reindex(b.index)
py = wa_signals(b, f, source='zone1', min_delta=200)
print("WA C# default vs Python (zone ±1, |d|>=200): identical bars", int((py == s.wa).sum()), "/", len(b), "| signals", int((s.wa != 'None').sum()))
setups = s.dsb.isin(['BullWickSetup', 'BearWickSetup', 'BullIgnition', 'BearIgnition'])
print("DSB wick bars that are also WA signals:", int((setups & (s.wa != 'None')).sum()), "of", int(setups.sum()),
      "| WA signal bars hidden in DSB by A/ignition priority:", int(((s.wa != 'None') & ~setups).sum()))
wa_dir = s.wa_dir.astype(int)
dsb_dir = s.dsb.map(lambda x: 0 if x == 'None' else (1 if x.startswith(('Bull', 'Sellers', 'Down')) else -1))
slices = {'ALL': None, 'A': b.half == 'A', 'B': b.half == 'B', 'Sep/Oct': b.regime == 'Sep/Oct', 'Nov': b.regime == 'Nov'}
rows = []
def add(name, dirs):
    r = {'signal': name}
    for k, m in slices.items():
        st = stats(b, dirs, m, 3); r[f'{k} n'] = st.get('n', 0); r[f'{k} edge'] = st.get('edge', np.nan)
        if k == 'ALL': r.update(win=st.get('win'), t=st.get('t'), MFE=st.get('mfe'), MAE=st.get('mae'))
    rows.append(r)
add('Wick Absorption NEW default (expected direction)', wa_dir)
classic = s.wa_classic.map(lambda x: 0 if x == 'None' else (1 if x.startswith('Bull') else -1))
add('Wick Absorption OLD classic default', classic)
for sig in ['BullWickSetup', 'BearWickSetup', 'BullIgnition', 'BearIgnition', 'SellersAbsorbed', 'BuyersAbsorbed']:
    add(f'DSB new: {sig}', (s.dsb == sig).astype(int) * (1 if sig.startswith(('Bull', 'Sellers')) else -1))
add('DSB new: all wick setups (• both sides)', dsb_dir.where(s.dsb.str.contains('WickSetup'), 0))
add('DSB new: all A', dsb_dir.where(s.dsb.str.contains('Absorbed'), 0))
print("\\n(5M RTH, 40 sessions; edge = pts after 15 min in the expected direction minus random entry, same sample)")
print(pd.DataFrame(rows).round(2).to_string(index=False))
print("\\nDSB signal share of bars (new defaults):", (s.dsb.value_counts(normalize=True) * 100).round(2).to_dict())
print("DSB signal share of bars (old classic):  ", (s.dsb_classic.value_counts(normalize=True) * 100).round(2).to_dict())
