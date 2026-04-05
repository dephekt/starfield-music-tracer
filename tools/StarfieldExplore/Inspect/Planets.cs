using System.Collections;
using System.Globalization;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;
using Noggog;
using StarfieldExplore.Game;

partial class Program
{
static void PrintRgdResourceLines(ILinkCache cache, FormKey rgdFk, string prefix)
{
    if (!cache.TryResolve<IResourceGenerationDataGetter>(rgdFk, out var rgd))
    {
        Console.WriteLine($"{prefix} → {rgdFk} (unresolved RGD)");
        return;
    }

    Console.WriteLine($"{prefix} → RGD {rgdFk} EDID={rgd.EditorID}");
    var items = rgd.Items;
    if (items is null)
    {
        Console.WriteLine($"{prefix}    Items: (null)");
        return;
    }

    foreach (var item in items)
    {
        if (item is null || item.Resource.IsNull) continue;
        var rf = item.Resource.FormKey;
        cache.TryResolve<IResourceGetter>(rf, out var res);
        Console.WriteLine($"{prefix}    Resource {rf}  EDID={res?.EditorID}");
    }
}

static bool ResourceEdidLooksLikeSurveyInteresting(string? edid)
{
    if (string.IsNullOrEmpty(edid)) return false;
    return edid.Contains("Argon", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Water", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("H2O", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Uranium", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Uran", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Benz", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Aromatic", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("C6H", StringComparison.OrdinalIgnoreCase);
}

static int RunInspectPlanetSurvey(StarfieldExploreSession session, string hint)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var h = hint.Trim();
    if (h.Length == 0)
    {
        Console.Error.WriteLine("Empty planet hint.");
        return 1;
    }

    var matches = mod.Planets
        .Where(p =>
            p.EditorID?.Contains(h, StringComparison.OrdinalIgnoreCase) == true
            || p.FormKey.ToString().Contains(h, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (matches.Count == 0)
    {
        Console.WriteLine($"No planets matching hint \"{h}\" (EditorID substring or FormKey string fragment).");
        return 0;
    }

    Console.WriteLine($"Planets matching \"{h}\": {matches.Count}");
    foreach (var planet in matches)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Planet {planet.FormKey}  EDID={planet.EditorID} ===");
        Console.WriteLine("--- Biome → ResourceGeneration → RGD.Items.Resource ---");
        var biomes = planet.Biomes;
        if (biomes is null || biomes.Count == 0)
        {
            Console.WriteLine("(no biomes)");
        }
        else
        {
            for (var bi = 0; bi < biomes.Count; bi++)
            {
                var biome = biomes[bi];
                string? biomeEdid = null;
                if (!biome.Biome.IsNull && cache.TryResolve<IBiomeGetter>(biome.Biome.FormKey, out var br))
                    biomeEdid = br.EditorID;
                Console.WriteLine($"  [{bi}] PlanetBiome  Biome={biomeEdid}  ({biome.Biome.FormKey})");
                var rgPlanetBiome = biome.ResourceGeneration;
                if (rgPlanetBiome.IsNull)
                    Console.WriteLine("      PlanetBiome.ResourceGeneration: (null)");
                else
                    PrintRgdResourceLines(cache, rgPlanetBiome.FormKey, "      PlanetBiome.ResourceGeneration");

                if (!biome.Biome.IsNull && cache.TryResolve<IBiomeGetter>(biome.Biome.FormKey, out var biomeRec))
                {
                    var rgList = biomeRec.ResourceGeneration;
                    if (rgList is null || rgList.Count == 0)
                        Console.WriteLine("      IBiomeGetter.ResourceGeneration: (empty)");
                    else
                    {
                        for (var ri = 0; ri < rgList.Count; ri++)
                        {
                            var link = rgList[ri];
                            if (link.IsNull)
                            {
                                Console.WriteLine($"      IBiomeGetter.ResourceGeneration[{ri}]: (null link)");
                                continue;
                            }

                            PrintRgdResourceLines(
                                cache,
                                link.FormKey,
                                $"      IBiomeGetter.ResourceGeneration[{ri}]");
                        }
                    }
                }
            }
        }

        Console.WriteLine(
            "--- IResourceGetter under planet EnumerateFormLinks(true), filtered (Argon/Water/Uran/Benz/Aromatic/C6H…) ---");
        if (planet is not IFormLinkContainerGetter flc)
        {
            Console.WriteLine("(planet does not implement IFormLinkContainerGetter)");
            continue;
        }

        var seenRes = new HashSet<FormKey>();
        try
        {
            foreach (var raw in flc.EnumerateFormLinks(true))
            {
                if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out _)) continue;
                if (!cache.TryResolve<IResourceGetter>(fk, out var res)) continue;
                if (!ResourceEdidLooksLikeSurveyInteresting(res.EditorID)) continue;
                if (!seenRes.Add(fk)) continue;
                Console.WriteLine($"  {fk}  EDID={res.EditorID}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"(EnumerateFormLinks failed: {ex.Message})");
        }
    }

    return 0;
}

/// <summary>
/// Lists <see cref="IPlanetBiomeGetter.Fauna"/> spawn targets per biome: direct <see cref="INpcGetter"/> or expanded <see cref="ILeveledNpcGetter"/> leaves (same graph as <see cref="RunInspectPenHerdPlanets"/>).
/// </summary>
static int RunInspectPlanetFauna(StarfieldExploreSession session, string hint, int listLimit)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var h = hint.Trim();
    if (h.Length == 0)
    {
        Console.Error.WriteLine("Empty planet hint.");
        return 1;
    }

    var matches = mod.Planets
        .Where(p =>
            p.EditorID?.Contains(h, StringComparison.OrdinalIgnoreCase) == true
            || p.FormKey.ToString().Contains(h, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (matches.Count == 0)
    {
        Console.WriteLine($"No planets matching hint \"{h}\" (EditorID substring or FormKey string fragment).");
        return 0;
    }

    Console.WriteLine($"Planets matching \"{h}\": {matches.Count}");
    Console.WriteLine(
        "Source: Planet → PlanetBiome.Fauna (INpcSpawn). LeveledNpc rows are expanded to leaf Npc. " +
        "Does not list POI / quest / ship / scripted spawns outside this table.");

    foreach (var planet in matches)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Planet {planet.FormKey}  EDID={planet.EditorID} ===");
        var biomes = planet.Biomes;
        if (biomes is null || biomes.Count == 0)
        {
            Console.WriteLine("(no biomes)");
            continue;
        }

        var allLeafNpcs = new HashSet<FormKey>();

        for (var bi = 0; bi < biomes.Count; bi++)
        {
            var pb = biomes[bi];
            string? biomeEdid = null;
            if (!pb.Biome.IsNull && cache.TryResolve<IBiomeGetter>(pb.Biome.FormKey, out var br))
                biomeEdid = br.EditorID;

            var fauna = pb.Fauna;
            Console.WriteLine();
            Console.WriteLine($"  [{bi}] PlanetBiome  Biome={biomeEdid}  ({pb.Biome.FormKey})");
            if (fauna is null || fauna.Count == 0)
            {
                Console.WriteLine("      Fauna: (empty)");
                continue;
            }

            var slot = 0;
            foreach (var link in fauna)
            {
                if (link.IsNull) continue;
                var fk = link.FormKey;
                var vlev = new HashSet<FormKey>();
                var leaves = new HashSet<FormKey>();
                CollectNpcFormKeysFromFaunaSpawnTarget(fk, cache, vlev, leaves);

                Console.WriteLine($"      Fauna[{slot}]  spawn {fk}");
                slot++;

                if (cache.TryResolve<INpcGetter>(fk, out var directNpc))
                {
                    Console.WriteLine($"        → Npc {directNpc.FormKey}  EDID={directNpc.EditorID}");
                    foreach (var lf in leaves)
                        allLeafNpcs.Add(lf);
                }
                else if (cache.TryResolve<ILeveledNpcGetter>(fk, out var lev))
                {
                    Console.WriteLine($"        → LeveledNpc {lev.FormKey}  EDID={lev.EditorID}  ({leaves.Count} leaf Npc(s))");
                    foreach (var lf in leaves)
                        allLeafNpcs.Add(lf);
                    foreach (var nfk in leaves.OrderBy(x =>
                                 cache.TryResolve<INpcGetter>(x, out var n) && n.EditorID is not null
                                     ? n.EditorID
                                     : x.ToString(),
                                 StringComparer.OrdinalIgnoreCase))
                    {
                        if (cache.TryResolve<INpcGetter>(nfk, out var n))
                            Console.WriteLine($"           • {nfk}  EDID={n.EditorID}");
                    }
                }
                else
                {
                    Console.WriteLine("        → (unresolved — neither Npc nor LeveledNpc at this FormKey)");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("  --- Unique leaf Npc (all biomes; --limit applies here; 0 = unlimited) ---");
        var summaryLines = allLeafNpcs
            .OrderBy(nfk =>
                cache.TryResolve<INpcGetter>(nfk, out var n) && n.EditorID is not null
                    ? n.EditorID
                    : nfk.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .Select(nfk =>
                cache.TryResolve<INpcGetter>(nfk, out var n)
                    ? $"      {nfk}  EDID={n.EditorID}"
                    : $"      {nfk}  (Npc resolve failed)");
        PrintLimited(summaryLines, listLimit);
    }

    return 0;
}

/// <summary><see cref="IKeywordGetter"/> FormKeys whose EditorID starts with ActorTypeHerd (vanilla pen <c>FaunaCreation</c> <c>CreatureKeyword</c> targets).</summary>
static Dictionary<FormKey, string?> BuildActorTypeHerdKeywordEdidByFormKey(IStarfieldModGetter mod)
{
    var map = new Dictionary<FormKey, string?>();
    foreach (var k in mod.Keywords)
    {
        var e = k.EditorID;
        if (e is null || !e.StartsWith("ActorTypeHerd", StringComparison.OrdinalIgnoreCase)) continue;
        map[k.FormKey] = e;
    }

    return map;
}

/// <summary>
/// Planet biome fauna links often resolve to leveled or variant <see cref="INpcGetter"/> rows that omit herd keywords;
/// those keywords may live on <see cref="IRaceGetter"/>, on <see cref="ITemplateActorsGetter.KeywordsTemplate"/>, or on the <see cref="INpcGetter.DefaultTemplate"/> chain.
/// </summary>
static void AddHerdKeywordsFromFaunaNpcAndAncestors(
    INpcGetter npc,
    ILinkCache cache,
    IReadOnlySet<FormKey> herdKeySet,
    HashSet<FormKey> herdsOnPlanet,
    HashSet<FormKey> visitedNpcFormKeys)
{
    if (!visitedNpcFormKeys.Add(npc.FormKey)) return;

    foreach (var lk in npc.Keywords ?? [])
    {
        if (!lk.IsNull && herdKeySet.Contains(lk.FormKey))
            herdsOnPlanet.Add(lk.FormKey);
    }

    if (!npc.Race.IsNull && cache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race))
    {
        foreach (var lk in race.Keywords ?? [])
        {
            if (!lk.IsNull && herdKeySet.Contains(lk.FormKey))
                herdsOnPlanet.Add(lk.FormKey);
        }
    }

    var ta = npc.TemplateActors;
    if (ta is not null && !ta.KeywordsTemplate.IsNull &&
        cache.TryResolve<INpcGetter>(ta.KeywordsTemplate.FormKey, out var keywordsTemplateNpc))
        AddHerdKeywordsFromFaunaNpcAndAncestors(keywordsTemplateNpc, cache, herdKeySet, herdsOnPlanet, visitedNpcFormKeys);

    if (!npc.DefaultTemplate.IsNull && cache.TryResolve<INpcGetter>(npc.DefaultTemplate.FormKey, out var parent))
        AddHerdKeywordsFromFaunaNpcAndAncestors(parent, cache, herdKeySet, herdsOnPlanet, visitedNpcFormKeys);
}

/// <summary>
/// Planet biome fauna rows are typed in Mutagen as <see cref="IFormLinkGetter{INpcGetter}"/>, but the same <see cref="FormKey"/> can resolve to
/// <see cref="ILeveledNpcGetter"/> at runtime (<see cref="INpcSpawnGetter"/> is implemented by both <see cref="Npc"/> and <see cref="LeveledNpc"/>).
/// </summary>
static void AddHerdFromPlanetFaunaSpawnTarget(
    FormKey spawnTargetFk,
    ILinkCache cache,
    IReadOnlySet<FormKey> herdKeySet,
    HashSet<FormKey> herdsOnPlanet,
    HashSet<FormKey> visitedNpcFormKeys,
    HashSet<FormKey> visitedLeveledNpcFormKeys,
    HashSet<FormKey> expandedNpcFormKeysCollector)
{
    if (cache.TryResolve<INpcGetter>(spawnTargetFk, out var npc))
    {
        expandedNpcFormKeysCollector.Add(npc.FormKey);
        AddHerdKeywordsFromFaunaNpcAndAncestors(npc, cache, herdKeySet, herdsOnPlanet, visitedNpcFormKeys);
        return;
    }

    if (cache.TryResolve<ILeveledNpcGetter>(spawnTargetFk, out var lev))
        AddHerdFromLeveledNpcForPlanetFauna(
            lev, cache, herdKeySet, herdsOnPlanet, visitedNpcFormKeys, visitedLeveledNpcFormKeys, expandedNpcFormKeysCollector);
}

static void AddHerdFromLeveledNpcForPlanetFauna(
    ILeveledNpcGetter lev,
    ILinkCache cache,
    IReadOnlySet<FormKey> herdKeySet,
    HashSet<FormKey> herdsOnPlanet,
    HashSet<FormKey> visitedNpcFormKeys,
    HashSet<FormKey> visitedLeveledNpcFormKeys,
    HashSet<FormKey> expandedNpcFormKeysCollector)
{
    if (!visitedLeveledNpcFormKeys.Add(lev.FormKey)) return;

    foreach (var row in lev.Entries ?? [])
    {
        if (row.Reference.IsNull) continue;
        AddHerdFromPlanetFaunaSpawnTarget(
            row.Reference.FormKey,
            cache,
            herdKeySet,
            herdsOnPlanet,
            visitedNpcFormKeys,
            visitedLeveledNpcFormKeys,
            expandedNpcFormKeysCollector);
    }
}

/// <summary>Resolves the same <see cref="INpcSpawn"/> graph as <see cref="AddHerdFromPlanetFaunaSpawnTarget"/> but only collects leaf <see cref="INpcGetter"/> FormKeys.</summary>
static void CollectNpcFormKeysFromFaunaSpawnTarget(
    FormKey spawnTargetFk,
    ILinkCache cache,
    HashSet<FormKey> visitedLeveledNpcFormKeys,
    HashSet<FormKey> outNpcFormKeys)
{
    if (cache.TryResolve<INpcGetter>(spawnTargetFk, out var npc))
    {
        outNpcFormKeys.Add(npc.FormKey);
        return;
    }

    if (!cache.TryResolve<ILeveledNpcGetter>(spawnTargetFk, out var lev)) return;
    if (!visitedLeveledNpcFormKeys.Add(lev.FormKey)) return;

    foreach (var row in lev.Entries ?? [])
    {
        if (row.Reference.IsNull) continue;
        CollectNpcFormKeysFromFaunaSpawnTarget(row.Reference.FormKey, cache, visitedLeveledNpcFormKeys, outNpcFormKeys);
    }
}

static int RunInspectPenHerdPlanets(StarfieldExploreSession session)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;

    var herdKwEdid = BuildActorTypeHerdKeywordEdidByFormKey(mod);
    var herdKeySet = herdKwEdid.Keys.ToHashSet();
    var herdToPlanets = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var hk in herdKeySet)
        herdToPlanets[hk] = [];

    var planetToHerds = new Dictionary<FormKey, HashSet<FormKey>>();
    var planetFaunaNpcFormKeys = new HashSet<FormKey>();
    var planetsWithFaunaRows = 0;
    var faunaEntryRows = 0;
    var faunaTopLevelNpc = 0;
    var faunaTopLevelLeveledNpc = 0;
    var faunaUnresolvedSpawnTarget = 0;

    foreach (var planet in mod.Planets)
    {
        var herdsOnPlanet = new HashSet<FormKey>();
        var biomes = planet.Biomes;
        if (biomes is null) continue;
        var anyFaunaRow = false;
        foreach (var pb in biomes)
        {
            var fauna = pb.Fauna;
            if (fauna is null || fauna.Count == 0) continue;
            foreach (var link in fauna)
            {
                if (link.IsNull) continue;
                anyFaunaRow = true;
                faunaEntryRows++;
                var fk = link.FormKey;
                if (cache.TryResolve<INpcGetter>(fk, out _))
                    faunaTopLevelNpc++;
                else if (cache.TryResolve<ILeveledNpcGetter>(fk, out _))
                    faunaTopLevelLeveledNpc++;
                else
                    faunaUnresolvedSpawnTarget++;

                var visitedNpc = new HashSet<FormKey>();
                var visitedLeveled = new HashSet<FormKey>();
                AddHerdFromPlanetFaunaSpawnTarget(
                    fk,
                    cache,
                    herdKeySet,
                    herdsOnPlanet,
                    visitedNpc,
                    visitedLeveled,
                    planetFaunaNpcFormKeys);
            }
        }

        if (!anyFaunaRow) continue;
        planetsWithFaunaRows++;
        if (herdsOnPlanet.Count == 0) continue;
        planetToHerds[planet.FormKey] = herdsOnPlanet;
        foreach (var h in herdsOnPlanet)
            herdToPlanets[h].Add(planet.FormKey);
    }

    Console.WriteLine(
        "Fauna pen herd tiers vs planet data: vanilla **`FaunaCreation`** uses **`CreatureKeyword`** = **`ActorTypeHerdLarge`** / **`Medium`** / **`Small`**. " +
        "Here: **`Planet` → `PlanetBiome.Fauna`** (form links typed **`INpcGetter`**, but each **`FormKey`** is resolved as **`INpcSpawn`**: **`Npc`** or **`LeveledNpc`**, **`LeveledNpc`** expanded recursively) → herd keywords on each resolved **`Npc`** (same NPC/race/KeywordsTemplate/DefaultTemplate rules). " +
        "Does **not** model full-scan unlock (player progression).");
    Console.WriteLine();

    if (herdKwEdid.Count == 0)
    {
        Console.WriteLine("(no ActorTypeHerd* keywords in Keywords group — unexpected)");
        return 0;
    }

    var npcFormKeysWithHerdKeyword = new HashSet<FormKey>();
    foreach (var n in mod.Npcs)
    {
        var tierScratch = new HashSet<FormKey>();
        var visitedNpc = new HashSet<FormKey>();
        AddHerdKeywordsFromFaunaNpcAndAncestors(n, cache, herdKeySet, tierScratch, visitedNpc);
        if (tierScratch.Count > 0)
            npcFormKeysWithHerdKeyword.Add(n.FormKey);
    }

    var planetFaunaListedWithHerd = planetFaunaNpcFormKeys.Count(npcFormKeysWithHerdKeyword.Contains);

    Console.WriteLine(
        $"Coverage: distinct **`Npc`** FormKeys reachable from planet fauna (after **`LeveledNpc`** expansion): {planetFaunaNpcFormKeys.Count}  |  " +
        $"NPC records in plugin with ≥1 ActorTypeHerd* (same NPC/race/KeywordsTemplate/DefaultTemplate rules): {npcFormKeysWithHerdKeyword.Count}  |  " +
        $"intersection: {planetFaunaListedWithHerd}");
    if (planetFaunaListedWithHerd == 0 && npcFormKeysWithHerdKeyword.Count > 0)
        Console.WriteLine(
            "(Herd keywords exist on some NPCs, but not on any planet fauna–reachable **`Npc`** in this plugin — pen logic may use a different graph, or DLC/overrides.)");
    Console.WriteLine();

    var raceToHerdTiers = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var herdNpcFk in npcFormKeysWithHerdKeyword)
    {
        if (!cache.TryResolve<INpcGetter>(herdNpcFk, out var hn) || hn.Race.IsNull) continue;
        var raceFk = hn.Race.FormKey;
        if (!raceToHerdTiers.TryGetValue(raceFk, out var tierSet))
            raceToHerdTiers[raceFk] = tierSet = [];
        var scratch = new HashSet<FormKey>();
        var vn = new HashSet<FormKey>();
        AddHerdKeywordsFromFaunaNpcAndAncestors(hn, cache, herdKeySet, scratch, vn);
        foreach (var t in scratch)
            tierSet.Add(t);
    }

    var planetToHerdsRaceBridge = new Dictionary<FormKey, HashSet<FormKey>>();
    var herdToPlanetsRace = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var hk in herdKeySet)
        herdToPlanetsRace[hk] = [];

    foreach (var planet in mod.Planets)
    {
        var npcsHere = new HashSet<FormKey>();
        foreach (var pb in planet.Biomes ?? [])
        {
            foreach (var link in pb.Fauna ?? [])
            {
                if (link.IsNull) continue;
                var vl = new HashSet<FormKey>();
                CollectNpcFormKeysFromFaunaSpawnTarget(link.FormKey, cache, vl, npcsHere);
            }
        }

        if (npcsHere.Count == 0) continue;
        var tiers = new HashSet<FormKey>();
        foreach (var nfk in npcsHere)
        {
            if (!cache.TryResolve<INpcGetter>(nfk, out var pn) || pn.Race.IsNull) continue;
            if (raceToHerdTiers.TryGetValue(pn.Race.FormKey, out var fromRace))
            {
                foreach (var t in fromRace)
                    tiers.Add(t);
            }
        }

        if (tiers.Count == 0) continue;
        planetToHerdsRaceBridge[planet.FormKey] = tiers;
        foreach (var t in tiers)
            herdToPlanetsRace[t].Add(planet.FormKey);
    }

    var racesWithHerd = raceToHerdTiers.Count;
    var racesOnPlanetFauna = new HashSet<FormKey>();
    foreach (var nfk in planetFaunaNpcFormKeys)
    {
        if (cache.TryResolve<INpcGetter>(nfk, out var pn) && !pn.Race.IsNull)
            racesOnPlanetFauna.Add(pn.Race.FormKey);
    }

    var raceOverlapCount = racesOnPlanetFauna.Count(raceToHerdTiers.ContainsKey);

    Console.WriteLine(
        "Race bridge (heuristic): if any **`Npc`** with **`ActorTypeHerd*`** shares a **`Race`** FormKey with a planet fauna **`Npc`**, union those herd tiers onto the planet. " +
        "Not guaranteed to match runtime pen filtering (same race can split herd behavior).");
    Console.WriteLine(
        $"  Races that carry herd tiers (via herd-tagged NPCs): {racesWithHerd}  |  distinct races on planet fauna NPCs: {racesOnPlanetFauna.Count}  |  overlapping races: {raceOverlapCount}");
    Console.WriteLine(
        $"  Planets with ≥1 tier via race bridge: {planetToHerdsRaceBridge.Count}");
    Console.WriteLine();
    Console.WriteLine("  Per herd keyword — planets (race bridge):");
    foreach (var hk in herdKwEdid.Keys.OrderBy(k => herdKwEdid[k] ?? "", StringComparer.Ordinal))
        Console.WriteLine($"    {herdKwEdid[hk]}  →  {herdToPlanetsRace[hk].Count} planet(s)");
    Console.WriteLine();
    Console.WriteLine("  Sample planets (up to 20 by EditorID), race-bridge tiers:");
    foreach (var planet in mod.Planets
        .Where(p => planetToHerdsRaceBridge.ContainsKey(p.FormKey))
        .OrderBy(p => p.EditorID, StringComparer.Ordinal)
        .Take(20))
    {
        var names = planetToHerdsRaceBridge[planet.FormKey]
            .OrderBy(k => herdKwEdid[k] ?? "", StringComparer.Ordinal)
            .Select(k => herdKwEdid[k] ?? k.ToString());
        Console.WriteLine($"    {planet.EditorID}  ({planet.FormKey})  →  {string.Join(", ", names)}");
    }

    if (planetToHerdsRaceBridge.Count == 0)
        Console.WriteLine("    (none — no race overlap between planet fauna NPCs and herd-tagged NPCs.)");
    Console.WriteLine();

    Console.WriteLine("ActorTypeHerd* keywords:");
    foreach (var kv in herdKwEdid.OrderBy(x => x.Value, StringComparer.Ordinal))
        Console.WriteLine($"  {kv.Key}  {kv.Value}");

    Console.WriteLine();
    Console.WriteLine(
        $"Planets with ≥1 PlanetBiome.Fauna row: {planetsWithFaunaRows}  |  fauna entry rows (non-null link): {faunaEntryRows}  |  " +
        $"top-level **`INpcSpawn`** → **`Npc`**: {faunaTopLevelNpc}  |  → **`LeveledNpc`**: {faunaTopLevelLeveledNpc}  |  unresolved: {faunaUnresolvedSpawnTarget}");
    Console.WriteLine($"Planets with ≥1 fauna row carrying ActorTypeHerd* (after resolution): {planetToHerds.Count}");

    Console.WriteLine();
    Console.WriteLine("Per herd keyword — how many planets have ≥1 fauna row whose NPC/race/template carries that keyword:");
    foreach (var hk in herdKwEdid.Keys.OrderBy(k => herdKwEdid[k] ?? "", StringComparer.Ordinal))
    {
        var ed = herdKwEdid[hk];
        Console.WriteLine($"  {ed}  →  {herdToPlanets[hk].Count} planet(s)");
    }

    Console.WriteLine();
    Console.WriteLine("Sample planets (up to 40 by EditorID) with herd tiers present:");
    var sample = mod.Planets
        .Where(p => planetToHerds.ContainsKey(p.FormKey))
        .OrderBy(p => p.EditorID, StringComparer.Ordinal)
        .Take(40)
        .ToList();
    foreach (var planet in sample)
    {
        var tierNames = planetToHerds[planet.FormKey]
            .OrderBy(k => herdKwEdid[k] ?? "", StringComparer.Ordinal)
            .Select(k => herdKwEdid[k] ?? k.ToString());
        Console.WriteLine($"  {planet.EditorID}  ({planet.FormKey})  →  {string.Join(", ", tierNames)}");
    }

    if (sample.Count == 0)
        Console.WriteLine("  (none — see Coverage line above; planet fauna rows may not reference herd-tagged NPCs in this plugin.)");

    Console.WriteLine();

    return 0;
}

static int RunInspectPlanetFloraMiscSubstr(StarfieldExploreSession session, string substr)
{
    var mod = session.StarfieldEsm;
    var floraEdid = mod.Florae.ToDictionary(x => x.FormKey, x => x.EditorID);
    var map = BuildPlanetFloraByResourceMisc(mod, floraEdid);
    var miscByKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var hits = new List<string>();
    foreach (var fk in map.Keys)
    {
        if (!miscByKey.TryGetValue(fk, out var misc)) continue;
        var e = misc.EditorID;
        if (e is not null && e.Contains(substr, StringComparison.OrdinalIgnoreCase))
            hits.Add($"{fk}  {e}  ({map[fk].Count} rows)");
    }

    hits.Sort(StringComparer.Ordinal);
    Console.WriteLine($"PlanetFlora.Resource misc EditorIDs containing \"{substr}\" ({hits.Count}):");
    foreach (var h in hits)
        Console.WriteLine($"  {h}");

    return 0;
}

static int RunInspectPlanetFloraForMisc(StarfieldExploreSession session, string miscEdid)
{
    var mod = session.StarfieldEsm;
    var floraEdid = mod.Florae.ToDictionary(x => x.FormKey, x => x.EditorID);
    var map = BuildPlanetFloraByResourceMisc(mod, floraEdid);
    var misc = mod.MiscItems.FirstOrDefault(m => m.EditorID == miscEdid);
    if (misc is null)
    {
        Console.Error.WriteLine($"MiscItem {miscEdid} not found.");
        return 1;
    }

    if (!map.TryGetValue(misc.FormKey, out var rows))
    {
        Console.WriteLine($"No PlanetFlora rows with Resource -> {miscEdid} ({misc.FormKey}).");
        return 0;
    }

    Console.WriteLine($"PlanetFlora rows for misc {miscEdid} ({misc.FormKey}): {rows.Count}");
    foreach (var row in rows.Take(40))
        Console.WriteLine($"  Flora {row.FloraKey} EDID={row.FloraEdid}  Planet {row.PlanetKey} EDID={row.PlanetEdid}");
    if (rows.Count > 40)
        Console.WriteLine($"  … {rows.Count - 40} more");

    return 0;
}

}
