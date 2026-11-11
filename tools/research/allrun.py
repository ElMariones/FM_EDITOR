"""Analyse every get/set property function and pickle raw events: {label: {'fn','cls','ofs','vt_ofs','deref','kind','res':{code:events}}}"""
import sys, os, pickle
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ana
from memlib import *
p,pe_,cl,h=ctx()
ana.MAXFN=1500; ana.CALLEE_LIMIT=300
T=[]
for name in sorted(cl):
    if not name.startswith('db::'): continue
    if not [v for v in cl[name] if v[1]==0]: continue
    if len(objs(name,0))==0: continue
    for slot,kind in ((1,'get'),(2,'set')):
        try:
            fn=vslot(pe_,cl,name,0,slot)
            if not func(pe_,fn): continue
            if len(ana.switch_map(fn)[0])<3: continue
        except Exception: continue
        T.append((name.replace('db::','').replace('::','_')+'_'+kind,fn,name,0,0,None,kind))
T+=[('PLAYER_get',0x1644980,'db::ACTUAL_PLAYER',0x288,0,None,'get'),
    ('PLAYER_set',0x16414c0,'db::ACTUAL_PLAYER',0x288,0,None,'set'),
    ('PLAYER18_get',0x1c2a340,'db::ACTUAL_PLAYER',0x18,0,None,'get'),
    ('PERSON_get',0xfa7f60,'db::ACTUAL_PLAYER',0x288,0,None,'get'),
    ('PERSON_set',0xf91290,'db::ACTUAL_PLAYER',0x288,0,None,'set'),
    ('NONPLAYER_get',0x181e060,'db::ACTUAL_NON_PLAYER',0x100,0x100,None,'get'),
    ('NONPLAYER_set',0x181b4e0,'db::ACTUAL_NON_PLAYER',0x100,0x100,None,'set'),
    ('NATION_get',0x11b7d50,'db::NATION',0,0,None,'get'),
    ('NATION_set',0x11b9cc0,'db::NATION',0,0,None,'set'),
    ('NATION108_get',0x13af1b0,'db::NATION',0,0,0x108,'get'),
    ('NATION108_set',0x13ab7d0,'db::NATION',0,0,0x108,'set')]
T+=[tuple(x) for x in (eval(open(SCR+'/extra_targets.txt').read()) if os.path.exists(SCR+'/extra_targets.txt') else [])]
out=pickle.load(open(SCR+'/events.pkl','rb')) if os.path.exists(SCR+'/events.pkl') else {}
seenfn={}
for label,fn,cls,ofs,vt_ofs,deref,kind in T:
    if label in out and '--force' not in sys.argv: continue
    a=objs(cls,vt_ofs)
    step=max(1,len(a)//150); samples=[int(x)-vt_ofs+ofs for x in a[::step]]
    if deref is not None: samples=[s for s in (p.u64(x+deref) for x in samples) if s]
    try: res,regs=ana.analyse(fn,samples,p,limit=450)
    except Exception as e:
        print(label,'FAILED',repr(e)); continue
    out[label]=dict(fn=fn,cls=cls,ofs=ofs,vt_ofs=vt_ofs,deref=deref,kind=kind,res=res)
    print(label,len(res),flush=True)
    pickle.dump(out,open(SCR+'/events.pkl','wb'))
