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
/// <summary>
/// <see cref="IPlanetBiomeGetter.Flora"/> pairs <see cref="IFloraGetter"/> with yield <see cref="IMiscItemGetter"/> (Resource field).
/// </summary>
static Dictionary<FormKey, List<(FormKey FloraKey, string? FloraEdid, FormKey PlanetKey, string? PlanetEdid)>> BuildPlanetFloraByResourceMisc(
    IStarfieldModGetter mod,
    IReadOnlyDictionary<FormKey, string?> floraEdidByFormKey)
{
    var map = new Dictionary<FormKey, List<(FormKey, string?, FormKey, string?)>>();
    foreach (var planet in mod.Planets)
    {
        var biomes = planet.Biomes;
        if (biomes is null) continue;
        foreach (var biome in biomes)
        {
            var pfl = biome.Flora;
            if (pfl is null) continue;
            foreach (var pf in pfl)
            {
                if (pf.Resource.IsNull || pf.Flora.IsNull) continue;
                var miscFk = pf.Resource.FormKey;
                var floraFk = pf.Flora.FormKey;
                floraEdidByFormKey.TryGetValue(floraFk, out var floraEdid);
                if (!map.TryGetValue(miscFk, out var rows))
                {
                    rows = [];
                    map[miscFk] = rows;
                }

                rows.Add((floraFk, floraEdid, planet.FormKey, planet.EditorID));
            }
        }
    }

    return map;
}

/// <summary>
/// Append resource FormKeys from one <see cref="IResourceGenerationDataGetter"/> into the planet/biome index.
/// </summary>
static void AppendBiomeResourceGenFromRgd(
    Dictionary<FormKey, List<(FormKey PlanetKey, string? PlanetEdid, string? BiomeEdid)>> map,
    ILinkCache cache,
    FormKey rgdFk,
    FormKey planetKey,
    string? planetEdid,
    string? biomeEdid)
{
    if (!cache.TryResolve<IResourceGenerationDataGetter>(rgdFk, out var rgd)) return;
    var items = rgd.Items;
    if (items is null) return;
    foreach (var item in items)
    {
        if (item is null || item.Resource.IsNull) continue;
        var resFk = item.Resource.FormKey;
        if (!map.TryGetValue(resFk, out var rows))
        {
            rows = [];
            map[resFk] = rows;
        }

        rows.Add((planetKey, planetEdid, biomeEdid));
    }
}

/// <summary>
/// For each <see cref="IResourceGetter"/> FormKey from <see cref="IPlanetBiomeGetter.ResourceGeneration"/> and from
/// <see cref="IBiomeGetter.ResourceGeneration"/> (list of RGD links) on the referenced biome,
/// → <see cref="IResourceGenerationDataGetter.Items"/>[].<see cref="IResourceGenerationDataItemGetter.Resource"/>.
/// Survey-style inorganics usually live on <see cref="IBiomeGetter"/>, not <see cref="IPlanetBiomeGetter"/>.
/// </summary>
static Dictionary<FormKey, List<(FormKey PlanetKey, string? PlanetEdid, string? BiomeEdid)>> BuildBiomeResourceGenByResourceFormKey(
    IStarfieldModGetter mod,
    ILinkCache cache)
{
    var map = new Dictionary<FormKey, List<(FormKey, string?, string?)>>();
    foreach (var planet in mod.Planets)
    {
        var biomes = planet.Biomes;
        if (biomes is null) continue;
        foreach (var biome in biomes)
        {
            IBiomeGetter? biomeRec = null;
            if (!biome.Biome.IsNull && cache.TryResolve<IBiomeGetter>(biome.Biome.FormKey, out var resolved))
                biomeRec = resolved;
            var biomeEdid = biomeRec?.EditorID;

            var rgPb = biome.ResourceGeneration;
            if (!rgPb.IsNull)
                AppendBiomeResourceGenFromRgd(map, cache, rgPb.FormKey, planet.FormKey, planet.EditorID, biomeEdid);

            var rgList = biomeRec?.ResourceGeneration;
            if (rgList is null) continue;
            foreach (var link in rgList)
            {
                if (link.IsNull) continue;
                AppendBiomeResourceGenFromRgd(map, cache, link.FormKey, planet.FormKey, planet.EditorID, biomeEdid);
            }
        }
    }

    return map;
}

/// <summary>
/// Full scan of <see cref="IStarfieldModGetter.ResourceGenerationData"/>: each distinct
/// <see cref="IResourceGenerationDataGetter"/> FormKey that lists <paramref name="resourceFk"/> in <c>Items[].Resource</c>.
/// </summary>
static Dictionary<FormKey, HashSet<FormKey>> BuildResourceToRgdFormKeysFullScan(IStarfieldModGetter mod)
{
    var map = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var rgd in mod.ResourceGenerationData)
    {
        var items = rgd.Items;
        if (items is null) continue;
        foreach (var item in items)
        {
            if (item is null) continue;
            if (item.Resource.IsNull) continue;
            var rf = item.Resource.FormKey;
            if (!map.TryGetValue(rf, out var set))
            {
                set = [];
                map[rf] = set;
            }

            set.Add(rgd.FormKey);
        }
    }

    return map;
}

