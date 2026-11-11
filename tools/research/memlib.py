"""Research toolkit: attach to fm.exe, RTTI from game_plugin.dll, heap census."""
import ctypes, ctypes.wintypes as wt, struct, os, pickle, sys, time
import numpy as np, psutil
k32 = ctypes.WinDLL('kernel32', use_last_error=True)
SCR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DLL = r"C:\Program Files (x86)\Steam\steamapps\common\Football Manager 27\fm_Data\Plugins\x86_64\game_plugin.dll"

class MBI(ctypes.Structure):
    _fields_=[('BaseAddress',ctypes.c_ulonglong),('AllocationBase',ctypes.c_ulonglong),('AllocationProtect',wt.DWORD),
              ('PartitionId',wt.WORD),('RegionSize',ctypes.c_ulonglong),('State',wt.DWORD),('Protect',wt.DWORD),('Type',wt.DWORD)]
k32.OpenProcess.restype=wt.HANDLE
k32.ReadProcessMemory.argtypes=[wt.HANDLE,ctypes.c_ulonglong,ctypes.c_void_p,ctypes.c_size_t,ctypes.POINTER(ctypes.c_size_t)]
k32.WriteProcessMemory.argtypes=[wt.HANDLE,ctypes.c_ulonglong,ctypes.c_void_p,ctypes.c_size_t,ctypes.POINTER(ctypes.c_size_t)]
k32.VirtualQueryEx.argtypes=[wt.HANDLE,ctypes.c_ulonglong,ctypes.POINTER(MBI),ctypes.c_size_t]
k32.VirtualQueryEx.restype=ctypes.c_size_t

class Proc:
    def __init__(self, write=False):
        p=[x for x in psutil.process_iter(['name','pid']) if x.info['name']=='fm.exe'][0]
        self.pid=p.info['pid']
        acc=0x0010|0x0400|(0x0020|0x0008 if write else 0)
        self.h=k32.OpenProcess(acc,False,self.pid)
        if not self.h: raise OSError(ctypes.get_last_error())
        self.base=None
        for m in p.memory_maps(grouped=False):
            if m.path.lower().endswith('game_plugin.dll'):
                a=int(m.addr,16) if isinstance(m.addr,str) else m.addr
                self.base=a if self.base is None else min(self.base,a)
        mbi=MBI(); k32.VirtualQueryEx(self.h,self.base,ctypes.byref(mbi),ctypes.sizeof(mbi)); self.base=mbi.AllocationBase
    def read(self,a,n):
        buf=(ctypes.c_char*n)(); got=ctypes.c_size_t()
        if not k32.ReadProcessMemory(self.h,a,buf,n,ctypes.byref(got)) or got.value!=n: return None
        return buf.raw
    def write(self,a,data):
        got=ctypes.c_size_t()
        return bool(k32.WriteProcessMemory(self.h,a,data,len(data),ctypes.byref(got))) and got.value==len(data)
    def u8(self,a): return self.read(a,1)[0]
    def i16(self,a): return struct.unpack('<h',self.read(a,2))[0]
    def u16(self,a): return struct.unpack('<H',self.read(a,2))[0]
    def i32(self,a): return struct.unpack('<i',self.read(a,4))[0]
    def u32(self,a): return struct.unpack('<I',self.read(a,4))[0]
    def u64(self,a):
        b=self.read(a,8); return struct.unpack('<Q',b)[0] if b else 0
    def fmstr(self,p):
        if not p: return None
        b=self.read(p,4)
        if not b: return None
        n=struct.unpack('<i',b)[0]
        if n<0 or n>400: return None
        if n==0: return ''
        s=self.read(p+4,n)
        try: return s.decode('utf8')
        except Exception: return None
    def regions(self,writable=True):
        a=0; mbi=MBI()
        while k32.VirtualQueryEx(self.h,a,ctypes.byref(mbi),ctypes.sizeof(mbi)):
            if mbi.State==0x1000 and mbi.Protect and not (mbi.Protect&0x101):
                if not writable or (mbi.Protect&0xCC): yield mbi.BaseAddress,mbi.RegionSize,mbi.Type
            n=mbi.BaseAddress+mbi.RegionSize
            if n<=a: break
            a=n

