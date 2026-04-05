# Outpost harvester Papyrus — decompiled behavior

**Status:** Champollion v1.3.2 → `.psc` verified **2026-04** (Wine); Starfield **Guard** / **`;***`** lines are experimental.  
**See also:** [outpost-organic-husbandry.md](outpost-organic-husbandry.md) (VMAD, PEX extract, Champollion how-to), [data-model.md](data-model.md).

**Regenerate locally:** default bundle is **`./tools/decompile_misc_pex.sh`** (see step 2). **Single-file manual path:**

Champollion: default **`~/.local/share/champollion/Champollion-1.3.2/Champollion.exe`** (see [outpost-organic-husbandry.md](outpost-organic-husbandry.md) **Where to put Champollion**) or **`CHAMPOLLION_EXE`**.

```bash
export STARFIELD_DATA=/path/to/Starfield/Data
./tools/extract_misc_ba2_script.py --name outpostharvesterfaunascript.pex -o research/decompiled/pe/outpostharvesterfaunascript.pex
CHAMP="${CHAMPOLLION_EXE:-$HOME/.local/share/champollion/Champollion-1.3.2/Champollion.exe}"
wine "$CHAMP" -p research/decompiled/psc research/decompiled/pe/outpostharvesterfaunascript.pex
```

Output lives under **`research/decompiled/`** (gitignored). Strings-only (no Wine): `./tools/dump_outpost_husbandry_pex_strings.py --only fauna`.

---

## Working the fauna pen — use this script first

When **ESM joins** (planet fauna ↔ `FaunaCreation` keywords) are empty or inconclusive, **authoritative gameplay flow** for the animal pen is in **`OutpostHarvesterFaunaScript`** (`scripts/outpostharvesterfaunascript.pex` in **`Starfield - Misc.ba2`**), not in `PlanetBiome.Fauna` rows.

### 1) Fast loop (strings, no Wine)

From repo root, with **`STARFIELD_DATA`** set:

```bash
./tools/dump_outpost_husbandry_pex_strings.py --only fauna
./tools/dump_outpost_husbandry_pex_strings.py --only fauna --all | less   # full ASCII runs; large
```

This surfaces identifiers (`GetActorBaseForResource`, `FaunaCreation`, `OrganicResourceAV`, `SetScanned`, `HasKeyword`, …) and confirms what is **named** in the bytecode before you decompile.

### 2) Extract + decompile (one command)

[`tools/decompile_misc_pex.sh`](../tools/decompile_misc_pex.sh) — extracts from **`Starfield - Misc.ba2`** into **`research/decompiled/pe/`**, then runs Champollion → **`research/decompiled/psc/`** (gitignored).

```bash
./tools/decompile_misc_pex.sh --preset organic-research   # default if you pass no args
./tools/decompile_misc_pex.sh --preset minimal             # harvester trio + sq_parent only
./tools/extract_misc_ba2_script.py --name outpostharvesterfaunascript.pex -o research/decompiled/pe/outpostharvesterfaunascript.pex  # single-file alternative
```

Manual Champollion flags: [outpost-organic-husbandry.md](outpost-organic-husbandry.md) (**PEX → PSC**). If you use **`extract_misc_ba2_script.py`** only, invoke **`champollion`** / **`wine … Champollion.exe`** on the **`.pex`** yourself.

### 3) What to read first in `OutpostHarvesterFaunaScript.psc`

| Topic | Search / read |
|-------|----------------|
| What species gets spawned | **`GetActorBaseForResource`**, **`OrganicResourceAV`**, **`CreateActor`**, **`PlaceAtMe`** |
| Herd count / tier | **`FaunaCreation`**, **`HasKeyword`**, **`createCount`** (matches container VMAD) |
| Scanner flags on spawns | **`SetValue(HandScannerTarget`**, **`SetScanned`** |
| Build menu hook | **`OnBuilderMenuSelect`** (organic AV chosen **there**, not from planet tables in this file) |
| Black box | **`GetActorBaseForResource`** is a **native** — which **ActorBase** exists for which **ActorValue** is **not** in the ESM; needs engine RE, empirical table, or runtime observation |

### 4) If you care about “must scan wild fauna” / terminal products

This harvester script **sets scanned on spawned pen animals**; **global scan / zoology** lives largely in **`sq_parentscript.pex`** (see **SQ_ParentScript** section below). Decompile that next and filter for **`Scan`**, **`Planet`**, **`Zoology`**, **`Harvest`**.

---

## Sources reviewed

