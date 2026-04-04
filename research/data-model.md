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
| Display names | Localization BA2 / strings; Mutagen `TranslatedString` on Linux needs path setup | [pipeline-mutagen-spriggit.md](pipeline-mutagen-spriggit.md) |
