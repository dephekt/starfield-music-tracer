# StarfieldExplore

Minimal **Mutagen** probe: **`Mutagen.Bethesda`** (Starfield via meta package). Every command path builds a full **`GameEnvironment`** with **`WithTargetDataFolder`**, **`WithStringParameters`** (**`ApplicableArchivePathsOverride`** for Linux string/BA2 resolution), and load order from **`STARFIELD_PLUGINS_TXT`** and/or **`STARFIELD_LOAD_ORDER`**.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Starfield `Data` folder (contains `Starfield.esm`)
- **Load order (required):** set **`STARFIELD_PLUGINS_TXT`** to the full path of the game’s plugin list (**`Plugins.txt`** on disk — capital **P**; Linux is case-sensitive), **or** set **`STARFIELD_LOAD_ORDER`** to a comma-separated list of plugin filenames (e.g. `Starfield.esm,MyMod.esm`). If **both** are missing or empty, the tool exits with an error.

**Workflow (Linux / programmatic):** This project does **not** assume **Creation Kit** or **xEdit/SSEEdit**. They are Windows-heavy, click-heavy tools and are a separate effort on Linux. Prefer **StarfieldExplore** (Mutagen) and repo **scripts** for repeatable, CLI-driven record inspection; extend the tool when something is missing.

## Run

**Linux (Steam + Proton, default library layout):** see committed **`env.example.sh`** — `source` it (or copy to `env.local.sh` and edit). Typical paths:

- **`STARFIELD_DATA`:** `$HOME/.steam/steam/steamapps/common/Starfield/Data`
- **`STARFIELD_PLUGINS_TXT`:** `$HOME/.steam/steam/steamapps/compatdata/1716740/pfx/drive_c/users/steamuser/AppData/Local/Starfield/Plugins.txt`

If you use a secondary Steam library, **`compatdata` still lives under the same Steam root** as the game (often **`~/.steam/steam`**, not **`~/.local/share/Steam`**).

```bash
source tools/StarfieldExplore/env.example.sh   # from repo root, or use your env.local.sh
dotnet run --project tools/StarfieldExplore
```

Default **ingestible** targets (BOM + gather hints): **`Chem_Craft_Amp`**, **`Aid_Craft_PenicillinX`**. Override with positional args or `STARFIELD_TARGET_EDIDS=Edid1,Edid2`.

```bash
dotnet run --project tools/StarfieldExplore -- Chem_Craft_Amp
dotnet run --project tools/StarfieldExplore -- --limit=0 Aid_Craft_PenicillinX    # unlimited flora / loot lines per section
```

For each craft: **COBJ** → **components + quantities** → **resource → refined misc** when applicable → **planet flora** (`Planet` → `PlanetBiome` → `PlanetFlora`, …), **survey inorganics** (`Planet` → `PlanetBiome` → linked **`IBiomeGetter`** → **`ResourceGeneration`** (often a **list** of RGD links) → **`ResourceGenerationData.Items.Resource`**; optional **`PlanetBiome.ResourceGeneration`** is usually empty), and **creature loot**. Loot is **not** outpost husbandry/greenhouse (see [research/crafting-and-resources.md](../../research/crafting-and-resources.md) and [research/outpost-organic-husbandry.md](../../research/outpost-organic-husbandry.md)).