| Script | Role |
|--------|------|
| `OutpostHarvesterFaunaScript` | Spawns pen **actors** from `OrganicResourceAV`; herd count from `FaunaCreation` keywords |
| `OutpostHarvesterFloraScript` | Resolves **Flora** from `OrganicResourceAV`; notifies linked **planters** |
| `OutpostHarvesterFloraPlanterScript` | Places **Flora** at named nodes on the planter ref |
| `sq_parentscript` | Planet trait scan pipeline, zoology harvest counters, outpost attacks — **not** organic AV picker |
| `planettraitscantargetscript` | **`OnScanned`** → **`SQ_Parent.DiscoverMatchingPlanetTraits`**; ties hand scanner to trait discovery |
| `outpostcontainerscript` | **ContainerMenu** + inventory shuffling between linked containers — **not** organic production UI |
| `herdcontrolscript` | Encounter **herd flee** AI (**`HerdKeyword`**, **`DMP_Herd`** AV) — **not** pen **`FaunaCreation`** / **`ActorTypeHerd*`** |
| `flora` (native) | **`Flora`** extends **Activator** — no Papyrus body (engine-only) |
| `floraonharvestscript` | World **flora** **`OnActivate`** → optional **global** / **quest stage** (content hooks, not greenhouse) |
| `outpostbuildermenuscript` | **`ShowWorkshopBuilderMenu()`** on linked builder or self — **native** workshop UI entry |
| `mq101outpostharvesterscript` | **MQ101** tutorial only: **`OnWorkshopObjectPlaced`** advances quest in one **Location** |

---

## `HerdControlScript` vs pen “herd” (decompiled)

**Two different meanings of “herd” in Starfield:**

| Concept | Where it lives | What it does |
|--------|----------------|--------------|
| **Pen herd size** | **`OutpostHarvesterFaunaScript`** + container VMAD **`FaunaCreation`** | **`CreatureKeyword`** (vanilla: **`ActorTypeHerdLarge`** / **Medium** / **Small**) + **`createCount`** — picks **how many** pen animals after **`GetActorBaseForResource`** |
| **`HerdControlScript`** | Placed ref + **`scripts/herdcontrolscript.pex`** | **World encounter** behavior: **`FindAllReferencesWithKeyword(HerdKeyword)`** (authoring comment: **`DMP_TypeHerd`**), registers **`OnCombatStateChanged`**, moves a **flee master marker**, toggles **`DMP_Herd`** on actors for **packages**, optional **fallback** fighter |

**`HerdControlScript`** has **no** **`OrganicResourceAV`**, **`FaunaCreation`**, **`GetActorBaseForResource`**, or workshop harvester APIs — it is **AI choreography** for wildlife-style setups, not organic production.

---

## `Flora` / `FloraOnHarvestScript` (decompiled)

- **`Flora.psc`** — `ScriptName Flora Extends Activator Native hidden` only. No scripted logic; flora **gameplay** is native + other scripts attached to refs.
- **`FloraOnHarvestScript`** — **`OnActivate`** when the **player** activates the ref: optionally sets **`GlobalToSet`** to **`ValueToSet`** and/or **`QuestToSetStage.SetStage(StageToSet)`**. Generic **content** hook for “player harvested / used this flora,” **not** tied to **`OutpostHarvesterFloraScript`** or **`GetFloraForResource`**.

---

## `OutpostBuilderMenuScript` / `MQ101OutpostHarvesterScript` (decompiled)

- **`OutpostBuilderMenuScript`:** **`OnActivate`** → **`GetLinkedRef(LinkOutpostBuilder)`** → **`ShowWorkshopBuilderMenu()`** on that ref, else on **`Self`**. Confirms the **outpost builder** is opened through the **native workshop menu** API; there is **no** branching here for organic modules — any organic-specific UI is **inside** that native menu + harvester **`OnBuilderMenuSelect`** path.
- **`MQ101OutpostHarvesterScript`:** **`OnWorkshopObjectPlaced`** — if the player is in **`SystemNarionPlanetAnselonMoonNexum`** and **MQ101** stage 900 not done, sets stage **740**. Tutorial **quest bookkeeping** only.

---

## `PlanetTraitScanTargetScript` (decompiled)

- **`OnLoad`:** **`BlockActivation`**, **`SQ_Parent.CheckForScanTargetUpdate(Self)`**.
- **`OnScanned` (ready state):** **`gotoState("done")`**, **`SQ_Parent.DiscoverMatchingPlanetTraits(Self, True)`** — increments / completes planet-trait scan bookkeeping on **`SQ_Parent`**, not harvester logic.

---

## `OutpostContainerScript` (decompiled)

