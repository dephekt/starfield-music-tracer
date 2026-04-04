# StarfieldExplore

Minimal **Mutagen.Bethesda.Starfield** probe: load `Starfield.esm` in **binary overlay** mode and print record-group counts plus a few sample rows.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Starfield `Data` folder (contains `Starfield.esm`)

## Run

```bash
export STARFIELD_DATA="$HOME/.steam/steam/steamapps/common/Starfield/Data"   # optional if this is your path
dotnet run
```

Default **ingestible** targets (BOM + gather hints): **`Chem_Craft_Amp`**, **`Aid_Craft_PenicillinX`**. Override with positional args or `STARFIELD_TARGET_EDIDS=Edid1,Edid2`.

```bash
dotnet run -- Chem_Craft_Amp
dotnet run -- --limit=0 Aid_Craft_PenicillinX    # unlimited flora / loot lines per section
```

For each craft: **COBJ** → **components + quantities** → **resource → refined misc** when applicable → **planet flora** (`Planet` → `PlanetBiome` → `PlanetFlora`, …), **survey inorganics** (`Planet` → `PlanetBiome` → linked **`IBiomeGetter`** → **`ResourceGeneration`** (often a **list** of RGD links) → **`ResourceGenerationData.Items.Resource`**; optional **`PlanetBiome.ResourceGeneration`** is usually empty), and **creature loot**. Loot is **not** outpost husbandry/greenhouse (see [research/crafting-and-resources.md](../../research/crafting-and-resources.md) and [research/outpost-organic-husbandry.md](../../research/outpost-organic-husbandry.md)).

Inspect helpers: `--inspect-cobj=EDID`, `--inspect-resource=EDID`, `--resourcegen-resource=ResInorgCommonArgon_G`, `--planet-survey=HINT`, **`--inspect-husbandry`** (FormLists → COBJ → PackIn → harvester links), **`--inspect-outpost-harvesters`** (**Transform** + referrer **VMAD** + verbose **FormLinks** + harvester-ish **Globals** / curves / game settings), **`--inspect-outpost-husbandry-cells`** (tier **PackIn** → storage **CELL** → **Persistent/Temporary** placed refs; **`OutpostBuilderOrganic*`** **Container** keywords + **VMAD** e.g. `OutpostHarvesterFaunaScript` / `OutpostHarvesterFloraScript`), **`--inspect-pen-herd-planets`** (planet fauna → **`INpcSpawn`** expansion, strict **`ActorTypeHerd*`** per planet, **Coverage** + optional **Race**-bridge heuristic vs **`FaunaCreation`** tiers; no scan state), **`--inspect-pen-fauna-script-trace`** (**`OutpostHarvesterFaunaScript`** VMAD → **`SQ_Parent`** / faction / **`HandScannerTarget`** **ActorValueInformation** + empty quest shell note), `--planetflora-misc-substr=Toxin`, `--cobjs-for-output-misc=OrgCommonToxin`, etc. (`--help`).

## Notes

- **Package version:** `Mutagen.Bethesda.Starfield` is referenced as **0.54.0-alpha.32** until a stable **0.54+** lands on NuGet.
- **Scope:** this is a **deliberately minimal probe** (single-plugin overlay). A production **StarfieldDataGen**-style tool should follow [Mutagen](https://mutagen-modding.github.io/Mutagen/) patterns: **GameEnvironment** + **load order**, **link cache** for `FormLink` resolution, and proper **Strings/BA2** setup instead of permanently avoiding display names.
- **Localized names:** avoiding `ing.Name` here sidestepped Linux `PluginListingsPathContext` behavior; see [research/pipeline-mutagen-spriggit.md](../../research/pipeline-mutagen-spriggit.md) (Linux strings caveat) for the planned proper fix.
