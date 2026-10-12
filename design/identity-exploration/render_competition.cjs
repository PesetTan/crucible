const fs=require('fs');
const path=require('path');
const sharp=require('/Users/pesettan/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const out=path.resolve(__dirname,'../../src/MtgSimulator/wwwroot/assets/brand/concepts/competition-sets');
const sets=JSON.parse(fs.readFileSync(path.join(out,'manifest.json')));
const names=['logo','white','blue','black','red','green','colorless'];
(async()=>{
 for(const s of sets) for(const n of names) for(const size of [16,24,32,64]) {
  await sharp(path.join(out,s.number,`${n}-${size<=24?'small':'master'}.svg`)).resize(size,size).png().toFile(path.join(out,s.number,`${n}-${size}.png`));
 }
 const layers=[];const text=[];
 const label=(x,y,t,size=14,fill='#26343a',weight=400)=>text.push(`<text x="${x}" y="${y}" font-size="${size}" fill="${fill}" font-weight="${weight}">${t}</text>`);
 label(30,42,'GAUNTLET / Five compact directions',28,undefined,700);
 label(30,71,'Original vectors + actual 16, 24 and 32 px PNG exports. Tiny versions simplify fine details.',14,'#667479');
 for(let i=0;i<sets.length;i++){
  const s=sets[i],y=95+i*205;
  text.push(`<rect x="20" y="${y}" width="1160" height="192" rx="12" fill="#fbf8f1" stroke="#dddcd3"/>`);
  label(38,y+28,`${s.number}  ${s.name}`,19,undefined,700);
  label(38,y+51,s.description,12,'#667479');
  for(let j=0;j<names.length;j++){
   const left=305+j*120;
   layers.push({input:path.join(out,s.number,`${names[j]}-64.png`),left,top:y+18});
   label(left,y+101,names[j]==='logo'?'Logo':names[j][0].toUpperCase()+names[j].slice(1),11,'#667479');
  }
  for(let k=0;k<3;k++){
   const size=[16,24,32][k],x=38+k*330,top=y+140;
   label(x,top+15,`${size} px`,12,'#667479');
   for(let j=0;j<names.length;j++)layers.push({input:path.join(out,s.number,`${names[j]}-${size}.png`),left:x+48+j*(size+6),top});
  }
 }
 const base=`<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="1140"><rect width="1200" height="1140" fill="#f0ede5"/><g font-family="Arial,sans-serif">${text.join('')}</g></svg>`;
 await sharp(Buffer.from(base)).composite(layers).png().toFile(path.join(out,'comparison.png'));
 // A separate enlarged view exposes the real pixel grid of each 16 px logo.
 const zoom=[];const captions=[];
 for(let i=0;i<sets.length;i++){
  const input=await sharp(path.join(out,sets[i].number,'logo-16.png')).resize(128,128,{kernel:'nearest'}).toBuffer();
  zoom.push({input,left:36+i*190,top:55});
  captions.push(`<text x="${36+i*190}" y="215" font-size="14">${sets[i].number} ${sets[i].name}</text>`);
 }
 await sharp(Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="980" height="240"><rect width="980" height="240" fill="#fbf8f1"/><g font-family="Arial" fill="#26343a"><text x="30" y="28" font-size="16">Actual 16 px logos enlarged 8× — no smoothing</text>${captions.join('')}</g></svg>`)).composite(zoom).png().toFile(path.join(out,'pixel-check.png'));
 const cards=sets.map(s=>`<section><h2>${s.number} ${s.name}</h2><p>${s.description}</p><div class="marks">${names.map(n=>`<div><img width="64" height="64" src="${s.number}/${n}-64.png" alt="${n}"><small>${n}</small></div>`).join('')}</div><div class="proof">${[16,24,32].map(size=>`<div><span>${size} px</span>${names.map(n=>`<img width="${size}" height="${size}" src="${s.number}/${n}-${size}.png" alt="${n}">`).join('')}</div>`).join('')}</div></section>`).join('');
 fs.writeFileSync(path.join(out,'index.html'),`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Gauntlet — compact logo options</title><style>body{margin:30px auto;padding:0 20px;max-width:1100px;background:#f0ede5;color:#26343a;font:15px system-ui}h1{font-size:28px}section{padding:24px;margin:18px 0;background:#fbf8f1;border:1px solid #dddcd3;border-radius:12px}h2{margin:0}p{color:#667479}.marks{display:flex;justify-content:space-between;gap:14px;flex-wrap:wrap}.marks>div{text-align:center}.marks small{display:block;margin-top:8px}.proof{display:flex;flex-wrap:wrap;gap:30px;border-top:1px solid #dddcd3;margin-top:22px;padding-top:18px}.proof>div{display:flex;align-items:center;gap:5px}.proof span{margin-right:8px;font-size:12px}img{object-fit:contain}.pixels{max-width:100%;height:auto}</style><h1>Five compact directions</h1><p>Actual PNG sizes below each set. The 16 and 24 px versions have simpler shapes and wider gaps. View at 100% browser zoom to compare display sizes.</p>${cards}<h2>16 px under a magnifying glass</h2><img class="pixels" src="pixel-check.png" alt="Actual 16 pixel logos enlarged without smoothing"></html>`);
 console.log(out);
})();
