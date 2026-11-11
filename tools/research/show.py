import json,sys,os
SCR=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
t=json.load(open(SCR+'/fields.json'))
def loc(c):
    if c['how']=='A': return f"attr[{c['idx']:#x}]"
    p=''.join(f'[{x:#x}]' if x>=0 else f'[-{-x:#x}]' for x in c['path'])
    o=c['off']; s=f"{p}{'+' if o>=0 else '-'}{abs(o):#x}:{c['size']}{c['how']}"
    if c.get('mask') is not None: s+=f"&{c['mask']:#x}"
    return s
for ent in sys.argv[1:]:
    print('==',ent)
    for code,f in sorted(t[ent].items()):
        if not f['name']: continue
        g=' '.join(dict.fromkeys(loc(c) for c in f.get('get',[]))); s=' '.join(dict.fromkeys(loc(c) for c in f.get('set',[])))
        print(f"{code} {f['name'][:34]:34s} {f['type'][:10]:10s} G {g[:50]:50s} S {s[:70]}")
