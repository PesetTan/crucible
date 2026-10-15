import {cp,mkdir,readFile,writeFile,rm} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';import path from 'node:path';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const ui=path.join(root,'src/Crucible.Ui');
await mkdir(path.join(ui,'public'),{recursive:true});
// Design archives may be cloud placeholders. Ship only assets used by the product.
await rm(path.join(ui,'public/assets'),{recursive:true,force:true});
for(const dir of ['assets/brand/crucible','assets/workspace','assets/mana','fonts']){
 try {await cp(path.join(root,'src/DeckTester/wwwroot',dir),path.join(ui,'public',dir),{recursive:true})}
 catch(error){if(error.code!=='ENOENT')throw error}
}
const shared=await Promise.all(['src/DeckTester/wwwroot/app.css','src/DeckTester/Components/AiNote.razor.css','src/DeckTester/Components/Pages/Runs.razor.css','src/DeckTester/Components/Pages/Pilots.razor.css','src/DeckTester/Components/PilotMatchRecord.razor.css','src/DeckTester/wwwroot/theme-polish.css'].map(file=>readFile(path.join(root,file),'utf8')));
await writeFile(path.join(ui,'src/legacy.css'),shared.join('\n'));
