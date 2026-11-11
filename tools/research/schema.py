import glob, json, re, xml.etree.ElementTree as ET, os, sys
SCR=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
def clean(s): return re.sub(r'\[COMMENT:.*?\]','',s or '').strip()
def parse():
    out={}
    for f in sorted(glob.glob(SCR+'/out_editor/*.bin')):
        t=open(f,'rb').read().decode('utf-8-sig','replace')
        try: root=ET.fromstring(t)
        except Exception as e:
            print('bad xml',f,e,file=sys.stderr); continue
        for rec in root.findall('record'):
            rid=rec.get('id')
            fl=[l for l in rec.findall('list') if l.get('id')=='fields']
            if not fl: continue
            fields=[]
            def walk(lst,parent):
                for r in lst.findall('record'):
                    d={}
                    for c in r:
                        i=c.get('id')
                        if i in('field','type','database_table_type'): d[i]=c.get('value')
                        elif i=='name': d['name']=clean(c.get('value'))
                        elif i=='category': d['category']=clean(c.get('value'))
                        elif i in('min_value','max_value','default_value'): d[i]=c.get('value')
                        elif i in('debug_only','is_read_only','is_ige_only','is_client_field','is_optional'): d[i]=c.get('value')
                        elif i=='enum_values' and c.tag=='list':
                            d['enum']=[(e.get('enum'), clean(next((x.get('value') for x in e if x.get('id')=='name'),''))) for e in c.findall('record')]
                        elif i=='enum_values': d['enum_ref']=c.get('value')
                        elif i=='fields' and c.tag=='list':
                            d['sub']=[]; 
                    if parent: d['parent']=parent
                    if 'field' in d: fields.append(d)
                    for c in r:
                        if c.tag=='list' and c.get('id')=='fields': walk(c,d.get('field'))
            walk(fl[0],None)
            out.setdefault(rid,[]).extend(fields)
    return out
if __name__=='__main__':
    s=parse(); json.dump(s,open(SCR+'/schema.json','w'),indent=1)
    for k,v in s.items(): print(k,len(v))
