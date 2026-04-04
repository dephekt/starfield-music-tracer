# Outpost harvester Papyrus — decompiled behavior

**Status:** from Champollion v1.3.2 `.psc` (2026-04); Starfield Guard/`;***` syntax is experimental.  
**See also:** [outpost-organic-husbandry.md](outpost-organic-husbandry.md) (VMAD, PEX extract, Champollion how-to), [data-model.md](data-model.md).

**Regenerate locally:** extract `.pex` from `Starfield - Misc.ba2`, then Champollion (see husbandry doc). Output is gitignored under `research/decompiled/` when present.

---

## Sources reviewed

| Script | Role |
|--------|------|
| `OutpostHarvesterFaunaScript` | Spawns pen **actors** from `OrganicResourceAV`; herd count from `FaunaCreation` keywords |
| `OutpostHarvesterFloraScript` | Resolves **Flora** from `OrganicResourceAV`; notifies linked **planters** |
| `OutpostHarvesterFloraPlanterScript` | Places **Flora** at named nodes on the planter ref |

---

## Fauna script — what it does

- **`OrganicResourceAV`** is set from **`OnBuilderMenuSelect`** (workshop build menu), not inferred in-script from planet data.
- **`CreateActor`** calls **`GetActorBaseForResource(OrganicResourceAV)`** → if non-null, **`PlaceAtMe`** spawns the actor, then:
  - **`SetValue(HandScannerTarget, 1.0)`** on the new actor
  - **`SetScanned(True)`** on the new actor  
  So this script **does not** require the player to have scanned wild fauna first for the **spawned pen animals** — it **marks them scanned** after placement. Any “you must scan to unlock terminal products” rule lives **elsewhere** (other scripts/UI), not in this gate logic.
- **Herd size:** after the first actor is created, the script walks **`FaunaCreation`** in order and uses the first entry where **`newActor.HasKeyword(theData.CreatureKeyword)`**; **`createCount`** from that struct drives extra **`CreateActor`** calls (minus one already placed). Matches VMAD **`FaunaCreation`** tier keywords (`ActorTypeHerd*` etc.).
- **Respawn / destroy:** timers adjust **`RespawnSeconds`**; if all tracked actors are dead, the workshop object is **damaged/destroyed** and actors cleared.
- **`ResourceGlobals`:** optional struct array toggles a linked **`GlobalVariable`** when the selected organic AV matches (same pattern as flora script).

**Planet / biome checks:** **none** in this decompilation — no `GetPlanet`, keyword checks on location, etc. **World eligibility** for “which organic AVs the menu offers” or which **`ActorBase`** `GetActorBaseForResource` returns is assumed to be **engine data or other systems**, not this file.

**Natives / engine calls to trace elsewhere:** `GetActorBaseForResource`, `GetWorkshop`, `PlaceAtMe`, `SetScanned`, `SetValue`, `HasKeyword`, `IgnoreFriendlyHits`, `ApplyToRef` (alias), faction/link/ref APIs, timer APIs.

---

## Flora script — what it does

- **`OnInit`:** **`GetRefsLinkedToMe(OutpostLinkFloraPlanter)`** → registers each linked **`OutpostHarvesterFloraPlanterScript`** for a custom **`CreateFlora`** event.
- **`CreateFlora`:** if **`OrganicResourceAV`** set, **`GetFloraForResource(OrganicResourceAV)`** → if non-null, **`SendCustomEvent("outpostharvesterflorascript_CreateFloraEvent", …)`** to planters with the **`Flora`** form.
- **`OnBuilderMenuSelect`:** same **`OrganicResourceAV`** + **`ResourceGlobals`** pattern as fauna.
- **`ClearCreatedFlora`:** sends event with **`None`** to clear.

**Planet / biome:** again **no** explicit planet logic in this file — resolution is **`GetFloraForResource`** (native/engine mapping from AV to flora).

---

## Flora planter script — what it does

- Listens for **`OutpostHarvesterFloraScript.CreateFloraEvent`**.
- **`CreateFlora(Flora)`:** clears prior placements; loops nodes **`FloraNode01`…`FloraNode08`** (zero-padded name); **`PlaceAtNode`**, then on each ref:
  - **`SetHarvested(True)`**
  - **`SetValue(HandScannerTarget, 1.0)`**
  - random **scale** between min/max consts
  - **`SetScanned(True)`**  
  Same theme as fauna: **scanner-related AV + scanned flag** applied to **spawned** instances.

