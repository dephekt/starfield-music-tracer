# Tooling catalog

**Status:** living index — add flags/scripts as they appear.  
**See also:** [README.md](README.md), [pipeline-mutagen-spriggit.md](pipeline-mutagen-spriggit.md), [crafting-and-resources.md](crafting-and-resources.md), [outpost-organic-husbandry.md](outpost-organic-husbandry.md).

## StarfieldExplore (`tools/StarfieldExplore`)

Run from repo: `dotnet run --project tools/StarfieldExplore` (optional `STARFIELD_DATA`). Deep dives on acquisition and husbandry are in the topic docs above; this section lists **CLI entry points**.

### Debug CLI (StarfieldExplore)

- `--planetflora-misc-substr=Toxin` — misc EditorIDs used as `PlanetFlora.Resource` (shows `OrgCommonToxin_Leaf`, etc.).
- `--planetflora-misc=OrgCommonToxin` — expect **no rows** (stackable misc is not what `PlanetFlora` references).
- `--cobjs-for-output-misc=OrgCommonToxin` — COBJs that output the stackable misc (water / fauna variants).
- `--resourcegen-resource=ResInorgCommonArgon_G` — full `**ResourceGenerationData`** scan for that `**IResourceGetter**` + biome `**ResourceGeneration**` rows + `**IPlanet**` `**EnumerateFormLinks**` referrers to those RGD FormKeys (see [crafting-and-resources.md](crafting-and-resources.md) inorganics / RGD chain).
- `--planet-survey=AltairIIPlanetData` — `**PlanetBiome**` + `**IBiomeGetter.ResourceGeneration**` → RGD → resources for matching planet(s).
- `--inspect-husbandry` — organic fauna/flora **FormLists**, builder **COBJ** BOMs, `**PackIn`** placed modules + sample `**EnumerateFormLinks**` (see [outpost-organic-husbandry.md](outpost-organic-husbandry.md)).
- `--inspect-outpost-harvesters` — harvester `**Transform**` + referrer **PackIn**/**Activator**/**Furniture**, **VMAD**, verbose `**EnumerateFormLinks`**, harvester-ish **Globals** / **CurveTables** / **GameSettings** ([outpost-organic-husbandry.md](outpost-organic-husbandry.md)).
- `--inspect-outpost-husbandry-cells` — tier **PackIn** → **CELL** → placed; `**OutpostBuilderOrganic*`** **Container** **keywords** + **VMAD** (`**OutpostHarvesterFaunaScript`** / `**FloraScript**`, `**FaunaCreation**` list count) ([outpost-organic-husbandry.md](outpost-organic-husbandry.md)).
- `--inspect-pen-herd-planets` — `**PlanetBiome.Fauna**` → `**INpcSpawn**` (`**Npc**` / expanded `**LeveledNpc**`) → strict herd keyword pass + **Coverage** line + **Race bridge** heuristic (shared `**Race`** between planet fauna `**Npc**` and herd-tagged `**Npc**`); [outpost-organic-husbandry.md](outpost-organic-husbandry.md).
- `--inspect-pen-fauna-script-trace` — `**OutpostHarvesterFaunaScript**` container VMAD → `**SQ_Parent**`, faction, `**HandScannerTarget**` **ActorValueInformation**, empty quest shell + quest VMAD dump ([outpost-organic-husbandry.md](outpost-organic-husbandry.md)).

## Python helpers (`tools/`)

- [`extract_misc_ba2_script.py`](../tools/extract_misc_ba2_script.py) — extract one `scripts/*.pex` from `Starfield - Misc.ba2`.
- [`dump_outpost_husbandry_pex_strings.py`](../tools/dump_outpost_husbandry_pex_strings.py) — extract the three vanilla harvester `**.pex**` and print filtered strings (`--all` for full ASCII runs).

## PEX → PSC (Champollion + Wine)

Full recipe (install, flags, caveats): **[outpost-organic-husbandry.md](outpost-organic-husbandry.md)** → subsection **PEX → PSC (Champollion + Wine)**.

**Batch (three vanilla harvesters)** — from repo root, after `Champollion.exe` is on path via `wine`:

```bash
mkdir -p research/decompiled/pe research/decompiled/psc
for n in outpostharvesterfaunascript.pex outpostharvesterflorascript.pex outpostharvesterfloraplanterscript.pex; do
  python3 tools/extract_misc_ba2_script.py --name "$n" -o "research/decompiled/pe/$n"
done
wine /path/to/Champollion.exe -p research/decompiled/psc \
  research/decompiled/pe/outpostharvesterfaunascript.pex \
  research/decompiled/pe/outpostharvesterflorascript.pex \
  research/decompiled/pe/outpostharvesterfloraplanterscript.pex
```

(`research/decompiled/` is gitignored; behavior notes: **[outpost-papyrus-notes.md](outpost-papyrus-notes.md)**.)