- **`RegisterForMenuOpenCloseEvent("ContainerMenu")`** and **`OnItemAdded` / `OnItemRemoved`** drive **`MoveContainerContentToUnfilledContainers`** when the player opens the vanilla container UI or moves items. **No** workshop organic picker, **no** **`OrganicResourceAV`**.

---

## Fauna script — what it does (from `OutpostHarvesterFaunaScript.psc`)

- **`OnBuilderMenuSelect(ActorValue akActorValue)`** stores **`OrganicResourceAV = akActorValue`**, calls **`UpdateResource()`**, then optionally finds **`ResourceGlobals`** by **`resourceAV`** and, if the linked global “is Bool” and differs from **`ResourceGlobalValueToSet`**, sets it (decompiler may mangle **`GlobalVariable`** typing here — treat as hint only).
- **`UpdateResource()`** → **`ClearCreatedActors(True)`** then **`CreateActors()`** (full refresh when the menu picks a different organic AV).
- **`CreateActors()`** (when **`createdActors`** is empty):
  1. **`newActor = CreateActor(None)`** — first spawn from **`GetActorBaseForResource(OrganicResourceAV)`** + **`PlaceAtMe`** (see below).
  2. Scan **`FaunaCreation[0..Length)`** in order **while `createCount == 0`**: on the **first** slot where **`newActor.HasKeyword(theData.CreatureKeyword)`**, set **`createCount = theData.createCount`**.  
     So **array order matters**: the CK comment “highest **createCount** first” is **authoring guidance** — the code uses the **first matching** keyword row, not the maximum across all rows.
  3. If **`createCount > 1`**, decrement once (one actor already placed), then loop **`createCount`** times calling **`CreateActor(None)`** and push each into **`createdActors`**.
- **`CreateActor`** (when **`createdActorRef` is `None`** and **`OrganicResourceAV`** is set):
  - **`GetActorBaseForResource(OrganicResourceAV)`** → **`PlaceAtMe`** → **`IgnoreFriendlyHits(True)`** → **`SetValue(HandScannerTarget, 1.0)`** → **`SetScanned(True)`** → **`OutpostFauna.ApplyToRef`** → **`AddToFaction(OutpostFaunaFaction)`** → register **`OnDeath` / `OnEnterBleedout`**, optional **`OutpostLinkCreatedActor`** / **`CreatedActorBaseRefType`** placement.
  - So **spawned pen animals are marked scanned** here; no pre-scan gate **in this file**.
- **Respawn / destroy:** **`RespawnCreatedActor`**, **`CheckForRespawnOrDestroy`** (damage/destroy workshop object when all herd dead; timers back off **`RespawnSeconds`**).

**Planet / biome checks:** **none** in this script — no `GetPlanet` / fauna list reads. **Which organic AVs appear in the build menu** and **`GetActorBaseForResource`**’s mapping **AV → ActorBase** are **outside** this file (native / UI / other data).

**Natives / engine calls to trace elsewhere:** **`GetActorBaseForResource`**, **`GetWorkshop`**, **`PlaceAtMe`**, **`SetScanned`**, **`SetValue`**, **`HasKeyword`**, **`IgnoreFriendlyHits`**, **`ApplyToRef`**, faction/link APIs, timers.

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
| Which **organic AV** the module builds | Set by menu event → stored on script | Offer list likely from **COBJ/builder data** or other records; **not** read from planet tables here. **Hand scanner** already advertises **drops + outpost use** for wild fauna — same story, different **native** UI path (see Misc.ba2 survey below). |
| **ActorBase** / **Flora** for an AV | `GetActorBaseForResource` / `GetFloraForResource` | Need **game/native** mapping or empirical table; **not** derivable from ESM alone without reverse-engineering those natives |
| **Herd tier** (`createCount`) | `FaunaCreation` + `HasKeyword` on spawned actor | **Indexable** from container VMAD **`FaunaCreation`** (already in Mutagen dumps) |
| **Player must scan** wild species | Not enforced here; spawns get **`SetScanned(True)`** | **SQ_ParentScript** (below) owns much of the global scan / planet-trait pipeline; organic **terminal** gating may still be **native UI** |
| **Planet-level organic eligibility** | Not in these three files | Still **planet × flora/fauna graphs** + future mapping from AV→world sources |

---

## Follow-up survey: terminal UI, `OrganicResource`, scan strings (Misc.ba2)

Method: walk **`Starfield - Misc.ba2`** with [`tools/misc_ba2_grep.py`](../tools/misc_ba2_grep.py) / [`iter_misc_ba2_entries`](../tools/starfield_misc_ba2.py); decompile follow-ons with **`./tools/decompile_misc_pex.sh --preset organic-research`** (or Champollion on a single extracted **`.pex`**).

