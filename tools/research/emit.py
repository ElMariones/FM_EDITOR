"""Write the definition, the generated catalog and a markdown table from gen.json."""
import sys, os, json, re, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen
from gen import p, ENT, samples, probe
SCR = gen.SCR
REPO = r'C:\Users\mario\Desktop\PROYECTOS\FM_EDITOR'
DEF = REPO + r'\definitions\fm27-27.0.1.json'
CAT = REPO + r'\src\FMOpenEditor.Core\Model\catalog.generated.json'

def hx(n): return ('-' if n < 0 else '') + f'0x{abs(n):X}'
def keypart(s): return ''.join(ch for ch in s.title() if ch.isalnum()) if s.islower() or ' ' in s else ''.join(ch for ch in s if ch.isalnum())

# code -> existing catalog key
EXISTING = {
    'Player': {'PCRI': 'Reputation.Current', 'PHRI': 'Reputation.Home', 'PWRI': 'Reputation.World', 'PbvE': 'Value',
               'Pcon': 'Condition', 'Pfit': 'MatchSharpness', 'Pjad': 'Jadedness',
               'Pgoa': 'Pos.GK', 'Pdls': 'Pos.DL', 'Pdce': 'Pos.DC', 'Pdrs': 'Pos.DR', 'Pwbl': 'Pos.WBL', 'Pwbr': 'Pos.WBR',
               'Pdmc': 'Pos.DM', 'Pmce': 'Pos.MC', 'Pmrs': 'Pos.MR', 'Paml': 'Pos.AML', 'Pamc': 'Pos.AMC', 'Pamr': 'Pos.AMR', 'Pace': 'Pos.ST'},
    'Staff': {'NcrI': 'Reputation.Current', 'NhmI': 'Reputation.Home', 'NwrI': 'Reputation.World',
              'Ncha': 'Coach.Attacking', 'Nchd': 'Coach.Defending', 'Nchf': 'Coach.Fitness', 'Ncta': 'Coach.Tactical', 'Ncte': 'Coach.Technical',
              'Npwy': 'Coach.WorkingWithYoungsters', 'Npde': 'Mental.Determination', 'Npld': 'Mental.LevelOfDiscipline',
              'Npmo': 'Mental.Motivating', 'Npmm': 'Mental.PeopleManagement', 'Npja': 'Knowledge.JudgingPlayerAbility',
              'Npjp': 'Knowledge.JudgingPlayerPotential', 'Npjs': 'Knowledge.JudgingStaffAbility', 'Nneg': 'Knowledge.Negotiating',
              'Nptk': 'Knowledge.TacticalKnowledge', 'Npph': 'Knowledge.Physiotherapy', 'NpIM': 'Knowledge.SportsScience'},
    'Club': {'Crep': 'Reputation', 'Cdvi': 'League', 'Ctlv': 'Facilities.Training', 'Ccfa': 'Facilities.Corporate'},
    'Competition': {'crep': 'Reputation', 'csnm': 'ShortName'},
    'Nation': {'Nrep': 'Reputation'},
}
SKIP = {
    'Player': {'PCAB', 'PPAB', 'Phei', 'Phes', 'PCRP', 'PHRP', 'PWRP', 'PPAR', 'PPMX', 'PSCR', 'Page', 'Phln', 'Pnjd', 'Pnbi', 'Prbi', 'Porl', 'PnrI', 'PFCS'},
    'Staff': {'Ncra', 'Npta', 'NRCA', 'NRCJ', 'Ncrr', 'Nhmr', 'Nwrr', 'NSCR', 'Pdet', 'Natt', 'Page', 'Phln', 'Pnjd', 'Pnbi', 'Prbi', 'Phei', 'N100', 'NpCw', 'PNJO', 'Pife'},
    'Club': {'CSbR', 'Cect', 'Cemn', 'Cemx', 'Cetl', 'Cmtm', 'Cffy', 'Cfty', 'Csmn', 'Csna', 'Sdcy', 'Sscl', 'levl', 'Cnti', 'Cebd', 'Cepr'},
    'Competition': {'cnam', 'cnti', 'tbcl', 'ttcl', 'comp', 'cmtc', 'nytc'},
    'Nation': {'Nnam', 'NMEf', 'NMEd', 'NTCn', 'Cnti', 'ctem'},
    'Team': {'Tmtd'},
}
# hand-added fields: kind -> key -> (label, category, vkind, ftype, path, off, extra)
JOBS = [(0, 'None'), (2, 'Coach'), (3, 'Player / Coach'), (4, 'Chairperson'), (6, 'Director'), (8, 'Managing Director'),
        (10, 'Director of Football'), (12, 'Physio'), (14, 'Scout'), (16, 'Manager'), (17, 'Player / Manager'), (20, 'Assistant Manager'),
        (21, 'Player / Assistant Manager'), (22, 'Set Piece Coach'), (26, 'Fitness Coach'), (34, 'Goalkeeping Coach'),
        (35, 'Player / Goalkeeping Coach'), (38, 'Head of Sports Medicine'), (40, 'Head of Sports Science'), (44, 'Chief Scout'),
        (46, 'Doctor'), (48, 'Sports Scientist'), (50, 'Head Physio'), (58, 'Recruitment Analyst'), (60, 'Performance Analyst'),
        (62, 'Chief Analyst'), (64, 'Head of Youth Development'), (66, 'Owner'), (80, 'Job 80'), (86, 'Job 86'), (88, 'Technical Director')]
