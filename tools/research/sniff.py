"""Classify every 8-byte slot of a class's objects: string pointer / pointer to a known class / other."""
import sys, os, struct
from collections import Counter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from memlib import *
p,pe,cl,h=ctx()
vt2name={}
for n,v in cl.items():
    for vt,o in v: vt2name[vt]=(n,o)
def classify(q):
    if q==0: return 'null'
    if q<0x10000 or q>0x7fffffffffff: return 'val'
    b=p.read(q,8)
    if not b: return 'val'
    v=struct.unpack('<Q',b)[0]
    if p.base<=v<p.base+0x18000000:
        r=vt2name.get(v-p.base)
        if r: return f'->{r[0]}' + (f'+{r[1]:#x}' if r[1] else '')
        return '->obj?'
    s=p.fmstr(q)
    if s is not None and len(s)>0 and s.isprintable(): return 'str'
    return 'ptr'
def sniff(name,vt_ofs=0,size=None,n=120,lo=0):
    a=objs(name,vt_ofs)
    if size is None:
        d=np.diff(a.astype(np.int64)); size=int(d[d>0].min())
    step=max(1,len(a)//n); S=[int(x)-vt_ofs for x in a[::step]][:n]
    print(f'== {name} n={len(a)} size~{size:#x}')
    for off in range(lo,size,8):
        c=Counter(); ex=None
        for s in S:
            b=p.read(s+off,8)
            if not b: continue
            q=struct.unpack('<Q',b)[0]; k=classify(q); c[k]+=1
            if k=='str' and ex is None: ex=p.fmstr(q)
        top=[(k,v) for k,v in c.most_common(3) if k not in('val','null')]
        if top and top[0][1]>=max(3,len(S)//20):
            print(f'  +{off:#05x} {top}' + (f'  e.g. {ex!r}' if ex else ''))
if __name__=='__main__':
    for spec in sys.argv[1:]:
        a=spec.split(','); sniff(a[0], int(a[1],16) if len(a)>1 else 0, int(a[2],16) if len(a)>2 else None)
