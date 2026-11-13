# Osiris DBase lineage — Divine Divinity story.000 v1.4

Дата исследования: 2026-11-13. Источник: `C:\Divine Divinity\main\startup\story.000`.

SHA-256 исходного Story: `bbc55e6a343f46f9df94666ac0bb3bd24f3aa68ba354a899320c643a66add838`.

## Краткий результат

Первичная карта `osiris-dbase-map.csv` сопоставляла DBase только с прямыми корнями `CReteFact` и декларациями функций kind 4. Поэтому статус `Unmapped` означал **«нет отдельного прямого CReteFact-корня»**, а не «база не используется» и не «база не имеет владельца в Rete».

Повторная проверка всех узлов Rete и обход графа до именованных листьев показали:

- Всего DBase: **680**; кортежей: **19 813**.
- Непустых баз: **196**; пустых: **484**.
- Все **680/680** баз имеют ровно одного непосредственного владельца в Rete-графе.
- Прямые владельцы: 447 `CReteFact`, 75 `CReteAnd`, 34 `CReteNAnd`, 29 `CReteRelCondition`, 95 `CReteRuleActionPart`.
- Все **196/196** непустых баз прослеживаются по графу до одной или нескольких именованных функций.
- 447 баз имеют прямое соответствие `CReteFact` и декларации Osiris kind 4; для этих записей прежние проверки схемы и арности остаются действительными.
- Оставшиеся 233 базы не имеют отдельного прямого `CReteFact`-корня. Из них 26 непустые, и все 26 имеют именованные upstream-источники.

Следовательно, 26 непустых баз из прежнего списка `Unmapped` не являются бесхозными. Их данные используются в Rete-подграфах, ведущих к обычным Osiris-фактам. Однако upstream-функции не являются доказанными именами самих DBase: одна база может обслуживать несколько предикатов. Поэтому `FactName` намеренно остаётся пустым, а источники указаны отдельно.

## Как построено соответствие

Использован декодированный `startup.json`, полученный из того же `story.000`. Для каждого DBase собраны Rete-узлы, у которых второе базовое поле (`BaseNodeField`, поле `parent` в независимом `rete_index.py`) равно ключу базы. Затем от непосредственного владельца выполнен обход:

- `CReteAnd` / `CReteNAnd`: по левому и правому дочерним узлам;
- `CReteRelCondition`: к узлу-источнику сравнения;
- `CReteRuleActionPart`: к родительскому условному узлу;
- именованный лист Rete: к соответствующей записи Function по NodeId.

Типы узлов сверены с `rete_index.py`: 1 Fact, 2 Event, 3 DIV Query, 4 AND, 5 NAND, 6 RelCondition, 7 RuleActionPart, 8 InternalQuery. Это статический обход сериализованного графа, а не трассировка работающей игры.

## 26 непустых баз без прямого CReteFact

| DBase | Схема | Кортежи | Upstream-функции |
|---:|---|---:|---|
| 70 | NPC, INTEGER, INTEGER | 850 | `HostileAtt`, `RepAttitude` |
| 74 | NPC, INTEGER, INTEGER | 850 | `HostileAtt`, `RepAttitude` |
| 75 | NPC, INTEGER, INTEGER | 850 | `HostileAtt`, `RepAttitude` |
| 81 | NPC, INTEGER, INTEGER | 321 | `RepAttitude`, `TradeStats` |
| 84 | NPC, INTEGER, INTEGER | 321 | `RepAttitude`, `TradeStats` |
| 85 | NPC, INTEGER, INTEGER | 321 | `RepAttitude`, `TradeStats` |
| 149 | INTEGER | 1 | `Reputation` |
| 152 | INTEGER | 1 | `Reputation` |
| 153 | INTEGER | 1 | `Reputation` |
| 281 | NPC, STRING | 85 | `NpcFollowsAlignment`, `IsGuardAlignment` |
| 285 | NPC, INTEGER, INTEGER, INTEGER | 235 | `IsGuard`, `CharacterStats` |
| 400 | NPC, REAL × 6, INTEGER, INTEGER | 321 | `TradeStats`, `HostileAtt` |
| 404 | NPC | 229 | `EvilDude`, `MoralNpc` |
| 406 | NPC | 36 | `Unintelligent`, `MoralNpc` |
| 449 | OBJECT_CLASS, DIALOG_EVENT | 2 | `ObjectClassQuest`, `NpcHasObjectType` |
| 454 | OBJECT, DIALOG_EVENT | 18 | `ObjectQuest`, `NpcHasObject` |
| 467 | INTEGER, DIALOG_EVENT | 3 | `ObjectClassesQuest`, `NpcHasObjectTypeGroup` |
| 521 | INTEGER | 1 | `OnMap`, `Time` |
| 524 | INTEGER | 1 | `OnMap`, `Time` |
| 527 | INTEGER | 1 | `OnMap`, `Time` |
| 528 | INTEGER | 1 | `OnMap`, `Time` |
| 529 | INTEGER | 1 | `OnMap`, `Time` |
| 556 | NPC | 15 | `StoryStarted`, `BarracksOrc` |
| 592 | NPC | 11 | `StoryStarted`, `ShopRegion` |
| 601 | INTEGER | 1 | `Reputation` |
| 622 | NPC, INTEGER, STRING | 323 | `RegenerateTradeItems`, `StoryStarted` |

Все перечисленные upstream-функции в этом списке — Osiris facts (FunctionKind=4). Это не означает, что любая из них по отдельности является именем базы: это именованные источники, достигнутые обходом общего Rete-подграфа.

## Файлы

- `osiris-dbase-map.csv` — прежняя карта прямых `CReteFact`-соответствий, 680 строк.
- `osiris-dbase-lineage.csv` — дополненная карта всех 680 баз: непосредственный владелец Rete, тип узла и upstream-функции.
- `osiris-dbase-map.md` — исходные проверки схем и ограничений.

## Ограничения

- Не следует присваивать 233 базам вымышленные имена по upstream-функциям.
- Наличие кортежей не доказывает, что они были созданы INIT: DBase — сериализованное состояние fact store.
- Этот анализ не доказывает причинность появления tuple и не заменяет runtime-трассировку.
- Не превращать текущие кортежи в INIT автоматически: это может дублировать уже существующее состояние.
- Исследование статическое; игра и сохранения не запускались и не изменялись.
