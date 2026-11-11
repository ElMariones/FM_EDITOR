"""Turn raw analyser events into one candidate location per (entity, field code), then probe live memory."""
import sys, os, pickle, json, struct
from collections import Counter, defaultdict
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ana
from memlib import *

# label prefix -> (entity, this-adjust to the entity's record base, path prefix)
GROUPS = {
    'PLAYER': ('Player', 0, []), 'PLAYER18': ('Player', -0x270, []), 'PERSON': ('Person', 0, []),
    'NONPLAYER': ('Staff', 0, []), 'NATION': ('Nation', 0, []), 'NATION108': ('Nation', 0, [0x108]),
    'CLUB': ('Club', 0, []), 'COMP': ('Competition', 0, []), 'STADIUM': ('Stadium', 0, []), 'CITY': ('City', 0, []),
    'TEAM': ('Team', 0, []), 'AWARD': ('Award', 0, []), 'INJURY': ('Injury', 0, []), 'LANGUAGE': ('Language', 0, []),
    'CONTINENT': ('Continent', 0, []), 'LOCAL_REGION': ('LocalRegion', 0, []), 'MEDIA_SOURCE': ('MediaSource', 0, []),
    'DERBY': ('Derby', 0, []), 'STAGE_NAME': ('StageName', 0, []), 'WEATHER': ('Weather', 0, []),
    'CURRENCY_RECORD': ('Currency', 0, []), 'FULL_CONTRACT': ('Contract', 0, []), 'AGREEMENT': ('Agreement', 0, []),
    'STADIUM_CHANGE': ('StadiumChange', 0, []), 'UNWANTED': ('Unwanted', 0, []), 'LOAN_CONTRACT': ('LoanContract', 0, []),
    'B_CLUB_CONTRACT': ('BClubContract', 0, []), 'BASIC_CONTRACT': ('BasicContract', 0, []),
    'CLUB_FINANCE_BASE': ('ClubFinance', 0, []), 'NATIONAL_TEAM': ('Team', 0, []),
    'OFFICIAL': ('Official', 0, []), 'AGENT': ('Agent', 0, []), 'JOURNALIST': ('Journalist', 0, []),
    'REGION': ('Region', 0, []),
}

def conv(v):
    """value -> (path, off) or None"""
    if v is None or v[0] != 'p': return None
    root, off = v[1], v[2]
    if root == 'this': return [], off
    if isinstance(root, tuple) and root[0] == 'L' and root[2] == 8:
        r = conv(root[1])
        if r is None: return None
        return r[0] + [r[1]], off
    return None

def key(c): return (tuple(c[0]), c[1])

def pick(events, kind, noise):
    """returns list of candidates: dict(path, off, size, how, sign, mask)"""
    out = []
    if kind == 'set':
        for e in events:
            if e[0] == 'W':
                c = conv(e[1])
                if c is None: continue
                src = e[3]
                if src is not None and src[0] == 'c': continue
                if src is not None and src[0] == 'p' and isinstance(src[1], tuple) and src[1][0] in ('g',): continue
                if src is not None and src[0] == 'p' and isinstance(src[1], tuple) and src[1][0] == 'L':
                    # value copied from elsewhere in the record/global: not the field value
                    continue
                out.append(dict(path=c[0], off=c[1], size=e[2], how='W'))
            elif e[0] == 'M' and e[3][0] in ('or', 'and', 'bts', 'btr', 'xor'):
                c = conv(e[1])
                if c is None: continue
                m = e[3][1]
                out.append(dict(path=c[0], off=c[1], size=e[2], how='M', op=e[3][0], mask=(m[1] if m and m[0] == 'c' else None)))
    else:
        last = None; idx = None
        for e in events:
            if e[0] == 'V': break
            if e[0] == 'A': idx = e[2]
            if e[0] in ('R', 'T'):
                c = conv(e[1])
                if c is None or key(c) in noise: continue
                last = dict(path=c[0], off=c[1], size=e[2], how=e[0], sign=e[3] if e[0] == 'R' else 'u',
                            mask=(e[3][1] if e[0] == 'T' else None))
        if last: out.append(last)
    for e in events:
        if e[0] == 'A':
            out.append(dict(how='A', idx=e[2], via=e[1] if isinstance(e[1], str) else hex(e[1])))
            break
    return out

def main():
    ev = pickle.load(open(SCR + '/events.pkl', 'rb'))
    sc = json.load(open(SCR + '/schema.json')); fdef = {}
    for rid, fl in sc.items():
        for f in fl: fdef.setdefault(f['field'], dict(f, rec=rid))
    table = defaultdict(dict)   # entity -> code -> {get:[], set:[]}
    for label, d in ev.items():
        base = label.rsplit('_', 1)[0]
        if base not in GROUPS: continue
        ent, adj, prefix = GROUPS[base]
        # noise: chains read in many handlers
        cnt = Counter()
        for code, events in d['res'].items():
            seen = set()
            for e in events:
                if e[0] in ('R', 'T'):
                    c = conv(e[1])
                    if c: seen.add(key(c))
            cnt.update(seen)
        n = max(1, len(d['res']))
        noise = {k for k, v in cnt.items() if v > 8 and v / n > 0.12}
        noise |= {((), 0), ((), -4)}
        for code, events in d['res'].items():
            for c in pick(events, d['kind'], noise):
                if 'path' in c:
                    if c['path']: c['path'] = prefix + [c['path'][0] + adj] + c['path'][1:] if not prefix else prefix + c['path']
                    else:
                        if prefix: c['path'] = list(prefix)
                        else: c['off'] += adj
                c['src'] = label
                table[ent].setdefault(code, {}).setdefault(d['kind'], []).append(c)
    out = {}
    for ent, codes in table.items():
        out[ent] = {}
        for code, gs in codes.items():
            f = fdef.get(code, {})
            out[ent][code] = dict(name=f.get('name', ''), type=f.get('type', ''), min=f.get('min_value'), max=f.get('max_value'),
                                  enum=f.get('enum'), cat=f.get('category'), rec=f.get('rec'), **gs)
    json.dump(out, open(SCR + '/fields.json', 'w'), indent=1)
    for ent in out:
        named = [c for c in out[ent] if out[ent][c]['name']]
        withloc = [c for c in named if any('path' in x for k in ('get', 'set') for x in out[ent][c].get(k, []))]
        print(f'{ent:14s} codes {len(out[ent]):4d} named {len(named):4d} with location {len(withloc):4d}')

if __name__ == '__main__':
    main()
