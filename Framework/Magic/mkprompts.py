import re,sys
from docx import Document
from docx.shared import Pt, RGBColor, Cm
from docx.enum.text import WD_BREAK
from docx.oxml.ns import qn
from docx.oxml import OxmlElement
src,out=sys.argv[1],sys.argv[2]
lines=open(src,encoding='utf-8').read().split('\n')
# sections by "## "
secs={}; cur=None
for l in lines:
    if l.startswith('## '): cur=l[3:].strip(); secs[cur]=[]; continue
    if l.startswith('# '): cur=None; continue
    if cur is not None: secs[cur].append(l)
def find(prefix):
    for k in secs:
        if k.startswith(prefix): return k,secs[k]
    raise SystemExit('no '+prefix)
def prompts(body): return [l[2:].strip() for l in body if l.startswith('> ')]
def notes(body): return [l.strip() for l in body if l.strip() and not l.startswith('> ')]

doc=Document()
st=doc.styles['Normal']; st.font.name='Calibri'; st.font.size=Pt(11)
for s in doc.sections: s.left_margin=s.right_margin=Cm(2); s.top_margin=s.bottom_margin=Cm(1.8)
def shade(cell,hexcol):
    tcPr=cell._tc.get_or_add_tcPr(); sh=OxmlElement('w:shd'); sh.set(qn('w:val'),'clear'); sh.set(qn('w:color'),'auto'); sh.set(qn('w:fill'),hexcol); tcPr.append(sh)
def box(text,label=None):
    if label:
        p=doc.add_paragraph(); r=p.add_run(label); r.bold=True; r.font.size=Pt(10); r.font.color.rgb=RGBColor(0x55,0x55,0x55)
    t=doc.add_table(rows=1,cols=1); t.style='Table Grid'; c=t.cell(0,0); shade(c,'F2EEE4')
    c.paragraphs[0].add_run(text).font.size=Pt(10)
    doc.add_paragraph()
def page(title, where, size, items, extra=None):
    doc.add_page_break()
    doc.add_heading(title,level=1)
    p=doc.add_paragraph(); r=p.add_run('Куда: '); r.bold=True; p.add_run(where)
    if size: p=doc.add_paragraph(); r=p.add_run('Размер и фон: '); r.bold=True; p.add_run(size)
    if extra: p=doc.add_paragraph(); r=p.add_run('Важно: '); r.bold=True; p.add_run(extra)
    for lab,txt in items: box(txt,lab)

doc.add_heading('Промпты: что ещё не нарисовано',0)
doc.add_paragraph('Один лист — одна страница. На каждой странице: куда кладётся, размер и фон, и сам промпт в рамке — копируй всё содержимое рамки целиком. Готовое (листы 18, 22, 24, 26, 28, 29, 31, 32, живые врата и весь лист 17) отсюда убрано. Листы 36–40 и глиняная стена — замена моих заглушек; 41–42 — Зенит и Блик наперёд.')
for b in ['Новый чат: сначала один раз отправь «Настройку чата» (следующая страница), потом листы по одному.',
          'Вышло грязно — допиши в том же чате: «cleaner shapes, smoother gradients, fewer particles». Вышло блёкло — «more light and depth, stronger glow and contrast, keep the clean shapes».',
          'Дал белый фон там, где нужен чёрный — «Redo exactly the same on a solid pure black background».',
          'Чёрный фон — для того, что светится. Прозрачный — для вещества, предметов, построек: в конце промпта уже стоит «Transparent Background», ChatGPT этого хватает.',
          'Готовые листы присылай мне как есть — режу и раскладываю сам.']:
    doc.add_paragraph(b,style='List Bullet')

k,b=find('Настройка чата'); page('Настройка чата (один раз в новом чате)','—','—',[(None,prompts(b)[0])])

# 17 remainder
if False:
  k,b=find('Лист 17'); p17=prompts(b)[0]
  pre=p17[:p17.index('Cells, left to right')].replace('1024x1024, 4x4 grid','1536x1024, 3x2 grid')
  cells=('Cells, left to right, top to bottom: (1) LIFE ROOTS A - a low tangle of pale grey-brown dead roots and thorns bursting from the ground, a faint sickly green light between them (2) roots B, another shape '
         '(3) roots C, another shape (4) DEAD GROUND A - withered grey grass lying flat, patchy, the ground showing through at the edges (an overlay) (5) DEAD GROUND B, another shape (6) TYPHOON - a white-grey spiral vortex of rain streaks')
  page('Лист 17б — корни Жизни, иссушённая земля, тайфун','Things/Mote/Avir/Magic/: Life/Roots_A..C, Life/DeadGround_A..B, Second/Typhoon','1536x1024, сетка 3x2, прозрачный фон',[(None,pre+cells)])

def simple(prefix,title=None):
    k,b=find(prefix); n=notes(b); ps=prompts(b)
    where=next((x.split(':',1)[1].strip() for x in n if x.startswith('Файл')), k.split('—',1)[-1].strip())
    other=[x for x in n if not x.startswith('Файл')]
    items=[(('Вариант '+str(i+1)) if len(ps)>1 else None,t) for i,t in enumerate(ps)]
    page(title or k, where, None, items, ' '.join(other) if other else None)

simple('Лист 19'); simple('Лист 20'); simple('Лист 21')
simple('Лист 23'); simple('Лист 25'); simple('Зелёный камень')
simple('Лист 30'); simple('Лист 35')


k,b=find('Остальные почвы')
base=('Clean, polished hand-painted 2D fantasy game art: smooth soft gradients, simple readable shapes, no noise, no grain, no text. '
      'A seamless tileable ground texture for RimWorld seen straight from above, 1024x1024, no transparency: {d} Many very small clean details evenly spread, no single large object, no shadows. Edges tile perfectly. Attach Avir_TwilightSoil.png as the style sample.')
items=[]
for l in b:
    m=re.match(r'- (Avir_\w+) \(([^)]*)\): (.*)',l.strip())
    if m: items.append((m.group(1)+' — '+m.group(2), base.format(d=m.group(3))))
page('Почвы мода — по одной картинке','Textures/Terrain/Surfaces/<имя>.png (имя — над рамкой)','1024x1024, бесшовная, без прозрачности; к каждой прикладывай Avir_TwilightSoil.png',items)
simple('Базальтовый пол'); simple('Базальтовая стена')
for k in ['Лист 36','Лист 37','Лист 38','Лист 39','Лист 40','Глиняная стена','Лист 41','Лист 42']: simple(k)
doc.save(out)
