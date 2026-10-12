"""Export once-only SVG armor assembly and matching static favicon assets."""
from pathlib import Path
from io import BytesIO
import base64
import math
import numpy as np
from PIL import Image
from animate_armor import prepare, NAMES

OUT = Path(__file__).parent.parent / 'assembly'
OUT.mkdir(exist_ok=True)

def data_url(image):
    buffer = BytesIO()
    image.save(buffer, format='PNG')
    return 'data:image/png;base64,' + base64.b64encode(buffer.getvalue()).decode()

for name in NAMES:
    source, pieces = prepare(name)
    static = source.convert('RGBA')
    static.putalpha(Image.fromarray(np.where(np.array(source).max(axis=2)>50,255,0).astype('uint8')))
    view = '-190 -210 380 420'
    start = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{view}" role="img" aria-label="Gauntlet">'
    finish = f'<image x="{-source.width/2}" y="{-source.height/2}" width="{source.width}" height="{source.height}" href="{data_url(static)}"/>'
    (OUT / f'{name}-static.svg').write_text(start+finish+'</svg>')
    # A doubled animation viewport lets the pieces begin farther apart. The
    # app renders it at twice the width, retaining the assembled logo's size.
    start = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-380 -420 760 840" role="img" aria-label="Gauntlet">'
    styles = []
    groups = []
    for i, (layer, dx, dy) in enumerate(pieces):
        angle = math.atan2(dy, dx)
        delay = .11+.075*(1+math.sin(angle*2))
        distance = 165+45*math.cos(angle*3)
        offset_x, offset_y = math.cos(angle)*distance, math.sin(angle)*distance
        rotation = 12*math.sin(angle*2)
        positions = []
        for k in range(21):
            u = k/20
            remaining = 1-math.expm1(6.5*u)/math.expm1(6.5)
            positions.append(f'{k*5}%{{transform:translate({offset_x*remaining:.3f}px,{offset_y*remaining:.3f}px) rotate({rotation*remaining:.3f}deg)}}')
        styles.append(f'@keyframes plate{i}{{'+''.join(positions)+'}')
        styles.append(f'.plate{i}{{animation:plate{i} {.8-delay:.3f}s linear {delay:.3f}s 1 both}}')
        groups.append(f'<g transform="translate({dx:.3f} {dy:.3f})"><g class="plate{i}"><image x="{-layer.width/2}" y="{-layer.height/2}" width="{layer.width}" height="{layer.height}" href="{data_url(layer)}"/></g></g>')
    styles.append('.finished{display:none}@media(prefers-reduced-motion:reduce){.parts{display:none}.finished{display:inline}}')
    (OUT / f'{name}.svg').write_text(start+'<style>'+''.join(styles)+'</style><g class="parts">'+''.join(groups)+'</g><g class="finished">'+finish+'</g></svg>')
    print(name, 'once-only animated SVG and static icon exported')
