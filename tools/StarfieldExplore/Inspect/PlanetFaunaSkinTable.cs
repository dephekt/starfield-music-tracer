using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using StarfieldExplore.Game;

partial class Program
{
/// <summary>
/// Collect <see cref="IArmorGetter"/> forms in <see cref="IFormLinkContainerGetter.EnumerateFormLinks"/> whose EditorID starts with <c>Skin_</c> (typical CCT creature mesh).
/// </summary>
static List<(FormKey Fk, string Edid)> CollectSkinArmorsFromNpcFormLinks(ILinkCache cache, INpcGetter npc)
{
    var seen = new HashSet<FormKey>();
    var list = new List<(FormKey Fk, string Edid)>();
    if (npc is not IFormLinkContainerGetter flc)
        return list;

    try
    {
        foreach (var raw in flc.EnumerateFormLinks(true))
        {
            if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out _))
                continue;
            if (fk == default || fk.IsNull)
                continue;
            if (!cache.TryResolve<IArmorGetter>(fk, out var arm))
                continue;
            var ed = arm.EditorID;
            if (ed is null || !ed.StartsWith("Skin_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (seen.Add(fk))
                list.Add((fk, ed));
        }
    }
    catch
    {
        /* EnumerateFormLinks can throw on odd records */
    }

    return list;
}

/// <summary>
/// Ordered distinct <see cref="INpcGetter.Skin"/> armors walking <see cref="INpcGetter.DefaultTemplate"/> (self, then each parent Npc).
/// </summary>
static List<string> CollectOrderedDistinctSkinEdidsFromTemplateChain(ILinkCache cache, INpcGetter start, int maxDepth)
{
    var seenFk = new HashSet<FormKey>();
    var edids = new List<string>();
    INpcGetter? cur = start;
    var visited = new HashSet<FormKey>();
    var depth = 0;
    while (cur is not null && depth < maxDepth && visited.Add(cur.FormKey))
    {
        if (!cur.Skin.IsNull
            && cache.TryResolve<IArmorGetter>(cur.Skin.FormKey, out var arm)
            && arm.EditorID is not null
            && seenFk.Add(cur.Skin.FormKey))
            edids.Add(arm.EditorID);

        if (cur.DefaultTemplate.IsNull)
            break;
        if (!cache.TryResolve<INpcGetter>(cur.DefaultTemplate.FormKey, out cur))
            break;
        depth++;
    }

    return edids;
}

static string? TryNpcSkinFieldEdid(ILinkCache cache, INpcGetter npc)
{
    if (npc.Skin.IsNull)
        return null;
    if (cache.TryResolve<IArmorGetter>(npc.Skin.FormKey, out var arm) && arm.EditorID is not null)
        return arm.EditorID;
    return npc.Skin.FormKey.ToString();
}

static string TrySnapTemplateEdid(ILinkCache cache, INpcGetter npc)
{
    if (npc.SnapTemplate.IsNull)
        return "";
    if (cache.TryResolve<ISnapTemplateGetter>(npc.SnapTemplate.FormKey, out var st) && st.EditorID is not null)
        return st.EditorID;
    return npc.SnapTemplate.FormKey.ToString();
}

static string? TryExtractFaunaSlotLabel(string? npcEdid)
{
    if (npcEdid is null)
        return null;
    var m = Regex.Match(
        npcEdid,
        @"_(?<slot>(?:Critter|Prey|Predator)\d+(?:_Flyer)?)$",
        RegexOptions.IgnoreCase);
    return m.Success ? m.Groups["slot"].Value : null;
}

/// <summary>
/// Tab-separated table: each leaf planet-fauna <see cref="INpcGetter"/> → <see cref="INpcGetter.Skin"/>, template-chain skins, SnapTemplate, and <c>Skin_*</c> from <see cref="IFormLinkContainerGetter.EnumerateFormLinks"/>.
/// </summary>
static int RunInspectPlanetFaunaSkinTable(StarfieldExploreSession session, string hint, int listLimit)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var h = hint.Trim();
    if (h.Length == 0)
    {
        Console.Error.WriteLine("Empty planet hint.");
        return 1;
    }

    CollectLeafFaunaNpcFormKeysForPlanetHint(mod, cache, h, out var matches, out var allLeafNpcs);

    if (matches.Count == 0)
    {
        Console.WriteLine($"No planets matching hint \"{h}\" (EditorID substring or FormKey string fragment).");
        return 0;
    }

