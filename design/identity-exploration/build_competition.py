from pathlib import Path
import json

out=Path(__file__).resolve().parents[2]/'src/DeckTester/wwwroot/assets/brand/concepts/competition-sets'
out.mkdir(parents=True,exist_ok=True)
ink='#26343a'
def p(d,fill=ink,**kw):
    return f'<path d="{d}" fill="{fill}" '+ ' '.join(f'{k.replace("_","-")}="{v}"' for k,v in kw.items())+'/>'
def circle(x,y,r,fill=ink): return f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}"/>'
def svg(content): return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">{content}</svg>'

# The small versions use fewer cutouts and wider gaps; these are optical variants.
logos=[
('01','Face-off','Two cards, one contest.',
 p('M10 10h23a5 5 0 0 1 5 5v36a5 5 0 0 1-5 5H10a5 5 0 0 1-5-5V15a5 5 0 0 1 5-5Zm3 8v30h17V18Z',fill_rule='evenodd',transform='rotate(-13 22 32)')+p('M33 8h21a5 5 0 0 1 5 5v36a5 5 0 0 1-5 5H33a5 5 0 0 1-5-5V13a5 5 0 0 1 5-5Z',fill='#fbf8f1',transform='rotate(13 44 32)')+p('M33 8h21a5 5 0 0 1 5 5v36a5 5 0 0 1-5 5H33a5 5 0 0 1-5-5V13a5 5 0 0 1 5-5Zm3 8v30h15V16Z',fill_rule='evenodd',transform='rotate(13 44 32)'),
 p('M9 9h24v46H9Zm7 8v30h10V17Z',fill_rule='evenodd',transform='rotate(-13 21 32)')+p('M30 6h27v49H30Z',fill='#fbf8f1',transform='rotate(13 43 32)')+p('M33 8h23v46H33Zm7 8v30h9V16Z',fill_rule='evenodd',transform='rotate(13 43 32)')),
('02','Arena','A ring for repeated battles.',
 p('M4 26C4 5 60 5 60 26v13C60 60 4 60 4 39Zm8-1c0 10 40 10 40 0S12 15 12 25Zm1 13v6l5 3v-7Zm16 5v8h6v-8Zm17-3v7l5-3v-6Z',fill_rule='evenodd'),
 p('M4 26C4 5 60 5 60 26v13C60 60 4 60 4 39Zm9-1c0 10 38 10 38 0s-38-10-38 0ZM28 43v9h8v-9Z',fill_rule='evenodd')),
('03','Victor','A laurel for completing the gauntlet.',
 p('M28 57C9 51 5 35 12 19l5 2c-5 15-1 26 14 30Zm8 0c19-6 23-22 16-38l-5 2c5 15 1 26-14 30Z')+p('M15 23C9 14 17 5 25 5c0 9-3 16-10 18Zm-4 14C1 33 1 23 5 17c7 4 12 11 6 20Zm7 13C7 52 2 44 3 36c9 0 16 5 15 14Zm31-27C55 14 47 5 39 5c0 9 3 16 10 18Zm4 14c10-4 10-14 6-20-7 4-12 11-6 20Zm-7 13c11 2 16-6 15-14-9 0-16 5-15 14Z'),
 p('M28 58C8 52 4 32 13 18l5 3c-7 14-2 25 14 30 16-5 21-16 14-30l5-3c9 14 5 34-15 40Z')+p('M15 26C7 16 14 5 26 4c0 10-4 17-11 22Zm-1 20C2 45 0 33 4 25c10 4 15 10 10 21Zm35-20C57 16 50 5 38 4c0 10 4 17 11 22Zm1 20c12-1 14-13 10-21-10 4-15 10-10 21Z')),
('04','Challenge','A banner carried into the gauntlet.',
 p('M12 5h8v51a4 4 0 0 1-8 0Zm13 4c11-8 17 8 32 0v28c-15 8-21-8-32 0Z')+p('M33 17c8 2 11 6 18 3v7c-7 3-10-1-18-3Z',fill='#fbf8f1'),
 p('M9 5h10v54H9Zm16 5c10-7 19 7 31 0v29c-12 7-21-7-31 0Z')),
('05','Spell clash','Two opposing spells at impact.',
 p('M5 13c15-8 27 3 24 16C24 20 17 19 5 22Zm54 38c-15 8-27-3-24-16 5 9 12 10 24 7Z')+p('M7 27c11-4 20-1 23 6-12-1-15 8-25 5Zm50 10c-11 4-20 1-23-6 12 1 15-8 25-5Z')+p('M31 15 40 31 33 49 24 33Z'),
 p('M4 12c15-7 27 3 25 17C21 20 13 21 4 23Zm56 40c-15 7-27-3-25-17 8 9 16 8 25 6Z')+p('M4 31c8-4 15-4 21 0-6 9-13 10-21 9Zm56 2c-8 4-15 4-21 0 6-9 13-10 21-9Z')+p('M32 17 41 32 32 47 23 32Z')),
]
colors={'white':'#f0dfae','blue':'#a7c6d9','black':'#c0accb','red':'#e7ae9c','green':'#b5c6a1','colorless':'#cbd2d4'}
mana={
'white':circle(32,32,10)+''.join(p('M29 18C24 13 27 7 33 5c-4 8 5 7 2 13Z',transform=f'rotate({a} 32 32)') for a in range(0,360,45)),
'blue':p('M32 7C26 19 14 29 14 40a18 18 0 0 0 36 0C50 29 38 19 32 7Z'),
'black':p('M32 10C18 10 10 20 10 33c0 8 5 12 12 14v9h7v-7h6v7h7v-9c7-2 12-6 12-14 0-13-8-23-22-23Zm-15 20a7 7 0 1 0 14 0 7 7 0 1 0-14 0Zm16 0a7 7 0 1 0 14 0 7 7 0 1 0-14 0Z',fill_rule='evenodd'),
'red':p('M36 5c14 17-8 22-1 32 6-3 10-9 10-14 17 26 3 35-14 35-19 0-26-17-12-32-1 12 5 11 5 5-1-11 9-14 12-26Z'),
'green':p('M26 53V40H18c-12 0-15-15-5-20-1-9 9-15 17-10 9-9 22-1 21 9 14 5 10 21-2 21H38v13l7 5H19Z')+p('M28 39 18 30l3-4 7 7v-9h7v9l8-8 4 4-12 12v9h-7Z',fill=colors['green']),
'colorless':p('M32 5C40 17 47 24 59 32 47 40 40 47 32 59 24 47 17 40 5 32 17 24 24 17 32 5Zm0 15L20 32l12 12 12-12Z',fill_rule='evenodd'),
}
micro=dict(mana)
micro['white']=circle(32,32,12)+p('M28 6h8v10h-8zm0 42h8v10h-8zM6 28h10v8H6zm42 0h10v8H48zM11 15l5-5 8 8-5 5zm29 30 5-5 8 8-5 5zM10 48l8-8 5 5-8 8zm30-30 8-8 5 5-8 8z')
micro['green']=p('M27 56V40H18C6 40 3 25 13 20c-1-9 9-15 17-10 9-9 22-1 21 9 14 5 10 21-2 21H37v16Z')
manifest=[]
for num,name,desc,large,small in logos:
    folder=out/num;folder.mkdir(exist_ok=True)
    for variant,logo,glyphs in [('master',large,mana),('small',small,micro)]:
        (folder/f'logo-{variant}.svg').write_text(svg(logo))
        for element,glyph in glyphs.items():
            (folder/f'{element}-{variant}.svg').write_text(svg(circle(32,32,32,colors[element])+f'<g transform="translate(4 4) scale(.875)">{glyph}</g>'))
    manifest.append(dict(number=num,name=name,description=desc))
(out/'manifest.json').write_text(json.dumps(manifest))
print(out)
