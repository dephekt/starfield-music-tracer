# Data model (crafting explorer) — draft

**Status:** stub — fill as export/schema work proceeds.  
**See also:** [product-vision.md](product-vision.md), [crafting-and-resources.md](crafting-and-resources.md).

Map **app entities** to **sources of truth** in plugins, BA2 scripts, or heuristics. Extend rows only when a feature needs them.

| Entity / concept | Intended source (vanilla) | Notes |
|------------------|---------------------------|--------|
| Craftable output | `ConstructibleObject` → `CreatedObject`; workbench via keyword on COBJ | Ingestible / misc / etc. |
| Recipe line (ingredient) | `ConstructibleObject.ConstructableComponents` → `IResourceGetter` / nested COBJ | Quantities from ESM |
| Refined stackable misc | `co_Resource_*` COBJ chains from `Resource.Produce` | e.g. `OrgCommonToxin` |
| Organic world sources | `Planet` → `PlanetBiome` → `PlanetFlora` / `PlanetBiome.Fauna`; part-misc prefix match | Named **Flora** / **Npc** rows per planet |
| Inorganic survey-style | `Biome.ResourceGeneration` → RGD → `IResourceGetter` | Not `IPlanetBiome.ResourceGeneration` alone |
| Creature loot | `Npc.DeathItem` → leveled lists → leaf items | Multiple drops per species |
| Vendor / merchant | TBD (editor IDs, factions, or dedicated records) | danplan placeholder |
| Outpost organic production | Scripts in `Starfield - Misc.ba2`; VMAD on containers | See [outpost-organic-husbandry.md](outpost-organic-husbandry.md) |
| Pen herd size tier | VMAD `FaunaCreation` struct list on container | `createCount` + `CreatureKeyword`; matches `HasKeyword` loop in fauna script ([outpost-papyrus-notes.md](outpost-papyrus-notes.md)) |
| Organic AV → fauna ActorBase | Native `GetActorBaseForResource(OrganicResourceAV)` | Not an ESM field in these scripts; mapping is engine/TBD |
| Organic AV → Flora form | Native `GetFloraForResource(OrganicResourceAV)` | Same |
| Scanner flags on pen spawns | `SetScanned(True)` + `HandScannerTarget` on placed refs | Applied **after** spawn; does **not** encode “player scanned wild species” in these three scripts |
| Workshop pen / greenhouse picker | Presumed **native workshop UI** calling **`OnBuilderMenuSelect(ActorValue)`** on harvesters; **`OutpostContainerScript`** = **ContainerMenu** only (not organic picker) | Misc.ba2 survey: [outpost-papyrus-notes.md](outpost-papyrus-notes.md) |
| Hand scanner UI (wild fauna) | **Observation:** overlay shows **loot/resource**, **biome**, etc., and **explicitly** states that the resource can be **produced at an outpost** — same product knowledge surfaced **outside** the pen terminal | Implementation unknown (likely **native** scanner / **ActorValueInformation** / condition graph); treat as a **second UI surface** tied to the same eligibility story, not “terminal-only” data |
| Planet scan / zoology harvest counters | **`SQ_ParentScript`** (`OnPlayerScannedObject`, scan count AVs, `HarvestActor`, …) | Decompile `sq_parentscript.pex` next for wild-scan gates |
| Display names | Localization BA2 / strings; Mutagen `TranslatedString` on Linux needs path setup | [pipeline-mutagen-spriggit.md](pipeline-mutagen-spriggit.md) |
