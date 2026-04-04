# Outpost organic husbandry (research)

**Status:** active — supports accurate organic sourcing; full pen logic partly in compiled Papyrus.  
**See also:** [crafting-and-resources.md](crafting-and-resources.md), [tooling-catalog.md](tooling-catalog.md), [product-vision.md](product-vision.md).

---

### Creature loot vs outpost husbandry

- `**StarfieldExplore`** indexes **creature loot** as `**INpcGetter.DeathItem`** → `**ILeveledItemGetter**` (recursive `**Entries**` / `**Reference**`) → leaf `**IMiscItemGetter` / `IIngestibleGetter` / `IResourceGetter**` FormKeys. That matches “drops when killed / looted,” including multiple different ingredients from the same creature.
- **Outpost organic fauna pen / greenhouse (vanilla plugin shape):**
  - `**OutpostBuilderOrganic_FaunaList`** and `**OutpostBuilderOrganic_FloraList**` are `**FormList**` records whose **items are tier `ConstructibleObject` recipes** (`co_Outpost_Builder_OrganicFauna01`…`03`, `co_Outpost_BuilderOrganicFlora01`…`03`), **not** a whitelist of `**NPC_`** or `**Flora**` species.
  - `**co_Outpost_Builder_OrganicFauna**` / `**co_Outpost_BuilderOrganicFlora**` (parent recipes) have `**CreatedObject**` → those same `**FormList**` entries (builder UI tier unlock pattern).
  - Tier `**CreatedObject**` for each pen/greenhouse is a `**PackIn**` (e.g. `**OutpostPI_BuilderOrganicFauna01**`) whose `**EnumerateFormLinks**` surface `**Transform**` rows like `**Outpost_HarvesterFauna01**` / `**Outpost_HarvesterFlora01**` and internal cells — **creature/plant eligibility and per-slot outputs** are likely **scripts / VMAD / non-NPC FormLists**, not the misnamed `_FaunaList` / `_FloraList`.
  - `**IFurniture.EditorID`** is often **empty** when read through `**CreateFromBinaryOverlay`** here, so filtering the **Furniture** group by EditorID is unreliable; **COBJ → `PackIn`** is the practical entry point.
