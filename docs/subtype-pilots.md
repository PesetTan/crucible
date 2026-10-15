# Exploratory subtype pilots v001

The current opt-in mode `subtypes-v001+aggro-v001+control-v1+reanimator-v011` assigns a first-release profile to each of the 30 non-Reanimator library subtypes and unified v011 to Reanimator. Exact case-insensitive subtype matches take precedence over broad strategy. The original release used `subtypes-v001+aggro-v001+control-v1+reanimator-v9`; the comparison and publication records below describe that historical release, including its original Reanimator variants. Unknown subtypes use the existing exact broad Aggro/Control policy or Forge Default. Legacy modes keep their prior assignments. Historical deck manifests, attributes, sessions and engine bundles remain unchanged.

## Bounded behavior

Each profile has its own metadata priorities for reviewed permanent development. After Forge passes, during an empty-stack own main phase, the bridge checks at most 24 own-hand cards. It considers only annotated mana-only permanent spells with reviewed API shapes, engine timing and payment eligibility. It excludes targeted spells, cloned sources, subabilities, variable printed mana costs and a same-name legendary permanent already controlled. Highest priority wins; equal scores retain hand/ability order. The engine pays costs and resolves the spell and every subsequent action.

This supplements Forge's choices. It does not override a selected Forge action. Flash, triggered effects, activated abilities, targeting, aura carriers, craft, sacrifice, Fling finishes, graveyard setup, planeswalker loyalty decisions, mulligans and combat remain inherited Forge unless an existing parent hook applies. The policies are deliberately bounded first releases, not complete strategic players. Static development priorities can spend mana that could have been held for interaction; matched regressions must be reported, not dismissed.

Profiles for exact Control subtypes retain Control v1 sweeper-discard and graveyard-exile hooks; Burn, Creature Aggro and Allies retain Aggro v001's reviewed opponent-burn hook. Other hybrid subtypes inherit Forge Default. A profile's parent is explicitly recorded in the registry. The scheduled opposing manifest, concealed hand/library and future outcomes never enter the new development decision. The full inherited Forge information audit remains open.

## Frozen metadata and assignments

`src/DeckTester/SubtypePilots.cs` contains the assignment registry. `docs/subtype-pilots.seed.json` annotates 73 reviewed catalog cards through `subtypePilot.developOnPass`, independently of earlier namespaces. Printed text and pinned Forge scripts were inspected; the runtime branches on attributes and actual ability structure, not card names. Unknown keys are inert; cloned or unsupported shapes fall back. New sessions freeze complete annotations and their catalog hash. Baseline and candidate comparisons receive the same annotations, with old bridge/profile behavior in the baseline arm.

The new mode and profile IDs join the existing engine, adapter, attribute, seed, Performance Mode and policy compatibility checks. The subtype mode is an explicit choice in Advanced Options; the established Aggro/Control/Reanimator mode and existing selection preferences are retained. Library rows identify the available subtype pilot, and source details distinguish it from the default policy. This avoids automatic promotion of unproven development priorities. Stored run defaults are retained for historical deserialization.

## Validation

Focused engine checks exercise every profile's actual reviewed permanent, mana/timing legality, no-mana fallback, missing metadata, a chosen baseline action, wrong phase/player and paired concealed opponent hand/library variants. Additional positions protect a same-name legend and reject inappropriate damage/aura/sweeper shapes. All 66 exact saved manifests contain 60 loaded main-deck cards. The initial suite has 215 positions across 30 profiles; the full deck-specific elementary fixture and inherited information-boundary gates remain incomplete.

Existing Reanimator, Control and Aggro regressions and .NET profile/cache identity checks are retained. Evidence is under `data/reports/subtype-pilots-v001/`: registry, complete inventory, reviewed scripts/text, frozen exact inputs, historical evidence audits, candidate classes, requests, raw serial replays, class/replay hashes and terminal outcomes.