JOB_CODES = [c for c, _ in JOBS]
MANUAL = {
    'Player': {
        'Attr.Consistency': (None, None, None, 'u8', [], -0xFD, dict(scale=5)),
        'Attr.ImportantMatches': (None, None, None, 'u8', [], -0xFA, dict(scale=5)),
        'Pos.ML': (None, None, None, 'u8', [], -0x132, dict(scale=1)),
        'Morale': (None, None, None, 'u8', [], -0x1C, {}),
        'Looks.SkinTone': (None, None, None, 'u8', [], 0x90, {}),
        'Looks.HairColour': (None, None, None, 'u8', [], 0x8E, {}),
        'Looks.HairLength': (None, None, None, 'u8', [], 0x8F, {}),
        'Ethnicity': ('Ethnicity (code)', 'Looks', 'Integer', 'u8', [], 0x8D, dict(min=0, max=255)),
    },
    'Staff': {
        'Role': (None, None, None, 'u8', [], -0x22, dict(values=JOB_CODES)),
        'Looks.SkinTone': ('Skin tone', 'Looks', 'Integer', 'u8', [], 0x90, dict(min=1, max=20)),
        'Looks.HairColour': ('Hair colour', 'Looks', 'Integer', 'u8', [], 0x8E, dict(min=0, max=18)),
        'Looks.HairLength': ('Hair length', 'Looks', 'Integer', 'u8', [], 0x8F, dict(min=0, max=3)),
        'Ethnicity': ('Ethnicity (code)', 'Looks', 'Integer', 'u8', [], 0x8D, dict(min=0, max=255)),
    },
    'Club': {
        'Finance.TicketPrice': (None, None, None, 'f32', [0x150], 0x10, {}),
        'Finance.SeasonTicketPrice': (None, None, None, 'f32', [0x150], 0x14, {}),
        'Finance.EmbargoStart': ('Transfer embargo start', 'Finances', 'Date', 'fmDate', [0x150], 0x24, {}),
        'Finance.EmbargoEnd': ('Transfer embargo end', 'Finances', 'Date', 'fmDate', [0x150], 0x28, {}),
        'Finance.EmbargoAppeal': ('Transfer embargo appeal date', 'Finances', 'Date', 'fmDate', [0x150], 0x2C, {}),
        'Facilities.Youth': (None, None, None, 'u8', [0x100], 0x12B, {}),
        'Facilities.YouthRecruitment': (None, None, None, 'u8', [0x100], 0x12D, {}),
        'Facilities.JuniorCoaching': (None, None, None, 'u8', [0x100], 0x12C, {}),
        'Stadium.Name': (None, None, None, 'fmString', [0x18, 0x0, 0x78], 0x40, {}),
        'Stadium.Capacity': (None, None, None, 'i32', [0x18, 0x0, 0x78], 0x7C, {}),
        'Stadium': ('Stadium', 'Kits & stadium', 'Reference', 'entityPtr', [0x18, 0x0], 0x78, dict(refKind='Stadium', readOnly=True)),
    },
    'Competition': {'ThreeLetterName': ('Three letter name', 'Profile', 'Text', 'fmString', [], 0x50, {})},
    'Team': {'Club': ('Club', 'Profile', 'Reference', 'entityPtr', [], 0x30, dict(refKind='Club', readOnly=True))},
}
NAME_AT = {'Stadium': 0x40, 'City': 0x18, 'Continent': 0x18, 'LocalRegion': 0x18, 'Award': 0x30, 'Injury': 0x18, 'Language': 0x18,
           'MediaSource': 0x18, 'Derby': 0x18, 'StageName': 0x18, 'Weather': 0x18, 'Currency': 0x18}
