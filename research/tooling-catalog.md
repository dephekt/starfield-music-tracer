# Tooling catalog

**Status:** living index — add flags/scripts as they appear.  
**See also:** [README.md](README.md), [pipeline-mutagen-spriggit.md](pipeline-mutagen-spriggit.md), [crafting-and-resources.md](crafting-and-resources.md), [outpost-organic-husbandry.md](outpost-organic-husbandry.md).

## StarfieldExplore (`tools/StarfieldExplore`)

Run from repo: `dotnet run --project tools/StarfieldExplore` (optional `STARFIELD_DATA`; see **`tools/StarfieldExplore/env.example.sh`** for Linux Steam + Proton defaults). **Required:** **`STARFIELD_PLUGINS_TXT`** (full path to **`Plugins.txt`** — capital **P**, case-sensitive on Linux) **or** **`STARFIELD_LOAD_ORDER`** (comma-separated plugin filenames) so **`GameEnvironment`** + string BA2 resolution match the game. Optional **`STARFIELD_TARGET_LANGUAGE`**: Mutagen **`Language`** enum name for string resolution. Deep dives on acquisition and husbandry are in the topic docs above; this section lists **CLI entry points**.

**CK / xEdit:** Not assumed in this workflow (Linux-friendly, programmatic inspection). When docs elsewhere mention Creation Kit or xEdit for “open the row,” treat that as optional; prefer extending **StarfieldExplore** or adding scripts so results stay reproducible from the shell.

### Debug CLI (StarfieldExplore)

- `--planetflora-misc-substr=Toxin` — misc EditorIDs used as `PlanetFlora.Resource` (shows `OrgCommonToxin_Leaf`, etc.).
- `--planetflora-misc=OrgCommonToxin` — expect **no rows** (stackable misc is not what `PlanetFlora` references).
- `--cobjs-for-output-misc=OrgCommonToxin` — COBJs that output the stackable misc (water / fauna variants).
- `--resourcegen-resource=ResInorgCommonArgon_G` — full `**ResourceGenerationData`** scan for that `**IResourceGetter**` + biome `**ResourceGeneration**` rows + `**IPlanet**` `**EnumerateFormLinks**` referrers to those RGD FormKeys (see [crafting-and-resources.md](crafting-and-resources.md) inorganics / RGD chain).
- `--planet-survey=AltairIIPlanetData` — `**PlanetBiome**` + `**IBiomeGetter.ResourceGeneration**` → RGD → resources for matching planet(s).
- `--planet-fauna=Serpentis` — same planet matching hint; `**PlanetBiome.Fauna**` per biome (direct `**Npc**` + expanded `**LeveledNpc**`) and a **unique leaf Npc** summary (`--limit` caps the summary; `--limit=0` = full list).
- `--planet-fauna-detail=Serpentis` — same planet hint; for each **distinct leaf** fauna `**Npc**`: **`AttackRace` (`ATKR`)**, **`Skin` (`WNAM`)**, **`TemplateActors.TraitTemplate`**, **`Npc.Components`** (**`FullNameComponent`** FULL + **`FormLinkDataComponent`** race/armor links — often aligns with CK **Traits** when `ATKR`/`WNAM` are null), RNAM/CCT note, `**DefaultTemplate**` chain, keywords, capped `**EnumerateFormLinks**`, `**DeathItem**` → leaves (`--limit` caps Npcs detailed; `0` = all).
- `--planet-fauna-skin-table=SerpentisIV` — same planet hint; **TSV**: **`AttackRaceEdid`**, **`TraitTemplateEdid`**, **`ComponentFullName`**, **`FormLinkDataRaceEdids`**, **`FormLinkDataSkinEdids`**, **`NpcNameLocalized`**, slot, **`Npc.Skin` (WNAM)**, chain skins, **`SnapTemplate`**, `**Skin_*`** from **`EnumerateFormLinks`** (`--limit` caps rows; `0` = all).
- `--planet-fauna-loot-table=SerpentisIV` — same planet hint; **TSV**: **`AttackRaceEdid`**, **`SkinWnamEdid`**, **`TraitTemplateEdid`**, **`ComponentFullName`**, **`FormLinkDataRaceEdids`**, **`FormLinkDataSkinEdids`**, **`NpcNameLocalized`**, **`DeathItem`** root, leaf totals, **`OrganicMiscFamilyHist`**.
- `--planet-fauna-keyword-table=SerpentisIV` — same planet hint; **TSV**: record-level **`AttackRace` / `Skin` / `TraitTemplate`** + **`ComponentFullName` / `FormLinkData*`** + **`KeywordsSorted`** + localized keyword names when present.
- `--planet-fauna-extras-table=SerpentisIV` — same planet hint; **TSV**: record-level identity + **`ComponentFullName` / `FormLinkData*`** + **`NpcNameLocalized`**, Short/Long/ActivateText, **`SkinToneIndex`**, **`ObjectTemplatesCompact`**, **`NonSkinArmorFormLinksEdids`**.
- `--search-edid-substring=Skin_Octopede` — scans every enumerable major-record group on `Starfield.esm` for EditorID substring; prints hits then **FormLink backlinks** (full mod scan; up to 30 referrers per hit; `--limit` caps hit rows, `0` = all).
- `--planet-flora=SerpentisIVPlanetData` — `**PlanetBiome.Flora**` per biome (`**Flora**` + `**Resource**` misc yield + frequency) and a **unique Flora** summary (localized names when strings resolve).
- `--inspect-npc=PCM_Serpentis_Serpentis-IV_Predator01` — `**Npc**` names, **`Race` (RNAM)**, **`AttackRace` (ATKR)**, **`Skin` (WNAM)**, **`TraitTemplate`**, **`DefaultTemplate`** chain, **Keywords**, **DeathItem**; substring match on EditorID or FormKey fragment.
- `--inspect-game-environment` — prints the same **`GameEnvironment`** as every other command (plugin list, link cache type, effective language, **`LoadOrderFilePath`** when plugins path is set, sample **`Chem_Craft_Amp`** localized name). Requires **`STARFIELD_PLUGINS_TXT`** or **`STARFIELD_LOAD_ORDER`** like all runs; uses **`PluginListingsPathInjection`** + **`WithResolver`** when **`STARFIELD_PLUGINS_TXT`** is set (see **`vendor/Mutagen/.../PluginListingsPathContext.cs`**).
- `--inspect-husbandry` — organic fauna/flora **FormLists**, builder **COBJ** BOMs, `**PackIn`** placed modules + sample `**EnumerateFormLinks**` (see [outpost-organic-husbandry.md](outpost-organic-husbandry.md)).
- `--inspect-outpost-harvesters` — harvester `**Transform**` + referrer **PackIn**/**Activator**/**Furniture**, **VMAD**, verbose `**EnumerateFormLinks`**, harvester-ish **Globals** / **CurveTables** / **GameSettings** ([outpost-organic-husbandry.md](outpost-organic-husbandry.md)).
- `--inspect-outpost-husbandry-cells` — tier **PackIn** → **CELL** → placed; `**OutpostBuilderOrganic*`** **Container** **keywords** + **VMAD** (`**OutpostHarvesterFaunaScript`** / `**FloraScript**`, `**FaunaCreation**` list count) ([outpost-organic-husbandry.md](outpost-organic-husbandry.md)).
- `--inspect-pen-herd-planets` — `**PlanetBiome.Fauna**` → `**INpcSpawn**` (`**Npc**` / expanded `**LeveledNpc**`) → strict herd keyword pass + **Coverage** line + **Race bridge** heuristic (shared `**Race`** between planet fauna `**Npc**` and herd-tagged `**Npc**`); [outpost-organic-husbandry.md](outpost-organic-husbandry.md).
- `--inspect-pen-fauna-tiers` — **TSV**: `**OutpostBuilderOrganicFauna01`…`03**` → VMAD `**FaunaCreation**` (`CreatureKeyword`, `createCount`) per slot ([outpost-organic-husbandry.md](outpost-organic-husbandry.md)).
- `--planet-fauna-pen-bridge=HINT` — planets matching hint: leaf fauna **Npc** count vs each tier’s `**FaunaCreation**` keyword set (static join; not scan state) ([outpost-organic-husbandry.md](outpost-organic-husbandry.md)).
- `--inspect-pen-fauna-script-trace` — `**OutpostHarvesterFaunaScript**` container VMAD → `**SQ_Parent**`, faction, `**HandScannerTarget**` **ActorValueInformation**, empty quest shell + quest VMAD dump ([outpost-organic-husbandry.md](outpost-organic-husbandry.md)).