### `OrganicResource` literal in `.pex` bodies

Only these two scripts contain the **`OrganicResource`** substring in the compiled blob:

- `scripts/outpostharvesterfaunascript.pex`
- `scripts/outpostharvesterflorascript.pex`

So there is **no** separate Papyrus “organic resource picker” script that names that property in Misc.ba2 — the **pen’s** resource choice is almost certainly **native workshop UI** calling into **`OnBuilderMenuSelect(ActorValue)`** on the harvester scripts (already decompiled above).

**Hand scanner (wild fauna):** In play, scanning a creature shows **name, health, biome-style info, harvest/resource**, and the UI **explicitly says** the resource can be **crafted or produced at the outpost** (wording varies). That means the “which organics exist for this species / planet” knowledge is **surfaced in at least two places** — scanner overlay **and** workshop — and is unlikely to live **only** in the pen terminal widget. The implementation is still **unknown** here (native scanner pipeline, **ActorValueInformation**, condition functions, string tables, etc.); it is **not** something the three harvester **`.pex`** files or container VMAD fully spell out. When validating data tooling, compare ESM-derived graphs to **both** the scanner text and the terminal list.

### `OnBuilderMenuSelect` substring in `.pex` (Misc.ba2)

[`tools/misc_ba2_grep.py`](../tools/misc_ba2_grep.py) **`OnBuilderMenuSelect --suffix .pex`** also matches **`refcollectionalias.pex`**, **`objectreference.pex`**, **`referencealias.pex`**, **`activemagiceffect.pex`** — treat as **shared engine / alias plumbing**, not organic-specific UI. **Harvesters** remain the only **gameplay** scripts in that hit list tied to **`OrganicResourceAV`**.

### “Terminal” vs container UI

**`OutpostContainerScript`** (decompiled): registers **`RegisterForMenuOpenCloseEvent("ContainerMenu")`** and shuffles inventory between linked containers when the **vanilla container menu** opens/closes or the player adds/removes items. It does **not** implement a custom organic production menu or product list — it is **logistics for linked storage**, not eligibility logic.

No **`HarvesterMenu`**, **`OrganicFauna`**, or **`OrganicFlora`** string hits appeared in a Misc.ba2 substring sweep (aside from normal word fragments inside other symbols).

### Scan-related string hits (all `.pex` in Misc.ba2)

| Substring | Count / notes |
|-----------|----------------|
| **`GetScanned`** | **0** hits as an embedded identifier string (call may still exist under another representation). |
| **`SetScanned`** | `outpostharvesterfaunascript`, `outpostharvesterfloraplanterscript`, `sq_parentscript`, one DLC quest fragment, plus base **`objectreference.pex`** plumbing. |
| **`IsScanned`** | **`objectreference.pex`**, **`planettraitscantargetscript.pex`** only. |

### **`SQ_ParentScript`** — scan / zoology / outpost (decompiled check)

`scripts/sq_parentscript.pex` (~46 KB) decompiles cleanly with Champollion. A quick grep of **`sq_parentscript.psc`** shows **no** references to **`OutpostHarvesterFaunaScript`**, **`OrganicResourceAV`**, **`GetActorBaseForResource`**, or **`Harvester`** — so **organic pen choice and spawns** stay in the harvester script + natives; **`SQ_Parent`** covers **planet trait scan targets**, **`OnPlayerScannedObject` → `CheckCompletePlanetSurvey`**, **`KeywordType_PlanetFaunaAbundance`**, **`ZoologyNonLethalHarvestCount`**, **outpost attack** story events, smuggling scan jammer math, etc.

**Suggested reading order in `sq_parentscript.psc`:** **`OnPlayerScannedObject`**, **`DiscoverMatchingPlanetTraits`**, **`OnPlayerScanPlanet`**, **`CheckForAttack`** (outpost), and any function touching **`ZoologyNonLethalHarvestCount`**.

### Native mapping (`OrganicResourceAV` → ActorBase / Flora)

Confirmed again: only the two harvester scripts name **`OrganicResource`** in Misc.ba2. **`GetActorBaseForResource`** / **`GetFloraForResource`** are **engine natives**; treat AV→form resolution as **black box** for data tooling until reverse-engineered or tabled empirically.

---

## Champollion caveat

Lines using **`Guard`** / **`EndGuard`** and some APIs may be **wrong or incomplete** for Starfield; treat behavioral conclusions as **strong hints**, not Creation Kit truth.
