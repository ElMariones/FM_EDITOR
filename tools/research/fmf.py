import sys, zstandard, os
def frames(path):
    data = open(path,'rb').read()
    magic = b'\x28\xb5\x2f\xfd'
    out=[]; pos=0
    d = zstandard.ZstdDecompressor()
    while True:
        i = data.find(magic,pos)
        if i<0: break
        try:
            o = d.decompressobj()
            chunk = o.decompress(data[i:])
            used = len(data[i:]) - len(o.unused_data)
            out.append((i,chunk)); pos = i+max(used,4)
        except Exception as e:
            pos = i+4
    return data,out
if __name__=='__main__':
    data,fr = frames(sys.argv[1])
    print(len(data), 'bytes', len(fr),'frames')
    outdir = sys.argv[2] if len(sys.argv)>2 else None
    for i,(off,c) in enumerate(fr):
        print(i, hex(off), len(c), c[:80])
        if outdir:
            os.makedirs(outdir,exist_ok=True)
            open(os.path.join(outdir,f'{i:04d}.bin'),'wb').write(c)