## Python helpers (`tools/`)

- [`extract_misc_ba2_script.py`](../tools/extract_misc_ba2_script.py) — extract one `scripts/*.pex` from `Starfield - Misc.ba2`.
- [`decompile_misc_pex.sh`](../tools/decompile_misc_pex.sh) — batch: extract named `scripts/*.pex` from Misc.ba2 into **`research/decompiled/pe/`**, then Champollion → **`research/decompiled/psc/`** (`--preset organic-research` | `minimal`, or positional basenames; **`PEX_OUT`** / **`PSC_OUT`** override dirs).
- [`starfield_misc_ba2.py`](../tools/starfield_misc_ba2.py) — `iter_misc_ba2_entries` / `extract_named_file` for BA2 research.
- [`misc_ba2_grep.py`](../tools/misc_ba2_grep.py) — list archive paths whose name or payload contains a substring (e.g. `OrganicResource`, `SetScanned`, `--suffix .pex`).
- [`dump_outpost_husbandry_pex_strings.py`](../tools/dump_outpost_husbandry_pex_strings.py) — extract the three vanilla harvester `**.pex**` and print filtered strings (`--only fauna` / `flora` / `planter` / `all`; `--all` for full ASCII runs).

## PEX → PSC (Champollion + Wine)

Full recipe (install, flags, caveats): **[outpost-organic-husbandry.md](outpost-organic-husbandry.md)** → subsection **PEX → PSC (Champollion + Wine)**.

**Batch (default: organic-research preset)** — from repo root; Champollion on **`PATH`** as **`champollion`** or via **`CHAMPOLLION_EXE`** / XDG default (see husbandry doc):

```bash
./tools/decompile_misc_pex.sh --preset organic-research
```

**Manual equivalent** (harvester trio only): `mkdir -p research/decompiled/pe research/decompiled/psc`, loop **`extract_misc_ba2_script.py`**, then **`wine "$CHAMP" -p research/decompiled/psc`** with the three **`.pex`** paths.

(`research/decompiled/` is gitignored; behavior notes: **[outpost-papyrus-notes.md](outpost-papyrus-notes.md)**.)
