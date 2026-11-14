import sys, os, struct, pickle
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from memlib import *
import numpy as np
p, pe, cl, h = ctx()
persons = [int(a) + 0x288 for a in objs('db::ACTUAL_PLAYER', 0)]
rows = []
for per in persons:
    try:
        arr = p.read(per - 0x138, 0x48)   # attribute array idx 0..71
        ca, pa = struct.unpack('<hh', p.read(per - 0x24, 4))
        extra = p.read(per - 0x40, 0x40)   # around CA, for unknown fields
        rows.append((per, arr, ca, pa, extra))
    except Exception:
        pass
pickle.dump(rows, open(os.path.join(os.path.dirname(__file__), 'ca_rows.pkl'), 'wb'))
print(len(rows))
