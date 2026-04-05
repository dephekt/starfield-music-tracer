using System.Collections;
using System.Globalization;
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
static bool TraceCraftTarget(
    IStarfieldModGetter mod,
    ILinkCache cache,
    string targetIngestibleEdid,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey,
    IReadOnlyDictionary<FormKey, List<(FormKey FloraKey, string? FloraEdid, FormKey PlanetKey, string? PlanetEdid)>> planetFloraByResourceMisc,
    IReadOnlyDictionary<FormKey, List<(FormKey PlanetKey, string? PlanetEdid, string? BiomeEdid)>> biomeResourceGenByResource,
    IReadOnlyDictionary<FormKey, List<(FormKey NpcKey, string? Edid)>> lootNpcsByItemKey,
    IReadOnlyDictionary<FormKey, HashSet<FormKey>> cobjOutputToInputs,
    int listLimit)
{
    var ingestible = mod.Ingestibles.FirstOrDefault(i => i.EditorID == targetIngestibleEdid);
    if (ingestible is null)
    {
        Console.Error.WriteLine($"Ingestible {targetIngestibleEdid} not found.");
        return false;
    }

    Console.WriteLine($"=== {targetIngestibleEdid} ===");
    Console.WriteLine($"Ingestible: {ingestible.FormKey}  EDID={ingestible.EditorID}");

    var cobj = mod.ConstructibleObjects.FirstOrDefault(c => c.CreatedObject.FormKey == ingestible.FormKey);
    if (cobj is null)
    {
        Console.Error.WriteLine($"No ConstructibleObject with CreatedObject -> {ingestible.FormKey}.");
        return false;
    }

    Console.WriteLine($"ConstructibleObject: {cobj.FormKey}  EDID={cobj.EditorID}");
    var wb = cobj.WorkbenchKeyword.IsNull
        ? "(null)"
        : cobj.WorkbenchKeyword.TryResolve<IKeywordGetter>(cache, out var kw)
            ? $"{cobj.WorkbenchKeyword.FormKey}  EDID={kw.EditorID}"
            : cobj.WorkbenchKeyword.FormKey.ToString();
    Console.WriteLine($"  WorkbenchKeyword: {wb}");
    Console.WriteLine($"  CreatedObject:     {cobj.CreatedObject.FormKey}");
    Console.WriteLine("  Components (ConstructableComponents):");

    var componentKeys = new List<FormKey>();
    foreach (var line in cobj.ConstructableComponents ?? [])
    {
        var comp = line.Component ?? throw new InvalidOperationException("COBJ line missing Component");
        componentKeys.Add(comp.FormKey);
        Console.WriteLine($"    - {comp.FormKey}  ({DescribeComponent(cache, comp.FormKey, miscByFormKey, constructibleByFormKey)})");
    }

    Console.WriteLine();
    Console.WriteLine("(Quantities: repeated ConstructableComponents rows for the same FormKey.)");
    var qty = (cobj.ConstructableComponents ?? [])
        .Select(x => x.Component?.FormKey)
        .Where(fk => fk.HasValue)
        .Select(fk => fk!.Value)
        .GroupBy(k => k)
        .Select(g => (g.Key, g.Count()))
        .ToList();
    Console.WriteLine("  Quantities by FormKey:");
    foreach (var (fk, n) in qty)
        Console.WriteLine($"    x{n}  {fk}");

    Console.WriteLine();
    Console.WriteLine("Gather hints (flora + planet resource gen + creature loot; not vendors / outpost husbandry):");

    foreach (var fk in componentKeys.Distinct())
    {
        Console.WriteLine($"  Component {fk}:");
        var gather = ResolveGatherKeys(fk, cache, miscByFormKey, constructibleByFormKey, mod);
        if (gather.ResourceNote is not null)
            Console.WriteLine($"    {gather.ResourceNote}");

        var keysToTry = gather.Keys.ToList();
        if (keysToTry.Count == 0)
        {
            Console.WriteLine("    (no gather lookup keys — investigate record type)");
            continue;
        }

        var planetFloraByFloraKey = new Dictionary<FormKey, (string? FloraEdid, HashSet<string?> Planets)>();
        foreach (var lk in keysToTry)
        {
            if (!planetFloraByResourceMisc.TryGetValue(lk, out var rows)) continue;
            foreach (var row in rows)
            {
                if (!planetFloraByFloraKey.TryGetValue(row.FloraKey, out var agg))
                {
                    agg = (row.FloraEdid, []);
                    planetFloraByFloraKey[row.FloraKey] = agg;
                }

                agg.Planets.Add(row.PlanetEdid);
                if (agg.FloraEdid is null && row.FloraEdid is not null)
                    planetFloraByFloraKey[row.FloraKey] = (row.FloraEdid, agg.Planets);
            }
        }

        if (planetFloraByFloraKey.Count > 0)
        {
            Console.WriteLine(
                "    Flora (planet biome spawn; IPlanetFlora.Resource misc matches gather key — INARA-style):");
            PrintLimited(
                planetFloraByFloraKey
                    .OrderBy(kv => kv.Value.FloraEdid, StringComparer.Ordinal)
                    .Select(kv =>
                    {
                        var planetSample = string.Join(", ", kv.Value.Planets.Where(p => !string.IsNullOrEmpty(p)).Take(4));
                        var more = kv.Value.Planets.Count > 4 ? $" +{kv.Value.Planets.Count - 4} planets" : "";
                        return $"      Flora {kv.Key}  EDID={kv.Value.FloraEdid}  [planets: {planetSample}{more}]";
                    }),
                listLimit);
        }
        else
            Console.WriteLine("    Flora (planet PCM): (no PlanetFlora.Resource hit for these gather keys)");

        var resourceGenByPlanet = new Dictionary<FormKey, (string? PlanetEdid, HashSet<string?> Biomes)>();
        foreach (var lk in keysToTry)
        {
            if (!biomeResourceGenByResource.TryGetValue(lk, out var rgRows)) continue;
            foreach (var row in rgRows)
            {
                if (!resourceGenByPlanet.TryGetValue(row.PlanetKey, out var agg))
                {
                    agg = (row.PlanetEdid, []);
                    resourceGenByPlanet[row.PlanetKey] = agg;
                }

                agg.Biomes.Add(row.BiomeEdid);
                if (agg.PlanetEdid is null && row.PlanetEdid is not null)
                    resourceGenByPlanet[row.PlanetKey] = (row.PlanetEdid, agg.Biomes);
            }
        }

        if (resourceGenByPlanet.Count > 0)
        {
            Console.WriteLine(
                "    Planet / biome resource generation (ResourceGenerationData.Items.Resource; inorganics / survey):");
            PrintLimited(
                resourceGenByPlanet
                    .OrderBy(kv => kv.Value.PlanetEdid, StringComparer.Ordinal)
                    .Select(kv =>
                    {
                        var biomeSample = string.Join(", ", kv.Value.Biomes.Where(b => !string.IsNullOrEmpty(b)).Take(6));
                        var more = kv.Value.Biomes.Count > 6 ? $" +{kv.Value.Biomes.Count - 6} biomes" : "";
                        return $"      Planet {kv.Key}  EDID={kv.Value.PlanetEdid}  [biomes: {biomeSample}{more}]";
                    }),
                listLimit);
        }
        else
            Console.WriteLine(
                "    Planet / biome resource generation: (no ResourceGenerationData.Items.Resource hit for these gather keys)");

        var precursorKeys = ExpandPrecursorFormKeys(keysToTry, cobjOutputToInputs);
        var ingredientHits = new Dictionary<FormKey, (FormKey FloraKey, string? Edid)>();
        foreach (var flora in mod.Florae)
        {
            var ing = flora.Ingredient;
            if (ing.IsNull) continue;
            if (!precursorKeys.Contains(ing.FormKey)) continue;
            ingredientHits.TryAdd(flora.FormKey, (flora.FormKey, flora.EditorID));
        }

        if (ingredientHits.Count > 0)
        {
            Console.WriteLine(
                "    Flora (Flora.Ingredient in COBJ precursor chain; Flora.Production is seasonal weights only):");
            PrintLimited(
                ingredientHits.Values
                    .OrderBy(f => f.Edid, StringComparer.Ordinal)
                    .Select(f => $"      Flora {f.FloraKey}  EDID={f.Edid}"),
                listLimit);
        }

        var lootNpcs = new Dictionary<FormKey, (FormKey NpcKey, string? Edid)>();
        foreach (var lookupKey in keysToTry)
        {
            if (!lootNpcsByItemKey.TryGetValue(lookupKey, out var npcs)) continue;
            foreach (var n in npcs)
                lootNpcs.TryAdd(n.NpcKey, n);
        }

        if (lootNpcs.Count > 0)
        {
            Console.WriteLine(
                "    Looted from creature (Npc.DeathItem → LeveledItem → item; not outpost husbandry whitelist):");
            PrintLimited(
                lootNpcs.Values
                    .OrderBy(n => n.Edid, StringComparer.Ordinal)
                    .Select(n => $"      Npc {n.NpcKey}  EDID={n.Edid}"),
                listLimit);
        }
        else
            Console.WriteLine("    Creature loot: (no Npc.DeathItem expansion hits these keys)");

        if (gather.GasOrExtractorLikely && resourceGenByPlanet.Count == 0)
            Console.WriteLine(
                "    Note: component looks like gas/inorganic; if no ResourceGeneration rows above, check other bodies or mod load order.");
    }

    return true;
}

