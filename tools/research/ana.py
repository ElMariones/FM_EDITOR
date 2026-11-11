"""Static analyser for the game's per-record get_property / set_property switch functions.

Maps each 4-char field code to the memory locations its handler reads (getter) or writes (setter),
expressed as pointer chains from the record object. Virtual calls are resolved against live objects.
"""
import sys, os, struct, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from memlib import *
import capstone
from capstone import x86_const as X

pe = PE()
cs = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64); cs.detail = True
VARIANT_FN = 0x4b60
MAXFN = 400
CALLEE_LIMIT = 120

_canon = {}
def _init():
    fam = {
        'rax': ['rax','eax','ax','al','ah'], 'rbx': ['rbx','ebx','bx','bl','bh'], 'rcx': ['rcx','ecx','cx','cl','ch'],
        'rdx': ['rdx','edx','dx','dl','dh'], 'rsi': ['rsi','esi','si','sil'], 'rdi': ['rdi','edi','di','dil'],
        'rbp': ['rbp','ebp','bp','bpl'], 'rsp': ['rsp','esp','sp','spl'],
    }
    for i in range(8, 16):
        fam[f'r{i}'] = [f'r{i}', f'r{i}d', f'r{i}w', f'r{i}b']
    for k, v in fam.items():
        for n in v: _canon[n] = k
_init()
VOLATILE = ['rax','rcx','rdx','r8','r9','r10','r11']

_insn_cache = {}
def insn_at(rva):
    i = _insn_cache.get(rva)
    if i is None:
        b = pe.bytes(rva, 16)
        l = list(cs.disasm(b, rva, 1))
        i = l[0] if l else False
        _insn_cache[rva] = i
    return i or None

def fourcc(v):
    b = struct.pack('<I', v & 0xffffffff)[::-1]
    return b.decode() if all(32 <= c < 127 for c in b) else None

def P(root, off=0): return ('p', root, off)
def add(v, k):
    if v is None: return None
    if v[0] == 'p': return ('p', v[1], v[2] + k)
    if v[0] == 'c': return ('c', v[1] + k)
    return None

def fmt(v):
    """pretty chain for a value/address"""
    if v is None: return '?'
    if v[0] == 'c': return hex(v[1])
    if v[0] != 'p': return str(v[0])
    root, off = v[1], v[2]
    o = ('+' if off >= 0 else '-') + hex(abs(off))
    if root == 'this': return 'this' + o
    if isinstance(root, tuple) and root[0] == 'L': return '[' + fmt(root[1]) + ']' + o
    if isinstance(root, tuple) and root[0] == 'g': return f'g:{root[1]:x}' + o
    if isinstance(root, tuple) and root[0] == 'ret': return f'ret:{root[1]:x}' + o
    return str(root) + o

def rooted(v):
    """True when the chain bottoms out at `this`"""
    while v is not None and v[0] == 'p':
        r = v[1]
        if r == 'this': return True
        if isinstance(r, tuple) and r[0] == 'L': v = r[1]; continue
        return False
    return False

class Live:
    def __init__(self, proc, samples):
        self.p = proc; self.samples = samples
    def ev(self, v, this):
        if v is None: return None
        if v[0] == 'c': return v[1]
        root, off = v[1], v[2]
        if root == 'this': return this + off
        if isinstance(root, tuple) and root[0] == 'L':
            a = self.ev(root[1], this)
            if not a: return None
            b = self.p.read(a, root[2])
            if not b: return None
            x = int.from_bytes(b, 'little')
            if not x and root[2] == 8: return None
            return x + off
        if isinstance(root, tuple) and root[0] == 'g':
            return self.p.base + root[1] + off
        return None
    def const_i32(self, a):
        key = ('k', fmt(a))
        c = getattr(self, '_c', None)
        if c is None: c = self._c = {}
        if key not in c:
            vals = set()
            for s in self.samples[:25]:
                root = a[1]
                t = self.ev(root[1], s)
                tp = self.p.u64(t) if t else 0
                if not (self.p.base <= tp < self.p.base + 0x18000000): vals.add(None); break
                b = self.p.read(tp + a[2], 4)
                vals.add(int.from_bytes(b, 'little', signed=True) if b else None)
            c[key] = vals.pop() if len(vals) == 1 else None
        return c[key]
    def vfunc(self, obj, slot_off):
        key = (fmt(obj), slot_off)
        c = getattr(self, '_c', None)
        if c is None: c = self._c = {}
        if key not in c: c[key] = self._vfunc(obj, slot_off)
        return c[key]
    def _vfunc(self, obj, slot_off):
        out = []
        for s in self.samples:
            a = self.ev(obj, s)
            if not a: continue
            vt = self.p.u64(a)
            if not (self.p.base <= vt < self.p.base + 0x18000000): continue
            f = self.p.u64(vt + slot_off)
            if self.p.base <= f < self.p.base + 0x18000000 and f - self.p.base not in out:
                out.append(f - self.p.base)
                if len(out) >= 4: break
        return out

