import test from 'node:test';import assert from 'node:assert/strict';import {parseExact} from '../src/lib/api.ts';import {points,performance,pilotRates} from '../src/lib/data.ts';import type {Slot,HistoryRun,Run,Matchup,Sample} from '../src/lib/types.ts';
test('signed 64-bit seeds and sequence identities survive parsing exactly',()=>{
 const data=parseExact<{seed:string;min:string;small:number;text:string;decimal:number}>('{"seed":9223372036854775807,"min":-9223372036854775808,"small":42,"text":"9223372036854775807","decimal":0.125}');
 assert.equal(data.seed,'9223372036854775807');assert.equal(data.min,'-9223372036854775808');assert.equal(data.small,42);assert.equal(data.text,'9223372036854775807');assert.equal(data.decimal,.125);
 assert.deepEqual(parseExact('{"rate":0.1234567890123456789,"scientific":1e17}'),{rate:0.1234567890123456789,scientific:1e17});
 assert.equal(parseExact<{id:number}>('{"id":9007199254740991}').id,Number.MAX_SAFE_INTEGER);
 assert.deepEqual(parseExact('{"name":"a\\\" 9999999999999999999, b","seed":-9007199254740992}'),{name:'a" 9999999999999999999, b',seed:'-9007199254740992'});
});
const sample=(turn:number,player:string,life:number,handSpells?:number)=>({actualTurn:turn,playerTurn:turn,player,life,handSpells} as Sample);
const slot=(winner:string|null,draw:boolean,actualTurns:number,samples:Sample[])=>({status:'completed',deckA:'A',deckB:'B',result:{winner,draw,actualTurns,samples}} as Slot);
test('charts use actual-turn grouping and exclude absent hand observations',()=>{
 const slots=[slot('A',false,4,[sample(1,'A',20,3),sample(2,'A',18),sample(2,'B',15,5)]),slot('B',false,6,[sample(1,'A',16,1)]),{...slot('A',false,6,[sample(1,'A',1)]),status:'failed'}];
 assert.deepEqual(points(slots,'A','life',true),[{turn:1,value:18,count:3}]);assert.deepEqual(points(slots,'A','handSpells',true),[{turn:1,value:2,count:2}]);assert.deepEqual(points(slots,'A','life',false),[{turn:1,value:18,count:2},{turn:2,value:18,count:1}]);
});
test('game-length rates include draws as nonwins and exclude unfinished games',()=>{
 const slots=[slot('A',false,4,[]),slot(null,true,4,[]),slot('B',false,5,[]),{...slot('A',false,4,[]),status:'running'}];assert.deepEqual(points(slots,'A','winRate',true),[{turn:2,value:50,count:2},{turn:3,value:0,count:1}]);
});
const run=(id:string,profiles:Record<string,string>|undefined,createdAt='2026-10-15T00:00:00Z')=>({id,createdAt,config:{deckIds:['A','B','C'],pilotProfiles:profiles}} as Run);
const matchup=(a:string,b:string,wa:number,wb:number,d=0)=>({deckA:a,deckB:b,valid:wa+wb+d,winsA:wa,winsB:wb,draws:d,winRateA:wa/(wa+wb+d),winRateB:wb/(wa+wb+d)} as Matchup);
const session=(r:Run,matchups:Matchup[])=>({progress:{run:r},matchups} as HistoryRun);
test('history gives each observed opponent equal weight rather than pooling game counts',()=>{
 const s=session(run('r',{}),[matchup('A','B',1,1),matchup('A','C',9,1)]);const a=performance(s).find(p=>p.id==='A')!;assert.equal(a.rate,.7);assert.equal(a.wins,10);assert.equal(a.losses,2);assert.equal(a.opponents,2);
});
test('pilot evidence uses recorded assignments, counts both sides, and separates sessions',()=>{
 const sessions=[session(run('old',{A:'same',B:'same'},'2026-10-14T00:00:00Z'),[matchup('A','B',1,0,1)]),session(run('new',{A:'same',B:'other'}),[matchup('A','B',0,1)]),session(run('unknown',undefined),[matchup('A','B',100,0)])];const rates=pilotRates(sessions);assert.equal(rates.same.length,2);assert.equal(rates.same[0].run.id,'new');assert.deepEqual([rates.same[1].wins,rates.same[1].losses,rates.same[1].draws],[1,1,2]);assert.equal(rates.other[0].wins,1);
});