The frozen diagnostic comparison has two starts per subtype: 60 matched schedules, 120 serial executions. The representative lists are exact original library versions; the unchanged opponent is Mono-Red Burn with Aggro v001, except Burn is tested against unchanged Mono-White Auras with Forge Default. This checks coverage across subtypes against a limited opponent set. It cannot establish broad matchup performance or Arena forecasts. Failures and safeguards are typed failures, never losses.

Completed comparison and live publication evidence are recorded below.

## Profile registry

| Subtype | Recorded profile | Inherited policy | Reviewed development priority order |
|---|---|---|---|
| Auras | `auras-v001` | `forge-default` | Optimistic Scavenger, Veteran Survivor, Spellbook Vendor |
| Lifegain | `lifegain-v001` | `forge-default` | Hinterland Sanctifier, Ajani's Pridemate, Essence Channeler |
| Artifacts | `artifacts-v001` | `forge-default` | Simulacrum Synthesizer, The Enigma Jewel, Clay-Fired Bricks |
| Skeletons | `skeletons-v001` | `forge-default` | Forsaken Miner, Corpses of the Lost, Case of the Stashed Skeleton |
| Jace Control | `jace-control-v001` | `control-shared-v1` | Jace, Reality Sculptor, The Theorist, Jace Beleren |
| Sacrifice | `sacrifice-v001` | `forge-default` | Vengeful Bloodwitch, Infestation Sage, Greedy Freebooter |
| Dragons | `dragons-v001` | `forge-default` | Sarkhan, Dragon Ascendant, Momo, Friendly Flier, Magmatic Hellkite |
| Dwarves | `dwarves-v001` | `forge-default` | Dwarven Mauler, Dwalin, Weaponmaster, Lavaspur Boots |
| Planeswalkers | `planeswalkers-v001` | `control-shared-v1` | The Theorist, Jace Beleren, Jace, Reality Sculptor, Liliana, Dreadhorde General |
| Discard | `discard-v001` | `forge-default` | Marauding Mako, Inti, Seneschal of the Sun, Hardened Academic |
| Landfall | `landfall-v001` | `forge-default` | Llanowar Elves, Mossborn Hydra, Pawpatch Recruit |
| Rabbits | `rabbits-v001` | `forge-default` | Valley Mightcaller, Burrowguard Mentor, Valley Questcaller |
| Omniscience | `omniscience-v001` | `forge-default` | Kona, Rescue Beastie, Omniscience, Formidable Speaker |
| Flash | `flash-v001` | `forge-default` | Voice of Victory, High Noon |
| Fling | `fling-v001` | `forge-default` | Drake Hatcher, Emberheart Challenger, Callous Sell-Sword |
| Angels | `angels-v001` | `forge-default` | Giada, Font of Hope, Youthful Valkyrie, Resplendent Angel |
| Dinosaur Reanimator | `dinosaur-reanimator-v001` | `forge-default` | Melded Moxite, Palani's Hatcher, Vaultborn Tyrant |
| Burn | `burn-v001` | `aggro-v001` | Hired Claw, Emberheart Challenger |
| Midrange | `midrange-v001` | `forge-default` | Spyglass Siren, Dream Beavers, Ouroboroid |
| Tokens | `tokens-v001` | `forge-default` | Stadium Headliner, Voice of Victory, Warleader's Call |
| Creature Aggro | `creature-aggro-v001` | `aggro-v001` | Hired Claw, Iridescent Vinelasher, Gev, Scaled Scorch |
| Allies | `allies-v001` | `aggro-v001` | Earth King's Lieutenant, Earthen Ally, Great Divide Guide |
| Spell Control | `spell-control-v001` | `control-shared-v1` | Tablet of Discovery, Pyromancer's Goggles, Resonating Lute |
| Spellementals | `spellementals-v001` | `forge-default` | Eddymurk Crab, Hearth Elemental |
| Jace Exile | `jace-exile-v001` | `forge-default` | Jace, Reality Sculptor, The Theorist, Jace Beleren |
| Lessons | `lessons-v001` | `control-shared-v1` | Tablet of Discovery, Thor, God of Thunder |
| Superfriends | `superfriends-v001` | `control-shared-v1` | Chandra, Torch of Defiance, Elspeth, Storm Slayer, The Theorist, Jace Beleren |
| Demons | `demons-v001` | `forge-default` | Deep-Cavern Bat, Unstoppable Slasher, Bloodletter of Aclazotz |
| Elementals | `elementals-v001` | `forge-default` | Eddymurk Crab, Hydro-Man, Fluid Felon, Namor the Sub-Mariner |
| Big Creatures | `big-creatures-v001` | `forge-default` | Llanowar Elves, Hulking Raptor, Studious First-Year |