- `**--inspect-husbandry**` dumps the above chain. `**--inspect-outpost-harvesters**` lists harvester `**Transform**` rows, scans **PackIn** / **Activator** / **Furniture** for `**EnumerateFormLinks`** backlinks to each Transform, prints `**VirtualMachineAdapter**` (scripts + **ScriptObjectProperty** targets + nested links on properties) and a **verbose** link list (resolved `**DescribeComponent`**, null slots, unparsed items), then **Globals** / **CurveTables** / **GameSettings** harvester-ish hits. Vanilla organic **tier PackIns** use `**OutpostPackinDummyScript`** only; real logic is **not** on that VMAD.
- `**--inspect-outpost-husbandry-cells`** follows `**OutpostPI_BuilderOrganicFauna01`…`03**` and `**OutpostPI_BuilderOrganicFlora01`…`03**`: `**EnumerateFormLinks**` → `**ICellGetter**` storage cell → `**Persistent` / `Temporary**` `**IPlacedObjectGetter**` (geometry + interactables), then a consolidated dump of `**OutpostBuilderOrganicFauna*` / `OutpostBuilderOrganicFlora***` `**IContainerGetter**` records: **Keywords** (e.g. `**ResourceTypeOrganic`**, `**CrewFurniture_Zoology**` / `**_Botany**`), `**VirtualMachineAdapter**` with `**OutpostHarvesterFaunaScript**` / `**OutpostHarvesterFloraScript**`, **ScriptObjectProperty** links (`**SQ_Parent`**, factions, outpost keywords). `**DumpVirtualMachineAdapter**` expands any `**ScriptStructListProperty**` (including `**FaunaCreation**`) into `**ScriptEntryStructs**`: each slot has `**Members**` such as `**CreatureKeyword**` (`**IKeywordGetter**`, vanilla pens use `**ActorTypeHerdLarge**` / `**Medium**` / `**Small**`) and `**createCount**` (int). That is **herd-size tiering**, not a per-species toxin list. `**PlanetBiome.Fauna`:** Mutagen types rows as `**IFormLinkGetter<INpcGetter>`**, but `**INpcSpawnGetter**` is implemented by both `**Npc**` and `**LeveledNpc**` — `**--inspect-pen-herd-planets**` resolves each `**FormKey**` as `**INpcSpawn**` and expands `**LeveledNpc**` recursively. On vanilla `**Starfield.esm**`, sampled data had **all** top-level fauna links resolving as `**Npc`** (no top-level `**LeveledNpc**` in that count). **Strict join:** herd `**ActorTypeHerd*`** on those resolved NPCs (plus `**Race**`, `**KeywordsTemplate**`, `**DefaultTemplate**`) still yields **empty** planet∩tier for vanilla — listed creatures are not the same records that carry herd keywords. **Race bridge (heuristic):** union tiers from herd-tagged NPCs onto planets whose fauna includes **any NPC with the same `Race` FormKey** — can be non-empty but does not guarantee script-accurate pen behavior. The inspect prints Coverage, strict counts, race-bridge stats, and samples. `**Default.Items`** on those containers is typically **null in binary overlay**. **Further:** script yields, `**HandScannerTarget`**, biome condition functions, fuller load order.
- **Pen script VMAD trace (`--inspect-pen-fauna-script-trace`):** On `**OutpostBuilderOrganicFauna01`…`03`**, `**OutpostHarvesterFaunaScript**` exposes `**FaunaCreation**` (herd `**CreatureKeyword**` + `**createCount**`), `**OutpostFaunaFaction**`, `**OutpostLinkCreatedActor` / `OutpostLinkCreatedActorTarget**` keywords, `**OutpostFauna**` → `**IQuestGetter**` form `**07092C**` with EditorID `**SQ_Parent**`, and `**HandScannerTarget**`. The latter is **not** an NPC: it resolves to `**ActorValueInformation`** `**HandScannerTarget**` (scanner-related **actor value**). The `**SQ_Parent`** row in `**Starfield.esm**` is an **empty quest shell** (no stages/objectives/quest VMAD properties in overlay) — runtime fauna pen logic lives in **compiled Papyrus** and **save-backed** quest state, not in a second planet table next to `**PlanetBiome.Fauna`**.
- **Compiled Papyrus (authoritative husbandry/greenhouse logic):** Game scripts are **not** in the ESM. Vanilla harvester scripts in `**Starfield - Misc.ba2`**: `**scripts/outpostharvesterfaunascript.pex**`, `**scripts/outpostharvesterflorascript.pex**`, `**scripts/outpostharvesterfloraplanterscript.pex**`. `**tools/extract_misc_ba2_script.py**` extracts one `**.pex**` by name; `**tools/dump_outpost_husbandry_pex_strings.py**` pulls all three and prints **filtered** printable strings (`**--all`** = full ASCII runs). A `**strings**`-style pass on the fauna `**.pex**` surfaces identifiers such as `**GetActorBaseForResource**`, `**SetScanned**`, `**HasKeyword**`, `**OrganicResourceAV**`, `**HandScannerTarget**`, `**FaunaCreationData**`, `**ResourceGlobalData**`; flora scripts surface `**GetFloraForResource**`, `**CreateFlora**`, `**OutpostLinkFloraPlanter**`, planter `**FloraNodeMax**` / scale fields, etc. For readable `**.psc**` source, use **[Champollion](https://github.com/Orvid/Champollion)** on Linux via **Wine** — see **PEX → PSC (Champollion + Wine)** below; then trace callees (native / other scripts) the same way as for ESM-backed data.
- **Pen / greenhouse terminal (product scope for data tools):** In play, the terminal lists products tied to **fauna (or flora) on that planet**, and only after the player has **fully scanned** relevant species. For **ESM-only / planner tooling**, treat **full scan** as a **player-known unlock rule** — **do not** try to read scan state from plugins. Focus indexes on **planet-level eligibility** (what organic outputs the world supports on **this** planet) and **reverse queries** (which planets support a given resource), by joining script templates (`**FaunaCreation`**, etc.) with **planet → fauna / loot / flora** graphs already in scope for gather traces.

### PEX → PSC (Champollion + Wine)

Research workflow to turn vanilla `**.pex**` into decompiled `**.psc**` when you need full control flow and identifiers beyond `strings` / `dump_outpost_husbandry_pex_strings.py`.

**Prerequisites**

- **[Champollion v1.3.2](https://github.com/Orvid/Champollion/releases/download/v1.3.2/Champollion.v1.3.2.zip)** — zip contains `Champollion.exe` only; unpack anywhere persistent (e.g. `~/tools/Champollion-1.3.2/`).
- **Wine** — on Ubuntu, `sudo apt install wine64` provides **`/usr/bin/wine`** (a `wine64` standalone binary may not appear on `PATH`; invoking `wine` is enough). First run initializes `~/.wine` and may log benign OLE/RpcSs noise.

**Extract `.pex` from `Starfield - Misc.ba2`**

From repo root, with `STARFIELD_DATA` set or defaulting to the Linux Steam `Data` folder (see [`tools/extract_misc_ba2_script.py`](../tools/extract_misc_ba2_script.py)):

```bash
python3 tools/extract_misc_ba2_script.py \
  --name outpostharvesterfaunascript.pex \
  -o /tmp/outpostharvesterfaunascript.pex
```

Use `--name` with the archive path (`scripts/...`) or a basename; the helper normalizes to `scripts/`.

**Decompile with Champollion**

```bash
wine /path/to/Champollion.exe -p /tmp/psc_out /tmp/outpostharvesterfaunascript.pex
```

- **`-p` / `--psc`** — output directory for `**.psc**` files.
- **`wine ... Champollion.exe --help`** — full flags (e.g. **`-r`** recurse for directories, **`-i`** print PEX header and exit, **`-c`** embed assembly in PSC comments).

**Caveats**

- Champollion emits a **Starfield preliminary syntax** warning: Guard / some builtins are **guessed**; affected lines may be marked with `;***`. Treat output as **research-grade**, not guaranteed Creation Kit source.
- Do not commit large decompiled trees to this repo unless you adopt an explicit policy; keep generated `**.psc**` local or summarize behavior in notes.

**Vanilla husbandry-related `scripts/*.pex` names**

| Script | BA2 path (for `--name`) |
| ------ | ----------------------- |
| Fauna harvester | `outpostharvesterfaunascript.pex` |
| Flora harvester | `outpostharvesterflorascript.pex` |
| Flora planter | `outpostharvesterfloraplanterscript.pex` |
