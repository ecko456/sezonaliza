import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pandas as pd
from opt5m_common import prep
from wa_model import wick_features, wa_signals
b, l = prep('data/R5M')
f = wick_features(b, l)
s = pd.read_csv('data/R5M_signals.csv', sep=';', keep_default_na=False).set_index('bar')
checks = {'wa': {}, 'wa_whole': dict(source='whole'), 'wa_zone': dict(source='zone1'), 'wa_d200': dict(min_delta=200), 'wa_r25': dict(min_ratio=0.25)}
for col, kw in checks.items():
    py = wa_signals(b, f, **kw); cs = s[col].reindex(b.index)
    print(f"{col:9s} C# signals {int((cs!='None').sum()):3d}  Python {int((py!='None').sum()):3d}  identical bars {int((py==cs).sum())}/{len(b)}")
f.to_pickle('data/R5M_wickfeat.pkl'); b.to_pickle('data/R5M_prep.pkl')