A coverage pass found that 5-Color Spell Control lacks Tablet/Goggles. Its reviewed Resonating Lute development annotation was added separately and fixture-checked. It is absent from the selected representative comparison lists, so both frozen arms and their attributes stay unchanged. This variant has no measured matched outcome claim.

## Completed matched comparison

All **120 serial executions** completed: 60 matched schedules, one start per side for each of 30 exact subtype representatives. Raw replays, sequences, 57,488 state checkpoints, terminal outcomes and identical paired requests were independently audited. The candidate made **102** audited development overrides, each from its own captured hand and followed by the actual engine hand-to-stack event. The 27 original saved run payloads remain hash-identical.

The paired cohort has **1 gained win, 2 regressions and 57 unchanged winners**. Its baseline/candidate win counts are 29/28 across 60 heterogeneous diagnostic samples. These are paired diagnostic counts, not a pooled matchup estimate or an Arena forecast. No general performance gain is established. The new subtype mode is available explicitly; the established Aggro/Control/Reanimator mode remains the default.

| Subtype | Exact representative | Baseline wins / 2 | Candidate wins / 2 | Gains / regressions | Development overrides |
|---|---|---:|---:|---:|---:|
| Auras | Mono-White Auras | 1 | 0 | 0 / 1 | 4 |
| Lifegain | Mono-White Lifegain | 1 | 1 | 0 / 0 | 4 |
| Artifacts | Jeskai Artifacts | 1 | 1 | 0 / 0 | 4 |
| Skeletons | Orzhov Skeletons | 1 | 1 | 0 / 0 | 3 |
| Jace Control | Azorius Control | 0 | 0 | 0 / 0 | 1 |
| Sacrifice | Orzhov Sacrifice | 1 | 1 | 0 / 0 | 4 |
| Dragons | Boros Dragons | 1 | 1 | 0 / 0 | 5 |
| Dwarves | Boros Dwarf | 1 | 1 | 0 / 0 | 5 |
| Planeswalkers | Dimir Control | 1 | 1 | 0 / 0 | 2 |
| Discard | Mardu Discard | 0 | 0 | 0 / 0 | 1 |
| Landfall | Selesnya Landfall | 1 | 1 | 0 / 0 | 4 |
| Rabbits | Selesnya Rabbits | 1 | 1 | 0 / 0 | 3 |
| Omniscience | Temur Kona | 0 | 0 | 0 / 0 | 3 |
| Flash | Azorius Flash | 0 | 0 | 0 / 0 | 3 |
| Fling | Grixis Fling | 0 | 0 | 0 / 0 | 0 |
| Angels | Mono-White Angels | 1 | 1 | 0 / 0 | 3 |
| Dinosaur Reanimator | 4-Color Dinosaurs | 0 | 0 | 0 / 0 | 3 |
| Burn | Mono-Red Burn | 2 | 2 | 0 / 0 | 2 |
| Midrange | Dimir Midrange | 2 | 1 | 0 / 1 | 5 |
| Tokens | Boros Tokens | 1 | 1 | 0 / 0 | 4 |
| Creature Aggro | Rakdos Aggro | 2 | 2 | 0 / 0 | 1 |
| Allies | 4-Color Allies | 2 | 2 | 0 / 0 | 6 |
| Spell Control | 4-Color Control | 0 | 0 | 0 / 0 | 2 |
| Spellementals | Izzet Spellementals | 2 | 2 | 0 / 0 | 3 |
| Jace Exile | Dimir Jace | 1 | 1 | 0 / 0 | 3 |
| Lessons | Jeskai Lessons | 1 | 1 | 0 / 0 | 2 |
| Superfriends | Jeskai Superfriends | 1 | 1 | 0 / 0 | 5 |
| Demons | Mono-Black Demons | 2 | 2 | 0 / 0 | 9 |
| Elementals | Mono-Blue Elementals | 1 | 2 | 1 / 0 | 3 |
| Big Creatures | Naya World War Hulk | 1 | 1 | 0 / 0 | 5 |