NEW_KINDS = [k for k in ENT if k not in ('Player', 'Staff', 'Club', 'Competition', 'Nation')]
COLLECTIONS = {k: ENT[k][0] for k in NEW_KINDS}

DEBUG = {f['field'] for fl in json.load(open(SCR + '/schema.json')).values() for f in fl if f.get('debug_only') == 'true'}
SMALL = {'Of', 'To', 'In', 'For', 'At', 'By', 'And', 'The', 'A', 'On', 'From', 'Or'}

def nice_label(kind, code, name):
    name = re.sub(r'\s*\(Debug\)', '', name).strip()
    words = name.split(' ')
    def keep(w): return sum(1 for ch in w if ch.isupper()) >= 2 or any(ch.isdigit() for ch in w) or w in ('Celsius',)
    name = ' '.join(w if (i == 0 or keep(w)) else w.lower() for i, w in enumerate(words))
    if kind == 'Weather' and re.fullmatch(r'w\d\w\w', code): name = f'Period {int(code[1]) + 1}: {name}'
    return name

def cat_for(kind, code, v, c):
    n = v['name'].lower(); t = v['type']
    if kind == 'Weather' and re.fullmatch(r'w\d\w\w', code): return f'Period {int(code[1]) + 1}'
    if t == 'colour' or 'colour' in n: return 'Colours'
    if kind in ('Player', 'Staff'):
        if 'international' in n or 'u21' in n or 'declared' in n: return 'International'
        if any(k in n for k in ('formation', 'set piece')): return 'Formations & set pieces'
        if c.get('attr') and kind == 'Staff': return 'Abilities' if c.get('scale') == 5 else 'Tendencies'
        if any(k in n for k in ('birth', 'language', 'body', 'female', 'shirt name')): return 'Profile'
        if 'date' in n or 'joined' in n: return 'Contract'
        if kind == 'Staff' and any(k in n for k in ('coaching style', 'side of game')): return 'Profile'
        return 'Details'
    if any(k in n for k in ('name', 'nickname')) and t == 'string': return 'Names'
    if kind == 'Stadium':
        if 'pitch' in n: return 'Pitch'
        if 'capacity' in n: return 'Capacity'
        if any(k in n for k in ('city', 'nation', 'latitude', 'longitude', 'nearby')): return 'Location'
        if 'date' in n: return 'History'
        return 'Features'
    if kind == 'Nation' and (c.get('attr') or any(k in n for k in ('formation', 'tempo', 'width', 'pressing', 'marking', 'offside', 'playmaker'))): return 'Playing style'
    if kind == 'Club' and 'formation' in n: return 'Playing style'
    if 'pitch' in n: return 'Pitch rules'
    if any(k in n for k in ('latitude', 'longitude', 'altitude', 'boundary', 'inhabitants')) or (t.startswith('database') and any(k in n for k in ('city', 'region', 'nation', 'continent', 'weather', 'language', 'currency'))): return 'Location'
    if any(k in n for k in ('year', 'date')): return 'History'
    if any(k in n for k in ('reputation', 'rating', 'importance', 'power', 'economic', 'strength', 'points', 'morale', 'attraction')): return 'Standing'
    return 'Details'

