using System.Globalization;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using StarfieldExplore.Game;

partial class Program
{
static readonly string[] VanillaPenFaunaContainerEdids =
    ["OutpostBuilderOrganicFauna01", "OutpostBuilderOrganicFauna02", "OutpostBuilderOrganicFauna03"];

/// <summary>
/// Same graph as <see cref="AddHerdKeywordsFromFaunaNpcAndAncestors"/> but against an arbitrary keyword set (pen <c>CreatureKeyword</c> FormKeys).
/// </summary>
static void AddPenCreatureKeywordMatchesFromFaunaNpcAndAncestors(
    INpcGetter npc,
    ILinkCache cache,
    IReadOnlySet<FormKey> penCreatureKeywords,
    HashSet<FormKey> matched,
    HashSet<FormKey> visitedNpcFormKeys)
{
    if (!visitedNpcFormKeys.Add(npc.FormKey))
        return;

    foreach (var lk in npc.Keywords ?? [])
    {
        if (!lk.IsNull && penCreatureKeywords.Contains(lk.FormKey))
            matched.Add(lk.FormKey);
    }

    if (!npc.Race.IsNull && cache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race))
    {
        foreach (var lk in race.Keywords ?? [])
        {
            if (!lk.IsNull && penCreatureKeywords.Contains(lk.FormKey))
                matched.Add(lk.FormKey);
        }
    }

    var ta = npc.TemplateActors;
    if (ta is not null && !ta.KeywordsTemplate.IsNull &&
        cache.TryResolve<INpcGetter>(ta.KeywordsTemplate.FormKey, out var keywordsTemplateNpc))
        AddPenCreatureKeywordMatchesFromFaunaNpcAndAncestors(
            keywordsTemplateNpc, cache, penCreatureKeywords, matched, visitedNpcFormKeys);

    if (!npc.DefaultTemplate.IsNull && cache.TryResolve<INpcGetter>(npc.DefaultTemplate.FormKey, out var parent))
        AddPenCreatureKeywordMatchesFromFaunaNpcAndAncestors(
            parent, cache, penCreatureKeywords, matched, visitedNpcFormKeys);
}

static void CollectLeafFaunaNpcKeysForPlanet(IPlanetGetter planet, ILinkCache cache, HashSet<FormKey> outNpcKeys)
{
    foreach (var pb in planet.Biomes ?? [])
    {
        foreach (var link in pb.Fauna ?? [])
        {
            if (link.IsNull)
                continue;
            var vlev = new HashSet<FormKey>();
            CollectNpcFormKeysFromFaunaSpawnTarget(link.FormKey, cache, vlev, outNpcKeys);
        }
    }
}

/// <summary>
/// Pen-side model: <c>OutpostBuilderOrganicFauna01</c>…<c>03</c> container VMAD → <c>FaunaCreation</c> slots (<c>CreatureKeyword</c> + <c>createCount</c>).
/// </summary>
static int RunInspectPenFaunaTiers(StarfieldExploreSession session)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;

    Console.WriteLine(
        "**A (pen slots):** what each fauna pen **tier** requests via **`FaunaCreation`** (`CreatureKeyword` + `createCount`). " +
        "**B (planet fauna):** `PlanetBiome.Fauna` → leaf **`Npc`** (see `--planet-fauna`). " +
        "**C (outputs):** harvest yields / terminal products — mostly **script + scan state**, not this table. " +
        "**Build menu / COBJ** (Zoology rank, materials) is **global recipe gating**, not “buildable on this planet.”");
    Console.WriteLine();
    Console.WriteLine(
        "TSV: tier container → **`OutpostHarvesterFaunaScript`** **`FaunaCreation`** structs. Empty **CreatureKeyword** = parse gap or unused slot.");
    Console.WriteLine(
        "penTierEdid\tContainerFormKey\tslotIndex\tCreatureKeywordFormKey\tCreatureKeywordEdid\tcreateCount");
    foreach (var edid in VanillaPenFaunaContainerEdids)
    {
        var cont = mod.Containers.FirstOrDefault(c => c.EditorID == edid);
        if (cont is null)
        {
            Console.WriteLine($"{edid}\t(not found)\t\t\t\t");
            continue;
        }

        IVirtualMachineAdapterGetter? vmad;
        try
        {
            vmad = cont.VirtualMachineAdapter;
        }
        catch
        {
            vmad = null;
        }

        var slots = TryExtractFaunaCreationSlots(vmad, "OutpostHarvesterFaunaScript", "FaunaCreation");
        if (slots.Count == 0)
        {
            Console.WriteLine($"{edid}\t{cont.FormKey}\t\t\t\t(no FaunaCreation rows)");
            continue;
        }

        foreach (var row in slots)
        {
            var ck = row.CreatureKeyword;
            var ed = "";
            if (ck is { } fk && !fk.IsNull && cache.TryResolve<IKeywordGetter>(fk, out var kw))
                ed = kw.EditorID ?? "";
            var ccStr = row.CreateCount.HasValue
                ? row.CreateCount.Value.ToString(CultureInfo.InvariantCulture)
                : "";
            Console.WriteLine(
                $"{edid}\t{cont.FormKey}\t{row.SlotIndex}\t{ck}\t{ed}\t{ccStr}");
        }
    }

    return 0;
}

