"""Current indicator settings on 5M RTH data (signals from Replay.cs)."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pandas as pd, numpy as np
from opt5m_common import prep, stats, direction
pd.set_option('display.width', 250)
prefix = sys.argv[1] if len(sys.argv) > 1 else 'data/R5M'
b, l = prep(prefix)
s = pd.read_csv(prefix + '_signals.csv', sep=';', keep_default_na=False).set_index('bar')
b = b.join(s)
print(f"{len(b)} bars, {b.sess.nunique()} sessions; random-entry drift per 15 min: all {b.f3.mean():+.2f}, "
      f"Sep/Oct {b[b.regime=='Sep/Oct'].f3.mean():+.2f}, Nov {b[b.regime=='Nov'].f3.mean():+.2f} pts")
rows = []
for col in ['wa', 'wa_whole', 'wa_zone', 'wa_d200', 'wa_r25', 'dsb']:
    for sig in sorted(set(b[col]) - {'None'}):
        dirs = (b[col] == sig).astype(int) * direction(sig)
        r = {'set': col, 'signal': sig}
        for name, m in (('ALL', None), ('A', b.half == 'A'), ('B', b.half == 'B'), ('SepOct', b.regime == 'Sep/Oct'), ('Nov', b.regime == 'Nov')):
            st = stats(b, dirs, m, 3)
            r[f'{name}_n'] = st.get('n', 0); r[f'{name}_edge'] = st.get('edge', np.nan)
            if name == 'ALL': r.update(win=st.get('win'), t=st.get('t'), mfe=st.get('mfe'), mae=st.get('mae'))
        rows.append(r)
print("\nedge = points in signal direction after 15 min (3 bars) minus random entry in the same direction & sample")
print(pd.DataFrame(rows).round(2).to_string(index=False))
