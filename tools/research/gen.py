"""Derive typed field definitions from fields.json, validate against live memory, write gen.json."""
import sys, os, json, struct, re
from collections import Counter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from memlib import *
p, pe, cl, h = ctx()

# app kind -> (class, vtable offset whose slot address is the record base, analysis groups)
ENT = {
    'Player': ('db::ACTUAL_PLAYER', 0x288, ['Player', 'Person']),
    'Staff': ('db::ACTUAL_NON_PLAYER', 0x100, ['Staff', 'Person']),
    'Club': ('db::CLUB', 0, ['Club']),
    'Competition': ('db::COMP', 0, ['Competition']),
    'Nation': ('db::NATION', 0, ['Nation']),
    'Stadium': ('db::STADIUM', 0, ['Stadium']),
    'City': ('db::CITY', 0, ['City']),
    'Team': ('db::TEAM', 0, ['Team']),
    'Continent': ('db::CONTINENT', 0, ['Continent']),
    'LocalRegion': ('db::LOCAL_REGION', 0, ['LocalRegion']),
    'Award': ('db::AWARD', 0, ['Award']),
    'Injury': ('db::INJURY', 0, ['Injury']),
    'Language': ('db::LANGUAGE', 0, ['Language']),
    'MediaSource': ('db::MEDIA_SOURCE', 0, ['MediaSource']),
    'Derby': ('db::DERBY', 0, ['Derby']),
    'StageName': ('db::STAGE_NAME', 0, ['StageName']),
    'Weather': ('db::WEATHER', 0, ['Weather']),
    'Currency': ('db::CURRENCY_RECORD', 0, ['Currency']),
}
CLASS_KIND = {'db::NATION': 'Nation', 'db::CLUB': 'Club', 'db::LEAGUE_BODY_CLUB': 'Club', 'db::COMP': 'Competition', 'db::CITY': 'City',
              'db::STADIUM': 'Stadium', 'db::CONTINENT': 'Continent', 'db::LOCAL_REGION': 'LocalRegion', 'db::WEATHER': 'Weather',
              'db::LANGUAGE': 'Language', 'db::CURRENCY_RECORD': 'Currency', 'db::TEAM': 'Team', 'db::NATIONAL_TEAM': 'Team',
              'db::AWARD': 'Award', 'db::INJURY': 'Injury', 'db::MEDIA_SOURCE': 'MediaSource', 'db::DERBY': 'Derby',
              'db::STAGE_NAME': 'StageName'}
# pointers to people land on a sub-object; (class, sub-object offset) -> kind when that is our record base
PERSON_BASE = {('db::ACTUAL_PLAYER', 0x288): 'Player', ('db::ACTUAL_NON_PLAYER', 0x100): 'Staff',
               ('db::ACTUAL_PLAYER_AND_NON_PLAYER', 0x380): 'Staff', ('db::HUMAN_NON_PLAYER', 0x440): 'Staff'}
vt2name = {}
for n, v in cl.items():
    for vt, o in v: vt2name[vt] = (n, o)

