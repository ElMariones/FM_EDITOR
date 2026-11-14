"""Fit the game's per-position current-ability weights from every player in a live save.

CA ~ const_p + sum_k w_pk * attr_k, attributes on the game's internal 1-100 scale, for players whose
only natural position (rating 20) is p. Left and right sides share a model. Attributes that cost no
ability anywhere (hidden ones, goalkeeper tendencies) are dropped. Small groups are pulled toward the
pooled outfield fit (ridge). Writes ca_weights.json: weights per *displayed* point (x5).
"""
import os, pickle, json, numpy as np
here = os.path.dirname(__file__)
rows = pickle.load(open(os.path.join(here, 'ca_rows.pkl'), 'rb'))
A = np.array([list(r[1]) for r in rows], dtype=float)
CA = np.array([r[2] for r in rows], dtype=float)
POS = A[:, :15]
d = json.load(open(r'C:\Users\mario\Desktop\PROYECTOS\FM_EDITOR\definitions\fm27-27.0.1.json'))
key = {}
for k, v in d['structs']['Player'].items():
    if isinstance(v, dict) and v.get('type') == 'u8' and isinstance(v.get('offset'), str) and v['offset'].startswith('-'):
        o = -int(v['offset'][1:], 16) + 0x138
        if 0 <= o < 72: key[o] = k
POSKEY = {0: 'Pos.GK', 2: 'Pos.DL', 3: 'Pos.DC', 4: 'Pos.DR', 5: 'Pos.DM', 6: 'Pos.ML', 7: 'Pos.MC', 8: 'Pos.MR',
          9: 'Pos.AML', 10: 'Pos.AMC', 11: 'Pos.AMR', 12: 'Pos.ST', 13: 'Pos.WBL', 14: 'Pos.WBR'}
assert all(key[i] == k for i, k in POSKEY.items()), [(i, key.get(i)) for i in POSKEY]
GROUPS = [[0], [2, 4], [3], [5], [6, 8], [7], [9, 11], [10], [12], [13, 14]]
ALL = list(range(15, 69))
single = (CA > 0) & ((POS == 20).sum(1) == 1)

def lstsq(X, y, prior=None, lam=0.0):
    X1 = np.hstack([X, np.ones((len(y), 1))])
    if prior is None: return np.linalg.lstsq(X1, y, rcond=None)[0]
    R = np.sqrt(lam) * np.eye(X1.shape[1]); R[-1, -1] = 0
    return np.linalg.lstsq(np.vstack([X1, R]), np.concatenate([y, R @ prior]), rcond=None)[0]

def mask(g): return single & np.isin(POS.argmax(1), g)
# 1. Which attributes cost ability at all: weight under 0.02 per internal point for every big group.
keepers = mask([0]); out = single & ~keepers
def used(m):
    w = lstsq(A[m][:, ALL], CA[m])[:-1]
    return {ALL[j] for j in range(len(ALL)) if w[j] > 0.02}
used_gk = used(keepers)
from collections import Counter
votes = Counter()
for g in ([3], [7], [12], [2, 4], [9, 11], [10], [5]): votes.update(used(mask(g)))
GK_ONLY = {'Attr.Handling', 'Attr.AerialReach', 'Attr.CommandOfArea', 'Attr.Communication', 'Attr.Kicking', 'Attr.OneOnOnes',
           'Attr.Reflexes', 'Attr.RushingOut', 'Attr.Punching', 'Attr.Throwing', 'Attr.Eccentricity'}
HIDDEN = {'Attr.Versatility', 'Attr.Consistency', 'Attr.Dirtiness', 'Attr.ImportantMatches', 'Attr.InjuryProneness'}
used_out = {i for i, n in votes.items() if n >= 2 and key[i] not in GK_ONLY | HIDDEN}
used_gk = {i for i in used_gk if key[i] not in HIDDEN}
print('goalkeeper attributes:', sorted(key[i] for i in used_gk))
print('outfield attributes  :', sorted(key[i] for i in used_out))
# 2. Pooled outfield prior, then each group pulled toward it.
OUT = sorted(used_out)
prior = lstsq(A[out][:, OUT], CA[out])
model = {}
rng = np.random.default_rng(1)
for g in GROUPS:
    m = mask(g); cols = sorted(used_gk) if g == [0] else OUT
    X, y = A[m][:, cols], CA[m]
    best = None
    for lam in ([0.0] if g == [0] else [0, 1e3, 1e4, 3e4, 1e5, 3e5, 1e6]):
        idx = rng.permutation(len(y)); folds = np.array_split(idx, 5); err = []
        for f in folds:
            tr = np.setdiff1d(idx, f)
            w = lstsq(X[tr], y[tr], None if g == [0] else prior, lam)
            err.append(np.hstack([X[f], np.ones((len(f), 1))]) @ w - y[f])
        e = np.sqrt(np.mean(np.concatenate(err) ** 2))
        if best is None or e < best[0]: best = (e, lam)
    w = lstsq(X, y, None if g == [0] else prior, best[1])
    print('%-14s n=%5d  held-out rms %.2f  (ridge %g)' % ('/'.join(POSKEY[i] for i in g), len(y), best[0], best[1]))
    entry = {'Const': round(float(w[-1]), 3), 'Weights': {key[c]: round(float(w[j]) * 5, 4) for j, c in enumerate(cols) if abs(w[j]) > 1e-4}}
    for i in g: model[POSKEY[i]] = entry
json.dump(model, open(os.path.join(here, 'ca_weights.json'), 'w'), indent=1)
# 3. How a player with several natural positions fits: max over them.
def predict(i):
    best = None
    for p, k in POSKEY.items():
        if POS[i, p] != 20: continue
        e = model[k]; v = e['Const'] + sum(wt / 5 * A[i, [o for o, kk in key.items() if kk == a][0]] for a, wt in e['Weights'].items())
        best = v if best is None else max(best, v)
    return best
multi = np.nonzero((CA > 0) & ((POS == 20).sum(1) >= 2))[0][:1500]
pm = np.array([predict(i) for i in multi])
print('players with 2+ natural positions: rms(max) %.2f mean err %+.2f' % (np.sqrt(np.mean((pm - CA[multi]) ** 2)), np.mean(pm - CA[multi])))