test('legacy browser choices migrate without changing seed or pilot identity',async()=>{
 const {loadSaved}=await import('../src/lib/api.ts');const values=new Map<string,string>();
 Object.defineProperty(globalThis,'localStorage',{configurable:true,value:{getItem:(key:string)=>values.get(key)??null,setItem:(key:string,value:string)=>values.set(key,value)}});
 values.set('gauntlet:selections:v1:home','{"DeckIds":["exact-a","exact-b"],"ExplicitDeckSelection":true,"Games":20,"Workers":2,"Timeout":180,"TurnLimit":150,"Seed":9223372036854775807,"PilotMode":"recorded-policy","PerformanceMode":true}');
 const state=loadSaved('home',{selected:[] as string[],games:200,seed:'42',pilot:''});assert.equal(state.seed,'9223372036854775807');assert.equal(state.pilot,'recorded-policy');assert.deepEqual(state.selected,['exact-a','exact-b']);assert.equal(state.games,20);
 values.set('gauntlet:selections:v1:workshop:::::','{"Name":"Draft","Cards":[{"ArenaId":12,"Name":"Card","Quantity":4,"Attributes":{"CaseSensitiveKey":true}}],"Sideboard":[],"Seed":42}');
 const draft=loadSaved('workshop:::::',{name:'',cards:[] as {arenaId:number;attributes:Record<string,unknown>}[]});assert.equal(draft.name,'Draft');assert.equal(draft.cards[0].arenaId,12);assert.deepEqual(draft.cards[0].attributes,{CaseSensitiveKey:true});
 values.set('crucible:svelte:home','not JSON');assert.equal(loadSaved('home',state),state);
});
test('read cache revalidates unchanged data and releases older and large snapshots',async()=>{
 const {api,clearCache}=await import('../src/lib/api.ts');const original=globalThis.fetch;const requests:{path:string;tag:string|undefined}[]=[];clearCache();
 globalThis.fetch=async(input,init)=>{const path=String(input);const headers=init?.headers as Record<string,string>;requests.push({path,tag:headers['If-None-Match']});if(headers['If-None-Match'])return new Response(null,{status:304});return new Response(JSON.stringify({value:path,body:path.endsWith('/large')?'x'.repeat(4*1024*1024):''}),{headers:{ETag:'"fixture"'}})};
 try{const first=await api('/api/v1/cache/0');assert.equal(await api('/api/v1/cache/0'),first);for(let n=1;n<8;n++)await api('/api/v1/cache/'+n);await api('/api/v1/cache/0');assert.equal(requests.at(-1)?.tag,undefined);await api('/api/v1/cache/large');await api('/api/v1/cache/large');assert.equal(requests.at(-1)?.tag,undefined);clearCache();await api('/api/v1/cache/7');assert.equal(requests.at(-1)?.tag,undefined)}finally{globalThis.fetch=original;clearCache()}
});
test('mutations refresh a stale UI token once and preserve the idempotency key',async()=>{
 const {api}=await import('../src/lib/api.ts');const original=globalThis.fetch;let sessions=0;const keys:string[]=[],tokens:string[]=[];
 globalThis.fetch=async(input,init)=>{if(String(input).endsWith('/session'))return Response.json({instanceToken:'ui-'+(++sessions)});const h=init?.headers as Record<string,string>;keys.push(h['Idempotency-Key']);tokens.push(h['X-Instance-Token']);return tokens.length===1?new Response('',{status:403}):Response.json({id:'saved-exact-id'})};
 try{assert.deepEqual(await api('/api/v1/runs',{method:'POST',body:{rootSeed:'9223372036854775807'},key:'one-action'}),{id:'saved-exact-id'});assert.deepEqual(tokens,['ui-1','ui-2']);assert.deepEqual(keys,['one-action','one-action'])}finally{globalThis.fetch=original}
});
