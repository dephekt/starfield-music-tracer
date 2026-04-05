# Pipeline: Mutagen, Spriggit, StarfieldExplore

**Status:** settled for Linux dev (Spriggit blocked; Mutagen primary).  
**Last updated:** wiki split from archival (2026-04).  
**See also:** [README.md](README.md), [product-vision.md](product-vision.md), [tooling-catalog.md](tooling-catalog.md).

---

# Mutagen & Spriggit — exploration notes (2026-04-03)

## Goal

Validate **Spriggit** (YAML/JSON tree export) and **Mutagen** (typed C# API) for the outpost planner and a future broader **ESM explorer** (weapons, armor, mods, books, ammo, form IDs, display names, crafting links).

## Spriggit CLI (Linux)

- **Official Linux binary:** `SpriggitLinuxCLI.zip` from [Spriggit releases](https://github.com/Mutagen-Modding/Spriggit/releases) (tested **0.40.0**). The binary runs and lists commands (`serialize`, `deserialize`, …).
- `**dotnet tool install spriggit.cli`** fails: NuGet packages report missing `DotnetToolSettings.xml` (same failure for `**Spriggit.Yaml.Starfield**` when the Linux CLI tries `dotnet tool install` to pull translation packages into `/tmp/Spriggit/Translations/...`).
- **Conclusion:** On this Linux/.NET 8 setup, **Spriggit is not usable end-to-end** without a fix upstream (packaging) or a workaround (e.g. run on Windows, or pre-install translation packages another way). The **GitHub zip** is the right distribution for CLI, but **Starfield serialize still depends on broken `dotnet tool` translation installs**.

## Mutagen (C# library)

- **Local source (optional):** shallow clone under **`vendor/Mutagen/`** (gitignored) for API / builder browsing — `**git clone --depth 1 -b dev https://github.com/Mutagen-Modding/Mutagen.git vendor/Mutagen**`. Entry points: `**Mutagen.Bethesda.Core/Environments/GameEnvironmentBuilder.cs**`, `**…/GameEnvironment.cs**`, `**Mutagen.Bethesda.Starfield/**` for game mix-ins.
- **Package:** `Mutagen.Bethesda.Starfield` — stable **0.54.x** is not on NuGet yet; `**0.54.0-alpha.32`** restores and builds on **net8.0**.
- `**StarfieldMod.CreateFromBinaryOverlay(ModPath, StarfieldRelease.Starfield)`** loads `**Starfield.esm**` quickly (~1–2s cold, ~7s with `dotnet run` overhead) and exposes major record groups with **real counts** (see below).
- **COBJ:** exposed as `**ConstructibleObjects`** (not `Constructibles`). `**CreatedObject**` links to the produced form — suitable for recipe/BOM graphs.
- **Ingestibles:** includes craftables such as `**Chem_Craft_Amp`** (`29A856:Starfield.esm`), matching the pharmaceutical “Amp” use case.

**Future “ESM explorer” alignment:** same load path gives **Weapons (406)**, **Armors (1017)**, **ObjectModifications (2541)**, **Books**, **Ammo**, **MiscItems**, **Keywords**, **NPCs**, **Florae**, etc. — all enumerable from one mod.

### Linux caveat: localized strings

- Accessing `**TranslatedString`** fields (e.g. `**ing.Name**`) triggered resolution via **archive / plugin listings** that expect **Windows `LocalAppData`**-style layout (`PluginListingsPathContext`). On Linux without that environment, `**.Name` can throw**.
- **Mitigation for tooling:** resolve strings explicitly (e.g. load `**Starfield - Localization.ba2`** / `.strings` the same way as [extract.py](../extract.py)), or set up Mutagen’s string lookup paths for Linux/Wine; or ship **EditorID + FormKey** in v1 of exports and add friendly names in a second pass.

### GameEnvironment on Linux (2026-04)

- `**GameEnvironment.Typical.Construct(GameRelease.Starfield)**` still relies on default **Plugins.txt** discovery → same **LocalAppData** problem on bare Linux.
- **Recommended:** set **`STARFIELD_PLUGINS_TXT`** to the absolute path of the game’s **`Plugins.txt`** (capital **P**; Linux paths are case-sensitive). Repo default layout: **`tools/StarfieldExplore/env.example.sh`**. Then **`WithResolver`** → **`PluginListingsPathInjection`** implements **`IPluginListingsPathContext`** (see **`vendor/Mutagen/Mutagen.Bethesda.Core/Plugins/Order/DI/PluginListingsPathContext.cs`**). That satisfies archive/string code that still asks for the listings file path even when you also use **`WithLoadOrder`**.
- **Load order:** omit **`STARFIELD_LOAD_ORDER`** to read order from that **`plugins.txt`**; or set **`STARFIELD_LOAD_ORDER`**=`Plugin1.esm,…` to **override** the file. If neither file nor override: fallback **`WithLoadOrder(Starfield.esm)`** only.
- **Strings:** if **`Name.String`** still fails, add **`WithStringParameters(StringsReadParameters)`** (**`StringsFolderOverride`** / **`BsaFolderOverride`**) or Proton **`LOCALAPPDATA`**. **`--inspect-game-environment`** ([tooling-catalog.md](tooling-catalog.md)).

## Suitability vs product direction


| Need                                                        | Mutagen                                          | Spriggit (current Linux)                                       |
| ----------------------------------------------------------- | ------------------------------------------------ | -------------------------------------------------------------- |
| Reliable **Starfield.esm** read, typed records, links       | **Yes** (alpha package acceptable)               | Blocked by translation `dotnet tool` failure                   |
| **Greppable** full-plugin YAML/JSON without custom code     | Would be ideal                                   | **Not yet** on this host                                       |
| **Custom export** (JSON/SQLite for web app)                 | **Yes** — small C# console is the practical path | Optional later if packaging fixed                              |
| Long-term **ESM explorer** (weapons, mods, crafting, pivot) | **Strong fit** — same API covers those groups    | Nice for human diff/git; not required if you own serialization |


**Recommendation:** Proceed with **Mutagen-backed C# data generation** as the primary pipeline; **retry Spriggit** when upstream fixes global-tool packaging or document a non–`dotnet tool` install for `Spriggit.Yaml.Starfield`. Keep **Python `extract.py` BA2 string** logic (or equivalent) for **display names** on Linux.

---

## Spriggit vs own console app (architecture)

**Short answer: prefer Mutagen directly in your own console app(s).** Treat Spriggit as **optional sugar**, not the spine of the product.

### What Spriggit is good for

- **Git-friendly text mods:** round-trip plugin ↔ YAML/JSON for **mod authors** who want diffs and merges.
- **Quick human inspection** of a record’s serialized shape **if** the CLI works on your OS.
- It still **uses Mutagen under the hood** for binary semantics; it does not replace understanding Mutagen for anything custom.

### What Spriggit does *not* solve for you

- **Your web app schema** (SQLite tables, APIs, rollups, pivots). You still design and own that.
- **Linux today:** broken `dotnet tool` path for Starfield translation packages — you cannot rely on it in CI or dev without workarounds.
- **Performance / size:** a full `Starfield.esm` text export is enormous; grepping it is a research trick, not necessarily your shipping pipeline.

### Why “own console + Mutagen” is the right default

- **Stable, testable pipeline:** `dotnet run` → deterministic artifact → Python/FastAPI reads DB. No extra moving part that fails to install.
- **Typed API:** you emit exactly the fields you need (COBJ ingredients, mod links, flora, etc.) instead of ingesting Spriggit’s **file-per-record** layout.
- **Aligns with [Mutagen docs](https://mutagen-modding.github.io/Mutagen/):** you can adopt the recommended building blocks **on purpose** (see below), not fight `TranslatedString` defaults on Linux.

### Using Mutagen “properly” (avoid wacky apologies)

`[tools/StarfieldExplore](../tools/StarfieldExplore/)` is intentionally a **thin probe** (single-plugin overlay, no display names). For production extraction, plan to move toward documented patterns:

1. **Environment + load order** — [Environment](https://mutagen-modding.github.io/Mutagen/environment/): build a `**GameEnvironment`** (or equivalent Starfield entry) from your **Data** folder and **active plugin list** when you need behavior that matches the game (overrides, patches, DLC).
2. **Link cache** — [Link cache](https://mutagen-modding.github.io/Mutagen/linkcache/): resolve `**FormLink` / `CreatedObject` / mod targets** to real records instead of only printing form keys.
3. **Strings** — [Strings](https://mutagen-modding.github.io/Mutagen/) + [Archives](https://mutagen-modding.github.io/Mutagen/): use Mutagen’s **string + BA2** APIs (or a documented path for Linux) so `**TranslatedString` / FULL** names work; avoid ad hoc “never touch `.Name`” as a permanent design — that was a **probe limitation**, not the end state.
4. **Readonly getters** — [Best practices](https://mutagen-modding.github.io/Mutagen/): prefer getter interfaces in public extraction code.

**Verdict:** Saying **“we use Mutagen in our own exporter, following Environment + LinkCache + Strings”** is the coherent architecture. **Spriggit** is a nice-to-have for **mod-text workflows** or **one-off dumps** if/when packaging works — it should not block or define your stack.

## Reference implementation in this repo

- `[tools/StarfieldExplore/](../tools/StarfieldExplore/)` — `dotnet run` with `STARFIELD_DATA` optional (defaults to Steam Linux path).

Sample output (counts from vanilla `Starfield.esm`):

- Npc ~7.1k, Keyword ~5.9k, ConstructibleObject ~3.0k, ObjectModification ~2.5k, MiscItem ~1.3k, Armor ~1.0k, Book ~1k, Weapon ~406, Ingestible ~353, Flora ~300, Ammo ~150.
