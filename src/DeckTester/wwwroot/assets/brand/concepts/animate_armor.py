from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageOps

BASE = Path(__file__).parent
OUT = BASE / 'animations' / 'magnetic'
OUT.mkdir(exist_ok=True)
NAMES = ['silver', 'steel-blue', 'cobalt-steel', 'verdigris', 'red-copper']
LABELS = ['Silver', 'Steel blue', 'Cobalt steel', 'Verdigris', 'Red copper']
BG = (9, 19, 35)
SIZE = 560
FONT = '/System/Library/Fonts/Supplemental/Arial Bold.ttf'
TITLE = ImageFont.truetype(FONT, 22)
WORD = ImageFont.truetype(FONT, 43)

def prepare(name):
    source = Image.open(BASE / f'gauntlet-26-{name}-solid-cuff.png').convert('RGB')
    source = source.crop((260, 120, 1052, 956))
    a = np.array(source)
    active = a.max(axis=2) > 60
    ys, xs = np.where(active)
    source = source.crop((int(xs.min()), int(ys.min()), int(xs.max())+1, int(ys.max())+1))
    source = ImageOps.contain(source, (280, 360), Image.Resampling.LANCZOS)
    a = np.array(source)
    active = a.max(axis=2) > 60
    h, w = active.shape
    seen = np.zeros_like(active)
    pieces = []
    for y, x in zip(*np.where(active)):
        if seen[y, x]:
            continue
        seen[y, x] = True
        stack = [(int(y), int(x))]
        pts = []
        while stack:
            yy, xx = stack.pop()
            pts.append((yy, xx))
            for ny, nx in ((yy-1, xx), (yy+1, xx), (yy, xx-1), (yy, xx+1)):
                if 0 <= ny < h and 0 <= nx < w and active[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    stack.append((ny, nx))
        if len(pts) < 40:
            continue
        coords = np.array(pts)
        mask = np.zeros((h, w), dtype=np.uint8)
        mask[coords[:, 0], coords[:, 1]] = 255
        mask = Image.fromarray(mask).filter(ImageFilter.MaxFilter(3))
        layer = source.convert('RGBA')
        layer.putalpha(mask)
        bounds = mask.getbbox()
        layer = layer.crop(bounds)
        cx, cy = (bounds[0]+bounds[2])/2, (bounds[1]+bounds[3])/2
        pieces.append((layer, cx-w/2, cy-h/2))
    print(name, 'armor plates:', len(pieces), flush=True)
    assert 8 <= len(pieces) <= 35, 'Unexpected plate segmentation'
    return source, pieces

def frame(name, label, source, pieces, t):
    im = Image.new('RGBA', (SIZE, SIZE), BG+(255,))
    d = ImageDraw.Draw(im)
    d.text((280, 24), label, font=TITLE, fill='#b4c1d0', anchor='mt')
    impact = max(0, 1-abs(t-0.99)/0.13)
    shake = math.sin(t*150)*5*impact
    center = (280+shake, 260+math.cos(t*120)*3*impact)
    for layer, dx, dy in pieces:
        angle = math.atan2(dy, dx)
        delay = .11 + .075*(1+math.sin(angle*2))
        u = min(1, max(0, (t-delay)/(.80-delay)))
        # Magnetic attraction: a slow drift builds to a steep speed increase near contact.
        remaining = 1-math.expm1(6.5*u)/math.expm1(6.5)
        if u >= 1:
            remaining = -.028*math.exp(-18*max(0,t-.80))*math.sin(35*max(0,t-.80))
        distance = 55+15*math.cos(angle*3)
        x = center[0]+dx+math.cos(angle)*distance*remaining
        y = center[1]+dy+math.sin(angle)*distance*remaining
        rotated = layer.rotate(12*math.sin(angle*2)*remaining, resample=Image.Resampling.BICUBIC, expand=True)
        im.alpha_composite(rotated, (round(x-rotated.width/2), round(y-rotated.height/2)))
    if .80 <= t < 1.14:
        burst = (t-.80)/.34
        effects = Image.new('RGBA', im.size)
        ed = ImageDraw.Draw(effects)
        for i in range(16):
            theta = i*math.tau/16+.14
            r = 35+burst*195
            x, y = center[0]+math.cos(theta)*r, center[1]+math.sin(theta)*r
            ed.line((x,y,x+math.cos(theta)*12*(1-burst),y+math.sin(theta)*12*(1-burst)), fill=(211,225,236,int(150*(1-burst))),width=2)
        im = Image.alpha_composite(im,effects)
    if t >= 1.15:
        # Exact complete source art on the final held pose.
        final = source.convert('RGBA')
        aa = np.array(source).max(axis=2)
        final.putalpha(Image.fromarray(np.where(aa>50,255,0).astype('uint8')))
        im = Image.new('RGBA',(SIZE,SIZE),BG+(255,))
        ImageDraw.Draw(im).text((280,24),label,font=TITLE,fill='#b4c1d0',anchor='mt')
        im.alpha_composite(final,(280-final.width//2,260-final.height//2))
    word_alpha = min(1,max(0,(t-.80)/.3))
    word = Image.new('RGBA',im.size)
    ImageDraw.Draw(word).text((280,492),'Gauntlet',font=WORD,fill=(246,248,252,round(255*word_alpha)),anchor='mt')
    return Image.alpha_composite(im,word).convert('RGB')

def main():
    TIMES = [i*.04 for i in range(33)]
    DURATIONS = [40]*32+[1080]
    all_frames=[]
    for name,label in zip(NAMES,LABELS):
        source,pieces=prepare(name)
        frames=[frame(name,label,source,pieces,t) for t in TIMES]
        frames[0].save(OUT/f'gauntlet-{name}-assembly.gif',save_all=True,append_images=frames[1:],duration=DURATIONS,loop=0,optimize=False,disposal=2)
        all_frames.append(frames)

    comparison=[]
    for k in range(len(TIMES)):
        canvas=Image.new('RGB',(1050,700),BG)
        for i,frames in enumerate(all_frames):
            thumb=frames[k].resize((350,350),Image.Resampling.LANCZOS)
            x=i*350 if i<3 else 175+(i-3)*350
            y=0 if i<3 else 350
            canvas.paste(thumb,(x,y))
        comparison.append(canvas)
    comparison[0].save(OUT/'gauntlet-five-armor-assembly.gif',save_all=True,append_images=comparison[1:],duration=DURATIONS,loop=0,optimize=False,disposal=2)
    qa=Image.new('RGB',(1120,560),BG)
    for i,k in enumerate([0,10,19,32]):
        qa.paste(all_frames[0][k].resize((280,280)),(i*280,0))
        qa.paste(comparison[k].resize((420,280)),((i%2)*560,280)) if i<2 else None
    qa.save(OUT/'assembly-review.png')
    for p in OUT.glob('*.gif'):
        gif=Image.open(p)
        duration=0
        for k in range(gif.n_frames):
            gif.seek(k)
            duration+=gif.info.get('duration',0)
        assert gif.n_frames>20 and duration==2360
        print(p.name,gif.n_frames,'frames',duration,'ms',p.stat().st_size,'bytes',flush=True)

if __name__ == "__main__":
    main()
