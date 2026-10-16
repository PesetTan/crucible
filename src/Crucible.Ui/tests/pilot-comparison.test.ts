import test from "node:test";
import assert from "node:assert/strict";
import { defaultPilotResults } from "../src/lib/pilot-comparison.ts";
import type { HistoryRun } from "../src/lib/types.ts";
const session = (profiles: Record<string,string>, winsA=3, winsB=1, valid=5) => ({progress:{run:{id:"r",createdAt:"2026-10-15",config:{pilotProfiles:profiles}}},matchups:[{deckA:"a",deckB:"b",winsA,winsB,draws:1,valid}]}) as HistoryRun;
test("default comparison handles both seats and retains draws",()=>{
  const a=defaultPilotResults([session({a:"custom",b:"forge-default"})])[0];
  assert.deepEqual([a.wins,a.losses,a.draws,a.games],[3,1,1,5]);
  const b=defaultPilotResults([session({b:"custom",a:"forge-default-v1"})])[0];
  assert.deepEqual([b.wins,b.losses],[1,3]);
});
test("excludes unknown, custom opponents, default mirrors and invalid outcomes",()=>{
  assert.equal(defaultPilotResults([session({a:"custom"}),session({a:"custom",b:"other"}),session({a:"forge-default-v1",b:"forge-default-v1"}),session({a:"custom",b:"forge-default-v1"},3,1,6)]).length,0);
});
test("sessions remain separate even when cached results repeat",()=>{
  const s=session({a:"custom",b:"forge-default-v1"});
  assert.equal(defaultPilotResults([s,s]).length,2);
});
