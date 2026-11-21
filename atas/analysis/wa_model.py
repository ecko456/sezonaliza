"""Python port of WickAbsorption.cs (for fast parameter sweeps). Parity with the C# code is checked in opt5m_sweep.py."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, pandas as pd

def wick_features(b, l, tick=0.25):
    """Per bar: body, wicks, VPOC (highest price among ties, like the C# loop), and per-wick volume/delta,
    plus delta/volume at the VPOC level and in VPOC±1/±2 ticks restricted to the wick."""
    f = pd.DataFrame(index=b.index)
    f['range'] = b.high - b.low
    f['body_lo'] = b[['open', 'close']].min(axis=1); f['body_hi'] = b[['open', 'close']].max(axis=1)
    f['lw'] = f.body_lo - b.low; f['uw'] = b.high - f.body_hi
    L = l.merge(f[['body_lo', 'body_hi']], left_on='bar', right_index=True)
    L = L.sort_values(['bar', 'price'], ascending=[True, False])
    vp = L.loc[L.groupby('bar').volume.idxmax(), ['bar', 'price']].set_index('bar').price
    f['vpoc'] = vp
    L = L.merge(vp.rename('vpoc'), left_on='bar', right_index=True)
    L['in_lw'] = L.price < L.body_lo; L['in_uw'] = L.price > L.body_hi
    L['dist'] = ((L.price - L.vpoc).abs() / tick).round().astype(int)
    for side in ('lw', 'uw'):
        m = L[f'in_{side}']
        g = L[m].groupby('bar')
        f[f'{side}_vol'] = g.volume.sum(); f[f'{side}_delta'] = g.delta.sum()
        for z in (0, 1, 2):
            gz = L[m & (L.dist <= z)].groupby('bar')
            f[f'{side}_z{z}_vol'] = gz.volume.sum(); f[f'{side}_z{z}_delta'] = gz.delta.sum()
    return f.fillna(0)

def wa_signals(b, f, min_wick_ratio=0.35, min_wick_ticks=3, require_vpoc=True, min_wick_vol_share=0.20,
               source='level', min_delta=100, min_ratio=0.15, absorption=True, initiative=True, tick=0.25):
    """Returns a Series of signal names exactly like WickAbsorption.GetSignal()."""
    rng = f['range']; vol = b.volume
    out = pd.Series('None', index=b.index, dtype=object)
    res = {}
    for side, lower in (('lw', True), ('uw', False)):
        wick = f[side]
        vpoc_in = (f.vpoc < f.body_lo) if lower else (f.vpoc > f.body_hi)
        if source == 'whole':
            sd, sv = f[f'{side}_delta'], f[f'{side}_vol']
        else:
            z = {'level': 0, 'zone1': 1, 'zone2': 2}[source]
            sd, sv = f[f'{side}_z{z}_delta'], f[f'{side}_z{z}_vol']
        ok = (rng > 0) & (vol > 0) & (wick >= min_wick_ticks * tick) & (wick / rng.where(rng > 0, 1) >= min_wick_ratio)
        if require_vpoc: ok &= vpoc_in
        wv = f[f'{side}_vol']
        ok &= (wv > 0) & (wv / vol.where(vol > 0, 1) >= min_wick_vol_share)
        ad = sd.abs()
        ok &= (ad > 0) & (sv > 0) & (ad >= min_delta) & (ad / sv.where(sv > 0, 1) >= min_ratio)
        against = (sd < 0) if lower else (sd > 0)
        name_abs = 'BullAbsorption' if lower else 'BearAbsorption'
        name_ini = 'BullInitiative' if lower else 'BearInitiative'
        sig = pd.Series('None', index=b.index, dtype=object)
        if absorption: sig[ok & against] = name_abs
        if initiative: sig[ok & ~against] = name_ini
        res[side] = (sig, wv)
    bull, lowvol = res['lw']; bear, upvol = res['uw']
    take_bull = (bull != 'None') & ((bear == 'None') | (lowvol >= upvol))
    out[take_bull] = bull[take_bull]
    take_bear = (~take_bull) & (bear != 'None')
    out[take_bear] = bear[take_bear]
    return out