class Emu:
    def __init__(self, live=None, maxdepth=4):
        self.live = live; self.maxdepth = maxdepth

    def run(self, start, regs, stack=None, limit=90, depth=0, is_fn=False, stop_at=None):
        """linear emulation. returns (rax, events). events: ('R'|'W', addr_value, size, signed/src) | ('V', type) | ('C', rva)"""
        regs = dict(regs); stack = dict(stack or {}); ev = []
        pc = start; n = 0; seen = set(); pend_variant = False
        work = []; visited = set(); ret_rax = [None]; fb = func(pe, start)
        def end_path():
            nonlocal pc, regs, stack, pend_variant
            if ret_rax[0] is None and regs.get('rax') is not None: ret_rax[0] = regs.get('rax')
            while work:
                t, r, st = work.pop()
                if t in visited: continue
                pc, regs, stack = t, r, st; pend_variant = False
                return True
            return False
        def val(op, insn):
            if op.type == X.X86_OP_REG:
                return regs.get(_canon.get(insn.reg_name(op.reg)))
            if op.type == X.X86_OP_IMM: return ('c', op.imm)
            return None
        def memaddr(op, insn):
            m = op.mem
            if m.base == 0: return None
            bn = insn.reg_name(m.base)
            if m.index != 0:
                bv = regs.get(_canon.get(bn)); iv = regs.get(_canon.get(insn.reg_name(m.index)))
                if bv is None or iv is None: return None
                if iv[0] == 'c' and bv[0] == 'p': return add(bv, iv[1] * m.scale + m.disp)
                if bv[0] == 'c' and iv[0] == 'p' and m.scale == 1: return add(iv, bv[1] + m.disp)
                return None
            if bn == 'rip': return P(('g', insn.address + insn.size + m.disp), 0)
            b = _canon.get(bn)
            if b in ('rsp', 'rbp') and regs.get(b) is None: return ('stk', b, m.disp)
            return add(regs.get(b), m.disp)
        def vb_const(a, size):
            # a displacement read from a virtual-base table: the table pointer sits in the object and points into the module
            if self.live is None or size != 4 or a is None or a[0] != 'p': return None
            root = a[1]
            if not (isinstance(root, tuple) and root[0] == 'L' and root[2] == 8 and rooted(root[1]) and 0 <= a[2] <= 0x40): return None
            return self.live.const_i32(a)
        def setreg(op, insn, v):
            name = insn.reg_name(op.reg); c = _canon.get(name)
            if c: regs[c] = v
        while n < limit:
            if (stop_at and pc in stop_at and n > 0) or pc in visited:
                if end_path(): continue
                break
            insn = insn_at(pc)
            if insn is None:
                if end_path(): continue
                break
            visited.add(pc)
            n += 1; m = insn.mnemonic; ops = insn.operands
            nxt = pc + insn.size
            if m == 'ret':
                if end_path(): continue
                break
            if m == 'jmp':
                if ops[0].type == X.X86_OP_IMM:
                    t = ops[0].imm
                    if is_fn and func(pe, t) != fb:   # tail call
                        r, e2 = self.call(t, regs, depth); ev += e2; regs['rax'] = r
                        if end_path(): continue
                        break
                    pc = t; continue
                elif ops[0].type == X.X86_OP_MEM and is_fn:
                    a = memaddr(ops[0], insn)
                    r = self.vcall(a, regs, depth, ev); regs['rax'] = r
                if end_path(): continue
                break
            if m == 'call':
                tgt = None
                _dx = regs.get('rdx')
                if _dx is not None and _dx[0] == 'c' and 0 <= _dx[1] < 0x200:
                    _t = ops[0].imm if ops[0].type == X.X86_OP_IMM else (fmt(memaddr(ops[0], insn)) if ops[0].type == X.X86_OP_MEM else '?')
                    if _t != VARIANT_FN: ev.append(('A', _t, _dx[1], pc))
                if ops[0].type == X.X86_OP_IMM:
                    tgt = ops[0].imm
                    if tgt == VARIANT_FN:
                        pend_variant = True
                        for r in VOLATILE: regs[r] = None
                        regs['rax'] = P(('variant',), 0); pc = nxt; continue
                    r, e2 = self.call(tgt, regs, depth); ev += e2
                elif ops[0].type == X.X86_OP_MEM:
                    a = memaddr(ops[0], insn)
                    r = self.vcall(a, regs, depth, ev)
                else:
                    r = None
                for x in VOLATILE: regs[x] = None
                regs['rax'] = r; pc = nxt; continue
            if m in ('mov', 'movzx', 'movsx', 'movsxd', 'movss', 'movsd', 'movups', 'movaps', 'movdqu', 'movq', 'movd') and len(ops) == 2:
                d, s = ops
                if s.type == X.X86_OP_MEM:
                    a = memaddr(s, insn)
                    if a is not None and a[0] == 'stk':
                        v = stack.get(a)
                    elif a is not None and vb_const(a, s.size) is not None:
                        v = ('c', vb_const(a, s.size))
                    elif a is not None:
                        signed = m in ('movsx', 'movsxd')
                        v = P(('L', a, s.size, signed), 0)
                        ev.append(('R', a, s.size, 'f' if m in ('movss','movsd') else ('s' if signed else 'u'), pc))
                    else:
                        v = None
                else:
                    v = val(s, insn)
                if d.type == X.X86_OP_REG:
                    setreg(d, insn, v)
                elif d.type == X.X86_OP_MEM:
                    a = memaddr(d, insn)
                    if a is not None and a[0] == 'stk': stack[a] = v
                    elif a is not None:
                        ev.append(('W', a, d.size, v, pc))
                pc = nxt; continue
            if m == 'lea':
                a = memaddr(ops[1], insn)
                setreg(ops[0], insn, None if (a is None or a[0] == 'stk') else a)
                pc = nxt; continue
            if m == 'sub' and ops[0].type == X.X86_OP_REG and ops[1].type == X.X86_OP_MEM and regs.get(_canon.get(insn.reg_name(ops[0].reg))) == ('c', 0) and ops[1].mem.disp == -4:
                pc = nxt; continue   # vtordisp: zero in practice
            if m == 'cdqe': pc = nxt; continue
            if m == 'add' and len(ops) == 2 and ops[0].type == X.X86_OP_REG and ops[1].type == X.X86_OP_REG:
                c0 = _canon.get(insn.reg_name(ops[0].reg)); a0 = regs.get(c0); a1 = regs.get(_canon.get(insn.reg_name(ops[1].reg)))
                if a0 is not None and a1 is not None and a1[0] == 'c': regs[c0] = add(a0, a1[1])
                elif a0 is not None and a1 is not None and a0[0] == 'c': regs[c0] = add(a1, a0[1])
                else: regs[c0] = None
                pc = nxt; continue
            if m in ('add', 'sub') and ops[0].type == X.X86_OP_REG and ops[1].type == X.X86_OP_IMM:
                c = _canon.get(insn.reg_name(ops[0].reg))
                if c in ('rsp',): pc = nxt; continue
                k = ops[1].imm if m == 'add' else -ops[1].imm
                v = regs.get(c)
                if pend_variant and c == 'rax' and m == 'add':
                    ev.append(('V', k, pc)); pend_variant = False
                regs[c] = add(v, k) if v is not None else None
                pc = nxt; continue
            if m in ('or', 'and', 'xor', 'not', 'neg', 'bts', 'btr', 'btc', 'shl', 'shr', 'sar', 'inc', 'dec', 'add', 'sub', 'imul') and ops and ops[0].type == X.X86_OP_MEM:
                a = memaddr(ops[0], insn)
                if a is not None and a[0] != 'stk':
                    src = val(ops[1], insn) if len(ops) > 1 else None
                    ev.append(('M', a, ops[0].size, (m, src), pc))
                pc = nxt; continue
            if m in ('cmp', 'test', 'bt') and ops:
                for o in ops:
                    if o.type == X.X86_OP_MEM:
                        a = memaddr(o, insn)
                        if a is not None and a[0] != 'stk': ev.append(('T', a, o.size, (m, ops[1].imm if ops[1].type == X.X86_OP_IMM else None), pc))
                pc = nxt; continue
            if m.startswith('j') and ops and ops[0].type == X.X86_OP_IMM:
                t = ops[0].imm
                if t not in visited and not (stop_at and t in stop_at) and (fb is None or func(pe, t) == fb):
                    work.append((t, dict(regs), dict(stack)))
                pc = nxt; continue
            if m.startswith('j') or m in ('nop', 'push', 'pop', 'int3', 'cdq', 'cqo'):
                if m == 'pop' and ops[0].type == X.X86_OP_REG: setreg(ops[0], insn, None)
                pc = nxt; continue
            # generic: reads from memory operand are still interesting; destination register becomes unknown
            for o in ops[1:]:
                if o.type == X.X86_OP_MEM:
                    a = memaddr(o, insn)
                    if a is not None and a[0] != 'stk': ev.append(('R', a, o.size, 'u', pc))
            if ops and ops[0].type == X.X86_OP_REG:
                if m == 'xor' and len(ops) == 2 and ops[1].type == X.X86_OP_REG and ops[0].reg == ops[1].reg:
                    setreg(ops[0], insn, ('c', 0))
                else:
                    setreg(ops[0], insn, None)
            pc = nxt
        return (ret_rax[0] if ret_rax[0] is not None else regs.get('rax')), ev

    def call(self, rva, regs, depth):
        # vtordisp adjustor thunk: xor eax,eax; sub eax,[rcx-4]; cdqe; add rcx,rax  (the displacement is 0 in practice)
        i1 = insn_at(rva)
        if i1 is not None and i1.mnemonic == 'xor' and i1.size == 2:
            i2 = insn_at(rva + 2)
            if i2 is not None and i2.mnemonic == 'sub' and 'rcx - 4' in i2.op_str:
                i3 = insn_at(rva + 2 + i2.size); i4 = insn_at(rva + 2 + i2.size + i3.size)
                if i3.mnemonic == 'cdqe' and i4.mnemonic == 'add' and i4.op_str == 'rcx, rax':
                    rva = rva + 2 + i2.size + i3.size + i4.size
        if depth >= self.maxdepth: return P(('ret', rva), 0), [('C', rva)]
        f = func(pe, rva)
        if f and f[1] - f[0] > MAXFN: return P(('ret', rva), 0), [('C', rva)]
        r = {k: regs.get(k) for k in ('rcx', 'rdx', 'r8', 'r9')}
        rax, ev = self.run(rva, r, limit=CALLEE_LIMIT, depth=depth + 1, is_fn=True)
        return rax, ev

    def vcall(self, a, regs, depth, ev):
        """call [a] where a = vtable(obj) + slot"""
        if a is None or a[0] != 'p': return None
        root, off = a[1], a[2]
        if not (isinstance(root, tuple) and root[0] == 'L' and root[2] == 8): return None
        obj = root[1]
        if self.live is None: return None
        fs = self.live.vfunc(obj, off)
        if not fs:
            ev.append(('C?', fmt(obj), off)); return None
        r = None
        for f in fs:
            r2, e2 = self.call(f, regs, depth); ev += e2
            if r is None: r = r2
        return r

