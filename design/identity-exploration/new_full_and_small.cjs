const fs=require('fs'),path=require('path');
const sharp=require('/Users/pesettan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const out=path.resolve(__dirname,'../../src/DeckTester/wwwroot/assets/brand/concepts/full-and-small');fs.mkdirSync(out,{recursive:true});
const ink='#29373a',paper='#faf7f0';
const P=(d,attrs='')=>`<path d="${d}" ${attrs}/>`;
const circle=(x,y,r)=>`<circle cx="${x}" cy="${y}" r="${r}"/>`;
const wrap=body=>`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><g fill="${ink}">${body}</g></svg>`;
const sets=[
 {id:'01',name:'Crucible',desc:'A deck tested by fire.',master:P('M15 51Q50 64 85 51C81 71 69 80 56 83L61 91H39L44 83C31 80 19 71 15 51ZM24 61C30 72 39 76 48 77V71C39 70 31 67 24 61Z','fill-rule="evenodd"')+P('M55 7C72 27 41 29 50 46C30 44 25 29 38 20C35 31 43 33 42 24C42 17 52 16 55 7ZM67 27C81 41 63 49 58 48C56 39 68 38 67 27Z'),small:P('M12 50Q50 65 88 50C82 73 67 80 57 82L63 92H37L43 82C33 80 18 73 12 50Z')+P('M54 6C71 25 40 30 50 46C28 42 28 30 39 19C36 35 48 22 54 6ZM68 27C83 43 67 51 57 48C59 36 69 40 68 27Z')},
 {id:'02',name:'Spellguard',desc:'A shield carrying a living spell.',master:P('M50 7C64 19 77 21 89 20C88 58 76 77 50 93C24 77 12 58 11 20C23 21 36 19 50 7ZM50 20C40 27 31 30 23 31C26 57 34 68 50 80C66 68 74 57 77 31C69 30 60 27 50 20Z','fill-rule="evenodd"')+P('M52 30C66 45 43 45 51 60C58 58 62 53 62 48C71 68 43 77 36 61C30 47 49 44 52 30Z'),small:P('M50 6C66 19 78 20 90 19C89 58 75 80 50 94C25 80 11 58 10 19C22 20 34 19 50 6ZM50 23Q36 31 24 32Q27 62 50 80Q73 62 76 32Q64 31 50 23Z','fill-rule="evenodd"')+P('M52 31C67 47 40 51 58 61C74 57 57 79 40 67C27 55 50 42 52 31Z')},
 {id:'03',name:'Card current',desc:'A single deck moving through a trial.',master:P('M66 8C33-2 5 26 9 52C14 30 36 20 54 23C49 15 56 10 66 8ZM34 92C67 102 95 74 91 48C86 70 64 80 46 77C51 85 44 90 34 92Z')+P('M39 24H65Q70 24 69 30L59 71Q58 77 52 77H27Q22 77 23 71L33 30Q34 24 39 24ZM42 34 34 67H50L58 34Z','fill-rule="evenodd"'),small:P('M67 7C34-3 4 25 8 55C15 33 36 23 53 25C49 15 57 10 67 7ZM33 93C66 103 96 75 92 45C85 67 64 77 47 75C51 85 43 90 33 93Z')+P('M36 24H70L57 77H23ZM43 36 36 65H49L56 36Z','fill-rule="evenodd"')},
 {id:'04',name:'Trial key',desc:'A passage earned through play.',master:P('M33 8C14 8 5 23 10 39C13 50 23 57 35 55L66 86Q69 89 72 85L88 69 78 59 70 67 61 58 69 50 58 39 48 49 43 44C59 28 50 8 33 8ZM31 19A12 12 0 1 1 31 43A12 12 0 1 1 31 19Z','fill-rule="evenodd"')+P('M25 28Q31 20 37 28L31 35Z'),small:P('M31 7C10 7 4 24 9 39C13 51 23 57 36 54L67 88 88 68 77 57 69 65 60 56 68 48 56 36 47 45 43 42C58 23 48 7 31 7ZM31 20A11 11 0 1 1 31 42A11 11 0 1 1 31 20Z','fill-rule="evenodd"')},
 {id:'05',name:'Crowned deck',desc:'Three contenders and one winning deck.',master:P('M11 33Q18 27 24 27L35 61 22 65ZM38 15Q50 10 62 15L59 58H41ZM76 27Q82 27 89 33L78 65 65 61Z')+P('M15 64Q50 79 85 64L76 82Q50 94 24 82ZM36 80Q50 85 64 80L66 86Q50 92 34 86Z','fill-rule="evenodd"'),small:P('M9 31 24 26 37 62 21 66ZM37 15Q50 9 63 15L60 60H40ZM76 26 91 31 79 66 63 62Z')+P('M14 66Q50 82 86 66L76 86Q50 96 24 86Z')}
];
const colors={white:'#eddba7',blue:'#aac7d9',black:'#beaccb',red:'#e6ab97',green:'#b4c49d',colorless:'#ccd3d5'};
const mana={
 white:circle(50,50,17)+Array.from({length:8},(_,i)=>P('M44 28C37 20 42 10 53 8C45 19 59 18 55 28Z',`transform="rotate(${i*45} 50 50)"`)).join(''),
 blue:P('M50 8C44 23 20 42 20 61A30 30 0 0 0 80 61C80 42 56 23 50 8ZM35 54C26 67 35 79 47 78C36 72 43 65 35 54Z','fill-rule="evenodd"'),
 black:P('M50 12C26 12 13 26 13 47C13 62 23 68 33 71V85H44V76H56V85H67V71C77 68 87 62 87 47C87 26 74 12 50 12ZM25 42C40 36 48 48 42 57C26 63 20 53 25 42ZM75 42C60 36 52 48 58 57C74 63 80 53 75 42ZM50 58 43 69H57Z','fill-rule="evenodd"'),
 red:P('M58 6C76 33 37 42 50 60C66 55 66 39 66 36C95 66 78 90 49 90C13 90 6 61 29 36C24 57 38 56 37 44C35 27 52 23 58 6ZM47 67C33 82 57 88 62 73C51 80 58 66 56 59Z','fill-rule="evenodd"'),
 green:P('M42 77V64H27C5 64 3 39 21 33C18 17 36 8 48 19C62 3 84 18 80 33C100 44 89 64 73 64H58V77Q59 84 70 88H30Q41 84 42 77Z')+P('M46 62 30 47 35 43 46 53V34H54V53L67 40 72 45 54 63V76H46Z',`fill="${colors.green}"`),
 colorless:P('M50 7C61 27 73 39 93 50C73 61 61 73 50 93C39 73 27 61 7 50C27 39 39 27 50 7ZM50 28Q41 41 28 50Q41 59 50 72Q59 59 72 50Q59 41 50 28Z','fill-rule="evenodd"')
};
const micro={...mana,
 blue:P('M50 8C44 23 20 42 20 61A30 30 0 0 0 80 61C80 42 56 23 50 8Z'),
 green:P('M42 90V65H27C5 65 3 39 21 33C18 17 36 8 48 19C62 3 84 18 80 33C100 44 89 65 73 65H58V90Z'),
 red:P('M58 6C76 33 37 42 50 60C66 55 66 39 66 36C95 66 78 90 49 90C13 90 6 61 29 36C24 57 38 56 37 44C35 27 52 23 58 6Z')};
const names=['logo',...Object.keys(mana)];
async function run(){
 for(const set of sets){
  const dir=path.join(out,set.id);fs.mkdirSync(dir,{recursive:true});
  for(const variant of ['master','small'])for(const name of names){
   const body=name==='logo'?set[variant]:`<circle cx="50" cy="50" r="50" fill="${colors[name]}"/><g transform="translate(7 7) scale(.86)">${(variant==='master'?mana:micro)[name]}</g>`;
   const source=wrap(body);fs.writeFileSync(path.join(dir,`${name}-${variant}.svg`),source);
  }
  for(const size of [16,24,32,256,1024])for(const name of names){
   await sharp(path.join(dir,`${name}-${size<=24?'small':'master'}.svg`)).resize(size,size).png().toFile(path.join(dir,`${name}-${size}.png`));
  }
 }
 const text=[],layers=[];
 const label=(x,y,t,size=18,fill=ink,weight=400)=>text.push(`<text x="${x}" y="${y}" fill="${fill}" font-size="${size}" font-weight="${weight}">${t}</text>`);
 label(44,55,'GAUNTLET / New full-resolution designs',34,ink,700);label(44,89,'Scalable SVG masters · individual 1024 px exports included',19,'#687776');
 for(let i=0;i<5;i++){
  const s=sets[i],y=120+i*250;
  text.push(`<rect x="24" y="${y}" width="1512" height="234" rx="15" fill="${paper}" stroke="#dbddd6"/>`);
  label(45,y+40,`${s.id}  ${s.name}`,22,ink,700);label(45,y+70,s.desc,15,'#687776');
  layers.push({input:await sharp(path.join(out,s.id,'logo-1024.png')).resize(180,180).toBuffer(),left:300,top:y+26});
  for(let j=1;j<7;j++){
   const x=550+(j-1)*157;
   layers.push({input:await sharp(path.join(out,s.id,`${names[j]}-1024.png`)).resize(112,112).toBuffer(),left:x,top:y+40});
   label(x+22,y+185,names[j][0].toUpperCase()+names[j].slice(1),15,'#687776');
  }
 }
 const base=`<svg xmlns="http://www.w3.org/2000/svg" width="1560" height="1390"><rect width="1560" height="1390" fill="#eeeee6"/><g font-family="Arial,sans-serif">${text.join('')}</g></svg>`;
 await sharp(Buffer.from(base)).composite(layers).png().toFile(path.join(out,'full-resolution.png'));
 const smallText=[],smallLayers=[];
 const st=(x,y,t,size=15)=>smallText.push(`<text x="${x}" y="${y}" font-size="${size}" fill="${ink}">${t}</text>`);
 st(28,38,'The same five designs / actual low-resolution PNGs',26);st(28,65,'16 and 24 px use simplified artwork. View the HTML at 100% zoom for exact display size.');
 for(let i=0;i<5;i++){
  const s=sets[i],y=90+i*143;st(28,y+24,`${s.id}  ${s.name}`,20);
  for(let k=0;k<3;k++){
   const size=[16,24,32][k],x=30+k*335;st(x,y+74,`${size} px`,12);
   for(let j=0;j<7;j++)smallLayers.push({input:path.join(out,s.id,`${names[j]}-${size}.png`),left:x+45+j*(size+4),top:y+55});
  }
 }
 await sharp(Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="1060" height="815"><rect width="1060" height="815" fill="${paper}"/><g font-family="Arial">${smallText.join('')}</g></svg>`)).composite(smallLayers).png().toFile(path.join(out,'low-resolution.png'));
 const sections=sets.map(s=>`<section><h2>${s.id} ${s.name}</h2><p>${s.desc}</p><h3>Full resolution · SVG</h3><div class="masters">${names.map(n=>`<div><img class="${n}" src="${s.id}/${n}-master.svg" alt="${n}"><span>${n}</span></div>`).join('')}</div><h3>Actual low-resolution PNGs</h3><div class="proof">${[16,24,32].map(sz=>`<div><span>${sz} px</span>${names.map(n=>`<a href="${s.id}/${n}-${sz}.png"><img width="${sz}" height="${sz}" src="${s.id}/${n}-${sz}.png" alt="${n}, ${sz} pixels"></a>`).join('')}</div>`).join('')}</div><a href="${s.id}/logo-1024.png">1024 px logo</a> · <a href="${s.id}/logo-master.svg">Master SVG</a></section>`).join('');
 fs.writeFileSync(path.join(out,'index.html'),`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Gauntlet — full and low resolution</title><style>body{max-width:1400px;padding:25px;margin:auto;font:16px system-ui;background:#eeeee6;color:${ink}}section{padding:26px;background:${paper};margin:24px 0;border:1px solid #dbddd6;border-radius:16px}h2{margin:0}p{color:#687776}h3{font-size:12px;text-transform:uppercase;letter-spacing:2px;margin-top:28px;color:#687776}.masters{display:flex;justify-content:space-between;gap:20px;flex-wrap:wrap;align-items:center}.masters>div{text-align:center}.masters img{width:100px;height:100px}.masters .logo{width:220px;height:220px}.masters span{display:block}.proof{display:flex;flex-wrap:wrap;gap:30px;margin:24px 0}.proof>div{display:flex;gap:4px;align-items:center}.proof span{font-size:12px;margin-right:8px}.proof img{display:block}a{color:inherit} @media(max-width:700px){.masters img{width:64px;height:64px}.masters .logo{width:180px;height:180px}}</style><h1>Five new directions, large and small</h1><p>Large masters and actual 16, 24 and 32 px versions of every icon. View at 100% browser zoom.</p>${sections}</html>`);
 console.log(out);
}
run();