class PE:
    def __init__(self,path=DLL):
        self.d=np.fromfile(path,dtype=np.uint8); d=self.d[:0x1000].tobytes()
        pe=struct.unpack_from('<I',d,0x3c)[0]; nsec=struct.unpack_from('<H',d,pe+6)[0]; opt=struct.unpack_from('<H',d,pe+20)[0]
        self.imagebase=struct.unpack_from('<Q',d,pe+24+24)[0]
        self.secs=[]
        o=pe+24+opt
        for i in range(nsec):
            name=d[o:o+8].rstrip(b'\0').decode(); vs,va,rs,ro=struct.unpack_from('<IIII',d,o+8)
            self.secs.append((name,va,vs,ro,rs)); o+=40
    def off(self,rva):
        for n,va,vs,ro,rs in self.secs:
            if va<=rva<va+rs: return ro+rva-va
        return None
    def rva(self,off):
        for n,va,vs,ro,rs in self.secs:
            if ro<=off<ro+rs: return va+off-ro
        return None
    def bytes(self,rva,n):
        o=self.off(rva); return self.d[o:o+n].tobytes()

def demangle(m):
    b=m[4:]
    if b.endswith('@@'): b=b[:-2]
    if '?' in b or '$' in b: return b
    return '::'.join(reversed([x for x in b.split('@') if x]))