def switch_map(fn):
    """code -> handler rva for a field switch function"""
    b, e = func(pe, fn)
    out = {}
    insns = list(cs.disasm(pe.bytes(b, e - b), b))
    for i, ins in enumerate(insns):
        ops = ins.operands
        if ins.mnemonic == 'cmp' and len(ops) == 2 and ops[0].type == X.X86_OP_REG and ops[1].type == X.X86_OP_IMM and ops[0].size == 4:
            c = fourcc(ops[1].imm)
            if not c or i + 1 >= len(insns): continue
            j = insns[i + 1]
            if j.mnemonic == 'je': out.setdefault(c, j.operands[0].imm)
            elif j.mnemonic == 'jne': out.setdefault(c, j.address + j.size)
        # jump table: lea eax,[reg-BASE]; cmp eax,N; ja d; lea rcx,[rip+T]; movsxd rax,[rcx+rax*4]; add rax,rcx; jmp rax
        basec = None
        if ins.mnemonic == 'lea' and ops[1].type == X.X86_OP_MEM and ops[1].mem.index == 0 and ops[1].mem.disp < 0:
            basec = (-ops[1].mem.disp) & 0xffffffff
        elif ins.mnemonic == 'add' and ops[1].type == X.X86_OP_IMM and ops[0].size == 4 and ops[1].imm < 0:
            basec = (-ops[1].imm) & 0xffffffff
        if basec and fourcc(basec):
            win = insns[i + 1:i + 9]
            cnt = None; tbl = None
            for w in win:
                wo = w.operands
                if w.mnemonic == 'cmp' and wo[1].type == X.X86_OP_IMM and cnt is None: cnt = wo[1].imm
                if w.mnemonic == 'lea' and wo[1].type == X.X86_OP_MEM and w.reg_name(wo[1].mem.base) == 'rip': tbl = w.address + w.size + wo[1].mem.disp
                if w.mnemonic == 'jmp' and wo[0].type == X.X86_OP_REG and cnt is not None and tbl is not None:
                    raw = pe.bytes(tbl, (cnt + 1) * 4)
                    for k in range(cnt + 1):
                        t = tbl + struct.unpack_from('<i', raw, k * 4)[0]
                        c = fourcc(basec + k)
                        if c and b <= t < e: out.setdefault(c, t)
                    break
    return out, (b, e)

