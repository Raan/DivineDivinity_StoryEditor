# Osiris DBase map - Divine Divinity story.000 v1.4

Generated from the local main/startup/story.000 on 2026-11-13.
This is a read-only structural inventory. It does not edit the game or saves.

## Source and validation

- Source: C:\Divine Divinity\main\startup\story.000
- SHA-256: bbc55e6a343f46f9df94666ac0bb3bd24f3aa68ba354a899320c643a66add838
- DBases: **680**; total tuples: **19813**.
- Non-empty DBases: **196**; empty DBases: **484**.
- DBase keys matched to exactly one CReteFact root via BaseNodeField: **447 / 680**.
- Matched facts with one Osiris function declaration (kind 4), linked by node ID: **447**.
- Function signature vs DBase schema mismatches: **0**.
- Tuple arity vs schema mismatches: **0**.
- Unmapped DBase keys (no CReteFact root): **233**.

The direct fact-name mapping is strong where present: each of the 447 matched DBase keys has one unique CReteFact root, and its node ID resolves to one Osiris fact declaration. The declaration TypeValues can be compared directly with the DBase schema. The other 233 keys have no standalone CReteFact root; this does **not** mean they are ownerless or unused. A separate full Rete-lineage pass is documented in [osiris-dbase-lineage.md](osiris-dbase-lineage.md) and [osiris-dbase-lineage.csv](osiris-dbase-lineage.csv).

## Osiris value type IDs

| ID | Type | Schema-column occurrences |
|---:|---|---:|
| 1 | Integer | 453 |
| 2 | Real | 21 |
| 3 | String | 74 |
| 4 | NPC | 308 |
| 5 | OBJECT | 105 |
| 6 | DIALOG | 30 |
| 7 | REGION | 40 |
| 8 | LOCATION | 11 |
| 9 | NPC_CLASS | 6 |
| 10 | OBJECT_CLASS | 20 |
| 11 | DIALOG_EVENT | 64 |
| 12 | ENGINE | 2 |

Type IDs and labels are based on the reverse-engineering inventory in osiris_callback_types.md. ID 14 was not present in the source corpus.

## Schema distribution

| Schema IDs | DBase count | Tuple count |
|---|---:|---:|
| 1 | 169 | 21 |
| 4 | 136 | 2252 |
| 5 | 30 | 86 |
| 1,1 | 22 | 309 |
| 1,11 | 22 | 3 |
| 3 | 20 | 17 |
| 4,1 | 19 | 3747 |
| 4,1,1 | 18 | 3564 |
| 5,5 | 15 | 28 |
| 4,4 | 14 | 3 |
| 4,3 | 11 | 421 |
| 4,7 | 9 | 60 |
| 4,6 | 9 | 56 |
| 7,4 | 8 | 0 |
| 1,3 | 8 | 38 |
| 11,4 | 7 | 1 |
| 5,1 | 7 | 17 |
| 6 | 6 | 11 |
| 6,1,11 | 5 | 50 |
| 11,1 | 5 | 3 |
| 4,5 | 5 | 0 |
| 4,1,1,1 | 5 | 1109 |
| 1,1,1 | 4 | 4 |
| 4,11 | 4 | 7 |
| 10,11 | 4 | 2 |
| 1,10 | 4 | 7 |
| 1,5 | 4 | 25 |
| 1,7,4 | 4 | 0 |
| 5,11 | 4 | 18 |
| 1,4 | 4 | 18 |
| 11 | 4 | 3 |
| 3,1 | 4 | 3938 |
| 4,6,1 | 3 | 388 |
| 1,7,4,1 | 3 | 0 |
| 4,10,1 | 3 | 0 |
| 4,1,3 | 3 | 646 |
| 10,1 | 2 | 71 |
| 4,8 | 2 | 1 |
| 1,7 | 2 | 0 |
| 5,1,3,1 | 2 | 17 |
| 11,5 | 2 | 0 |
| 4,2,2,2,2,2,2,1,1 | 2 | 321 |
| 7 | 2 | 3 |
| 5,3 | 2 | 77 |
| 4,4,1 | 2 | 1 |
| 9,6 | 2 | 121 |
| 4,3,1 | 2 | 52 |
| 6,9,1 | 2 | 0 |
| 3,3 | 2 | 5 |
| 5,1,8 | 1 | 0 |
| 12,3 | 1 | 0 |
| 1,11,1 | 1 | 3 |
| 12 | 1 | 0 |
| 10,11,1 | 1 | 2 |
| 4,10 | 1 | 0 |
| 5,5,5,5,10 | 1 | 20 |
| 5,11,1 | 1 | 18 |
| 5,1,1,8,1,1 | 1 | 20 |
| 4,11,1 | 1 | 0 |
| 1,1,1,1 | 1 | 60 |
| 1,7,7,4,3 | 1 | 5 |
| 1,10,1 | 1 | 2 |
| 10 | 1 | 13 |
| 1,4,1 | 1 | 106 |
| 4,3,3 | 1 | 6 |
| 7,8 | 1 | 6 |
| 4,7,1 | 1 | 12 |
| 1,8,8,5,5,1 | 1 | 5 |
| 1,11,1,1 | 1 | 388 |
| 5,10,10 | 1 | 30 |
| 4,8,8,1 | 1 | 0 |
| 1,5,5,1 | 1 | 0 |
| 7,1 | 1 | 0 |
| 3,11 | 1 | 2 |
| 4,3,7 | 1 | 0 |
| 3,4 | 1 | 0 |
| 5,4 | 1 | 0 |
| 4,2 | 1 | 0 |
| 4,3,3,1 | 1 | 81 |
| 4,2,2,2,2,2,2,1 | 1 | 321 |
| 5,1,1,1 | 1 | 0 |
| 4,3,1,1 | 1 | 39 |
| 7,4,6 | 1 | 11 |
| 7,7,1,1 | 1 | 26 |
| 6,4,4 | 1 | 0 |
| 5,8 | 1 | 0 |
| 6,1 | 1 | 3 |
| 4,1,1,2 | 1 | 850 |
| 7,3,1 | 1 | 2 |
| 9,3 | 1 | 30 |
| 5,5,5 | 1 | 4 |
| 1,11,1,1,1 | 1 | 3 |
| 9 | 1 | 44 |
| 3,1,3 | 1 | 3 |
| 1,4,3,1 | 1 | 150 |
| 1,3,3,1 | 1 | 18 |
| 1,8 | 1 | 5 |
| 7,1,1,1 | 1 | 0 |
| 1,5,3 | 1 | 4 |
| 2 | 1 | 0 |
| 7,1,1 | 1 | 0 |

