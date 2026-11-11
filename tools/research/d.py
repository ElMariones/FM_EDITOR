import sys, os, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from memlib import *
pe=PE()
def fourcc(v):
    b=struct.pack('<I',v&0xffffffff)[::-1]
    return b.decode() if all(32<c<127 for c in b) else None
def show(start,end=None,lo=None,hi=None):
    f=func(pe,start)
    if end is None:
        if f: start,end=f
        else: end=start+0x200; lo=None; hi=None; stop_ret=True
    for a,sz,m,o in dis(pe,start,end):
        if lo and a<lo: continue
        if hi and a>hi: break
        note=''
        for x in re.findall(r'0x[0-9a-f]{8}\b',o):
            c=fourcc(int(x,16))
            if c: note+='  ; '+c
        print(f'{a:08x} {m} {o}{note}')
        if not f and m in('ret','jmp','int3'): break
if __name__=='__main__':
    a=[int(x,16) for x in sys.argv[1:]]
    show(a[0],None,*(a[1:]))