/// <summary>
/// For planets matching <paramref name="hint"/>: count leaf fauna <see cref="INpcGetter"/> rows that carry any <c>CreatureKeyword</c> from each pen tier’s <c>FaunaCreation</c> (same NPC / race / KeywordsTemplate / DefaultTemplate rules as `--inspect-pen-herd-planets`).
/// </summary>
static int RunInspectPlanetFaunaPenBridge(StarfieldExploreSession session, string hint)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var h = hint.Trim();
    if (h.Length == 0)
    {
        Console.Error.WriteLine("Empty planet hint.");
        return 1;
    }

    var tierToKeywords = new Dictionary<string, HashSet<FormKey>>(StringComparer.OrdinalIgnoreCase);
    foreach (var edid in VanillaPenFaunaContainerEdids)
    {
        var cont = mod.Containers.FirstOrDefault(c => c.EditorID == edid);
        var set = new HashSet<FormKey>();
        tierToKeywords[edid] = set;
        if (cont is null)
            continue;
        IVirtualMachineAdapterGetter? vmad;
        try
        {
            vmad = cont.VirtualMachineAdapter;
        }
        catch
        {
            vmad = null;
        }

        foreach (var row in TryExtractFaunaCreationSlots(vmad, "OutpostHarvesterFaunaScript", "FaunaCreation"))
        {
            if (row.CreatureKeyword is { } fk && !fk.IsNull)
                set.Add(fk);
        }
    }

    Console.WriteLine(
        "**Pen → planet bridge:** for each vanilla pen tier container, **`FaunaCreation`** **`CreatureKeyword`** FormKeys; then each **planet** matching the hint and its **leaf fauna Npcs** — count Npcs that **inherit** any of those keywords (Npc / Race / KeywordsTemplate / DefaultTemplate).");
    Console.WriteLine(
        "Does **not** prove in-game pen assignment (scan state, script filters). **COBJ / Zoology** is unrelated to this join.");
    Console.WriteLine();
    Console.WriteLine("Per-tier **CreatureKeyword** FormKeys (from VMAD):");
    foreach (var edid in VanillaPenFaunaContainerEdids)
    {
        var ks = tierToKeywords[edid];
        if (ks.Count == 0)
        {
            Console.WriteLine($"  {edid}: (none — missing container or empty FaunaCreation)");
            continue;
        }

        var parts = ks.OrderBy(x => x.ToString(), StringComparer.Ordinal)
            .Select(fk =>
                cache.TryResolve<IKeywordGetter>(fk, out var kw) && kw.EditorID is not null
                    ? $"{fk}={kw.EditorID}"
                    : fk.ToString());
        Console.WriteLine($"  {edid}: {string.Join(", ", parts)}");
    }

    Console.WriteLine();

    var matches = mod.Planets
        .Where(p =>
            p.EditorID?.Contains(h, StringComparison.OrdinalIgnoreCase) == true
            || p.FormKey.ToString().Contains(h, StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p.EditorID, StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (matches.Count == 0)
    {
        Console.WriteLine($"No planets matching hint \"{h}\".");
        return 0;
    }

    Console.WriteLine(
        "TSV: **PlanetEditorID** | distinct **leaf fauna Npc** count | for each pen tier, count of those Npcs with **≥1** matching **CreatureKeyword**");
    Console.WriteLine(
        "PlanetFormKey\tPlanetEditorID\tLeafFaunaNpcCount\tMatch_" +
        string.Join("\tMatch_", VanillaPenFaunaContainerEdids));

    foreach (var planet in matches)
    {
        var leaf = new HashSet<FormKey>();
        CollectLeafFaunaNpcKeysForPlanet(planet, cache, leaf);
        var counts = new int[VanillaPenFaunaContainerEdids.Length];
        for (var ti = 0; ti < VanillaPenFaunaContainerEdids.Length; ti++)
        {
            var kwSet = tierToKeywords[VanillaPenFaunaContainerEdids[ti]];
            if (kwSet.Count == 0)
                continue;
            foreach (var nfk in leaf)
            {
                if (!cache.TryResolve<INpcGetter>(nfk, out var npc))
                    continue;
                var hit = new HashSet<FormKey>();
                var vis = new HashSet<FormKey>();
                AddPenCreatureKeywordMatchesFromFaunaNpcAndAncestors(npc, cache, kwSet, hit, vis);
                if (hit.Count > 0)
                    counts[ti]++;
            }
        }

        var ped = planet.EditorID ?? "";
        var countStr = string.Join('\t', counts.Select(c => c.ToString(CultureInfo.InvariantCulture)));
        Console.WriteLine($"{planet.FormKey}\t{ped}\t{leaf.Count}\t{countStr}");
    }

    return 0;
}

}