static (HashSet<FormKey> Keys, string? ResourceNote, bool GasOrExtractorLikely) ResolveGatherKeys(
    FormKey componentFk,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey,
    IStarfieldModGetter mod)
{
    var keys = new HashSet<FormKey> { componentFk };
    string? note = null;
    var gas = false;

    if (cache.TryResolve<IResourceGetter>(componentFk, out var res) && !res.Produce.IsNull)
    {
        var produceFk = res.Produce.FormKey;
        var produceLabel = FormatProduceLabel(produceFk, cache, miscByFormKey, constructibleByFormKey, mod);
        note = $"Resource chain: {res.EditorID}  -> Produce {produceFk} ({produceLabel})";

        keys.Add(produceFk);
        if (constructibleByFormKey.TryGetValue(produceFk, out var nestedCobj))
        {
            var created = nestedCobj.CreatedObject.FormKey;
            keys.Add(created);
            var createdDesc = DescribeComponent(cache, created, miscByFormKey, constructibleByFormKey);
            note += $"; refined misc {created} ({createdDesc})";
            AddOrganPartHarvestMiscKeys(keys, miscByFormKey, created);
        }

        if (res.EditorID?.Contains("Inorg", StringComparison.OrdinalIgnoreCase) == true
            || res.EditorID?.Contains("Argon", StringComparison.OrdinalIgnoreCase) == true)
            gas = true;
    }

    return (keys, note, gas);
}