## Largest populated databases

| Key | Fact | Schema | Tuples |
|---:|---|---|---:|
| 664 | StringTable | String,Integer | 3937 |
| 61 | CharacterStats | NPC,Integer,Integer,Integer | 850 |
| 363 | HelpCosts | NPC,Integer,Integer,Real | 850 |
| 49 | Attitude | NPC,Integer | 850 |
| 60 | LastTradeBalanceUsed | NPC,Integer | 850 |
| 63 | HostileAtt | NPC,Integer | 850 |
| 75 | unmapped | NPC,Integer,Integer | 850 |
| 39 | RepAttitude | NPC,Integer | 850 |
| 70 | unmapped | NPC,Integer,Integer | 850 |
| 74 | unmapped | NPC,Integer,Integer | 850 |
| 168 | CannotTrade | NPC | 546 |
| 37 | DefaultDialog | NPC,DIALOG,Integer | 388 |
| 502 | DialogEvent_QuestData | Integer,DIALOG_EVENT,Integer,Integer | 388 |
| 622 | unmapped | NPC,Integer,String | 323 |
| 402 | RegenerateTradeItems | NPC,Integer,String | 323 |
| 624 | LastTradeItemGeneration | NPC,Integer | 322 |
| 81 | unmapped | NPC,Integer,Integer | 321 |
| 85 | unmapped | NPC,Integer,Integer | 321 |
| 84 | unmapped | NPC,Integer,Integer | 321 |
| 80 | TradeStats | NPC,Real,Real,Real,Real,Real,Real,Integer | 321 |
| 400 | unmapped | NPC,Real,Real,Real,Real,Real,Real,Integer,Integer | 321 |
| 397 | IsTrader | NPC | 312 |
| 172 | Amoral | NPC | 266 |
| 284 | IsGuard | NPC | 235 |
| 285 | unmapped | NPC,Integer,Integer,Integer | 235 |

## Rete lineage follow-up

The complementary lineage pass found exactly one direct Rete owner for each of the 680 DBases. All 196 populated DBases trace through the graph to one or more named upstream functions. The 26 populated entries without a direct CReteFact root are linked to named Osiris fact functions through AND/NAND/RelCondition subgraphs. The upstream names are not necessarily the semantic names of the DBase itself; see the dedicated report before assigning labels.

## Interpretation and limitations

- A DBase is a typed fact table in the serialized Osiris state. Its tuples are a runtime/state snapshot, not source declarations.
- Do not turn all current tuples into INIT statements: that could duplicate state when the story is loaded or merged.
- `Unmapped` in the original CSV means **no direct CReteFact declaration was found**, not that a DBase is engine-internal, unused, or has no Rete owner.
- The original CSV contains one row for every DBase (680 rows), with raw schema IDs, readable type labels, tuple count, mapping status, and one example tuple where available. The companion lineage CSV adds direct Rete owner and upstream-function data.
- The CSV sample tuple is illustrative only; it is not a complete tuple export.