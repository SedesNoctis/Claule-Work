import sys, xml.etree.ElementTree as ET
from PIL import Image, ImageDraw
src, out = sys.argv[1], sys.argv[2]
crop = [int(v) for v in sys.argv[3].split(',')] if len(sys.argv) > 3 and sys.argv[3] else None
r=ET.parse(src).getroot().find('LivingCities.LayoutDef')
L=lambda t:[li.text or '' for li in r.find(t)] if r.find(t) is not None else []
W,H=[int(v) for v in r.find('size').text.strip('()').split(',')]
keys=L('terrainKeys'); rows=L('terrain'); roof=L('roof')
def code(row,i):
    a=row[i]; b=row[i+1]
    A='0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ'
    return A.index(a)*62+A.index(b)
def tcol(k):
    k=k.lower()
    if 'ice' in k: return (150,190,215)
    if 'water' in k or 'marsh' in k: return (60,90,150)
    if 'bridge' in k: return (140,105,60)
    if 'soilrich' in k: return (70,85,60)
    if 'soil' in k: return (85,95,70)
    if 'gravel' in k: return (110,105,95)
    if 'rough' in k and 'ks_' not in k: return (75,72,78)
    if 'ancient' in k: return (95,95,100)
    return (150,140,125)   # built floors
img=Image.new('RGB',(W,H))
px=img.load()
for i,row in enumerate(rows):
    z=H-1-i
    for x in range(min(W,len(row)//2)):
        try: k=keys[code(row,x*2)]
        except Exception: k='?'
        px[x,H-1-z]=tcol(k)
# roof shading
for i,row in enumerate(roof):
    for x,ch in enumerate(row[:W]):
        if ch in 'th':
            c=px[x,i]; px[x,i]=tuple(int(v*0.55) for v in c)
        elif ch=='c':
            c=px[x,i]; px[x,i]=tuple(int(v*0.8) for v in c)
for t in L('things'):
    p=t.split('|'); d=p[0]; x=int(p[2]); z=int(p[3]); y=H-1-z
    if not (0<=x<W and 0<=y<H): continue
    dl=d.lower()
    if d in ('Slate','Granite','Limestone','Sandstone','Marble') or 'mineable' in dl or 'golemrock' in dl: col=(40,38,44)
    elif 'wall' in dl and 'lamp' not in dl and 'torch' not in dl: col=(200,170,120)
    elif 'door' in dl or 'arch' in dl: col=(230,120,40)
    elif 'column' in dl: col=(240,240,200)
    elif 'boulder' in dl or 'chunk' in dl: col=(60,55,60)
    elif 'geyser' in dl: col=(255,60,200)
    else: col=(90,200,230)
    px[x,y]=col
for z in L('zones'):
    p=z.split('|')
    for run in p[3].split(';'):
        if not run: continue
        a=run.split(','); x0,zz,n=int(a[0]),int(a[1]),int(a[2])
        for k in range(n):
            x=x0+k; y=H-1-zz
            c=px[x,y]; px[x,y]=((c[0]+60)//2,(c[1]+200)//2,(c[2]+60)//2)
if crop:
    x0,z0,x1,z1=crop; img=img.crop((x0,H-1-z1,x1,H-1-z0))
s=int(sys.argv[4]) if len(sys.argv)>4 else 3
img=img.resize((img.width*s,img.height*s),Image.NEAREST)
img.save(out)
