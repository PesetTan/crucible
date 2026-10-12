from pathlib import Path
import json

OUT = Path(__file__).resolve().parents[2] / 'src/MtgSimulator/wwwroot/assets/brand/concepts/identity-sets'
OUT.mkdir(parents=True, exist_ok=True)

def path(d, **attrs):
    return '<path d="'+d+'" '+ ' '.join(f'{k.replace("_", "-")}="{v}"' for k,v in attrs.items())+'/>'

# All marks are native vector artwork on a 64-unit grid.
rounded = {
 'logo': path('M17 32V17a4 4 0 0 1 8 0v13h3V10a4 4 0 0 1 8 0v20h3V14a4 4 0 0 1 8 0v18h3V22a4 4 0 0 1 8 0v19c0 9-7 13-11 17H25L9 40a5 5 0 0 1 7-7l9 9v-6Z')+path('M27 49h17v5H27Z', fill='var(--paper)'),
 'white':'<circle cx="32" cy="32" r="12"/>'+path('M29 4h6v10h-6zm0 46h6v10h-6zM4 29h10v6H4zm46 0h10v6H50zM10 14l4-4 7 7-4 4zm33 33 4-4 7 7-4 4zM10 50l7-7 4 4-7 7zm33-33 7-7 4 4-7 7z'),
 'blue':path('M32 5C27 15 12 31 12 40a20 20 0 0 0 40 0C52 31 37 15 32 5Z'),
 'black':path('M32 7C17 7 10 17 10 30v12l11 5v10h22V47l11-5V30C54 17 47 7 32 7ZM18 27h11v11H18Zm17 0h11v11H35ZM29 47h6v10h-6Z',fill_rule='evenodd'),
 'red':path('M35 4c3 15-7 19-5 28l10-10c-1 11 14 15 14 28C54 61 11 61 11 44c0-9 8-16 12-20-1 13 4 12 5 9C20 20 29 10 35 4Z'),
 'green':path('M29 58V44H17a11 11 0 0 1-4-21 14 14 0 0 1 27-8 12 12 0 0 1 13 14 9 9 0 0 1-5 17H36v12Z'),
 'colorless':path('M32 4 60 32 32 60 4 32Zm0 13L17 32l15 15 15-15Z',fill_rule='evenodd'),
}
angular = {
 'logo':path('M18 30V15l4-4 5 4v17h4V9l4-4 5 4v23h4V15l4-4 5 4v17h4V25l4-4 4 4v18L50 59H26L9 39v-7l5-4 10 11v-6Z',transform='translate(-3 0)')+path('M26 47h19l-4 8H30Z',fill='var(--paper)'),
 'white':path('M32 3 38 16l14-4-4 14 13 6-13 6 4 14-14-4-6 13-6-13-14 4 4-14-13-6 13-6-4-14 14 4Z')+path('M26 26h12v12H26Z',fill='var(--paper)'),
 'blue':path('M32 4 52 34v14L42 58H22L12 48V34Z'),
 'black':path('M19 8h26l11 13v22l-13 6v10H21V49L8 43V21Zm-2 18v11h12V26Zm18 0v11h12V26ZM29 48v11h6V48Z',fill_rule='evenodd'),
 'red':path('M36 3 40 27 50 18l9 27-12 14H19L6 45l15-23 3 15Z')+path('m32 37-8 13h16Z',fill='var(--paper)'),
 'green':path('M32 3 51 25H42l16 18H37v16H27V43H6l16-18h-9Z'),
 'colorless':path('M32 3 61 32 32 61 3 32Zm0 14L17 32l15 15 15-15Z',fill_rule='evenodd'),
}
outline = {
 'logo':path('M21 37V18a4 4 0 0 1 8 0v13-20a4 4 0 0 1 8 0v20-16a4 4 0 0 1 8 0v17-10a4 4 0 0 1 8 0v18c0 8-5 13-9 17H26L11 39a5 5 0 0 1 7-7l10 10M29 48h14'),
 'white':'<circle cx="32" cy="32" r="11"/>'+path('M32 5v7m0 40v7M5 32h7m40 0h7M13 13l5 5m28 28 5 5M13 51l5-5m28-28 5-5'),
 'blue':path('M32 6C27 15 13 30 13 40a19 19 0 0 0 38 0C51 30 37 15 32 6Z'),
 'black':path('M32 8c-14 0-22 10-22 22v11l12 5v11h20V46l12-5V30C54 18 46 8 32 8ZM20 29v6m24-6v6M32 48v9'),
 'red':path('M34 5c-12 12-6 21-11 28l-4-10C2 44 14 58 32 58s28-13 17-31l-8 10c0-12 0-20-7-32Z'),
 'green':path('M32 58V32m0 12L21 34m11 4 10-11M19 46a11 11 0 0 1-7-20 13 13 0 0 1 24-14 12 12 0 0 1 16 16 10 10 0 0 1-7 18'),
 'colorless':path('M32 6 58 32 32 58 6 32Z'),
}
crest = {
 'logo':path('M13 29V19l7-5 7 4 7-6 8 5 8-2 8 7v17L47 49v10H22V48L8 36v-9Zm7-7v10h6V22Zm14-2v12h6V20Zm13 3v9h6v-9Z',fill_rule='evenodd')+path('M19 37h25v5H24Z',fill='var(--paper)'),
 'white':path('M32 4 37 18l12-8-3 15 15 2-12 9 11 10-15-1 2 15-12-10-9 11-2-15-15 4 8-13-13-7 15-4-6-14 13 7Z')+'<circle cx="32" cy="32" r="8" fill="var(--paper)"/>',
 'blue':path('M33 4C23 20 9 28 12 44c3 21 39 19 42-1C56 28 37 20 33 4Z')+path('M23 31c-7 11-2 20 7 21-6-7-4-13-1-21Z',fill='var(--paper)'),
 'black':path('M17 10 32 5l15 5 9 16-3 18-11 4v11H22V48l-11-4-3-18Zm0 17 3 12h10l-2-12Zm19 0-2 12h10l3-12ZM29 48v11h6V48Z',fill_rule='evenodd'),
 'red':path('M36 3c5 17-7 19-3 31l11-15c-3 11 16 18 13 30-4 19-47 14-48-4 0-10 10-19 10-27l7 17C16 19 29 14 36 3Z')+path('M33 40c-9 8-10 15 0 15s8-7 0-15Z',fill='var(--paper)'),
 'green':path('M26 58 29 40 18 46 7 37l4-13 10-3 2-12 15-5 11 12 10 5-1 15-12 9-11-5 4 18Z')+path('M30 24h5v12h-5Z',fill='var(--paper)'),
 'colorless':path('M32 3 40 23 61 32 40 41 32 61 24 41 3 32 24 23Zm0 20-9 9 9 9 9-9Z',fill_rule='evenodd'),
}
split = {
 'logo':path('M18 32V16l5-5 5 5v17h3V10l5-5 5 5v23h3V16l5-5 5 5v18h3V25h6v19L49 59H26L9 40v-9h7l10 12V32Z',transform='translate(-3 0)')+path('M32 34h25v9L45 55H32Z',fill='var(--facet)'),
 'white':path('M32 3 40 17l16-1-8 16 8 16-16-1-8 14-8-14-16 1 8-16-8-16 16 1Z')+path('M32 20 44 32 32 44Z',fill='var(--facet)'),
 'blue':path('M32 4 53 35v15L43 59H21L11 50V35Z')+path('M32 18 46 38v9l-8 6h-6Z',fill='var(--facet)'),
 'black':path('M20 7h24l12 14v22l-14 7v9H22v-9L8 43V21Z')+path('M32 13h10l8 11v15l-14 7v7h-4Z',fill='var(--facet)')+path('M16 27h12v11H16Zm20 0h12v11H36ZM29 48h6v11h-6Z',fill='var(--paper)'),
 'red':path('M35 3 39 29 50 20l9 27-13 12H18L6 45l16-22 4 13Z')+path('M35 31 48 47l-8 7H28Z',fill='var(--facet)'),
 'green':path('M19 7h26l14 19-4 15-18 6v12H27V47L9 41 5 26Z')+path('M32 13h10l11 14-3 10-18 7Z',fill='var(--facet)'),
 'colorless':path('M32 3 61 32 32 61 3 32Zm0 16L19 32l13 13 13-13Z',fill_rule='evenodd')+path('M32 3 61 32 32 61V45l13-13-13-13Z',fill='var(--facet)'),
}
sets = [
 ('01','Soft stamp','Rounded silhouettes with generous open space. Quiet and easy to read.',rounded,'solid'),
 ('02','Cut steel','Crisp corners and bold cuts. The most direct match for an armored gauntlet.',angular,'solid'),
 ('03','Continuous line','Round-ended strokes throughout. Lighter, with a clear sun and branching tree.',outline,'outline'),
 ('04','Guild seal','A clenched armored fist and more heraldic mana shapes. Strong fantasy character.',crest,'solid'),
 ('05','Split facet','Two flat tones create depth without texture. The same split face appears across the set.',split,'solid'),
]
names=['logo','white','blue','black','red','green','colorless']
colors={'logo':'#d1d7dd','white':'#f0dca7','blue':'#9ebed6','black':'#b8a7c5','red':'#e1a18d','green':'#acc397','colorless':'#c6ced4'}
facets={'logo':'#91a9bb','white':'#a58943','blue':'#487da6','black':'#7e618f','red':'#a95840','green':'#60833e','colorless':'#748492'}
def svg(glyph, mode, name, chip=True):
    bg=colors[name] if chip else '#fffaf1'
    attrs='fill="none" stroke="currentColor" stroke-width="5" stroke-linejoin="round" stroke-linecap="round"' if mode=='outline' else 'fill="currentColor"'
    # Circle is kept outside the glyph, so the glyph can also be reused as a mask.
    circle=f'<circle cx="32" cy="32" r="32" fill="{bg}"/>' if chip and name!='logo' else ''
    scale='translate(7 7) scale(.78125)' if chip and name!='logo' else 'translate(2 2) scale(.9375)'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" role="img" aria-label="{name if name=="logo" else name+" mana"}" style="color:#26333e;--paper:{bg};--facet:{facets[name]}">{circle}<g transform="{scale}" {attrs}>{glyph}</g></svg>'

