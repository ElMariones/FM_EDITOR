import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ana, d
from memlib import *
pe=ana.pe; cl=rtti(pe)
def slotfn(cls,ofs,slot_off):
    return vslot(pe,cl,cls,ofs,slot_off//8)
if __name__=='__main__':
    cls=sys.argv[1]; ofs=int(sys.argv[2],16); n=int(sys.argv[3])
    for so in sys.argv[4:]:
        f=slotfn(cls,ofs,int(so,16)); print(f'== {cls}+{ofs:#x} slot {so} -> {f:x}')
        import io,contextlib
        buf=io.StringIO()
        with contextlib.redirect_stdout(buf): d.show(f)
        print('\n'.join(buf.getvalue().splitlines()[:n]))