/// <summary>
/// Planets whose <see cref="IFormLinkContainerGetter.EnumerateFormLinks"/> (recursive) touches any FormKey in <paramref name="targetKeys"/>.
/// </summary>
static List<(FormKey PlanetKey, string? PlanetEdid, List<string> PathHints)> FindPlanetsWithFormLinksToKeys(
    IStarfieldModGetter mod,
    IReadOnlySet<FormKey> targetKeys)
{
    var list = new List<(FormKey, string?, List<string>)>();
    if (targetKeys.Count == 0) return list;

    foreach (var planet in mod.Planets)
    {
        if (planet is not IFormLinkContainerGetter flc) continue;
        var hints = new List<string>();
        try
        {
            foreach (var raw in flc.EnumerateFormLinks(true))
            {
                if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var pathHint)) continue;
                if (!targetKeys.Contains(fk)) continue;
                var label = string.IsNullOrEmpty(pathHint) ? fk.ToString() : $"{pathHint} → {fk}";
                hints.Add(label);
            }
        }
        catch
        {
            hints.Add("(EnumerateFormLinks threw — skipped rest for this planet)");
        }

        if (hints.Count > 0)
            list.Add((planet.FormKey, planet.EditorID, hints));
    }

    return list;
}

/// <summary>
/// Mutagen’s link enumerator yields an internal item type; resolve a <see cref="FormKey"/> and optional path hint via reflection.
/// </summary>
static bool TryGetFormKeyFromLinkEnumerationItem(object? item, out FormKey fk, out string? pathHint)
{
    fk = default;
    pathHint = null;
    if (item is null) return false;
    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
    var t = item.GetType();
    foreach (var p in t.GetProperties(flags))
    {
        if (p.PropertyType == typeof(string)
            && p.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
            && p.GetValue(item) is string s
            && !string.IsNullOrEmpty(s))
            pathHint = s;
    }

    foreach (var p in t.GetProperties(flags))
    {
        var val = p.GetValue(item);
        if (val is null) continue;
        var vt = val.GetType();
        var isNullProp = vt.GetProperty("IsNull");
        if (isNullProp?.PropertyType == typeof(bool) && (bool)isNullProp.GetValue(val)! == true) continue;
        var fkProp = vt.GetProperty("FormKey");
        if (fkProp?.GetValue(val) is FormKey f && f != default)
        {
            fk = f;
            return true;
        }
    }

    foreach (var p in t.GetProperties(flags))
    {
        if (p.PropertyType != typeof(FormKey)) continue;
        if (p.GetValue(item) is FormKey f2 && f2 != default)
        {
            fk = f2;
            return true;
        }
    }

    return false;
}

/// <summary>
/// For each COBJ output (<see cref="IConstructibleObjectGetter.CreatedObject"/>), all <see cref="ConstructableComponents"/> FormKeys.
/// Used to walk backward: harvest ingredients often match an *input* to a refinery recipe, not the chemlab resource/misc directly.
/// </summary>
static Dictionary<FormKey, HashSet<FormKey>> BuildCobjOutputToInputs(IStarfieldModGetter mod)
{
    var map = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var c in mod.ConstructibleObjects)
    {
        var created = c.CreatedObject;
        if (created.IsNull) continue;
        var o = created.FormKey;
        if (!map.TryGetValue(o, out var set))
        {
            set = [];
            map[o] = set;
        }

        foreach (var line in c.ConstructableComponents ?? [])
        {
            var comp = line.Component?.FormKey;
            if (comp.HasValue)
                set.Add(comp.Value);
        }
    }

    return map;
}

/// <summary>
/// Union of <paramref name="seeds"/> with every FormKey that appears as a COBJ component feeding into any form already in the set (fixpoint).
/// </summary>
static HashSet<FormKey> ExpandPrecursorFormKeys(
    IEnumerable<FormKey> seeds,
    IReadOnlyDictionary<FormKey, HashSet<FormKey>> cobjOutputToInputs)
{
    var expanded = new HashSet<FormKey>();
    var queue = new Queue<FormKey>();
    foreach (var s in seeds)
    {
        if (expanded.Add(s))
            queue.Enqueue(s);
    }

    while (queue.Count > 0)
    {
        var k = queue.Dequeue();
        if (!cobjOutputToInputs.TryGetValue(k, out var inputs)) continue;
        foreach (var inn in inputs)
        {
            if (expanded.Add(inn))
                queue.Enqueue(inn);
        }
    }

    return expanded;
}

/// <summary>
/// Maps item-like FormKey (misc, ingestible, resource, …) to NPCs whose <see cref="INpcGetter.DeathItem"/> expands to that form.
/// </summary>
static Dictionary<FormKey, List<(FormKey NpcKey, string? Edid)>> BuildLootNpcIndex(
    IStarfieldModGetter mod,
    ILinkCache cache)
{
    var map = new Dictionary<FormKey, List<(FormKey, string?)>>();
    foreach (var npc in mod.Npcs)
    {
        var death = npc.DeathItem;
        if (death.IsNull) continue;

        var itemKeys = new HashSet<FormKey>();
        var levVisited = new HashSet<FormKey>();
        ExpandItemKeysFromFormKey(death.FormKey, cache, levVisited, itemKeys);

        foreach (var fk in itemKeys)
        {
            if (!map.TryGetValue(fk, out var list))
            {
                list = [];
                map[fk] = list;
            }

            list.Add((npc.FormKey, npc.EditorID));
        }
    }

    return map;
}