def rtti(pe):
    """returns {name: [(vtable_rva, object_offset)]}"""
    cache=os.path.join(SCR,'rtti.pkl')
    if os.path.exists(cache): return pickle.load(open(cache,'rb'))
    raw=pe.d.tobytes()
    tds={}
    pos=0
    while True:
        i=raw.find(b'.?A',pos)
        if i<0: break
        pos=i+1
        if raw[i+3:i+4] not in (b'V',b'U'): continue
        e=raw.find(b'\0',i,i+600)
        if e<0 or raw[e-2:e]!=b'@@': continue
        r=pe.rva(i-16)
        if r is None: continue
        tds[r]=raw[i:e].decode('ascii','replace')
    cols={}
    for n,va,vs,ro,rs in pe.secs:
        if n not in ('.rodata','.data','.xpdata'): continue
        a=np.frombuffer(raw,dtype='<u4',count=rs//4,offset=ro)
        idx=np.nonzero(a[:-5]==1)[0]
        selfr=a[idx+5]; ok=selfr==(va+idx*4)
        for i in idx[ok]:
            td=int(a[i+3])
            if td in tds: cols[va+int(i)*4]=(td,int(a[i+1]))
    out={}
    colset=np.array(sorted(cols),dtype=np.uint64)+np.uint64(pe.imagebase)
    for n,va,vs,ro,rs in pe.secs:
        if n not in ('.rodata','.data','.xpdata'): continue
        a=np.frombuffer(raw,dtype='<u8',count=rs//8,offset=ro)
        hit=np.nonzero(np.isin(a,colset))[0]
        for i in hit:
            td,ofs=cols[int(a[i])-pe.imagebase]
            out.setdefault(demangle(tds[td]),[]).append((va+int(i)*8+8,ofs))
    for td,nm in tds.items(): out.setdefault(demangle(nm),[])
    pickle.dump(out,open(cache,'wb'))
    return out

def census(proc,pe,classes,force=False):
    """scan heap for vtable pointers. returns {vtable_rva: np.array(addresses of the slot)}"""
    cache=os.path.join(SCR,'census.pkl')
    if os.path.exists(cache) and not force:
        c=pickle.load(open(cache,'rb'))
        if c['pid']==proc.pid: return c['hits']
    vts=np.array(sorted({v for vs in classes.values() for v,_ in vs}),dtype=np.uint64)+np.uint64(proc.base)
    lo,hi=vts.min(),vts.max()
    modend=proc.base+max(va+vs for _,va,vs,_,_ in pe.secs)
    A=[];V=[]
    t=time.time(); total=0
    for start,size,typ in proc.regions():
        if proc.base<=start<modend: continue
        if typ==0x1000000: continue  # image
        off=0
        while off<size:
            n=min(64<<20,size-off)
            b=proc.read(start+off,n)
            if b:
                a=np.frombuffer(b,dtype='<u8',count=n//8)
                m=np.nonzero((a>=lo)&(a<=hi))[0]
                if len(m):
                    vals=a[m]; ok=np.isin(vals,vts)
                    A.append(m[ok].astype(np.uint64)*8+np.uint64(start+off)); V.append(vals[ok]-np.uint64(proc.base))
                total+=n
            off+=n
    A=np.concatenate(A);V=np.concatenate(V)
    hits={}
    order=np.argsort(V,kind='stable'); A=A[order];V=V[order]
    u,idx=np.unique(V,return_index=True)
    for j,v in enumerate(u):
        e=idx[j+1] if j+1<len(u) else len(V)
        hits[int(v)]=np.sort(A[idx[j]:e])
    pickle.dump({'pid':proc.pid,'hits':hits},open(cache,'wb'))
    print('census',total>>20,'MB in',round(time.time()-t,1),'s',file=sys.stderr)
    return hits

_ctx=None
def ctx(write=False):
    global _ctx
    if _ctx is None:
        pe=PE(); cl=rtti(pe); p=Proc(write); h=census(p,pe,cl)
        _ctx=(p,pe,cl,h)
    return _ctx

def objs(name,ofs=0):
    """addresses of the slot holding class `name`'s vtable at object offset `ofs`."""
    p,pe,cl,h=ctx()
    for vt,o in cl[name]:
        if o==ofs: return h.get(vt,np.array([],dtype=np.uint64))
    raise KeyError((name,ofs))

# ---- disassembly helpers ----
import bisect
_pd=None
def pdata(pe):
    global _pd
    if _pd is None:
        d=pe.d[:0x1000].tobytes(); peo=struct.unpack_from('<I',d,0x3c)[0]
        rva,size=struct.unpack_from('<II',d,peo+24+112+3*8)
        a=np.frombuffer(pe.bytes(rva,size),dtype='<u4').reshape(-1,3)
        a=a[a[:,0]!=0]; a=a[np.argsort(a[:,0])]
        _pd=(a[:,0].tolist(),a[:,1].tolist())
    return _pd
def func(pe,rva):
    b,e=pdata(pe); i=bisect.bisect_right(b,rva)-1
    return (b[i],e[i]) if i>=0 and rva<e[i] else None
_cs=None
def dis(pe,start,end):
    global _cs
    import capstone
    if _cs is None:
        _cs=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
    return list(_cs.disasm_lite(pe.bytes(start,end-start),start))
def find_imm(pe,code):
    """rvas where 4 bytes of fourcc (as u32 immediate) appear in code section"""
    n,va,vs,ro,rs=pe.secs[0]
    raw=pe.d[ro:ro+rs].tobytes(); pat=code[::-1] if isinstance(code,bytes) else code.encode()[::-1]
    out=[];pos=0
    while True:
        i=raw.find(pat,pos)
        if i<0: break
        out.append(va+i); pos=i+1
    return out

_ro=None
def rodata_q(pe):
    """(rva array, qword array) for .rodata"""
    global _ro
    if _ro is None:
        n,va,vs,ro,rs=[s for s in pe.secs if s[0]=='.rodata'][0]
        _ro=(va,np.frombuffer(pe.d[ro:ro+rs].tobytes(),dtype='<u8'))
    return _ro
_vtidx=None
def vt_owner(pe,cl,rva):
    """which (class, objofs, slot) vtable entries point at function rva"""
    global _vtidx
    va,q=rodata_q(pe)
    if _vtidx is None:
        _vtidx=sorted((vt,name,ofs) for name,v in cl.items() for vt,ofs in v)
    hits=np.nonzero(q==np.uint64(pe.imagebase+rva))[0]
    out=[]
    starts=[x[0] for x in _vtidx]
    for h in hits:
        r=va+int(h)*8
        i=bisect.bisect_right(starts,r)-1
        if i>=0: out.append((_vtidx[i][1],_vtidx[i][2],(r-_vtidx[i][0])//8))
    return out
def vslot(pe,cl,name,ofs,slot):
    """rva of function in class vtable at object offset ofs, slot index"""
    for vt,o in cl[name]:
        if o==ofs:
            return struct.unpack('<Q',pe.bytes(vt+slot*8,8))[0]-pe.imagebase
    raise KeyError