Inspect helpers: `--inspect-cobj=EDID`, `--inspect-resource=EDID`, `--resourcegen-resource=ResInorgCommonArgon_G`, `--planet-survey=HINT`, **`--planet-fauna=HINT`** (per-biome **`PlanetBiome.Fauna`** + unique leaf **`Npc`**), **`--planet-fauna-detail=HINT`** (same match; per leaf **`Npc`**: **`ATKR`/`WNAM`/`TraitTemplate`** + **`Npc.Components`** (**`FullNameComponent`** + **`FormLinkDataComponent`**), template chain, keywords, FormLinks, DeathItem→leaves), **`--planet-fauna-skin-table=HINT`** (TSV: those identity columns + **`ComponentFullName`** / **`FormLinkDataRaceEdids`** / **`FormLinkDataSkinEdids`** + slot + **`Skin` (WNAM)** + chain skins + **`SnapTemplate`** + **`Skin_*`**), **`--planet-fauna-loot-table=HINT`** (TSV: same identity block + **`DeathItem`** + counts + Org* hist), **`--planet-fauna-keyword-table=HINT`** (TSV: same identity block + keywords + localized names), **`--planet-fauna-extras-table=HINT`** (TSV: same identity block + Short/Long/ActivateText + **`SkinToneIndex`**, **`ObjectTemplates`**, non-**`Skin_*`** armors), **`--planet-fauna-pen-bridge=HINT`** (pen **`FaunaCreation`** keywords vs leaf fauna **Npc**s per matching planet), **`--search-edid-substring=TEXT`** (all major-record groups with EDID containing TEXT + FormLink backlinks; **`--limit`** caps rows, **`0`** = all), **`--planet-flora=HINT`** (per-biome **`PlanetBiome.Flora`** + unique **`Flora`** + resource misc yields), **`--inspect-npc=HINT`**, **`--inspect-game-environment`** (**`STARFIELD_PLUGINS_TXT`** / **`STARFIELD_LOAD_ORDER`**), **`--inspect-husbandry`** (FormLists → COBJ → PackIn → harvester links), **`--inspect-outpost-harvesters`** (**Transform** + referrer **VMAD** + verbose **FormLinks** + harvester-ish **Globals** / curves / game settings), **`--inspect-outpost-husbandry-cells`** (tier **PackIn** → storage **CELL** → **Persistent/Temporary** placed refs; **`OutpostBuilderOrganic*`** **Container** keywords + **VMAD** e.g. `OutpostHarvesterFaunaScript` / `OutpostHarvesterFloraScript`), **`--inspect-pen-fauna-tiers`** (TSV: vanilla pen **`FaunaCreation`** slots), **`--inspect-pen-herd-planets`** (planet fauna → **`INpcSpawn`** expansion, strict **`ActorTypeHerd*`** per planet, **Coverage** + optional **Race**-bridge heuristic vs **`FaunaCreation`** tiers; no scan state), **`--inspect-pen-fauna-script-trace`** (**`OutpostHarvesterFaunaScript`** VMAD → **`SQ_Parent`** / faction / **`HandScannerTarget`** **ActorValueInformation** + empty quest shell note), **`--inspect-fauna-production-index`** (every major with that script; TSV of nested VMAD FormKeys; distinct targets + FormLink backlinks; **`--limit`** caps referrers per target, **`0`** = all; harvester-hint **Globals**), `--planetflora-misc-substr=Toxin`, `--cobjs-for-output-misc=OrgCommonToxin`, etc. (`--help`).

## Notes

- **Mutagen source:** **`ProjectReference`** to **`../../vendor/Mutagen/Mutagen.Bethesda`** ( **`StringsReadParameters.ApplicableArchivePathsOverride`** for Linux string/BA2 resolution). **`vendor/Mutagen/Directory.Build.targets`** forces **net8.0** when the machine SDK has no **net9**.
- **Strings / language:** **`StringsReadParameters.TargetLanguage`** is always set (default **English**). **`TranslatedString.DefaultLanguage`** matches before **`GameEnvironment`** is built. **`NonLocalizedEncodingOverride`** uses **UTF-8** (Starfield often stores embedded strings as UTF-8). Optional **`STARFIELD_TARGET_LANGUAGE`** (Mutagen **`Language`** enum name, case-insensitive) overrides that target; invalid names fall back to English with a stderr warning. Optional **`STARFIELD_INI`**: full path to **`Starfield.ini`** when auto-discovery fails on Linux (feeds **`sResourceArchiveList`** ordering for string BA2 discovery). **`--inspect-npc`** / fauna TSV **`NpcNameLocalized`** also pull from **`ShortName`**, **`LongName`**, **`ObjectTemplates`**, **`KeywordsTemplate`**, and **`DefaultTemplate`** when the top-level **`Name`** subrecord is absent. CK **Traits** display names / species links on some **PCM** rows are mirrored in **`Npc.Components`** (**`FullNameComponent`**, **`FormLinkDataComponent`**) when **`ATKR`** / **`WNAM`** are empty; **`SkinFormComponent`** has no typed fields in Mutagen yet.
- **ESM-shaped scans:** **`session.StarfieldEsm`** is the **`Starfield.esm`** listing from the resolved load order (not a separate overlay). **`LinkCache`** is the full environment for **`FormLink`** resolution.
- **IL static analysis:** from repo root, **`./scripts/dotnet-lint.sh`** (after **`dotnet tool restore`**) runs **[`altcode.gendarme-tool`](https://www.nuget.org/packages/altcode.gendarme-tool)** on the built **`StarfieldExplore.dll`** (see **`.config/dotnet-tools.json`**). Optional **`FORMAT_CHECK=1`** runs **`dotnet format whitespace --verify-no-changes`** (may conflict with flush-left **`partial class Program`** style until EditorConfig matches).