/// <summary>
/// Planet PCM uses per-organ miscs (e.g. <c>OrgCommonToxin_Leaf</c>) as <see cref="IPlanetFloraGetter.Resource"/>,
/// while chemlab/refinery uses the stackable base misc (<c>OrgCommonToxin</c>). Link them by EditorID prefix <c>{base}_</c>.
/// </summary>
static void AddOrganPartHarvestMiscKeys(
    HashSet<FormKey> keys,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    FormKey refinedStackableMiscFk)
{
    if (!miscByFormKey.TryGetValue(refinedStackableMiscFk, out var baseMisc)) return;
    var bn = baseMisc.EditorID;
    if (string.IsNullOrEmpty(bn)) return;
    var prefix = bn + "_";
    foreach (var m in miscByFormKey.Values)
    {
        if (m.EditorID?.StartsWith(prefix, StringComparison.Ordinal) == true)
            keys.Add(m.FormKey);
    }
}

static string FormatProduceLabel(
    FormKey produceFk,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey,
    IStarfieldModGetter mod)
{
    if (miscByFormKey.TryGetValue(produceFk, out var pm))
        return $"MiscItem EDID={pm.EditorID}";
    if (constructibleByFormKey.TryGetValue(produceFk, out var refineCobj))
        return $"ConstructibleObject EDID={refineCobj.EditorID}  -> CreatedObject {refineCobj.CreatedObject.FormKey}";
    if (!constructibleByFormKey.ContainsKey(produceFk))
    {
        var located = FindMajorRecordGroup(mod, produceFk);
        if (located is not null)
            return $"{DescribeComponent(cache, produceFk, miscByFormKey, constructibleByFormKey)}  |  {located}";
    }

    return DescribeComponent(cache, produceFk, miscByFormKey, constructibleByFormKey);
}

static void PrintLimited(IEnumerable<string> lines, int limit)
{
    if (limit == 0)
    {
        foreach (var line in lines)
            Console.WriteLine(line);
        return;
    }

    var n = 0;
    foreach (var line in lines)
    {
        if (n >= limit)
        {
            Console.WriteLine($"      … (cap {limit}; use --limit=0 for full list)");
            return;
        }

        Console.WriteLine(line);
        n++;
    }
}

}