data=[]
for number,title,desc,glyphs,mode in sets:
    folder=OUT/number; folder.mkdir(exist_ok=True)
    icons={n:svg(glyphs[n],mode,n) for n in names}
    for n,s in icons.items(): (folder/f'{n}.svg').write_text(s)
    data.append(dict(number=number,title=title,desc=desc,icons=icons))

html='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Gauntlet — five identity sets</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#f5f2ec;color:#26333e;font:15px/1.5 system-ui,sans-serif}main{max-width:1160px;margin:auto;padding:42px 28px 60px}header{display:flex;align-items:flex-end;justify-content:space-between;gap:28px;margin-bottom:26px}.eyebrow{font-size:11px;font-weight:750;letter-spacing:2px;text-transform:uppercase;color:#747d80}h1{font-size:36px;line-height:1.15;letter-spacing:-1.4px;margin:9px 0 12px}header p{max-width:610px;margin:0;color:#687176}.controls{display:flex;gap:8px;flex-shrink:0}button{font:inherit;border:1px solid #c9cec9;padding:8px 14px;background:#fffdf8;color:#26333e;border-radius:8px;cursor:pointer}button[aria-pressed=true]{background:#26333e;color:white;border-color:#26333e}article{margin-top:18px;background:#fffdf8;border:1px solid #deded5;border-radius:16px;overflow:hidden}.head{padding:20px 24px 0;display:flex;justify-content:space-between;gap:20px}.title{display:flex;gap:14px;align-items:baseline}h2{font-size:20px;letter-spacing:-.4px;margin:0}.number{font-size:12px;color:#737d83;font-variant-numeric:tabular-nums}.head p{margin:5px 0 0;color:#687176;font-size:13px}.pick{font-size:12px;padding:6px 12px;align-self:flex-start}.set{display:grid;grid-template-columns:1.8fr repeat(6,1fr);align-items:center;padding:24px;gap:20px}.icon{text-align:center}.icon svg{display:block;width:52px;height:52px;margin:auto}.brand svg{width:76px;height:76px}.label{display:block;margin-top:9px;font-size:11px;color:#6e797d}.brand .label{font-size:16px;font-weight:750;letter-spacing:1.6px;color:inherit}.proof{border-top:1px solid #e5e5de;display:flex;align-items:center;justify-content:space-between;padding:14px 24px;gap:20px;flex-wrap:wrap;background:#f7f7f1}.sample{display:flex;align-items:center;gap:12px}.sample>span{font-size:11px;color:#748087}.cost{display:flex;gap:3px;align-items:center}.cost svg{width:var(--size);height:var(--size);display:block}.mini-logo{width:24px;height:24px;display:flex}.mini-logo svg{width:100%;height:100%}.selected{border:2px solid #657c69}.choice{position:sticky;bottom:16px;background:#26333e;color:#fff;border-radius:10px;padding:12px 20px;display:none;box-shadow:0 4px 20px #0002}.dark{background:#161e25;color:#eef1ec}.dark article{background:#27333d;border-color:#46505a}.dark .proof{background:#1d2932;border-color:#46505a}.dark .head p,.dark .label,.dark .sample>span{color:#b9c4ca}.dark .brand svg,.dark .mini-logo svg{filter:drop-shadow(0 0 1px #b9c4ca)}.dark .brand{background:#fffdf8;border-radius:10px;padding:8px;color:#26333e}.dark .mini-logo{background:#fffdf8;border-radius:4px}.dark header p,.dark .eyebrow{color:#bbc5cc}button:focus-visible{outline:3px solid #6199c2;outline-offset:3px}@media(max-width:700px){main{padding:24px 12px}header{display:block}.controls{margin-top:16px}.head{padding:16px 14px 0}.set{padding:18px 12px;gap:8px;grid-template-columns:1.4fr repeat(6,1fr)}.icon svg{width:30px;height:30px}.brand svg{width:48px;height:48px}.brand .label{font-size:10px}.label{font-size:9px}.proof{padding:12px 14px}h1{font-size:30px}.head p{max-width:430px}.title{gap:8px}}
</style><main><header><div><div class="eyebrow">Gauntlet / Identity exploration</div><h1>Five families. One clear silhouette.</h1><p>Each row is a complete set. Compare the large drawings, then judge the actual 16, 20, and 24 px samples underneath.</p></div><div class="controls"><button id="light" aria-pressed="true">Light</button><button id="dark" aria-pressed="false">Dark</button></div></header><div id="sets"></div><div class="choice" role="status" id="choice"></div></main>
<script>
const sets=DATA;
document.getElementById('sets').innerHTML=sets.map(s=>`<article id="set-${s.number}"><div class="head"><div><div class="title"><span class="number">${s.number}</span><h2>${s.title}</h2></div><p>${s.desc}</p></div><button class="pick" data-pick="${s.number}" aria-pressed="false">Shortlist</button></div><div class="set">${Object.entries(s.icons).map(([n,svg])=>`<div class="icon ${n==='logo'?'brand':''}">${svg}<span class="label">${n==='logo'?'GAUNTLET':n[0].toUpperCase()+n.slice(1)}</span></div>`).join('')}</div><div class="proof">${[16,20,24].map(size=>`<div class="sample"><span>${size} px</span><div class="mini-logo" style="width:${size}px;height:${size}px">${s.icons.logo}</div><div class="cost" style="--size:${size}px">${Object.entries(s.icons).filter(([n])=>n!=='logo').map(([,v])=>v).join('')}</div></div>`).join('')}</div></article>`).join('');
for (const mode of ['light','dark']) document.getElementById(mode).onclick=()=>{document.body.classList.toggle('dark',mode==='dark');for(const m of ['light','dark'])document.getElementById(m).setAttribute('aria-pressed',m===mode)};
document.querySelectorAll('[data-pick]').forEach(b=>b.onclick=()=>{const selected=b.getAttribute('aria-pressed')!=='true';b.setAttribute('aria-pressed',selected);b.textContent=selected?'Shortlisted':'Shortlist';document.getElementById('set-'+b.dataset.pick).classList.toggle('selected',selected);const picks=[...document.querySelectorAll('[data-pick][aria-pressed=true]')].map(x=>x.dataset.pick);const status=document.getElementById('choice');status.style.display=picks.length?'block':'none';status.textContent='Your shortlist: '+picks.join(', ')+'. Tell me the number(s) in chat to refine or apply.'});
</script></html>'''.replace('DATA',json.dumps(data))
(OUT/'index.html').write_text(html)
print(OUT/'index.html')

# An offline contact sheet for visual review and an inline chat preview.
import re
def concrete(mark):
    variables=dict(re.findall(r'(--[a-z]+):([^;"}]+)',mark))
    for key,value in variables.items(): mark=mark.replace(f'var({key})',value)
    return mark.replace('currentColor','#26333e')
board=['<svg xmlns="http://www.w3.org/2000/svg" width="1080" height="1250" viewBox="0 0 1080 1250"><rect width="1080" height="1250" fill="#f5f2ec"/><g font-family="Arial, sans-serif" fill="#26333e"><text x="40" y="48" font-size="28" font-weight="700">GAUNTLET / Five matching icon families</text><text x="40" y="76" font-size="14" fill="#687176">Logo + white, blue, black, red, green, colorless mana. Small rows shown at actual pixel sizes.</text>']
for i,s in enumerate(data):
    y=102+i*224
    board.append(f'<rect x="24" y="{y}" width="1032" height="208" rx="14" fill="#fffdf8" stroke="#deded5"/><text x="44" y="{y+31}" font-size="18" font-weight="700">{s["number"]}   {s["title"]}</text>')
    for j,(n,mark) in enumerate(s['icons'].items()):
        sz=64 if n=='logo' else 48
        x=76+j*141
        board.append(concrete(mark).replace('<svg ',f'<svg x="{x}" y="{y+48}" width="{sz}" height="{sz}" ',1))
        board.append(f'<text x="{x+sz/2}" y="{y+131}" font-size="11" text-anchor="middle" fill="#687176">{n.upper() if n=="logo" else n.capitalize()}</text>')
    for k,size in enumerate([16,20,24]):
        x=44+k*335
        board.append(f'<text x="{x}" y="{y+176}" font-size="11" fill="#687176">{size} px</text>')
        for j,mark in enumerate(s['icons'].values()):
            board.append(concrete(mark).replace('<svg ',f'<svg x="{x+48+j*(size+5)}" y="{y+162}" width="{size}" height="{size}" ',1))
board.append('</g></svg>')
(OUT/'comparison.svg').write_text(''.join(board))