def prologue(fn, live, extra=None):
    """register state when the switch starts: emulate from entry to the first compare against a code"""
    regs = {'rcx': P('this', 0), 'rdx': ('id',), 'r8': P(('arg',), 0), 'r9': P(('arg2',), 0)}
    if extra: regs.update(extra)
    emu = Emu(live)
    pc = fn; stack = {}
    # single-step with the emulator until a cmp reg,imm(fourcc)
    for _ in range(80):
        ins = insn_at(pc)
        if ins is None: break
        if ins.mnemonic == 'cmp' and len(ins.operands) == 2 and ins.operands[1].type == X.X86_OP_IMM and fourcc(ins.operands[1].imm): break
        if ins.mnemonic.startswith('j'): break
        if ins.mnemonic == 'call':
            for x in VOLATILE: regs[x] = None
            pc += ins.size; continue
        _, _ev = None, None
        # emulate exactly one instruction
        r2 = Emu1(emu, pc, regs, stack)
        regs, stack = r2
        pc += ins.size
    return regs, stack

def Emu1(emu, pc, regs, stack):
    """one-instruction step that returns the updated (regs, stack)"""
    ins = insn_at(pc)
    holder = {}
    class E2(Emu): pass
    # reuse run() with limit=1 but we need the mutated dicts: replicate via closure hack
    regs2 = dict(regs); stack2 = dict(stack)
    m = ins.mnemonic; ops = ins.operands
    def cn(op): return _canon.get(ins.reg_name(op.reg))
    def maddr(op):
        mm = op.mem
        if mm.index != 0 or mm.base == 0: return None
        bn = ins.reg_name(mm.base)
        if bn == 'rip': return P(('g', ins.address + ins.size + mm.disp), 0)
        b = _canon.get(bn)
        if b in ('rsp', 'rbp') and regs2.get(b) is None: return ('stk', b, mm.disp)
        return add(regs2.get(b), mm.disp)
    if m == 'mov' and len(ops) == 2:
        d, s = ops
        if s.type == X.X86_OP_REG: v = regs2.get(cn(s))
        elif s.type == X.X86_OP_MEM:
            a = maddr(s)
            v = stack2.get(a) if (a and a[0] == 'stk') else (P(('L', a, s.size, False), 0) if a else None)
        else: v = ('c', s.imm)
        if d.type == X.X86_OP_REG: regs2[cn(d)] = v
        elif d.type == X.X86_OP_MEM:
            a = maddr(d)
            if a and a[0] == 'stk': stack2[a] = v
    elif m == 'lea':
        a = maddr(ops[1]); regs2[cn(ops[0])] = None if (a is None or a[0] == 'stk') else a
    elif m in ('add', 'sub') and ops[0].type == X.X86_OP_REG and ops[1].type == X.X86_OP_IMM:
        c = cn(ops[0])
        if c != 'rsp':
            regs2[c] = add(regs2.get(c), ops[1].imm if m == 'add' else -ops[1].imm)
    elif ops and ops[0].type == X.X86_OP_REG and m not in ('push', 'cmp', 'test'):
        regs2[cn(ops[0])] = None
    return regs2, stack2

