const fs=require('fs'),path=require('path');
const sharp=require('/Users/pesettan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const out=path.resolve(__dirname,'../../src/DeckTester/wwwroot/assets/brand/concepts/workspace-options');
const ink='#29373a',paper='#faf8f2';
const p=(d,a='')=>`<path d="${d}" ${a}/>`;
const circle=(x,y,r)=>`<circle cx="${x}" cy="${y}" r="${r}"/>`;
const ring=(cx,cy,r,t)=>`<circle cx="${cx}" cy="${cy}" r="${r-t/2}" fill="none" stroke="currentColor" stroke-width="${t}"/>`;
const clock=(r=24)=>ring(32,32,r,7)+p('M28 16h7v15h12v7H28Z');
const play=p('M25 18Q23 17 23 20V45Q23 48 26 46L45 35Q49 32 45 30Z');
const stack=p('M21 6H49Q54 6 54 11V43Q54 48 49 48H21Q16 48 16 43V11Q16 6 21 6ZM24 14V40H46V14Z','fill-rule="evenodd"')+p('M7 20H12V48Q12 53 17 53H42V59H16Q6 59 6 49V21Z');
const book=p('M7 12Q20 9 29 16V55Q19 47 7 50Q3 50 3 46V17Q3 13 7 12ZM57 12Q44 9 35 16V55Q45 47 57 50Q61 50 61 46V17Q61 13 57 12Z');
const rewind=p('M12 13V5L3 17 12 28V20C18 11 30 7 41 12C61 21 56 49 35 52C25 54 17 49 13 41L6 44C12 59 32 65 47 55C65 43 66 21 50 10C37 1 21 3 12 13Z')+p('M28 19H35V32L43 37 39 43 28 36Z');
const sets=[
 {id:'01',name:'The play desk',desc:'Cards, a deck stack, and a clock.',icons:[
 p('M10 11H30Q34 11 34 15V45Q34 49 30 49H10Q6 49 6 45V15Q6 11 10 11ZM13 18V42H27V18Z','fill-rule="evenodd"')+p('M39 16H55Q59 16 59 20V53Q59 57 55 57H34Q30 57 30 53V51H34Q38 51 38 45V20Q38 17 39 16ZM44 25V48H52V25Z','fill-rule="evenodd"'),stack,clock()]},
 {id:'02',name:'Run & read',desc:'A play button, open book, and replay clock.',icons:[ring(32,32,27,7)+play,book,rewind]},
 {id:'03',name:'The testing lab',desc:'A flask, card catalogue, and recorded results.',icons:[
 p('M22 5H42V12H39V28L54 48Q62 59 48 59H16Q2 59 10 48L25 28V12H22ZM31 13V30L24 40Q32 35 42 42L33 30V13Z','fill-rule="evenodd"'),
 p('M7 12H27L32 18H55Q59 18 59 23V51Q59 56 54 56H10Q5 56 5 51V17Q5 12 7 12ZM14 29V35H49V29ZM14 41V47H39V41Z','fill-rule="evenodd"')+p('M33 7H53Q57 7 57 11V14H34L27 7Z'),
 p('M17 7H29V14H19V53H46V14H38V7H48Q53 7 53 12V55Q53 60 48 60H16Q11 60 11 55V12Q11 7 17 7Z')+p('M25 3H40V17H25Z M23 29H41V35H23ZM23 41H41V47H23Z')]},
 {id:'04',name:'The round robin',desc:'A cycle of games, a card fan, and a timeline.',icons:[
 p('M48 16C38 4 17 7 13 24H4C8 2 36-4 52 9V1L62 18 43 22Z M16 48C26 60 47 57 51 40H60C56 62 28 68 12 55V63L2 46 21 42Z')+circle(32,32,8),
 p('M25 8H44Q49 8 49 13V51Q49 56 44 56H25Q20 56 20 51V13Q20 8 25 8ZM27 16V48H42V16Z','fill-rule="evenodd"')+p('M15 18V53Q15 58 21 60L14 62Q11 62 10 58L2 24Q1 20 5 19Z M53 17 61 22Q64 24 62 28L54 50V18Z'),
 p('M15 11H21V54H15Z M26 12H57V19H26ZM26 29H49V36H26ZM26 46H57V53H26Z')+circle(18,15,7)+circle(18,32,7)+circle(18,49,7)]},
 {id:'05',name:'The matchup',desc:'Opposing players, a card tray, and past runs.',icons:[
 circle(15,16,8)+circle(49,16,8)+p('M3 29Q15 21 27 29V38H3ZM37 29Q49 21 61 29V38H37ZM9 46H40V40L56 50 40 60V54H9Z'),
 p('M18 7H46Q51 7 51 12V34H43V15H25V34H17V12Q17 7 18 7Z M5 32H22L26 40H38L42 32H59V53Q59 58 54 58H10Q5 58 5 53Z'),
 p('M13 5H55Q60 5 60 10V42H54V13H13Z M5 16H48V22H5Z')+`<g transform="translate(-3 9) scale(.82)">${clock(25)}</g>`]}
];
const labels=['Simulator','Deck Library','Run History'];
const names=['simulator','decks','history'];
const svg=body=>`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" style="color:${ink}"><g fill="currentColor">${body}</g></svg>`;
(async()=>{
 const text=[],layers=[];
 const txt=(x,y,t,size=14,fill=ink,weight=400)=>text.push(`<text x="${x}" y="${y}" font-size="${size}" fill="${fill}" font-weight="${weight}">${t.replaceAll("&","&amp;")}</text>`);
 txt(34,43,'GAUNTLET / Workspace icon options',29,ink,700);
 txt(34,72,'Five matching trios · large artwork on the left, actual 20 px navigation icons on the right.',14,'#756753');
 for(let i=0;i<5;i++){
  const s=sets[i],y=100+i*164,dir=path.join(out,s.id);fs.mkdirSync(dir,{recursive:true});
  text.push(`<rect x="20" y="${y}" width="1100" height="148" rx="12" fill="${paper}" stroke="#ded8cb"/>`);
  txt(38,y+30,`${s.id}  ${s.name}`,18,ink,700);txt(38,y+53,s.desc,12,'#756753');
  for(let j=0;j<3;j++){
   const source=svg(s.icons[j]);fs.writeFileSync(path.join(dir,names[j]+'.svg'),source);
   for(const size of [20,64,256])await sharp(Buffer.from(source)).resize(size,size).png().toFile(path.join(dir,`${names[j]}-${size}.png`));
   layers.push({input:path.join(dir,`${names[j]}-64.png`),left:345+j*130,top:y+26});
   txt(345+j*130,y+115,labels[j],12,'#756753');
   if(j===0)text.push(`<rect x="786" y="${y+10}" width="307" height="38" rx="7" fill="#e9e0ce"/>`);
   layers.push({input:path.join(dir,`${names[j]}-20.png`),left:803,top:y+19+j*43});
   txt(837,y+35+j*43,labels[j],16,j===0?'#4a351e':'#625645');
  }
 }
 await sharp(Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="1140" height="940"><rect width="1140" height="940" fill="#f2eee4"/><g font-family="Arial,sans-serif">${text.join('')}</g></svg>`)).composite(layers).png().toFile(path.join(out,'comparison.png'));
 fs.writeFileSync(path.join(out,'index.html'),`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Workspace icon options</title><style>body{font:16px system-ui;background:#f2eee4;color:${ink};max-width:1100px;margin:32px auto;padding:0 18px}h1{font-size:28px}section{display:grid;grid-template-columns:250px 1fr 230px;align-items:center;gap:20px;background:${paper};border:1px solid #ded8cb;padding:24px;margin:16px 0;border-radius:12px}h2{font-size:18px}p{font-size:13px;color:#756753}.big{display:flex;justify-content:space-around;text-align:center}.big img{width:64px;height:64px}.big small{display:block;margin-top:12px}.nav>div{display:flex;align-items:center;gap:13px;padding:10px;border-radius:7px;margin:3px 0;color:#625645}.nav>div:first-child{background:#e9e0ce;color:#4a351e}.nav img{width:20px;height:20px}@media(max-width:800px){section{grid-template-columns:1fr}.big{gap:20px}}</style><h1>Five workspace icon sets</h1><p>Each set contains Simulator, Deck Library, and Run History. Navigation samples use actual 20 px PNGs.</p>${sets.map(s=>`<section><div><h2>${s.id} ${s.name}</h2><p>${s.desc}</p></div><div class="big">${names.map((n,i)=>`<div><img src="${s.id}/${n}.svg" alt="${labels[i]}"><small>${labels[i]}</small></div>`).join('')}</div><div class="nav">${names.map((n,i)=>`<div><img src="${s.id}/${n}-20.png" alt="">${labels[i]}</div>`).join('')}</div></section>`).join('')}</html>`);
 console.log(out);
})();