static void ExpandItemKeysFromFormKey(
    FormKey fk,
    ILinkCache cache,
    HashSet<FormKey> leveledVisited,
    HashSet<FormKey> itemLikeKeys)
{
    if (cache.TryResolve<ILeveledItemGetter>(fk, out var lev))
    {
        if (!leveledVisited.Add(fk)) return;
        var entries = lev.Entries;
        if (entries is null) return;
        foreach (var entry in entries)
        {
            var r = entry?.Reference;
            if (r is null || r.IsNull) continue;
            ExpandItemKeysFromFormKey(r.FormKey, cache, leveledVisited, itemLikeKeys);
        }

        return;
    }

    if (cache.TryResolve<IMiscItemGetter>(fk, out _)
        || cache.TryResolve<IIngestibleGetter>(fk, out _)
        || cache.TryResolve<IResourceGetter>(fk, out _))
        itemLikeKeys.Add(fk);
}

/// <summary>Records whose <see cref="IFormLinkContainerGetter.EnumerateFormLinks"/> (recursive) touches a target FormKey set.</summary>
static Dictionary<FormKey, List<(string Group, IMajorRecordGetter Rec)>> BuildBacklinksToFormKeys(
    IStarfieldModGetter mod,
    IReadOnlySet<FormKey> targets)
{
    var map = new Dictionary<FormKey, List<(string Group, IMajorRecordGetter Rec)>>();
    if (targets.Count == 0) return map;

    void Consider(IMajorRecordGetter rec, string group)
    {
        if (rec is not IFormLinkContainerGetter flc) return;
        try
        {
            foreach (var raw in flc.EnumerateFormLinks(true))
            {
                if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out _)) continue;
                if (fk == default || fk.IsNull || !targets.Contains(fk)) continue;
                if (!map.TryGetValue(fk, out var list))
                {
                    list = [];
                    map[fk] = list;
                }

                if (list.Exists(x => x.Rec.FormKey == rec.FormKey)) continue;
                list.Add((group, rec));
            }
        }
        catch
        {
            /* skip broken record */
        }
    }

    foreach (var pk in mod.PackIns)
        Consider(pk, "PackIn");
    foreach (var a in mod.Activators)
        Consider(a, "Activator");
    foreach (var f in mod.Furniture)
        Consider(f, "Furniture");

    return map;
}

/// <summary>
/// Every enumerable major-record group on <paramref name="mod"/> (reflection on <see cref="IStarfieldModGetter"/>).
/// </summary>
static IEnumerable<(string Group, IMajorRecordGetter Rec)> EnumerateMajorRecords(IStarfieldModGetter mod)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
    foreach (var prop in mod.GetType().GetProperties(flags))
    {
        if (prop.GetIndexParameters().Length != 0) continue;
        if (!typeof(IEnumerable).IsAssignableFrom(prop.PropertyType)) continue;
        if (prop.PropertyType == typeof(string)) continue;
        object? val;
        try
        {
            val = prop.GetValue(mod);
        }
        catch
        {
            continue;
        }

        if (val is not IEnumerable seq) continue;

        var batch = new List<(string Group, IMajorRecordGetter Rec)>();
        try
        {
            foreach (var item in seq)
            {
                if (item is IMajorRecordGetter maj)
                    batch.Add((prop.Name, maj));
            }
        }
        catch
        {
            /* group enumeration can throw on some overlay shapes */
        }

        foreach (var pair in batch)
            yield return pair;
    }
}

/// <summary>
/// Like <see cref="BuildBacklinksToFormKeys"/> but scans every <see cref="IFormLinkContainerGetter"/> major record on the mod (slow; for research).
/// </summary>
static Dictionary<FormKey, List<(string Group, IMajorRecordGetter Rec)>> BuildFormLinkBacklinksToFormKeysFullScan(
    IStarfieldModGetter mod,
    IReadOnlySet<FormKey> targets)
{
    var map = new Dictionary<FormKey, List<(string Group, IMajorRecordGetter Rec)>>();
    if (targets.Count == 0) return map;

    foreach (var (group, rec) in EnumerateMajorRecords(mod))
    {
        if (rec is not IFormLinkContainerGetter flc) continue;
        try
        {
            foreach (var raw in flc.EnumerateFormLinks(true))
            {
                if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out _)) continue;
                if (fk == default || fk.IsNull || !targets.Contains(fk)) continue;
                if (!map.TryGetValue(fk, out var list))
                {
                    list = [];
                    map[fk] = list;
                }

                if (list.Exists(x => x.Rec.FormKey == rec.FormKey && x.Group == group)) continue;
                list.Add((Group: group, Rec: rec));
            }
        }
        catch
        {
            /* skip broken record */
        }
    }

    return map;
}


}