def analyse(fn, samples=None, proc=None, extra=None, limit=260):
    live = Live(proc, samples) if proc is not None and samples is not None else None
    hmap, (b, e) = switch_map(fn)
    regs, stack = prologue(fn, live, extra)
    emu = Emu(live)
    stops = set(hmap.values())
    res = {}
    for code, h in sorted(hmap.items()):
        _, ev = emu.run(h, regs, stack, limit=limit, stop_at=stops - {h})
        res[code] = ev
    return res, regs

def describe(ev, only_rooted=True):
    out = []
    for x in ev:
        k = x[0]
        if k in ('R', 'W', 'M', 'T'):
            if only_rooted and not rooted(x[1]): continue
            extra = ''
            if k == 'W': extra = ' <= ' + fmt(x[3])
            elif k in ('M', 'T'): extra = f' {x[3][0]} ' + (fmt(x[3][1]) if isinstance(x[3][1], tuple) else str(x[3][1]))
            elif k == 'R': extra = ' ' + x[3]
            out.append(f'{k} {fmt(x[1])}:{x[2]}{extra}')
        elif k == 'V': out.append(f'V{x[1]:x}')
        elif k == 'C': pass
        elif k == 'A': out.append(f'A idx={x[2]:#x} via {x[1] if isinstance(x[1],str) else hex(x[1])}')
        elif k == 'C?': out.append(f'vcall? {x[1]} slot {x[2]:x}')
    seen=set(); o2=[]
    for x in out:
        if x not in seen: seen.add(x); o2.append(x)
    return o2

if __name__ == '__main__':
    fn = int(sys.argv[1], 16)
    cls = sys.argv[2] if len(sys.argv) > 2 else None
    ofs = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0
    proc = samples = None
    if cls:
        p, _, cl, h = ctx(); proc = p
        a = objs(cls, 0)
        step = max(1, len(a) // 150)
        samples = [int(x) + ofs for x in a[::step]]
    res, regs = analyse(fn, samples, proc)
    print('prologue:', {k: fmt(v) for k, v in regs.items() if v is not None and v[0] == 'p'})
    names = {}
    try:
        sc = json.load(open(SCR + '/schema.json'))
        for rid, fl in sc.items():
            for f in fl: names.setdefault(f['field'], f.get('name', ''))
    except Exception: pass
    for code, ev in res.items():
        print(f'{code}  {names.get(code, "")[:40]:40s} | ' + ' ; '.join(describe(ev)[:14]))
