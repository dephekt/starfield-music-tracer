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
static int RunInspectCobjsForOutputMisc(StarfieldExploreSession session, string miscEdid)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);
    var misc = mod.MiscItems.FirstOrDefault(m => m.EditorID == miscEdid);
    if (misc is null)
    {
        Console.Error.WriteLine($"MiscItem {miscEdid} not found.");
        return 1;
    }

    var fk = misc.FormKey;
    var hits = mod.ConstructibleObjects.Where(c => c.CreatedObject.FormKey == fk).ToList();
    Console.WriteLine($"COBJs with CreatedObject -> {miscEdid} ({fk}): {hits.Count}");
    foreach (var c in hits)
    {
        Console.WriteLine($"  {c.FormKey} EDID={c.EditorID}");
        foreach (var line in c.ConstructableComponents ?? [])
        {
            var comp = line.Component;
            if (comp is null || comp.IsNull) continue;
            Console.WriteLine(
                $"    <- {comp.FormKey}  ({DescribeComponent(cache, comp.FormKey, miscByFormKey, constructibleByFormKey)})");
        }
    }

    return 0;
}

static int RunInspectResourceGenForResource(StarfieldExploreSession session, string resourceEdid)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var res = mod.Resources.FirstOrDefault(x => x.EditorID == resourceEdid);
    if (res is null)
    {
        Console.Error.WriteLine($"Resource {resourceEdid} not found.");
        return 1;
    }

    Console.WriteLine($"=== Resource {resourceEdid} ({res.FormKey}) — ResourceGenerationData ===");
    var resourceToRgd = BuildResourceToRgdFormKeysFullScan(mod);
    resourceToRgd.TryGetValue(res.FormKey, out var rgdKeySet);
    rgdKeySet ??= [];
    var rgdByKey = mod.ResourceGenerationData.ToDictionary(x => x.FormKey);
    Console.WriteLine(
        $"Distinct ResourceGenerationData records listing this resource in Items[].Resource: {rgdKeySet.Count} (of {mod.ResourceGenerationData.Count()} total RGD in ESM)");
    foreach (var rfk in rgdKeySet.OrderBy(x => x.ToString(), StringComparer.Ordinal))
    {
        rgdByKey.TryGetValue(rfk, out var rgd);
        var rowCount = 0;
        if (rgd?.Items is not null)
        {
            foreach (var it in rgd.Items)
            {
                if (it is null || it.Resource.IsNull) continue;
                if (it.Resource.FormKey == res.FormKey)
                    rowCount++;
            }
        }

        Console.WriteLine($"  RGD {rfk}  EDID={rgd?.EditorID}  ({rowCount} item row(s) for this resource)");
    }

    Console.WriteLine();
    Console.WriteLine("PlanetBiome + IBiomeGetter ResourceGeneration → RGD (same as main trace index):");
    var map = BuildBiomeResourceGenByResourceFormKey(mod, cache);
    if (!map.TryGetValue(res.FormKey, out var rows) || rows.Count == 0)
        Console.WriteLine($"  (none)");
    else
    {
        Console.WriteLine($"  {rows.Count} biome row(s):");
        foreach (var row in rows.Take(50))
            Console.WriteLine($"    Planet {row.PlanetKey} EDID={row.PlanetEdid}  Biome EDID={row.BiomeEdid}");
        if (rows.Count > 50)
            Console.WriteLine($"    … {rows.Count - 50} more");
    }

    Console.WriteLine();
    Console.WriteLine(
        "Planets with any FormLink (EnumerateFormLinks nested) to those RGD FormKeys — catches SurfaceTree / Details / etc., not only Biomes:");
    var planetRefs = FindPlanetsWithFormLinksToKeys(mod, rgdKeySet);
    if (planetRefs.Count == 0)
        Console.WriteLine("  (none — RGD may be unused, or links live outside IPlanet)");
    else
    {
        foreach (var pr in planetRefs.OrderBy(p => p.PlanetEdid, StringComparer.Ordinal).Take(40))
        {
            var sample = string.Join(" | ", pr.PathHints.Take(4));
            var more = pr.PathHints.Count > 4 ? $" (+{pr.PathHints.Count - 4} more link paths)" : "";
            Console.WriteLine(
                $"  Planet {pr.PlanetKey} EDID={pr.PlanetEdid}  links: {pr.PathHints.Count}  e.g. {sample}{more}");
        }

        if (planetRefs.Count > 40)
            Console.WriteLine($"  … {planetRefs.Count - 40} more planets");
    }

    return 0;
}

static int RunInspectResource(StarfieldExploreSession session, string resourceEdid)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);
    var r = mod.Resources.FirstOrDefault(x => x.EditorID == resourceEdid);
    if (r is null)
    {
        Console.Error.WriteLine($"Resource {resourceEdid} not found.");
        return 1;
    }

    Console.WriteLine($"Resource {r.FormKey} EDID={r.EditorID} ResourceType={r.ResourceType}");
    Console.WriteLine($"  Produce: {(r.Produce.IsNull ? "(null)" : r.Produce.FormKey.ToString())}");
    Console.WriteLine($"  List:    {(r.List.IsNull ? "(null)" : r.List.FormKey.ToString())}");
    var kws = r.Keywords;
    if (kws is { Count: > 0 })
    {
        Console.WriteLine($"  Keywords ({kws.Count}):");
        foreach (var kw in kws.Take(30))
        {
            if (kw.IsNull) continue;
            if (cache.TryResolve<IKeywordGetter>(kw.FormKey, out var kg))
                Console.WriteLine($"    {kw.FormKey}  EDID={kg.EditorID}");
            else
                Console.WriteLine($"    {kw.FormKey}");
        }

        if (kws.Count > 30)
            Console.WriteLine($"    … {kws.Count - 30} more");
    }

    if (!r.List.IsNull && cache.TryResolve<ILeveledItemGetter>(r.List.FormKey, out var lev))
    {
        Console.WriteLine($"  List entries ({lev.Entries?.Count ?? 0}):");
        var entries = lev.Entries;
        if (entries is not null)
        {
            foreach (var e in entries.Take(40))
            {
                var rr = e?.Reference;
                if (rr is null || rr.IsNull) continue;
                var fk = rr.FormKey;
                Console.WriteLine(
                    $"    - {fk}  ({DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey)})");
            }

            if (entries.Count > 40)
                Console.WriteLine($"    … {entries.Count - 40} more");
        }
    }

    return 0;
}

static int RunInspectCobj(StarfieldExploreSession session, string cobjEdid)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var c = mod.ConstructibleObjects.FirstOrDefault(x => x.EditorID == cobjEdid);
    if (c is null)
    {
        Console.Error.WriteLine($"ConstructibleObject {cobjEdid} not found.");
        return 1;
    }

    Console.WriteLine($"COBJ {c.FormKey} EDID={c.EditorID} CreatedObject={c.CreatedObject.FormKey}");
    var n = c.ConstructableComponents?.Count ?? 0;
    Console.WriteLine($"ConstructableComponents count: {n}");
    foreach (var line in c.ConstructableComponents ?? [])
    {
        var comp = line.Component;
        if (comp is null || comp.IsNull) continue;
        var fk = comp.FormKey;
        Console.WriteLine($"  - {fk}  ({DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey)})");
    }

    return 0;
}

}
