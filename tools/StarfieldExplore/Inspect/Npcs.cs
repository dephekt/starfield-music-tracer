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
        "Prints **Name**, **Race** (`RNAM`), **AttackRace** (`ATKR`), **Skin** (`WNAM`), **TemplateActors.TraitTemplate**, **Npc.Components** (**FullNameComponent** FULL, **FormLinkDataComponent** → race/armor), **DefaultTemplate** chain, **Keywords**, **DeathItem**. " +
        "Planet **PCM** fauna often has **`CCT_DummyRace`** on RNAM; CK *Traits* may match **ATKR**/**WNAM** or the **FormLinkData**/**FullName** component layer when those fields are null.");
    Console.WriteLine();

    foreach (var npc in matches)
    {
        Console.WriteLine($"=== Npc {npc.FormKey}  EDID={npc.EditorID} ===");
        Console.WriteLine($"  Name (localized): {FormatNpcLocalizedName(cache, npc)}");

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
                    "  Note: **RNAM** is CCT dummy — use **AttackRace** + **Skin** + **Components** below (matches CK **Traits** tab).");
            }
        }
        else
            Console.WriteLine($"  Race: {npc.Race.FormKey}  (resolve failed)");

        PrintNpcCctIdentityInspect(cache, npc);
        PrintNpcComponentTraitsInspect(cache, npc);

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

static void PrintNpcCctIdentityInspect(ILinkCache cache, INpcGetter npc)
{
    if (npc.AttackRace.IsNull)
        Console.WriteLine("  AttackRace (ATKR — CK Traits race): (null)");
    else if (cache.TryResolve<IRaceGetter>(npc.AttackRace.FormKey, out var ar))
        Console.WriteLine(
            $"  AttackRace (ATKR — CK Traits race): {npc.AttackRace.FormKey}  EDID={ar.EditorID}{TranslatedNameSuffix(ar)}");
    else
        Console.WriteLine($"  AttackRace (ATKR): {npc.AttackRace.FormKey}  (resolve failed)");

    if (npc.Skin.IsNull)
        Console.WriteLine("  Skin (WNAM — CK Traits skin): (null)");
    else if (cache.TryResolve<IArmorGetter>(npc.Skin.FormKey, out var skinArm))
        Console.WriteLine(
            $"  Skin (WNAM — CK Traits skin): {npc.Skin.FormKey}  EDID={skinArm.EditorID}{TranslatedNameSuffix(skinArm)}");
    else
        Console.WriteLine($"  Skin (WNAM): {npc.Skin.FormKey}  (resolve failed)");

    var ta = npc.TemplateActors;
    if (ta is null || ta.TraitTemplate.IsNull)
        Console.WriteLine("  TemplateActors.TraitTemplate: (null)");
    else
    {
        var fk = ta.TraitTemplate.FormKey;
        if (cache.TryResolve<IMajorRecordGetter>(fk, out var maj) && maj.EditorID is not null)
            Console.WriteLine($"  TemplateActors.TraitTemplate: {fk}  EDID={maj.EditorID}{DisplayNameSuffixForMajor(maj)}");
        else
            Console.WriteLine($"  TemplateActors.TraitTemplate: {fk}  (resolve failed)");
    }
}

static void PrintNpcComponentTraitsInspect(ILinkCache cache, INpcGetter npc)
{
    var full = FormatNpcComponentFullName(npc);
    if (string.IsNullOrEmpty(full))
        Console.WriteLine("  Components / FullNameComponent (FULL): (empty)");
    else
        Console.WriteLine(
            "  Components / FullNameComponent (FULL): \""
            + full.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            + "\"");

    var races = FormatNpcFormLinkDataRaceEdids(cache, npc);
    if (string.IsNullOrEmpty(races))
        Console.WriteLine("  Components / FormLinkDataComponent → Race: (none)");
    else
        Console.WriteLine($"  Components / FormLinkDataComponent → Race: {races}");

    var skins = FormatNpcFormLinkDataSkinArmorEdids(cache, npc);
    if (string.IsNullOrEmpty(skins))
        Console.WriteLine("  Components / FormLinkDataComponent → Armor (Skin-preferring): (none)");
    else
        Console.WriteLine($"  Components / FormLinkDataComponent → Armor (Skin-preferring): {skins}");
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

        Console.WriteLine(
            $"    [{depth}] {cur.FormKey}  EDID={cur.EditorID}{TranslatedNameSuffix(cur)}");
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

        Console.WriteLine(
            $"      DefaultTemplate → {cur.DefaultTemplate.FormKey}  EDID={parent.EditorID}{TranslatedNameSuffix(parent)}");
        cur = parent;
        depth++;
    }

    if (depth >= maxDepth && cur is not null && !cur.DefaultTemplate.IsNull)
        Console.WriteLine("    … (truncated at max depth)");
}

/// <summary>Expand <see cref="INpcGetter.DeathItem"/> through <see cref="ILeveledItemGetter"/> (same rules as <see cref="BuildLootNpcIndex"/>).</summary>
static void PrintNpcDeathItemLootLeaves(ILinkCache cache, INpcGetter npc)
{
    if (npc.DeathItem.IsNull)
    {
        Console.WriteLine("  DeathItem: (null)");
        return;
    }

    var dfk = npc.DeathItem.FormKey;
    Console.WriteLine($"  DeathItem root: {dfk}  ({DescribeComponent(cache, dfk)})");
    var leaves = new HashSet<FormKey>();
    var levVisited = new HashSet<FormKey>();
    ExpandItemKeysFromFormKey(dfk, cache, levVisited, leaves);
    if (leaves.Count == 0)
    {
        Console.WriteLine(
            "  DeathItem → leaf item-like: (none — may be non-item references, empty LL, or unresolved links)");
        return;
    }

    Console.WriteLine($"  DeathItem → leaf item-like ({leaves.Count}):");
    foreach (var lf in leaves.OrderBy(x =>
                 cache.TryResolve<IMiscItemGetter>(x, out var m) && m.EditorID is not null
                     ? m.EditorID
                     : x.ToString(),
                 StringComparer.OrdinalIgnoreCase))
        Console.WriteLine($"    • {lf}  ({DescribeComponent(cache, lf)})");
}


}
