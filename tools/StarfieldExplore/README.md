# StarfieldExplore

Minimal **Mutagen** probe: **`Mutagen.Bethesda`** (Starfield via meta package). Every command path builds a full **`GameEnvironment`** with **`WithTargetDataFolder`**, **`WithStringParameters`** (**`ApplicableArchivePathsOverride`** for Linux string/BA2 resolution), and load order from **`STARFIELD_PLUGINS_TXT`** and/or **`STARFIELD_LOAD_ORDER`**.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Starfield `Data` folder (contains `Starfield.esm`)
- **Load order (required):** set **`STARFIELD_PLUGINS_TXT`** to the full path of your enabled **`plugins.txt`**, **or** set **`STARFIELD_LOAD_ORDER`** to a comma-separated list of plugin filenames (e.g. `Starfield.esm,MyMod.esm`). If **both** are missing or empty, the tool exits with an error.

## Run

```bash
export STARFIELD_DATA="$HOME/.steam/steam/steamapps/common/Starfield/Data"   # optional if this is your path
export STARFIELD_PLUGINS_TXT="$HOME/.local/share/Steam/steamapps/compatdata/1716740/pfx/drive_c/users/steamuser/AppData/Local/Starfield/plugins.txt"   # example — use your real path
dotnet run
```

Default **ingestible** targets (BOM + gather hints): **`Chem_Craft_Amp`**, **`Aid_Craft_PenicillinX`**. Override with positional args or `STARFIELD_TARGET_EDIDS=Edid1,Edid2`.

```bash
dotnet run -- Chem_Craft_Amp
dotnet run -- --limit=0 Aid_Craft_PenicillinX    # unlimited flora / loot lines per section
```

For each craft: **COBJ** → **components + quantities** → **resource → refined misc** when applicable → **planet flora** (`Planet` → `PlanetBiome` → `PlanetFlora`, …), **survey inorganics** (`Planet` → `PlanetBiome` → linked **`IBiomeGetter`** → **`ResourceGeneration`** (often a **list** of RGD links) → **`ResourceGenerationData.Items.Resource`**; optional **`PlanetBiome.ResourceGeneration`** is usually empty), and **creature loot**. Loot is **not** outpost husbandry/greenhouse (see [research/crafting-and-resources.md](../../research/crafting-and-resources.md) and [research/outpost-organic-husbandry.md](../../research/outpost-organic-husbandry.md)).

Inspect helpers: `--inspect-cobj=EDID`, `--inspect-resource=EDID`, `--resourcegen-resource=ResInorgCommonArgon_G`, `--planet-survey=HINT`, **`--planet-fauna=HINT`** (per-biome **`PlanetBiome.Fauna`** + unique leaf **`Npc`**), **`--inspect-npc=HINT`**, **`--inspect-game-environment`** (**`STARFIELD_PLUGINS_TXT`** / **`STARFIELD_LOAD_ORDER`**), **`--inspect-husbandry`** (FormLists → COBJ → PackIn → harvester links), **`--inspect-outpost-harvesters`** (**Transform** + referrer **VMAD** + verbose **FormLinks** + harvester-ish **Globals** / curves / game settings), **`--inspect-outpost-husbandry-cells`** (tier **PackIn** → storage **CELL** → **Persistent/Temporary** placed refs; **`OutpostBuilderOrganic*`** **Container** keywords + **VMAD** e.g. `OutpostHarvesterFaunaScript` / `OutpostHarvesterFloraScript`), **`--inspect-pen-herd-planets`** (planet fauna → **`INpcSpawn`** expansion, strict **`ActorTypeHerd*`** per planet, **Coverage** + optional **Race**-bridge heuristic vs **`FaunaCreation`** tiers; no scan state), **`--inspect-pen-fauna-script-trace`** (**`OutpostHarvesterFaunaScript`** VMAD → **`SQ_Parent`** / faction / **`HandScannerTarget`** **ActorValueInformation** + empty quest shell note), `--planetflora-misc-substr=Toxin`, `--cobjs-for-output-misc=OrgCommonToxin`, etc. (`--help`).

## Notes

- **Mutagen source:** **`ProjectReference`** to **`../../vendor/Mutagen/Mutagen.Bethesda`** ( **`StringsReadParameters.ApplicableArchivePathsOverride`** for Linux string/BA2 resolution). **`vendor/Mutagen/Directory.Build.targets`** forces **net8.0** when the machine SDK has no **net9**.
- **Strings / language:** optional **`STARFIELD_TARGET_LANGUAGE`** (Mutagen **`Language`** enum name, case-insensitive). When set, **`StringsReadParameters.TargetLanguage`** and **`TranslatedString.DefaultLanguage`** follow that value; invalid names fall back to English with a stderr warning. If a language is set but the matching BA2/strings are missing, names may be empty until the export pipeline adds hard checks.
- **ESM-shaped scans:** **`session.StarfieldEsm`** is the **`Starfield.esm`** listing from the resolved load order (not a separate overlay). **`LinkCache`** is the full environment for **`FormLink`** resolution.
