# Product vision — crafting explorer (v1)

**Status:** active direction (2026-04).  
**See also:** [data-model.md](data-model.md), [README.md](README.md), UX sketch: [`.cursor/plans/danplan_user_story_crafting_insights_logistics.md`](../.cursor/plans/danplan_user_story_crafting_insights_logistics.md).

## What we are building first

A **crafting-focused helper / explorer** for Starfield — the closest match to research already done (COBJ, resources, planet flora, RGD, loot). Not a full “every item type in the game” encyclopedia in v1.

## Capabilities (target)

- **Discover** what can be crafted (chems, food, industrial outputs, etc.) and **what each recipe requires** (components, bench).
- **Acquisition:** where ingredients come from — refine chains, world sources, and distinct **axes** (bench vs planet survey vs outpost-adjacent organics vs loot) as documented in [crafting-and-resources.md](crafting-and-resources.md).
- **Reverse pivots:** given a resource or item, what recipes or outputs use it.
- **Loot & trade (when data allows):** who can drop it or sell it (see danplan sketch for “can be looted from” / “can be sold by vendors”).
- **Logistics / planet planning:** for a build goal (e.g. a base in a given system — food + meds + industrial lines), which **planets** cover needed **organic and inorganic** inputs.
- **Organic depth:** for organics, **biome-level** breakdown and **which named flora or fauna** on each planet yields the relevant misc — a level of detail often missing from aggregate sites.

## Research alignment

Implementation draws on the same Mutagen/`StarfieldExplore` and Python tooling paths described in [pipeline-mutagen-spriggit.md](pipeline-mutagen-spriggit.md) and domain notes in [crafting-and-resources.md](crafting-and-resources.md). [outpost-organic-husbandry.md](outpost-organic-husbandry.md) supports **accurate** organic sourcing and eligibility research without committing v1 UI to a full outpost planner.

## Later (one paragraph)

A broader data explorer or additional app sections can **reuse this wiki, schema, and extraction pipeline**. Scope and UX for that are intentionally unspecified until the crafting explorer is solid. **Document the research wiki well** so that handoff is cheap.