def samples(kind, n=400):
    c, vo, _ = ENT[kind]
    a = objs(c, vo)
    step = max(1, len(a) // n)
    return [int(x) for x in a[::step]][:n]

def addr(base, path, off):
    a = base
    for hop in path:
        a = p.u64(a + hop)
        if not a: return None
    return a + off

def rd(a, size, signed):
    b = p.read(a, size)
    if not b: return None
    return int.from_bytes(b, 'little', signed=signed)

def target_kind(q):
    """kind an entity pointer refers to, or None"""
    if not q: return None
    v = p.u64(q)
    r = vt2name.get(v - p.base) if p.base <= v < p.base + 0x18000000 else None
    if not r: return None
    if r in PERSON_BASE: return PERSON_BASE[r]
    if r[1] == 0: return CLASS_KIND.get(r[0])
    return None

def clean_name(s):
    s = re.sub(r'\[COMMENT.*', '', s or '').strip()
    return s

def derive(code, f):
    """-> list of candidate dicts {path, off, ftype, vkind, ...} in preference order"""
    t = f['type']; out = []
    G = [c for c in f.get('get', []) if 'path' in c]
    S = [c for c in f.get('set', []) if 'path' in c]
    W = [c for c in S if c['how'] == 'W']
    M = [c for c in S if c['how'] == 'M' and c.get('mask') is not None]
    gk = {(tuple(g['path']), g['off']) for g in G if g['how'] == 'R'}
    def k_(c): return (tuple(c['path']), c['off'])
    both = [c for c in W if k_(c) in gk]
    for c in both: c['strong'] = True
    if len(W) == 1: W[0]['strong'] = True
    Gr = [g for g in G if g['how'] == 'R' and not (g['size'] == 4 and g['off'] in (0x4, 0x8, 0xc))]
    order = both + (W if len(W) == 1 else []) + Gr + [c for c in W if c not in both]
    def noise(c): return False
    if t in ('integer', 'attribute', 'enum', 'short_distance', 'day_of_week', 'month', 'cash', 'colour', 'real'):
        for c in order:
            if c['size'] not in (1, 2, 4) or noise(c): continue
            if t == 'real':
                if c['size'] != 4: continue
                ft = 'f32'
            else:
                ft = {1: 'u8', 2: 'i16', 4: 'i32'}[c['size']]
            out.append(dict(path=c['path'], off=c['off'], ftype=ft, strong=bool(c.get('strong')), vkind='Money' if t == 'cash' else ('Decimal' if t == 'real' else 'Integer')))
    elif t == 'boolean':
        for c in M:
            mask = c['mask'] & 0xff
            if c['op'] == 'and': mask = (~mask) & 0xff
            if mask and bin(mask).count('1') == 1:
                out.append(dict(path=c['path'], off=c['off'], ftype='bool', vkind='Boolean', mask=mask))
        for c in G:
            if c['how'] == 'T' and c.get('mask') and c['size'] == 1 and bin(c['mask'] & 0xff).count('1') == 1:
                out.append(dict(path=c['path'], off=c['off'], ftype='bool', vkind='Boolean', mask=c['mask'] & 0xff))
        for c in order:
            if c['size'] == 1 and not noise(c):
                out.append(dict(path=c['path'], off=c['off'], ftype='bool', vkind='Boolean', whole=True, strong=bool(c.get('strong'))))
    elif t == 'date':
        for c in W:
            if c['size'] == 4: out.append(dict(path=c['path'], off=c['off'], ftype='fmDate', vkind='Date'))
        for c in G:
            if c['how'] == 'R' and c['size'] == 2: out.append(dict(path=c['path'], off=c['off'] - 2, ftype='fmDate', vkind='Date'))
    elif t == 'string':
        for c in order:
            if c['size'] == 8: out.append(dict(path=c['path'], off=c['off'], ftype='fmString', vkind='Text'))
    elif t.startswith('database_r'):
        for c in G:
            if c['how'] == 'R' and c['size'] == 4 and c['off'] == 0x10 and c['path']:
                out.append(dict(path=c['path'][:-1], off=c['path'][-1], ftype='entityPtr', vkind='Reference'))
            elif c['how'] == 'R' and c['size'] == 8:
                out.append(dict(path=c['path'], off=c['off'], ftype='entityPtr', vkind='Reference'))
        for c in W:
            if c['size'] == 8: out.append(dict(path=c['path'], off=c['off'], ftype='entityPtr', vkind='Reference'))
    # dedupe
    seen = set(); o2 = []
    for c in out:
        k = (tuple(c['path']), c['off'], c['ftype'], c.get('mask'))
        if k not in seen: seen.add(k); o2.append(c)
    return o2

def probe(kind, c, f, S):
    """validate a candidate on live samples; returns (ok, info) and may refine c"""
    vals = []
    size = {'u8': 1, 'bool': 1, 'i16': 2, 'i32': 4, 'f32': 4, 'fmDate': 4, 'fmString': 8, 'entityPtr': 8}[c['ftype']]
    reach = 0
    for s in S:
        a = addr(s, c['path'], c['off'])
        if a is None: continue
        b = p.read(a, size)
        if b is None: continue
        reach += 1; vals.append(b)
    if reach < max(3, len(S) // 50): return False, f'unreachable ({reach}/{len(S)})'
    ft = c['ftype']
    if ft == 'fmString':
        ok = 0; ex = None; nz = 0
        for b in vals:
            q = struct.unpack('<Q', b)[0]
            if not q: continue
            nz += 1
            s_ = p.fmstr(q)
            if s_ is not None and (s_ == '' or s_.isprintable()):
                ok += 1; ex = ex or s_
        if nz == 0 or ok / nz < 0.9: return False, f'not strings ({ok}/{nz})'
        return True, f'str e.g. {ex!r} ({nz}/{reach} set)'
    if ft == 'entityPtr':
        kc = Counter(); nz = 0
        for b in vals:
            q = struct.unpack('<Q', b)[0]
            if not q: continue
            nz += 1; kc[target_kind(q)] += 1
        if nz == 0: return False, 'all null'
        k, n = kc.most_common(1)[0]
        if k is None or n / nz < 0.9: return False, f'targets {dict(kc)}'
        c['refKind'] = k
        return True, f'-> {k} ({nz}/{reach} set)'
    if ft == 'fmDate':
        good = 0; ex = None
        for b in vals:
            d, y = struct.unpack('<HH', b)
            if (1 <= d <= 366 and 1800 <= y <= 2200): good += 1; ex = ex or (d, y)
        if good / len(vals) < 0.9: return False, f'not dates ({good}/{len(vals)})'
        return True, f'date e.g. day {ex[0]} of {ex[1]}'
    if ft == 'f32':
        fv = [struct.unpack('<f', b)[0] for b in vals]
        if any(x != x or abs(x) > 1e7 for x in fv): return False, 'not floats'
        return True, f'f32 {min(fv):.3g}..{max(fv):.3g}'
    signed = ft in ('i16', 'i32')
    iv = [int.from_bytes(b, 'little', signed=signed) for b in vals]
    if ft == 'bool':
        if c.get('mask'):
            on = sum(1 for x in iv if x & c['mask'])
            return True, f'bit {c["mask"]:#x}: {on}/{len(iv)} set'
        d = set(iv)
        if not d <= {0, 1}: return False, f'byte not 0/1: {sorted(d)[:8]}'
        return True, f'bool {sum(iv)}/{len(iv)} set'
    lo, hi = min(iv), max(iv)
    info = f'{ft} {lo}..{hi} ({len(set(iv))} distinct)'
    mn = int(f['min']) if f.get('min') not in (None, '') else None
    mx = int(f['max']) if f.get('max') not in (None, '') else None
    if f['type'] == 'attribute' or (mx == 20 and (mn in (0, 1, None))):
        if hi <= 20: c['scale'] = 1
        elif hi <= 100: c['scale'] = 5
        else: return False, info + ' out of attribute range'
        if ft == 'u8' and lo < 0: return False, info
        c['attr'] = True
        return True, info + f' scale {c["scale"]}'
    if mx is not None and f['type'] in ('integer',):
        if hi > max(mx, 1) * 1.01 + 1 or (mn is not None and lo < min(mn, 0) - 1): return False, info + f' outside {mn}..{mx}'
    if f['type'] == 'enum' and f.get('enum'):
        allowed = {int(e[0]) for e in f['enum'] if e[0] is not None and re.fullmatch(r'-?\d+', e[0])}
        if allowed and sum(1 for x in iv if x in allowed) / len(iv) < 0.95: return False, info + ' not in enum'
    return True, info

def main():
    t = json.load(open(SCR + '/fields.json'))
    res = {}
    for kind, (c_, vo, groups) in ENT.items():
        S = samples(kind)
        res[kind] = {}
        for g in groups:
            for code, f in sorted(t.get(g, {}).items()):
                if not f['name']: continue
                cands = derive(code, f)
                picked = None; why = []
                for c in cands:
                    ok, info = probe(kind, c, f, S)
                    why.append(('OK ' if ok else 'NO ') + info)
                    if ok: picked = dict(c, info=info); break
                res[kind][code] = dict(name=clean_name(f['name']), type=f['type'], cat=clean_name(f.get('cat') or ''), min=f.get('min'), max=f.get('max'),
                                       enum=f.get('enum'), picked=picked, why=why, ncand=len(cands))
    for kind in res:
        groups = {}
        for code, v in res[kind].items():
            c = v['picked']
            if c: groups.setdefault((tuple(c['path']), c['off'], c.get('mask')), []).append(code)
        for loc, codes in groups.items():
            names = {res[kind][c]['name'] for c in codes}
            if len(names) <= 1: continue
            strong = [c for c in codes if res[kind][c]['picked'].get('strong')]
            keep = strong if len({res[kind][c]['name'] for c in strong}) == 1 else []
            for c in codes:
                if c not in keep:
                    res[kind][c]['why'].append('DROPPED: shares its location with ' + ', '.join(x for x in codes if x != c))
                    res[kind][c]['picked'] = None
    json.dump(res, open(SCR + '/gen.json', 'w'), indent=1)
    for kind in res:
        ok = [c for c, v in res[kind].items() if v['picked']]
        print(f'{kind:12s} named {len(res[kind]):4d} accepted {len(ok):4d}')

if __name__ == '__main__':
    main()
