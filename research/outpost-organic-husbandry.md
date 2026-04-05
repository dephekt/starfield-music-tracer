# Outpost organic husbandry (research)

**Status:** active — supports accurate organic sourcing; full pen logic partly in compiled Papyrus.  
**See also:** [crafting-and-resources.md](crafting-and-resources.md), [tooling-catalog.md](tooling-catalog.md), [product-vision.md](product-vision.md).

---

### Three questions (keep them separate)

When reasoning about **fauna pens** vs **planet data**, treat these as **independent** layers:

| | Question | Typical data source | StarfieldExplore |
|---|----------|---------------------|------------------|
| **A** | What does each **pen tier** allow (slots / herd keywords / counts)? | **`OutpostBuilderOrganicFauna01`…`03`** **`IContainerGetter`** VMAD → **`OutpostHarvesterFaunaScript`** → **`FaunaCreation`** (`CreatureKeyword`, `createCount`) | **`--inspect-pen-fauna-tiers`** (TSV). Verbose: **`--inspect-outpost-husbandry-cells`**, **`--inspect-pen-fauna-script-trace`** |
| **B** | What **creatures** does the **planet** list under **`PlanetBiome.Fauna`**? | **`Planet` → `PlanetBiome.Fauna`** → **`INpcSpawn`** (Npc / expanded LeveledNpc) → leaf **`Npc`** | **`--planet-fauna`**, **`--planet-fauna-detail`**, **`--planet-fauna-keyword-table`**, etc. |
| **C** | What does the pen **produce** (misc/resource outputs, terminal list)? | Harvester **Transform**, container **defaults**, **LeveledItem** / script APIs — often **not** the same graph as **A** or **B** | Partially **`--inspect-outpost-harvesters`**; full behavior in **`.pex`** (see Papyrus notes) |

**Joining A ↔ B:** **`--planet-fauna-pen-bridge=HINT`** counts, per planet matching the hint, how many **leaf fauna Npcs** inherit any **`FaunaCreation`** **`CreatureKeyword`** for each vanilla tier (same keyword inheritance rules as **`--inspect-pen-herd-planets`**: Npc / Race / KeywordsTemplate / DefaultTemplate). This is a **static ESM join**, not a guarantee of in-game pen fill (scan state, script filters, progression).

### “Buildable here” / COBJ (deferred)

**Workshop Recipe Manager** **COBJ** rows (e.g. **`co_Outpost_Builder_OrganicFauna01`…`03`**) define **global** build requirements (**Zoology** rank, materials, **Created Object** → **PackIn**). They usually do **not** encode “this recipe only on planet X.” **Match Conditions** empty on those COBJs pushes any location gating to **other records** or **runtime script**. Treat **menu visibility** as a separate track from **A/B** above; see [outpost-papyrus-notes.md](outpost-papyrus-notes.md) for scan / **`SQ_Parent`**-adjacent behavior.

---

### Creature loot vs outpost husbandry

- `**StarfieldExplore`** indexes **creature loot** as `**INpcGetter.DeathItem`** → `**ILeveledItemGetter**` (recursive `**Entries**` / `**Reference**`) → leaf `**IMiscItemGetter` / `IIngestibleGetter` / `IResourceGetter**` FormKeys. That matches “drops when killed / looted,” including multiple different ingredients from the same creature.
- **Outpost organic fauna pen / greenhouse (vanilla plugin shape):**
  - `**OutpostBuilderOrganic_FaunaList`** and `**OutpostBuilderOrganic_FloraList**` are `**FormList**` records whose **items are tier `ConstructibleObject` recipes** (`co_Outpost_Builder_OrganicFauna01`…`03`, `co_Outpost_BuilderOrganicFlora01`…`03`), **not** a whitelist of `**NPC_`** or `**Flora**` species.
  - `**co_Outpost_Builder_OrganicFauna**` / `**co_Outpost_BuilderOrganicFlora**` (parent recipes) have `**CreatedObject**` → those same `**FormList**` entries (builder UI tier unlock pattern).
  - Tier `**CreatedObject**` for each pen/greenhouse is a `**PackIn**` (e.g. `**OutpostPI_BuilderOrganicFauna01**`) whose `**EnumerateFormLinks**` surface `**Transform**` rows like `**Outpost_HarvesterFauna01**` / `**Outpost_HarvesterFlora01**` and internal cells — **creature/plant eligibility and per-slot outputs** are likely **scripts / VMAD / non-NPC FormLists**, not the misnamed `_FaunaList` / `_FloraList`.
  - `**IFurniture.EditorID`** is often **empty** when read through `**CreateFromBinaryOverlay`** here, so filtering the **Furniture** group by EditorID is unreliable; **COBJ → `PackIn`** is the practical entry point.