---

## Indexable vs player-only (for the crafting app)

| Topic | In these scripts | For ESM-only tooling |
|-------|------------------|---------------------|
| Which **organic AV** the module builds | Set by menu event → stored on script | Offer list likely from **COBJ/builder data** or other records; **not** read from planet tables here |
| **ActorBase** / **Flora** for an AV | `GetActorBaseForResource` / `GetFloraForResource` | Need **game/native** mapping or empirical table; **not** derivable from ESM alone without reverse-engineering those natives |
| **Herd tier** (`createCount`) | `FaunaCreation` + `HasKeyword` on spawned actor | **Indexable** from container VMAD **`FaunaCreation`** (already in Mutagen dumps) |
| **Player must scan** wild species | Not enforced here; spawns get **`SetScanned(True)`** | **SQ_ParentScript** (below) owns much of the global scan / planet-trait pipeline; organic **terminal** gating may still be **native UI** |
| **Planet-level organic eligibility** | Not in these three files | Still **planet × flora/fauna graphs** + future mapping from AV→world sources |

---

## Follow-up survey: terminal UI, `OrganicResource`, scan strings (Misc.ba2)

Method: walk **`Starfield - Misc.ba2`** with [`tools/misc_ba2_grep.py`](../tools/misc_ba2_grep.py) / [`iter_misc_ba2_entries`](../tools/starfield_misc_ba2.py); decompile **`OutpostContainerScript`** with Champollion.

### `OrganicResource` literal in `.pex` bodies

Only these two scripts contain the **`OrganicResource`** substring in the compiled blob:

- `scripts/outpostharvesterfaunascript.pex`
- `scripts/outpostharvesterflorascript.pex`

So there is **no** separate Papyrus “organic resource picker” script that names that property in Misc.ba2 — the workshop flow is almost certainly **native UI** calling into **`OnBuilderMenuSelect(ActorValue)`** on the harvester scripts (already decompiled above).

### “Terminal” vs container UI

**`OutpostContainerScript`** (decompiled): registers **`RegisterForMenuOpenCloseEvent("ContainerMenu")`** and shuffles inventory between linked containers when the **vanilla container menu** opens/closes or the player adds/removes items. It does **not** implement a custom organic production menu or product list — it is **logistics for linked storage**, not eligibility logic.

No **`HarvesterMenu`**, **`OrganicFauna`**, or **`OrganicFlora`** string hits appeared in a Misc.ba2 substring sweep (aside from normal word fragments inside other symbols).

### Scan-related string hits (all `.pex` in Misc.ba2)

| Substring | Count / notes |
|-----------|----------------|
| **`GetScanned`** | **0** hits as an embedded identifier string (call may still exist under another representation). |
| **`SetScanned`** | `outpostharvesterfaunascript`, `outpostharvesterfloraplanterscript`, `sq_parentscript`, one DLC quest fragment, plus base **`objectreference.pex`** plumbing. |
| **`IsScanned`** | **`objectreference.pex`**, **`planettraitscantargetscript.pex`** only. |

### **`SQ_ParentScript`** — next Papyrus read for scan / zoology

`scripts/sq_parentscript.pex` (~46 KB) is the **shared parent quest** script. Embedded strings include **`OnPlayerScannedObject`**, **`OnPlayerScanPlanet`**, **`incrementScanCount`**, **`PlanetTraitLocationScanCount`**, **`UpdateScanTarget`**, **`ZoologyNonLethalHarvestCount`**, **`HarvestActor`**, **`KeywordType_PlanetFaunaAbundance`**, **`KeywordType_PlanetFloraAbundance`**, **`SetScanned`**, **`testSetScanned`**, etc. This is the **central** place (in Papyrus) for **planet trait / scan target / zoology harvest counters** — not the three harvester scripts.

**Suggested next decompile:** `sq_parentscript.pex` (large; filter for functions touching scan / harvest / outpost).

### Native mapping (`OrganicResourceAV` → ActorBase / Flora)

Confirmed again: only the two harvester scripts name **`OrganicResource`** in Misc.ba2. **`GetActorBaseForResource`** / **`GetFloraForResource`** are **engine natives**; treat AV→form resolution as **black box** for data tooling until reverse-engineered or tabled empirically.

---

## Champollion caveat

Lines using **`Guard`** / **`EndGuard`** and some APIs may be **wrong or incomplete** for Starfield; treat behavioral conclusions as **strong hints**, not Creation Kit truth.