The development hook was exercised in these complete games for Allies, Angels, Artifacts, Auras, Big Creatures, Burn, Creature Aggro, Demons, Dinosaur Reanimator, Discard, Dragons, Dwarves, Elementals, Flash, Jace Control, Jace Exile, Landfall, Lessons, Lifegain, Midrange, Omniscience, Planeswalkers, Rabbits, Sacrifice, Skeletons, Spell Control, Spellementals, Superfriends, Tokens. Other profiles retain their focused engine fixture evidence and complete game integration coverage; no new development contribution is claimed for an unexercised game profile.

Historical source-game integrity audits used `877b809a5fe441008fa59374f8224b0a` (Auras), `4eb132dcb9974ebca7368bda31e0b3a6` (Artifacts), `99e0c9073a2b4c1682b06e188e717c35` (Spell Control), `99e0c9073a2b4c1682b06e188e717c35` (Burn). These historical outcomes were not pooled with the fresh baseline. Profiles without historical games used fresh baseline/candidate execution from the frozen library manifests.

Regressions are retained and identified in `audit.json`: sample 2 (Auras), sample 38 (Midrange). They demonstrate that extra reviewed development can still change subsequent actions adversely. Full subtype strategy fixtures, varied opponents, every variant and the inherited information audit remain open.

## Historical publication and live verification

Published immutable bundle: `656092f7e33fb10bd16be765879d566e5c9800f0413293a8436249004f59dc43-943137409838ea936a47a5692a531f3257c1ce9859c0dd246b1e31850ef5b50c`. All seven bridge class hashes exactly match the tested candidate, with unchanged engine resources. Published sample 1 reproduced every candidate state checkpoint and selected action, including two exercised development choices. Existing runs remain pinned to their recorded bundles.

All 30 subtype preflights pass and exclude older incompatible results. Live session `dfbfb538ba6d4734a56d0ab2e7972f94` completed two normal-mode, single-worker opposed games, with one start per side. It records `auras-v001` versus unchanged `reanimator-terror-v9`, 6 subtype development overrides, 882 verified state checkpoints, continuous sequences, replay hashes and one terminal event per game. Attributes are frozen with catalog hash `b781d333de60782d9266509d0898be9c383262d93df06834af8ce5d7f84684b4`. These are smoke checks, not extra matchup-rate evidence.

The UI selector and saved-pilot panel were inspected live; [completed-session screenshot](subtype-pilots-live.jpg) shows the assigned profiles. The verification browser was left on the established Aggro/Control/Reanimator selection. API requests with an omitted pilot mode exclude the subtype session cache, while explicit subtype requests can reuse its two completed games. Thus the new policy is an explicit choice in both UI and API.

After the earlier live session finished 100/100 games, the idle service was reloaded from its required `src/DeckTester` content directory. The actual listener PID 52422 and published bundle are verified. All 29 saved run payloads remain unchanged across reload, including the smoke and resumed session. The full fixture checks comprise 215 initial positions plus two supplemental Resonating Lute positions. .NET build, profile/cache identity, card attributes and retained Reanimator/Control/Aggro regressions pass.

Skill: `/Users/pesettan/.codex/skills/crucible-pilot/SKILL.md`. Pilot assignment: `src/DeckTester/SubtypePilots.cs`; legal action hook: `engine/ForgeBridge.java`; frozen annotations: `docs/subtype-pilots.seed.json` (also merged into the canonical seed). The strategy validation and inherited Forge information gates remain open.
