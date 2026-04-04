# Crafting and resources (acquisition)

**Status:** active research / v1 app domain.  
**See also:** [product-vision.md](product-vision.md), [data-model.md](data-model.md), [pipeline-mutagen-spriggit.md](pipeline-mutagen-spriggit.md), [outpost-organic-husbandry.md](outpost-organic-husbandry.md), [tooling-catalog.md](tooling-catalog.md).

---

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

### Penicillin X (vanilla EditorIDs)


| Role               | EditorID                |
| ------------------ | ----------------------- |
| Crafted ingestible | `Aid_Craft_PenicillinX` |
| COBJ               | `co_Chem_PenicillinX`   |


Components (your install): `**ResOrgUncommonMembrane`**, `**ResOrgCommonMetabolicAgent**`, `**ResOrgUncommonAntimicrobial**` — each refines via nested `**co_Resource_***` COBJs to misc items the same way as Amp resources.