    Console.WriteLine(
        $"Planets matching \"{h}\": {matches.Count}  |  distinct leaf Npc from PlanetBiome.Fauna: {allLeafNpcs.Count}");
    Console.WriteLine(
        "**AttackRaceEdid** / **TraitTemplateEdid** = `ATKR` + `TemplateActors.TraitTemplate`; **ComponentFullName** / **FormLinkDataRaceEdids** / **FormLinkDataSkinEdids** = **Npc.Components** (see `--inspect-npc`). **NpcSkinEdid** = same row’s **`Skin` (`WNAM`)** (template-chain skins are in **ChainSkinEdids**).");
    Console.WriteLine(
        "NpcNameLocalized = same best-effort line as other fauna TSVs (Name / Short / Long / ObjectTemplates / template chain).");
    Console.WriteLine(
        "NpcSkinEdid = this record's INpcGetter.Skin only (often null on PCM rows — then appearance comes from the template chain).");
    Console.WriteLine(
        "ChainSkinEdids = distinct armor EditorIDs from Npc.Skin on self, then each DefaultTemplate parent (same order as in-game inheritance).");
    Console.WriteLine(
        "SnapTemplate often differentiates variants (e.g. flyers); empty if unset.");
    Console.WriteLine(
        "FormLinksSkinStarEdids = Skin_* armors anywhere under EnumerateFormLinks — often identical across CCT siblings because the whole template subgraph is linked.");
    Console.WriteLine();

    var ordered = allLeafNpcs
        .OrderBy(nfk =>
            cache.TryResolve<INpcGetter>(nfk, out var n) && n.EditorID is not null
                ? n.EditorID!
                : nfk.ToString(),
            StringComparer.OrdinalIgnoreCase)
        .ToList();

    var total = ordered.Count;
    if (listLimit > 0 && ordered.Count > listLimit)
    {
        Console.WriteLine($"--limit={listLimit}: showing first {listLimit} of {total} Npc(s) (use --limit=0 for all).");
        Console.WriteLine();
        ordered = ordered.Take(listLimit).ToList();
    }

    Console.WriteLine(
        "slot\tNpcFormKey\tNpcEditorID\tNpcNameLocalized\tAttackRaceEdid\tTraitTemplateEdid\tComponentFullName\tFormLinkDataRaceEdids\tFormLinkDataSkinEdids\tNpcSkinEdid\tChainSkinEdids\tSnapTemplateEdid\tFormLinksSkinStarEdids\tFormLinksSkinStarFormKeys");
    foreach (var nfk in ordered)
    {
        if (!cache.TryResolve<INpcGetter>(nfk, out var npc))
        {
            Console.WriteLine($"?\t{nfk}\t(resolve failed)\t\t\t\t\t\t\t\t\t\t");
            continue;
        }

        var slotLabel = TryExtractFaunaSlotLabel(npc.EditorID) ?? "";
        var npcLoc = SanitizeTsvCell(FormatNpcLocalizedName(cache, npc));
        var atk = SanitizeTsvCell(FormatNpcAttackRaceEdid(cache, npc));
        var ttrait = SanitizeTsvCell(FormatNpcTraitTemplateEdid(cache, npc));
        var cfn = SanitizeTsvCell(FormatNpcComponentFullName(npc));
        var flr = SanitizeTsvCell(FormatNpcFormLinkDataRaceEdids(cache, npc));
        var fls = SanitizeTsvCell(FormatNpcFormLinkDataSkinArmorEdids(cache, npc));
        var npcSkin = TryNpcSkinFieldEdid(cache, npc) ?? "";
        var chainSkins = string.Join("; ", CollectOrderedDistinctSkinEdidsFromTemplateChain(cache, npc, 14));
        var snap = TrySnapTemplateEdid(cache, npc);
        var flSkins = CollectSkinArmorsFromNpcFormLinks(cache, npc);
        var flEd = string.Join("; ", flSkins.Select(s => s.Edid));
        var flFk = string.Join("; ", flSkins.Select(s => s.Fk.ToString()));
        if (flSkins.Count == 0)
        {
            flEd = npc is IFormLinkContainerGetter ? "(none)" : "(not FormLink container)";
            flFk = flEd;
        }

        Console.WriteLine(
            $"{slotLabel}\t{npc.FormKey}\t{npc.EditorID}\t{npcLoc}\t{atk}\t{ttrait}\t{cfn}\t{flr}\t{fls}\t{npcSkin}\t{chainSkins}\t{snap}\t{flEd}\t{flFk}");
    }

    return 0;
}

}
