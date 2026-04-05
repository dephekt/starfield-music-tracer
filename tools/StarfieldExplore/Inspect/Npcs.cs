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
static int RunInspectNpc(StarfieldExploreSession session, string hint)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var h = hint.Trim();
    if (h.Length == 0)
    {
        Console.Error.WriteLine("Empty npc hint.");
        return 1;
    }

    var matches = mod.Npcs
        .Where(n =>
            n.EditorID?.Contains(h, StringComparison.OrdinalIgnoreCase) == true
            || n.FormKey.ToString().Contains(h, StringComparison.OrdinalIgnoreCase))
        .OrderBy(n => n.EditorID, StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (matches.Count == 0)
    {
        Console.WriteLine($"No Npc matching hint \"{h}\" (EditorID substring or FormKey fragment).");
        return 0;
    }

    Console.WriteLine($"Npc matching \"{h}\": {matches.Count}");
    Console.WriteLine(
        "Prints **Name** (when present), **Race**, **DefaultTemplate** chain, **Keywords**, **DeathItem**. " +
        "Clearer than `PCM_*` slot EDIDs for *classic* creatures; many planet fauna use **CCT** (**`CCT_DummyRace`** → **`CCT_Creature`**) — then there is no single Race EDID like “Coralcrawler”; mesh/variant lives in CCT-related data.");
    Console.WriteLine();

    foreach (var npc in matches)
    {
        Console.WriteLine($"=== Npc {npc.FormKey}  EDID={npc.EditorID} ===");
        Console.WriteLine($"  Name (localized): {FormatNpcLocalizedName(npc)}");

        if (npc.Race.IsNull)
            Console.WriteLine("  Race: (null)");
        else if (cache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race))
        {
            Console.WriteLine($"  Race: {npc.Race.FormKey}  EDID={race.EditorID}");
            var re = race.EditorID ?? "";
            if (re.Contains("CCT_Dummy", StringComparison.OrdinalIgnoreCase)
                || re.Contains("DummyRace", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    "  Note: **CCT** (Creature Creation Toolkit) dummy race — visible species is not this Race EDID; " +
                    "chunk/variant data on **`CCT_Creature`** (and related records) drives the mesh; colloquial names often need in-game UI, wikis, or a deeper CCT export.");
            }
        }
        else
            Console.WriteLine($"  Race: {npc.Race.FormKey}  (resolve failed)");

        Console.WriteLine("  DefaultTemplate chain (Npc → Npc, max depth 8):");
        PrintNpcTemplateChain(cache, npc, 8);

        var ta = npc.TemplateActors;
        if (ta is not null && !ta.KeywordsTemplate.IsNull)
        {
            if (cache.TryResolve<INpcGetter>(ta.KeywordsTemplate.FormKey, out var ktn))
                Console.WriteLine($"  TemplateActors.KeywordsTemplate: {ta.KeywordsTemplate.FormKey}  EDID={ktn.EditorID}");
            else
                Console.WriteLine($"  TemplateActors.KeywordsTemplate: {ta.KeywordsTemplate.FormKey}  (not an Npc?)");
        }

        var kws = npc.Keywords;
        if (kws is null || kws.Count == 0)
            Console.WriteLine("  Keywords: (none on this Npc)");
        else
        {
            Console.WriteLine($"  Keywords ({kws.Count}):");
            foreach (var lk in kws.Take(40))
            {
                if (lk.IsNull) continue;
                if (cache.TryResolve<IKeywordGetter>(lk.FormKey, out var kw))
                    Console.WriteLine($"    {lk.FormKey}  EDID={kw.EditorID}");
                else
                    Console.WriteLine($"    {lk.FormKey}");
            }

            if (kws.Count > 40)
                Console.WriteLine($"    … {kws.Count - 40} more");
        }

        if (npc.DeathItem.IsNull)
            Console.WriteLine("  DeathItem: (null)");
        else if (cache.TryResolve<ILeveledItemGetter>(npc.DeathItem.FormKey, out var li))
            Console.WriteLine($"  DeathItem: {npc.DeathItem.FormKey}  LeveledItem EDID={li.EditorID}");
        else if (cache.TryResolve<IMiscItemGetter>(npc.DeathItem.FormKey, out var mi))
            Console.WriteLine($"  DeathItem: {npc.DeathItem.FormKey}  MiscItem EDID={mi.EditorID}");
        else
            Console.WriteLine($"  DeathItem: {npc.DeathItem.FormKey}  ({DescribeComponent(cache, npc.DeathItem.FormKey)})");

        Console.WriteLine();
    }

    return 0;
}

static string FormatNpcLocalizedName(INpcGetter npc)
{
    try
    {
        var nameGetter = npc.Name;
        if (nameGetter is null)
            return "(null Name — use CK / strings pipeline for display name)";
        var s = nameGetter.String ?? "";
        return string.IsNullOrWhiteSpace(s)
            ? "(empty — check string BA2 / STARFIELD_TARGET_LANGUAGE; Race / DefaultTemplate / CCT keywords below)"
            : s;
    }
    catch (Exception ex)
    {
        return $"({ex.GetType().Name}: {ex.Message})";
    }
}

static void PrintNpcTemplateChain(ILinkCache cache, INpcGetter start, int maxDepth)
{
    var seen = new HashSet<FormKey>();
    INpcGetter? cur = start;
    var depth = 0;
    while (cur is not null && depth < maxDepth)
    {
        if (!seen.Add(cur.FormKey))
        {
            Console.WriteLine($"    … cycle at {cur.FormKey}");
            return;
        }

        Console.WriteLine($"    [{depth}] {cur.FormKey}  EDID={cur.EditorID}");
        if (cur.DefaultTemplate.IsNull)
        {
            if (depth == 0)
                Console.WriteLine("      DefaultTemplate: (null)");
            return;
        }

        if (!cache.TryResolve<INpcGetter>(cur.DefaultTemplate.FormKey, out var parent))
        {
            Console.WriteLine($"      DefaultTemplate: {cur.DefaultTemplate.FormKey}  (resolve failed)");
            return;
        }

        Console.WriteLine($"      DefaultTemplate → {cur.DefaultTemplate.FormKey}  EDID={parent.EditorID}");
        cur = parent;
        depth++;
    }

    if (depth >= maxDepth && cur is not null && !cur.DefaultTemplate.IsNull)
        Console.WriteLine("    … (truncated at max depth)");
}


}