def field_json(c):
    d = {'offset': hx(c['off']), 'type': c['ftype'], 'scale': c.get('scale', 1), 'maxLength': 64, 'verified': False}
    if c.get('path'): d['path'] = [hx(x) for x in c['path']]
    if c.get('mask'): d['mask'] = hx(c['mask'])
    if c.get('readOnly'): d['readOnly'] = True
    if c.get('values'): d['values'] = c['values']
    return d

def main():
    r = json.load(open(SCR + '/gen.json'))
    d = json.load(open(DEF, encoding='utf8'))
    cat = {}
    md = []
    for kind in ENT:
        S = samples(kind)
        structs = d['structs'].setdefault(kind, {})
        if kind in NEW_KINDS:
            structs.clear()
            structs['Id'] = {'offset': '0xC', 'type': 'i32', 'scale': 1, 'maxLength': 64, 'verified': False}
            d['collections'][kind] = {'layout': 'rttiInstances', 'classes': [{'class': COLLECTIONS[kind], 'vtableOffset': '0x0'}],
                                      'stride': 0, 'filterValue': 0, 'maxCount': 2000000}
        used_loc = {}
        for k, f in structs.items():
            if f.get('offset') is not None:
                used_loc[(tuple(f.get('path') or []), f['offset'], f.get('mask'))] = k
        props = []; rows = []
        def add(key, label, category, vkind, c, code='', extra=None, builtin=False):
            fj = field_json(c)
            loc = (tuple(fj.get('path') or []), fj['offset'], fj.get('mask'))
            if loc in used_loc and used_loc[loc] != key and not builtin: return False
            used_loc[loc] = key
            structs[key] = fj
            if not builtin:
                pd = {'key': key, 'label': label, 'category': category or 'Details', 'kind': vkind}
                pd.update(extra or {})
                props.append(pd)
            rows.append((label or key, code, fj, c.get('info', '')))
            return True
        if kind in NAME_AT:
            c = dict(path=[], off=NAME_AT[kind], ftype='fmString')
            ok, info = probe(kind, c, {'type': 'string'}, S); c['info'] = info
            add('Name', 'Name', 'Profile', 'Text', c)
        for key, (label, category, vkind, ftype, path, off, extra) in MANUAL.get(kind, {}).items():
            c = dict(path=path, off=off, ftype=ftype, **{k: v for k, v in extra.items() if k in ('scale', 'readOnly', 'values')})
            ok, info = probe(kind, {k: v for k, v in c.items() if k != 'values'}, {'type': 'integer' if ftype in ('u8', 'i32', 'i16') else 'x', 'min': None, 'max': None}, S)
            c['info'] = ('' if ok else 'UNCONFIRMED ') + info
            if label is None: add(key, None, None, None, c, builtin=True)
            else: add(key, label, category, vkind, c, extra={k: v for k, v in extra.items() if k in ('refKind', 'min', 'max')})
        keys = set(structs)
        for code, v in r[kind].items():
            c = v['picked']
            if not c or code in SKIP.get(kind, ()) or code in DEBUG: continue
            v = dict(v, name=nice_label(kind, code, v['name']))
            v['cat'] = cat_for(kind, code, v, c)
            if code in EXISTING.get(kind, {}):
                key = EXISTING[kind][code]
                if key.startswith('Pos.'): c['scale'] = 1
                if key == 'League': c['readOnly'] = True
                add(key, v['name'], None, None, c, code, builtin=True)
                continue
            name = v['name']
            base = keypart(name) or code
            key = base
            if key in keys or key in ('Id', 'FullName', 'Age'): key = f'{base}_{code}'
            vk = c['vkind']; extra = {}
            if c.get('attr'): extra.update(isAttribute=True, min=1 if c['scale'] == 5 else 0, max=20)
            elif vk == 'Integer':
                enum = v.get('enum') or []
                vals = [e[0] for e in enum]
                ints = v['type'] == 'enum' and vals and all(x is not None and re.fullmatch(r'-?\d+', x) for x in vals) and len(set(vals)) == len(vals)
                if ints and len(vals) <= 60 and c['ftype'] in ('u8', 'i16', 'i32'):
                    pairs = sorted(((int(e[0]), e[1] or f'#{e[0]}') for e in enum), key=lambda t: t[0])
                    if c['ftype'] == 'u8': pairs = [((n + 256) % 256, name) for n, name in pairs]
                    vk = 'Choice'; extra['choices'] = [name for _, name in pairs]
                    if [n for n, _ in pairs] != list(range(len(pairs))): c['values'] = [n for n, _ in pairs]
                else:
                    lim = {'u8': (0, 255), 'i16': (-32768, 32767), 'i32': (-2147483648, 2147483647)}[c['ftype']]
                    mn = int(v['min']) if v.get('min') not in (None, '') else lim[0]
                    mx = int(v['max']) if v.get('max') not in (None, '') else lim[1]
                    extra.update(min=max(mn, lim[0]), max=min(mx, lim[1]))
                    if v['type'] == 'enum' and enum:
                        extra['description'] = ', '.join(f'{e[0]} = {e[1]}' for e in enum[:24])
                    if v['type'] == 'colour': extra['description'] = 'Colour as a signed 32-bit ARGB number.'
            if vk == 'Reference': extra['refKind'] = c['refKind']
            label = name if v['type'] != 'colour' else name + ' (ARGB)'
            if add(key, label, v['cat'] or 'Details', vk, c, code, extra): keys.add(key)
        cat[kind] = props
        md.append((kind, rows))
    notes = d.get('notes', '')
    extra_note = (" Fields added in the second pass come from the game's own per-record get/set property code (the handlers keyed by the "
                  "4-character field codes also listed in shared/data/editor.fmf), each checked against a few hundred live records; they are "
                  "not yet confirmed on screen.")
    if 'second pass' not in notes: d['notes'] = notes + extra_note
    json.dump(d, open(DEF, 'w', encoding='utf8'), indent=2, ensure_ascii=False)
    json.dump(cat, open(CAT, 'w', encoding='utf8'), indent=1, ensure_ascii=False)
    with open(SCR + '/generated.md', 'w', encoding='utf8') as f:
        for kind, rows in md:
            f.write(f'\n### {kind}\n\n| Field | Code | Path | Offset | Type | Live check |\n|---|---|---|---|---|---|\n')
            for label, code, fj, info in rows:
                t = fj['type'] + (f" ×{fj['scale']}" if fj.get('scale', 1) != 1 else '') + (f" bit {fj['mask']}" if fj.get('mask') else '')
                path = '[' + ', '.join(fj['path']) + ']' if fj.get('path') else ''
                f.write(f"| {label} | {('`' + code + '`') if code else ''} | {path} | {fj['offset'].replace('-', '−')} | {t} | {info.replace('|', '/')} |\n")
    for kind, rows in md: print(f'{kind:12s} fields written {len(rows):3d}  new catalog props {len(cat[kind]):3d}')

if __name__ == '__main__':
    main()