- `**--inspect-husbandry**` dumps the above chain. **`--inspect-pen-fauna-tiers`** prints **TSV** **FaunaCreation** slots per **`OutpostBuilderOrganicFauna01`…`03`**; **`--planet-fauna-pen-bridge=HINT`** joins those keywords to each matching planet’s leaf fauna **Npc**s. `**--inspect-outpost-harvesters**` lists harvester `**Transform**` rows, scans **PackIn** / **Activator** / **Furniture** for `**EnumerateFormLinks`** backlinks to each Transform, prints `**VirtualMachineAdapter**` (scripts + **ScriptObjectProperty** targets + nested links on properties) and a **verbose** link list (resolved `**DescribeComponent`**, null slots, unparsed items), then **Globals** / **CurveTables** / **GameSettings** harvester-ish hits. Vanilla organic **tier PackIns** use `**OutpostPackinDummyScript`** only; real logic is **not** on that VMAD.
- `**--inspect-outpost-husbandry-cells`** follows `**OutpostPI_BuilderOrganicFauna01`…`03**` and `**OutpostPI_BuilderOrganicFlora01`…`03**`: `**EnumerateFormLinks**` → `**ICellGetter**` storage cell → `**Persistent` / `Temporary**` `**IPlacedObjectGetter**` (geometry + interactables), then a consolidated dump of `**OutpostBuilderOrganicFauna*` / `OutpostBuilderOrganicFlora***` `**IContainerGetter**` records: **Keywords** (e.g. `**ResourceTypeOrganic`**, `**CrewFurniture_Zoology**` / `**_Botany**`), `**VirtualMachineAdapter**` with `**OutpostHarvesterFaunaScript**` / `**OutpostHarvesterFloraScript**`, **ScriptObjectProperty** links (`**SQ_Parent`**, factions, outpost keywords). `**DumpVirtualMachineAdapter**` expands any `**ScriptStructListProperty**` (including `**FaunaCreation**`) into `**ScriptEntryStructs**`: each slot has `**Members**` such as `**CreatureKeyword**` (`**IKeywordGetter**`, vanilla pens use `**ActorTypeHerdLarge**` / `**Medium**` / `**Small**`) and `**createCount**` (int). That is **herd-size tiering**, not a per-species toxin list. `**PlanetBiome.Fauna`:** Mutagen types rows as `**IFormLinkGetter<INpcGetter>`**, but `**INpcSpawnGetter**` is implemented by both `**Npc**` and `**LeveledNpc**` — `**--inspect-pen-herd-planets**` resolves each `**FormKey**` as `**INpcSpawn**` and expands `**LeveledNpc**` recursively. On vanilla `**Starfield.esm**`, sampled data had **all** top-level fauna links resolving as `**Npc`** (no top-level `**LeveledNpc**` in that count). **Strict join:** herd `**ActorTypeHerd*`** on those resolved NPCs (plus `**Race**`, `**KeywordsTemplate**`, `**DefaultTemplate**`) still yields **empty** planet∩tier for vanilla — listed creatures are not the same records that carry herd keywords. **Race bridge (heuristic):** union tiers from herd-tagged NPCs onto planets whose fauna includes **any NPC with the same `Race` FormKey** — can be non-empty but does not guarantee script-accurate pen behavior. The inspect prints Coverage, strict counts, race-bridge stats, and samples. `**Default.Items`** on those containers is typically **null in binary overlay**. **Further:** script yields, `**HandScannerTarget`**, biome condition functions, fuller load order.
- **Pen script VMAD trace (`--inspect-pen-fauna-script-trace`):** On `**OutpostBuilderOrganicFauna01`…`03`**, `**OutpostHarvesterFaunaScript**` exposes `**FaunaCreation**` (herd `**CreatureKeyword**` + `**createCount**`), `**OutpostFaunaFaction**`, `**OutpostLinkCreatedActor` / `OutpostLinkCreatedActorTarget**` keywords, `**OutpostFauna**` → `**IQuestGetter**` form `**07092C**` with EditorID `**SQ_Parent**`, and `**HandScannerTarget**`. The latter is **not** an NPC: it resolves to `**ActorValueInformation`** `**HandScannerTarget**` (scanner-related **actor value**). The `**SQ_Parent`** row in `**Starfield.esm**` is an **empty quest shell** (no stages/objectives/quest VMAD properties in overlay) — runtime fauna pen logic lives in **compiled Papyrus** and **save-backed** quest state, not in a second planet table next to `**PlanetBiome.Fauna`**.
- **Compiled Papyrus (authoritative husbandry/greenhouse logic):** Game scripts are **not** in the ESM. Vanilla harvester scripts in `**Starfield - Misc.ba2`**: `**scripts/outpostharvesterfaunascript.pex**`, `**scripts/outpostharvesterflorascript.pex**`, `**scripts/outpostharvesterfloraplanterscript.pex**`. `**tools/extract_misc_ba2_script.py**` extracts one `**.pex**` by name; `**tools/decompile_misc_pex.sh**` batches extract + Champollion (`**--preset organic-research**` adds **`sq_parentscript`**, **`planettraitscantargetscript`**, **`outpostcontainerscript`** for scan/container follow-on research; **`minimal**` is harvesters + **`sq_parent`** only). `**tools/dump_outpost_husbandry_pex_strings.py**` pulls all three harvesters and prints **filtered** printable strings (`**--all`** = full ASCII runs). A `**strings**`-style pass on the fauna `**.pex**` surfaces identifiers such as `**GetActorBaseForResource**`, `**SetScanned**`, `**HasKeyword**`, `**OrganicResourceAV**`, `**HandScannerTarget**`, `**FaunaCreationData**`, `**ResourceGlobalData**`; flora scripts surface `**GetFloraForResource**`, `**CreateFlora**`, `**OutpostLinkFloraPlanter**`, planter `**FloraNodeMax**` / scale fields, etc. For readable `**.psc**` source, use **[Champollion](https://github.com/Orvid/Champollion)** on Linux via **Wine** — see **PEX → PSC (Champollion + Wine)** below; then trace callees (native / other scripts) the same way as for ESM-backed data.
- **Pen / greenhouse terminal + hand scanner (product scope for data tools):** In play, the **module terminal** lists products tied to **fauna (or flora) on that planet**, and only after the player has **fully scanned** relevant species (typical rule). Separately, the **hand scanner** UI on wild creatures already surfaces **what they drop** and **that those organics can be produced at an outpost** — so the game’s notion of “this species ↔ this organic output ↔ workshop” is **not** expressed only through the pen’s native picker; some **shared native layer** (scanner / AV / conditions / strings) drives both surfaces. We have **not** located that layer in plugins or Misc Papyrus. For **ESM-only / planner tooling**, treat **full scan** as a **player-known unlock rule** — **do not** try to read scan state from plugins. Focus indexes on **planet-level eligibility** (what organic outputs the world supports on **this** planet) and **reverse queries** (which planets support a given resource), by joining script templates (`**FaunaCreation`**, etc.) with **planet → fauna / loot / flora** graphs already in scope for gather traces; use in-game scanner text as a **cross-check** that those graphs match what Bethesda shows players.

### PEX → PSC (Champollion + Wine)

**Fauna pen logic (when plugins are not enough):** start with [outpost-papyrus-notes.md](outpost-papyrus-notes.md) → **Working the fauna pen — use this script first** (`OutpostHarvesterFaunaScript`). Quick strings: `tools/dump_outpost_husbandry_pex_strings.py --only fauna`.

Research workflow to turn vanilla `**.pex**` into decompiled `**.psc**` when you need full control flow and identifiers beyond `strings` / `dump_outpost_husbandry_pex_strings.py`.

**Prerequisites**

- **[Champollion v1.3.2](https://github.com/Orvid/Champollion/releases/download/v1.3.2/Champollion.v1.3.2.zip)** — zip contains **`Champollion.exe`** only.
- **Wine** — on Ubuntu, `sudo apt install wine64` provides **`/usr/bin/wine`** (a `wine64` standalone binary may not appear on `PATH`; invoking `wine` is enough). First run initializes `~/.wine` and may log benign OLE/RpcSs noise.

**Where to put Champollion (recommended)**

The **[XDG base directory](https://specifications.freedesktop.org/basedir-spec/basedir-spec-latest.html)** layout keeps third-party app files out of a cluttered **`$HOME`** root. **`~/.local/share`** is the usual place for **user-specific application data and static payloads** (as opposed to **`~/.config`** for configuration or **`~/.local/state`** for mutable state).

| Layout | Path | Why |
|--------|------|-----|
| **XDG default (used in examples below)** | **`~/.local/share/champollion/Champollion-1.3.2/Champollion.exe`** | Under **`$XDG_DATA_HOME`** (defaults to **`~/.local/share`**). Versioned subfolder for upgrades. Unzip so **`Champollion.exe`** is directly inside **`Champollion-1.3.2/`**. |
| **Optional env override** | **`CHAMPOLLION_EXE`** | e.g. `export CHAMPOLLION_EXE="$HOME/.local/share/champollion/Champollion-1.3.2/Champollion.exe"` |
| **`PATH` wrapper (optional)** | **`~/.local/bin/champollion`** | Runs **`wine`** on the install above; same **`CHAMPOLLION_EXE`** override. Ensure **`~/.local/bin`** is on your **`PATH`** (common on Ubuntu/Fedora login shells). |
| **Flat dev bucket** | **`~/tools/Champollion-1.3.2/Champollion.exe`** | Fine if you already keep CLIs in **`~/tools`**; same idea, less XDG-pure. |
| **Pseudo-opt under `~/.local`** | **`~/.local/opt/champollion-1.3.2/Champollion.exe`** | Common convention (not in the XDG spec) for “optional packages” in your home tree. |
| **Repo-adjacent** | **`tools/champollion/Champollion.exe`** | **`tools/champollion/`** is **gitignored** here; do not commit the **`.exe`**. |

Avoid **`/tmp`** — cleared on reboot. System-wide **`/opt/...`** is fine with **`sudo`** if you prefer one install for all users.

**One-time install (XDG layout):** unzip the release so **`Champollion.exe`** ends up at **`~/.local/share/champollion/Champollion-1.3.2/Champollion.exe`** (create the versioned directory first; if the zip drops the **`.exe`** at the archive root, use **`unzip … -d ~/.local/share/champollion/Champollion-1.3.2`**). A wrapper **`~/.local/bin/champollion`** (executable, runs **`wine`** on that **`.exe`**) can be added so you can run **`champollion -p …`** without typing **`wine`** each time.

**Batch extract + decompile (repo script)**

From repo root, with **`STARFIELD_DATA`** set if your **`Data`** folder is not the default Linux Steam path:

```bash
./tools/decompile_misc_pex.sh --preset organic-research   # default when you pass no args
./tools/decompile_misc_pex.sh --preset minimal             # harvesters + sq_parent only
./tools/decompile_misc_pex.sh someotherscript.pex          # ad-hoc basename(s)
```

Output defaults to **`research/decompiled/pe/`** and **`research/decompiled/psc/`** (override with **`PEX_OUT`** / **`PSC_OUT`**). See [`tools/decompile_misc_pex.sh`](../tools/decompile_misc_pex.sh) **`--help`**.

**Extract `.pex` from `Starfield - Misc.ba2` (single file)**

From repo root, with `STARFIELD_DATA` set or defaulting to the Linux Steam `Data` folder (see [`tools/extract_misc_ba2_script.py`](../tools/extract_misc_ba2_script.py)):

```bash
python3 tools/extract_misc_ba2_script.py \
  --name outpostharvesterfaunascript.pex \
  -o /tmp/outpostharvesterfaunascript.pex
```

Use `--name` with the archive path (`scripts/...`) or a basename; the helper normalizes to `scripts/`.

**Decompile with Champollion**

```bash
# Either call Wine explicitly:
CHAMP="${CHAMPOLLION_EXE:-$HOME/.local/share/champollion/Champollion-1.3.2/Champollion.exe}"
wine "$CHAMP" -p research/decompiled/psc research/decompiled/pe/outpostharvesterfaunascript.pex

# Or, if ~/.local/bin/champollion exists and is on PATH:
# champollion -p research/decompiled/psc research/decompiled/pe/outpostharvesterfaunascript.pex
```

(Adjust paths if you installed elsewhere; ensure **`research/decompiled/pe/`** and **`research/decompiled/psc/`** exist, or use other output dirs.)

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

### Decompiled behavior summary

After PEX → PSC, see **[outpost-papyrus-notes.md](outpost-papyrus-notes.md)** for line-level behavior: **`SetScanned(True)`** on spawned pen flora/fauna (not a player-scan gate in these files), **`GetActorBaseForResource` / `GetFloraForResource`**, herd sizing from **`FaunaCreation`**, and what is still **not** in script (planet eligibility, terminal product list).
