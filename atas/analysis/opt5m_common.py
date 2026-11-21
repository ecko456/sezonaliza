"""Shared helpers for the 5M RTH optimisation (ET session clock, forward returns, splits)."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sys, os, numpy as np, pandas as pd
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '.claude', 'skills', 'atas-indicator', 'scripts'))
from load_footprint import load

BULL_PREFIX = ('Bull', 'Sellers', 'Down')   # BullX, SellersAbsorbed, DownExhaustion
def direction(sig):
    if sig in ('None', '', None) or (isinstance(sig, float) and np.isnan(sig)): return 0
    return 1 if sig.startswith(BULL_PREFIX) else -1

def prep(prefix):
    b, l, _ = load(prefix + '_bars.csv')
    et = b.time.dt.tz_localize('UTC').dt.tz_convert('America/New_York')
    b['et_min'] = et.dt.hour * 60 + et.dt.minute
    b['sess'] = et.dt.date
    sessions = sorted(b.sess.unique())
    idx = {s: i for i, s in enumerate(sessions)}
    b['sess_i'] = b.sess.map(idx)
    b['half'] = np.where(b.sess_i % 2 == 0, 'A', 'B')               # interleaved split
    b['month'] = pd.to_datetime(b.sess.astype(str)).dt.strftime('%b')
    b['regime'] = np.where(b.month == 'Nov', 'Nov', 'Sep/Oct')
    pos = np.arange(len(b)); sess = b.sess_i.values; c = b.close.values; h = b.high.values; lo = b.low.values
    for k in (1, 3, 6):
        j = pos + k; ok = j < len(b); jj = np.where(ok, j, 0); ok &= sess[jj] == sess
        b[f'f{k}'] = np.where(ok, c[jj] - c, np.nan)
    for k in (3, 6):
        mfe = np.full(len(b), np.nan); mae = np.full(len(b), np.nan)
        for i in range(len(b) - k):
            if sess[i + k] != sess[i]: continue
            mfe[i] = h[i+1:i+k+1].max() - c[i]; mae[i] = c[i] - lo[i+1:i+k+1].min()
        b[f'up{k}'] = mfe; b[f'dn{k}'] = mae
    return b, l

def stats(b, dirs, mask=None, k=3):
    """dirs: Series of +1/-1/0 per bar. Returns dict with n, avg, edge (vs same-direction random entry), win, t, MFE/MAE."""
    sel = (dirs != 0) & b[f'f{k}'].notna()
    if mask is not None: sel &= mask
    if sel.sum() == 0: return dict(n=0)
    d = dirs[sel]; v = d * b.loc[sel, f'f{k}']
    pool = b[f'f{k}'][mask if mask is not None else slice(None)].mean()
    e = v - d * pool
    fav = np.where(d > 0, b.loc[sel, 'up3'], b.loc[sel, 'dn3']); adv = np.where(d > 0, b.loc[sel, 'dn3'], b.loc[sel, 'up3'])
    n = int(sel.sum())
    return dict(n=n, avg=v.mean(), edge=e.mean(), win=100 * (v > 0).mean(),
                t=e.mean() / (v.std(ddof=1) / np.sqrt(n)) if n > 2 else np.nan,
                mfe=np.nanmean(fav), mae=np.nanmean(adv))
