const fs=require('fs'),path=require('path');
const sharp=require('/Users/pesettan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const out=path.resolve(__dirname,'../../src/DeckTester/wwwroot/assets/brand/concepts/workspace-selected');
fs.mkdirSync(out,{recursive:true});
const ink='#29373a',paper='#faf8f2';
const icons=[
 {name:'simulator',label:'Simulator',shape:'<path fill-rule="evenodd" d="M23 5H41Q43 5 43 8V10Q43 12 40 12V28L54 49Q61 59 48 59H16Q3 59 10 49L24 28V12Q21 12 21 10V8Q21 5 23 5ZM30 12V30L22 42Q31 36 42 42L34 30V12Z"/>'},
 {name:'decks',label:'Deck Library',shape:'<path fill-rule="evenodd" d="M24 7H40Q45 7 45 12V51Q45 56 40 56H24Q19 56 19 51V12Q19 7 24 7ZM26 14V49H38V14Z"/><path d="M13 17 16 18V50Q16 55 19 58L14 59Q11 60 10 56L4 25Q3 21 7 19Z M51 17 48 18V50Q48 55 45 58L50 59Q53 60 54 56L60 25Q61 21 57 19Z"/>'},
 {name:'history',label:'Run History',shape:'<path d="M12 19A24 24 0 1 1 11 45" fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round"/><path d="M5 10V26H21Z"/><path d="M33 19V33L43 39" fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>'}
];
const svg=i=>`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" role="img" aria-label="${i.label}" style="color:${ink}"><title>${i.label} — Crucible workspace</title><g fill="currentColor">${i.shape}</g></svg>`;
(async()=>{
 for(const i of icons){fs.writeFileSync(path.join(out,i.name+'.svg'),svg(i));for(const size of [16,20,24,192,1024])await sharp(Buffer.from(svg(i))).resize(size,size).png().toFile(path.join(out,`${i.name}-${size}.png`));}
 const text=[],layers=[];
 const t=(x,y,s,size=16,fill=ink,weight=400)=>text.push(`<text x="${x}" y="${y}" fill="${fill}" font-size="${size}" font-weight="${weight}">${s}</text>`);
 t(35,47,'GAUNTLET / Your selected workspace set',28,ink,700);
 t(35,77,'Redrawn together with consistent weight, soft corners, and open negative space.',15,'#756753');
 for(let j=0;j<3;j++){
  const x=70+j*305;
  layers.push({input:path.join(out,`${icons[j].name}-192.png`),left:x+28,top:120});
  t(x+57,350,icons[j].label,22,ink,600);
 }
 text.push('<line x1="35" y1="383" x2="965" y2="383" stroke="#ded8cb"/>');
 t(35,422,'ACTUAL PIXEL SIZES',12,'#756753',700);
 for(let row=0;row<3;row++){
  const size=[16,20,24][row],y=453+row*50;
  t(40,y+size-2,`${size} px`,14,'#756753');
  for(let j=0;j<3;j++)layers.push({input:path.join(out,`${icons[j].name}-${size}.png`),left:110+j*90,top:y});
 }
 t(440,422,'IN THE SIDEBAR · 20 PX',12,'#756753',700);
 text.push('<rect x="425" y="442" width="530" height="42" rx="8" fill="#e9e0ce"/>');
 for(let j=0;j<3;j++){
  const y=452+j*50;
  let source=svg(icons[j]).replace(ink,j===0?'#4a351e':'#625645');
  layers.push({input:await sharp(Buffer.from(source)).resize(20,20).png().toBuffer(),left:446,top:y});
  t(481,y+17,icons[j].label,17,j===0?'#4a351e':'#625645');
 }
 const sheet=`<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="630"><rect width="1000" height="630" fill="${paper}"/><g font-family="Arial,sans-serif">${text.join('')}</g></svg>`;
 await sharp(Buffer.from(sheet)).composite(layers).png().toFile(path.join(out,'comparison.png'));
 fs.writeFileSync(path.join(out,'index.html'),`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Selected workspace set</title><style>body{max-width:1000px;margin:32px auto;padding:0 24px;font:16px system-ui;background:${paper};color:${ink}}h1{font-size:28px}.masters{display:flex;justify-content:space-around;flex-wrap:wrap;text-align:center}.masters img{width:192px;height:192px}.small{display:flex;align-items:center;gap:28px;margin:24px 0}.small span{width:50px}hr{border:0;border-top:1px solid #ded8cb;margin:32px 0}a{color:inherit}</style><h1>Your selected workspace set</h1><p>Matching shapes and weight, shown large and at actual navigation sizes.</p><div class="masters">${icons.map(i=>`<div><a href="${i.name}.svg"><img src="${i.name}.svg" alt="${i.label}"></a><h2>${i.label}</h2></div>`).join('')}</div><hr>${[16,20,24].map(s=>`<div class="small"><span>${s} px</span>${icons.map(i=>`<img src="${i.name}-${s}.png" width="${s}" height="${s}" alt="${i.label}">`).join('')}</div>`).join('')}</html>`);
 console.log(out);
})();
