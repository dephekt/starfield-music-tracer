# Research wiki (working notes)

**Purpose:** Durable notes from Starfield data exploration — stack choices, crafting/resource acquisition, outpost organic research, and tooling. **Canonical** knowledge for the crafting explorer app lives here (markdown only; no separate graph).

**Start here**

- **[product-vision.md](product-vision.md)** — what we are building first (crafting helper / explorer), capabilities, link to UX sketch.
- **[data-model.md](data-model.md)** — entities ↔ plugin/script sources (stub; grows with the app).

**Technical threads**

| Doc | Contents |
|-----|----------|
| [pipeline-mutagen-spriggit.md](pipeline-mutagen-spriggit.md) | Spriggit (Linux), Mutagen package, strings caveat, architecture, `StarfieldExplore` probe, vanilla record counts |
| [crafting-and-resources.md](crafting-and-resources.md) | COBJ / Amp / refineries, planet flora mapping, acquisition taxonomy & axes, Penicillin slice |
| [outpost-organic-husbandry.md](outpost-organic-husbandry.md) | Creature loot, organic builder COBJ/PackIn/VMAD, pen scripts, PEX → PSC (Champollion + Wine) |
| [outpost-papyrus-notes.md](outpost-papyrus-notes.md) | Decompiled harvester scripts: `GetActorBaseForResource` / `GetFloraForResource`, scan flags, indexable vs TBD gates |
| [tooling-catalog.md](tooling-catalog.md) | `StarfieldExplore` debug flags, Python BA2/pex helpers |

**Code**

- [`tools/StarfieldExplore/`](../tools/StarfieldExplore/) — Mutagen-backed console (`dotnet run`, optional `STARFIELD_DATA`).
- Other helpers under [`tools/`](../tools/) (see tooling catalog).

**Ephemeral execution plans**

- [`.cursor/plans/`](../.cursor/plans/) — time-bound tasks; promote stable conclusions into this wiki when done.

**Archive (frozen snapshot)**

- [MUTAGEN_SPRIGGIT_FINDINGS.md](MUTAGEN_SPRIGGIT_FINDINGS.md) — **do not extend.** Everything before the wiki split; kept for history and diff archaeology. New work goes into the files above.
