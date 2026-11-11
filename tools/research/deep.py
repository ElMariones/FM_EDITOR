import sys, os, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ana
from memlib import *
ana.MAXFN=int(os.environ.get('MAXFN','3000')); ana.CALLEE_LIMIT=int(os.environ.get('CL','500'))
p,pe_,cl,h=ctx()
sc=json.load(open(SCR+'/schema.json')); names={}
for rid,fl in sc.items():
    for f in fl: names.setdefault(f['field'],f.get('name',''))
def run(fn,cls,ofs=0,vt_ofs=0,deref=None,codes=None,limit=700,n=60,width=600):
    a=objs(cls,vt_ofs); step=max(1,len(a)//n); samples=[int(x)-vt_ofs+ofs for x in a[::step]]
    if deref is not None: samples=[s for s in (p.u64(x+deref) for x in samples) if s]
    live=ana.Live(p,samples); hm,_=ana.switch_map(fn); regs,stack=ana.prologue(fn,live); emu=ana.Emu(live,maxdepth=6)
    stops=set(hm.values())
    for c in (codes or sorted(hm)):
        if c not in hm: print(c,'-- no handler'); continue
        _,ev=emu.run(hm[c],regs,stack,limit=limit,stop_at=stops-{hm[c]})
        d=[x for x in ana.describe(ev) if not x.startswith('vcall? [(') and not x.startswith("vcall? ('variant")]
        print(f'{c} {names.get(c,"")[:30]:30s} | '+' ; '.join(d)[:width])
if __name__=='__main__':
    a=sys.argv
    run(int(a[1],16),a[2],int(a[3],16),int(a[4],16),None if a[5]=='-' else int(a[5],16),a[6].split(',') if len(a)>6 and a[6]!='*' else None)
