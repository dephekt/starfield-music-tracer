# Mutagen & Spriggit — exploration notes (2026-04-03)

## Goal

Validate **Spriggit** (YAML/JSON tree export) and **Mutagen** (typed C# API) for the outpost planner and a future broader **ESM explorer** (weapons, armor, mods, books, ammo, form IDs, display names, crafting links).

## Spriggit CLI (Linux)

- **Official Linux binary:** `SpriggitLinuxCLI.zip` from [Spriggit releases](https://github.com/Mutagen-Modding/Spriggit/releases) (tested **0.40.0**). The binary runs and lists commands (`serialize`, `deserialize`, …).
- `**dotnet tool install spriggit.cli`** fails: NuGet packages report missing `DotnetToolSettings.xml` (same failure for `**Spriggit.Yaml.Starfield**` when the Linux CLI tries `dotnet tool install` to pull translation packages into `/tmp/Spriggit/Translations/...`).
- **Conclusion:** On this Linux/.NET 8 setup, **Spriggit is not usable end-to-end** without a fix upstream (packaging) or a workaround (e.g. run on Windows, or pre-install translation packages another way). The **GitHub zip** is the right distribution for CLI, but **Starfield serialize still depends on broken `dotnet tool` translation installs**.

## Mutagen (C# library)

- **Package:** `Mutagen.Bethesda.Starfield` — stable **0.54.x** is not on NuGet yet; `**0.54.0-alpha.32`** restores and builds on **net8.0**.
- `**StarfieldMod.CreateFromBinaryOverlay(ModPath, StarfieldRelease.Starfield)`** loads `**Starfield.esm**` quickly (~1–2s cold, ~7s with `dotnet run` overhead) and exposes major record groups with **real counts** (see below).
- **COBJ:** exposed as `**ConstructibleObjects`** (not `Constructibles`). `**CreatedObject**` links to the produced form — suitable for recipe/BOM graphs.
- **Ingestibles:** includes craftables such as `**Chem_Craft_Amp`** (`29A856:Starfield.esm`), matching the pharmaceutical “Amp” use case.

**Future “ESM explorer” alignment:** same load path gives **Weapons (406)**, **Armors (1017)**, **ObjectModifications (2541)**, **Books**, **Ammo**, **MiscItems**, **Keywords**, **NPCs**, **Florae**, etc. — all enumerable from one mod.

### Linux caveat: localized strings

- Accessing `**TranslatedString`** fields (e.g. `**ing.Name**`) triggered resolution via **archive / plugin listings** that expect **Windows `LocalAppData`**-style layout (`PluginListingsPathContext`). On Linux without that environment, `**.Name` can throw**.
- **Mitigation for tooling:** resolve strings explicitly (e.g. load `**Starfield - Localization.ba2`** / `.strings` the same way as [extract.py](../extract.py)), or set up Mutagen’s string lookup paths for Linux/Wine; or ship **EditorID + FormKey** in v1 of exports and add friendly names in a second pass.

## Suitability vs product direction


| Need                                                        | Mutagen                                          | Spriggit (current Linux)                                       |
| ----------------------------------------------------------- | ------------------------------------------------ | -------------------------------------------------------------- |
| Reliable **Starfield.esm** read, typed records, links       | **Yes** (alpha package acceptable)               | Blocked by translation `dotnet tool` failure                   |
| **Greppable** full-plugin YAML/JSON without custom code     | Would be ideal                                   | **Not yet** on this host                                       |
| **Custom export** (JSON/SQLite for web app)                 | **Yes** — small C# console is the practical path | Optional later if packaging fixed                              |
| Long-term **ESM explorer** (weapons, mods, crafting, pivot) | **Strong fit** — same API covers those groups    | Nice for human diff/git; not required if you own serialization |


**Recommendation:** Proceed with **Mutagen-backed C# data generation** as the primary pipeline; **retry Spriggit** when upstream fixes global-tool packaging or document a non–`dotnet tool` install for `Spriggit.Yaml.Starfield`. Keep **Python `extract.py` BA2 string** logic (or equivalent) for **display names** on Linux.

---

## Spriggit vs own console app (architecture)

**Short answer: prefer Mutagen directly in your own console app(s).** Treat Spriggit as **optional sugar**, not the spine of the product.

### What Spriggit is good for

- **Git-friendly text mods:** round-trip plugin ↔ YAML/JSON for **mod authors** who want diffs and merges.
- **Quick human inspection** of a record’s serialized shape **if** the CLI works on your OS.
- It still **uses Mutagen under the hood** for binary semantics; it does not replace understanding Mutagen for anything custom.

### What Spriggit does *not* solve for you

- **Your web app schema** (SQLite tables, APIs, rollups, pivots). You still design and own that.
- **Linux today:** broken `dotnet tool` path for Starfield translation packages — you cannot rely on it in CI or dev without workarounds.
- **Performance / size:** a full `Starfield.esm` text export is enormous; grepping it is a research trick, not necessarily your shipping pipeline.

### Why “own console + Mutagen” is the right default

- **Stable, testable pipeline:** `dotnet run` → deterministic artifact → Python/FastAPI reads DB. No extra moving part that fails to install.
- **Typed API:** you emit exactly the fields you need (COBJ ingredients, mod links, flora, etc.) instead of ingesting Spriggit’s **file-per-record** layout.
- **Aligns with [Mutagen docs](https://mutagen-modding.github.io/Mutagen/):** you can adopt the recommended building blocks **on purpose** (see below), not fight `TranslatedString` defaults on Linux.

### Using Mutagen “properly” (avoid wacky apologies)

`[tools/StarfieldExplore](../tools/StarfieldExplore/)` is intentionally a **thin probe** (single-plugin overlay, no display names). For production extraction, plan to move toward documented patterns:

1. **Environment + load order** — [Environment](https://mutagen-modding.github.io/Mutagen/environment/): build a `**GameEnvironment`** (or equivalent Starfield entry) from your **Data** folder and **active plugin list** when you need behavior that matches the game (overrides, patches, DLC).
2. **Link cache** — [Link cache](https://mutagen-modding.github.io/Mutagen/linkcache/): resolve `**FormLink` / `CreatedObject` / mod targets** to real records instead of only printing form keys.
3. **Strings** — [Strings](https://mutagen-modding.github.io/Mutagen/) + [Archives](https://mutagen-modding.github.io/Mutagen/): use Mutagen’s **string + BA2** APIs (or a documented path for Linux) so `**TranslatedString` / FULL** names work; avoid ad hoc “never touch `.Name`” as a permanent design — that was a **probe limitation**, not the end state.
4. **Readonly getters** — [Best practices](https://mutagen-modding.github.io/Mutagen/): prefer getter interfaces in public extraction code.

**Verdict:** Saying **“we use Mutagen in our own exporter, following Environment + LinkCache + Strings”** is the coherent architecture. **Spriggit** is a nice-to-have for **mod-text workflows** or **one-off dumps** if/when packaging works — it should not block or define your stack.

## Reference implementation in this repo

- `[tools/StarfieldExplore/](../tools/StarfieldExplore/)` — `dotnet run` with `STARFIELD_DATA` optional (defaults to Steam Linux path).

Sample output (counts from vanilla `Starfield.esm`):

- Npc ~7.1k, Keyword ~5.9k, ConstructibleObject ~3.0k, ObjectModification ~2.5k, MiscItem ~1.3k, Armor ~1.0k, Book ~1k, Weapon ~406, Ingestible ~353, Flora ~300, Ammo ~150.

### Amp vertical slice (recipe + “where from” attempt)

`StarfieldExplore` now traces `**Chem_Craft_Amp`** → `**co_Chem_Amp**` (`IConstructibleObject`) → `**ConstructableComponents**`, resolves each line as `**IResourceGetter**`, and uses `**ToImmutableLinkCache()**` for `**WorkbenchKeyword**`.

Observed in vanilla `Starfield.esm` (your install):


| Role              | FormKey                | EditorID                                                     |
| ----------------- | ---------------------- | ------------------------------------------------------------ |
| Output ingestible | `29A856:Starfield.esm` | `Chem_Craft_Amp`                                             |
| COBJ              | `29CACE:Starfield.esm` | `co_Chem_Amp`                                                |
| Workbench         | `102158:Starfield.esm` | `WorkbenchChemlabRecipeKeyword`                              |
| Component         | `0057EA:Starfield.esm` | `ResInorgCommonArgon_G`                                      |
| Component         | `077823:Starfield.esm` | `ResOrgCommonToxin` (`ResourceType=Toxin`)                   |
| Component         | `29F3FD:Starfield.esm` | `ResOrgCommonMetabolicAgent` (`ResourceType=MetabolicAgent`) |


**Quantities in the ESM:** one row per resource (`x1` each in `ConstructableComponents`). Matches in-game chemlab (**1 of each** for Amp); treat third-party sites that disagree as unreliable unless backed by plugin data.

`**Resource.Produce` resolution (vanilla):** those FormKeys are `**ConstructibleObject`** refinement recipes, not misc items directly:


| Resource                     | Produce (COBJ)                              | Nested `CreatedObject` (misc)        |
| ---------------------------- | ------------------------------------------- | ------------------------------------ |
| `ResInorgCommonArgon_G`      | `co_Resource_Inorg_Argon` (`05EF46`)        | `InorgCommonArgon` (`005588`)        |
| `ResOrgCommonToxin`          | `co_Resource_Org_Toxin` (`2AC0EB`)          | `OrgCommonToxin` (`0055CB`)          |
| `ResOrgCommonMetabolicAgent` | `co_Resource_Org_MetabolicAgent` (`29F3FB`) | `OrgCommonMetabolicAgent` (`29F3FC`) |


`StarfieldExplore` indexes `**ConstructibleObjects**` and labels `Produce` accordingly; `**TryResolve<IConstructibleObjectGetter>**` works once you know the pattern.

**How INARA-style “gathered from flora” maps in the ESM**

1. `**IFloraGetter.Production`** (`SeasonalIngredientProduction`) is **only seasonal byte weights** — it does **not** name the material (Mutagen: `SeasonalIngredientProduction` has Spring/Summer/Fall/Winter counts only).
2. `**IFloraGetter.Ingredient`** is the **direct harvest** item for that plant record, but PCM **planet lists** use a different link.
3. `**Planet` → `PlanetBiome` → `PlanetFlora`**: each row has `**Flora**` (`IFloraGetter`) and `**Resource**` as an `**IMiscItemGetter**` link. For organics, that misc is almost always a **part-specific** yield (e.g. `OrgCommonToxin_Leaf`, `OrgCommonToxin_Sap`, …), **not** the stackable chemlab misc `OrgCommonToxin`.
4. The refinery COBJ `**co_Resource_Org_Toxin`** only lists **water** as a component to produce `**OrgCommonToxin`**; it does **not** list plant parts. So you **cannot** reach planet flora by walking **COBJ precursors** backward from `OrgCommonToxin` alone.
5. `**StarfieldExplore`** therefore adds gather keys: **stackable refined misc** + **all misc items whose EditorID starts with `{refinedEdid}_`** (e.g. `OrgCommonToxin_`), then matches those keys against `**PlanetFlora.Resource**`. That reproduces lists comparable to third-party sites (e.g. **Cage Brain** / `FloraBiomeCageBrain01` for toxin on some planets).
6. `**IResourceGetter.List`** on `ResOrgCommonToxin` is **null** in vanilla — not the flora pivot. **Argon** has no `Org*_*` style planet-flora misc hits in the same way; **inorganics** in the survey use `**IBiomeGetter.ResourceGeneration`** → RGD (see inorganics bullet below), not `**PlanetFlora**`.

### Acquisition path taxonomy (product model)

- **Shared crafting layer:** `IResourceGetter` + `ConstructibleObject` components (and nested `co_Resource_*` refineries) are the same mechanism for organics and gases.
- **Organics (e.g. Toxin, Metabolic agent):** after resolving **Produce → COBJ → misc**, pivot to **flora/fauna**, leveled lists, or other sources that reference that misc — not only `Resource` rows.
- **Inorganics / gas (e.g. Argon):** same graph up to the refined misc. **Survey-style PCM lists** usually come from `**IBiomeGetter.ResourceGeneration`** (a **list** of links to `**ResourceGenerationData`**), **not** from `**IPlanetBiomeGetter.ResourceGeneration`** (often **null**). Chain: `**Planet` → `PlanetBiome` → `Biome` → `IBiomeGetter.ResourceGeneration[]` → RGD → `Items[].Resource`**. Example **Altair II** (`05E05C`, `AltairIIPlanetData`): **FrozenLife06** uses RGD `**FrozenBarrenDefaultRes`** and lists `**ResInorgCommonArgon_G**`, `**ResInorgCommonCopper**`, `**ResInorgCommonWater_L**`; **DesertRockyLife02** lists **Uranium** / Lead; **OceanLife06** lists water only — matches in-game survey buckets per biome type. `**--resourcegen-resource=EDID`** still lists all RGD templates + planet referrers; `**--planet-survey=HINT**` dumps one planet’s biome RGD rows for validation.

### Acquisition axes (player / Inara mental model)

Keep **separate** traces in data and in the tool:

- **Organic resources** — world: flora / fauna (and loot); **outpost production**: **husbandry / greenhouse** module logic (scripts, containers), not the chemlab **COBJ** graph for the jug itself.
- **Inorganic resources** — **planetary extractors** + survey / **RGD** / biome links (see inorganics bullets above).
- **Manufactured goods** — **player benches** (industrial, lab, cooking, …): **COBJ** + workbench keyword (Amp / Penicillin traces).
- **Loot / hand-gather** — leveled lists, nodes, cutter, etc. can still drop organics, inorganics, or manufactured items; that does **not** replace the outpost-module recipe graph for “what the pen consumes / produces.”

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
- **Compiled Papyrus (authoritative husbandry/greenhouse logic):** Game scripts are **not** in the ESM. Vanilla harvester scripts in `**Starfield - Misc.ba2`**: `**scripts/outpostharvesterfaunascript.pex**`, `**scripts/outpostharvesterflorascript.pex**`, `**scripts/outpostharvesterfloraplanterscript.pex**`. `**tools/extract_misc_ba2_script.py**` extracts one `**.pex**` by name; `**tools/decompile_misc_pex.sh**` batches extract + Champollion (presets include follow-ons **`sq_parentscript`**, **`planettraitscantargetscript`**, **`outpostcontainerscript`** — see [outpost-organic-husbandry.md](outpost-organic-husbandry.md)); `**tools/dump_outpost_husbandry_pex_strings.py**` pulls all three harvesters and prints **filtered** printable strings (`**--all`** = full ASCII runs). A `**strings**`-style pass on the fauna `**.pex**` surfaces identifiers such as `**GetActorBaseForResource**`, `**SetScanned**`, `**HasKeyword**`, `**OrganicResourceAV**`, `**HandScannerTarget**`, `**FaunaCreationData**`, `**ResourceGlobalData**`; flora scripts surface `**GetFloraForResource**`, `**CreateFlora**`, `**OutpostLinkFloraPlanter**`, planter `**FloraNodeMax**` / scale fields, etc. For readable `**.psc**` source, use **[Champollion](https://github.com/Orvid/Champollion)** on Linux via **Wine** — see **PEX → PSC (Champollion + Wine)** below; then trace callees (native / other scripts) the same way as for ESM-backed data.
- **Pen / greenhouse terminal (product scope for data tools):** In play, the terminal lists products tied to **fauna (or flora) on that planet**, and only after the player has **fully scanned** relevant species. For **ESM-only / planner tooling**, treat **full scan** as a **player-known unlock rule** — **do not** try to read scan state from plugins. Focus indexes on **planet-level eligibility** (what organic outputs the world supports on **this** planet) and **reverse queries** (which planets support a given resource), by joining script templates (`**FaunaCreation`**, etc.) with **planet → fauna / loot / flora** graphs already in scope for gather traces.

### PEX → PSC (Champollion + Wine)

Research workflow to turn vanilla `**.pex**` into decompiled `**.psc**` when you need full control flow and identifiers beyond `strings` / `dump_outpost_husbandry_pex_strings.py`. **Fast path:** [`tools/decompile_misc_pex.sh`](../tools/decompile_misc_pex.sh) (install and manual single-file steps: [outpost-organic-husbandry.md](outpost-organic-husbandry.md)).

**Prerequisites**

- **[Champollion v1.3.2](https://github.com/Orvid/Champollion/releases/download/v1.3.2/Champollion.v1.3.2.zip)** — zip contains `Champollion.exe` only; unpack under **`~/.local/share/champollion/Champollion-1.3.2/`** (XDG) or see [outpost-organic-husbandry.md](outpost-organic-husbandry.md).
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
CHAMP="${CHAMPOLLION_EXE:-$HOME/.local/share/champollion/Champollion-1.3.2/Champollion.exe}"
wine "$CHAMP" -p research/decompiled/psc research/decompiled/pe/outpostharvesterfaunascript.pex
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

### Penicillin X (vanilla EditorIDs)


| Role               | EditorID                |
| ------------------ | ----------------------- |
| Crafted ingestible | `Aid_Craft_PenicillinX` |
| COBJ               | `co_Chem_PenicillinX`   |


Components (your install): `**ResOrgUncommonMembrane`**, `**ResOrgCommonMetabolicAgent**`, `**ResOrgUncommonAntimicrobial**` — each refines via nested `**co_Resource_***` COBJs to misc items the same way as Amp resources.

### Debug CLI (StarfieldExplore)

- `--planetflora-misc-substr=Toxin` — misc EditorIDs used as `PlanetFlora.Resource` (shows `OrgCommonToxin_Leaf`, etc.).
- `--planetflora-misc=OrgCommonToxin` — expect **no rows** (stackable misc is not what `PlanetFlora` references).
- `--cobjs-for-output-misc=OrgCommonToxin` — COBJs that output the stackable misc (water / fauna variants).
- `--resourcegen-resource=ResInorgCommonArgon_G` — full `**ResourceGenerationData`** scan for that `**IResourceGetter**` + biome `**ResourceGeneration**` rows + `**IPlanet**` `**EnumerateFormLinks**` referrers to those RGD FormKeys (see inorganics bullet above).
- `--planet-survey=AltairIIPlanetData` — `**PlanetBiome**` + `**IBiomeGetter.ResourceGeneration**` → RGD → resources for matching planet(s).
- `--inspect-husbandry` — organic fauna/flora **FormLists**, builder **COBJ** BOMs, `**PackIn`** placed modules + sample `**EnumerateFormLinks**` (see husbandry section above).
- `--inspect-outpost-harvesters` — harvester `**Transform**` + referrer **PackIn**/**Activator**/**Furniture**, **VMAD**, verbose `**EnumerateFormLinks`**, harvester-ish **Globals** / **CurveTables** / **GameSettings** (see husbandry section above).
- `--inspect-outpost-husbandry-cells` — tier **PackIn** → **CELL** → placed; `**OutpostBuilderOrganic*`** **Container** **keywords** + **VMAD** (`**OutpostHarvesterFaunaScript`** / `**FloraScript**`, `**FaunaCreation**` list count) (see husbandry section above).
- `--inspect-pen-herd-planets` — `**PlanetBiome.Fauna**` → `**INpcSpawn**` (`**Npc**` / expanded `**LeveledNpc**`) → strict herd keyword pass + **Coverage** line + **Race bridge** heuristic (shared `**Race`** between planet fauna `**Npc**` and herd-tagged `**Npc**`); see husbandry bullet above.
- `--inspect-pen-fauna-script-trace` — `**OutpostHarvesterFaunaScript**` container VMAD → `**SQ_Parent**`, faction, `**HandScannerTarget**` **ActorValueInformation**, empty quest shell + quest VMAD dump (see pen script trace bullet above).
- `**tools/extract_misc_ba2_script.py`** — extract one `**scripts/*.pex**` from `**Starfield - Misc.ba2**` (see compiled Papyrus bullet above).
- `**tools/decompile_misc_pex.sh**` — batch extract + Champollion for preset or positional script names (see husbandry doc).
- `**tools/dump_outpost_husbandry_pex_strings.py**` — extract the three vanilla harvester `**.pex**` (fauna + flora + flora planter) and print **filtered** printable strings (use `**--all`** for every ASCII run).
- **Champollion + Wine** — full `**.pex**` → `**.psc**` recipe (install, commands, flags, caveats): **PEX → PSC (Champollion + Wine)** in the husbandry section above.

