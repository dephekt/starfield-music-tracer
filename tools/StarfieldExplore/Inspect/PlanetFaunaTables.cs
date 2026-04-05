using System.Globalization;
using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using StarfieldExplore.Game;

partial class Program
{
static string DescribeDeathItemRootEdid(ILinkCache cache, INpcGetter npc)
{
    if (npc.DeathItem.IsNull)
        return "(null)";
    var dfk = npc.DeathItem.FormKey;
    if (cache.TryResolve<ILeveledItemGetter>(dfk, out var lev) && lev.EditorID is not null)
        return lev.EditorID;
    if (cache.TryResolve<IMiscItemGetter>(dfk, out var m) && m.EditorID is not null)
        return m.EditorID;
    if (cache.TryResolve<IIngestibleGetter>(dfk, out var ing) && ing.EditorID is not null)
        return ing.EditorID;
    return dfk.ToString();
}

/// <summary>Localized label on the DeathItem root (LL override name, misc/ingestible FULL), when present.</summary>
static string DescribeDeathItemRootLocalizedName(ILinkCache cache, INpcGetter npc)
{
    if (npc.DeathItem.IsNull)
        return "";
    var dfk = npc.DeathItem.FormKey;
    if (cache.TryResolve<ILeveledItemGetter>(dfk, out var lev))
        return SanitizeTsvCell(TryFormatTranslatedName(lev.OverrideName));
    if (cache.TryResolve<IMiscItemGetter>(dfk, out var m))
        return SanitizeTsvCell(TryFormatTranslatedName(m.Name));
    if (cache.TryResolve<IIngestibleGetter>(dfk, out var ing))
        return SanitizeTsvCell(TryFormatTranslatedName(ing.Name));
    return "";
}

/// <summary>
/// Expand <see cref="INpcGetter.DeathItem"/> to leaf misc / ingestible / resource; build organic misc family histogram (EditorID prefix before last '_' for Org* misc).
/// </summary>
static string BuildOrganicMiscFamilyHistogram(ILinkCache cache, INpcGetter npc, out int leafTotal, out int miscLeafCount, out int ingestLeafCount, out int resourceLeafCount, out int inorgMiscLeafCount)
{
    leafTotal = 0;
    miscLeafCount = 0;
    ingestLeafCount = 0;
    resourceLeafCount = 0;
    inorgMiscLeafCount = 0;
    var orgFamilyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    if (npc.DeathItem.IsNull)
        return "";

    var leaves = new HashSet<FormKey>();
    var levVisited = new HashSet<FormKey>();
    ExpandItemKeysFromFormKey(npc.DeathItem.FormKey, cache, levVisited, leaves);
    leafTotal = leaves.Count;

    foreach (var fk in leaves)
    {
        if (cache.TryResolve<IMiscItemGetter>(fk, out var misc))
        {
            miscLeafCount++;
            var ed = misc.EditorID;
            if (ed is null)
                continue;
            if (ed.StartsWith("Inorg", StringComparison.OrdinalIgnoreCase))
                inorgMiscLeafCount++;
            if (!ed.StartsWith("Org", StringComparison.OrdinalIgnoreCase))
                continue;
            var li = ed.LastIndexOf('_');
            var family = li > 0 ? ed[..li] : ed;
            orgFamilyCounts.TryGetValue(family, out var c);
            orgFamilyCounts[family] = c + 1;
            continue;
        }

        if (cache.TryResolve<IIngestibleGetter>(fk, out _))
        {
            ingestLeafCount++;
            continue;
        }

        if (cache.TryResolve<IResourceGetter>(fk, out _))
            resourceLeafCount++;
    }

    if (orgFamilyCounts.Count == 0)
        return "";

    var sb = new StringBuilder();
    foreach (var kv in orgFamilyCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
    {
        if (sb.Length > 0)
            sb.Append(';');
        sb.Append(kv.Key).Append(':').Append(kv.Value);
    }

    return sb.ToString();
}

/// <summary>
/// TSV: leaf planet-fauna <see cref="INpcGetter"/> → <see cref="INpcGetter.DeathItem"/> root + leaf counts + organic misc family histogram (survey / outpost resource fingerprint).
/// </summary>
static int RunInspectPlanetFaunaLootTable(StarfieldExploreSession session, string hint, int listLimit)
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
        "**AttackRaceEdid** / **SkinWnamEdid** / **TraitTemplateEdid** = record-level CCT (`ATKR` / `WNAM` / trait template). **ComponentFullName** / **FormLinkDataRaceEdids** / **FormLinkDataSkinEdids** = **Npc.Components** (**FullNameComponent** + **FormLinkDataComponent** links) when CK *Traits* diverges from null `ATKR`/`WNAM`.");
    Console.WriteLine(
        "DeathItem → leveled expansion (same rules as --planet-fauna-detail). **DeathItemRootLocalized** = LL **OverrideName** or misc/ingestible **Name** when resolvable. OrganicMiscFamilyHist counts Org* **misc** leaves by family prefix (EditorID up to last '_').");
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
        "slot\tNpcFormKey\tNpcEditorID\tAttackRaceEdid\tSkinWnamEdid\tTraitTemplateEdid\tComponentFullName\tFormLinkDataRaceEdids\tFormLinkDataSkinEdids\tNpcNameLocalized\tDeathItemRootEdid\tDeathItemRootLocalized\tLeafTotal\tMiscLeaves\tInorgMiscLeaves\tIngestibleLeaves\tResourceLeaves\tOrganicMiscFamilyHist");
    foreach (var nfk in ordered)
    {
        if (!cache.TryResolve<INpcGetter>(nfk, out var npc))
        {
            Console.WriteLine($"?\t{nfk}\t(resolve failed)\t\t\t\t\t\t\t\t\t\t\t\t\t\t");
            continue;
        }

        var slot = TryExtractFaunaSlotLabel(npc.EditorID) ?? "";
        var atk = SanitizeTsvCell(FormatNpcAttackRaceEdid(cache, npc));
        var wnam = SanitizeTsvCell(FormatNpcSkinWnamEdid(cache, npc));
        var ttrait = SanitizeTsvCell(FormatNpcTraitTemplateEdid(cache, npc));
        var cfn = SanitizeTsvCell(FormatNpcComponentFullName(npc));
        var flr = SanitizeTsvCell(FormatNpcFormLinkDataRaceEdids(cache, npc));
        var fls = SanitizeTsvCell(FormatNpcFormLinkDataSkinArmorEdids(cache, npc));
        var npcLoc = SanitizeTsvCell(FormatNpcLocalizedName(cache, npc));
        var root = DescribeDeathItemRootEdid(cache, npc);
        var rootLoc = DescribeDeathItemRootLocalizedName(cache, npc);
        var hist = BuildOrganicMiscFamilyHistogram(
            cache,
            npc,
            out var leafTotal,
            out var miscLeaves,
            out var ingestLeaves,
            out var resLeaves,
            out var inorgMisc);

        Console.WriteLine(
            $"{slot}\t{npc.FormKey}\t{npc.EditorID}\t{atk}\t{wnam}\t{ttrait}\t{cfn}\t{flr}\t{fls}\t{npcLoc}\t{root}\t{rootLoc}\t{leafTotal}\t{miscLeaves}\t{inorgMisc}\t{ingestLeaves}\t{resLeaves}\t{hist}");
    }

    return 0;
}

/// <summary>
/// TSV: resolved keyword EditorIDs per leaf planet-fauna <see cref="INpcGetter"/> (sorted, semicolon-separated).
/// </summary>
static int RunInspectPlanetFaunaKeywordTable(StarfieldExploreSession session, string hint, int listLimit)
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
        "**AttackRaceEdid** / **SkinWnamEdid** / **TraitTemplateEdid** = record-level CCT. **ComponentFullName** / **FormLinkDataRaceEdids** / **FormLinkDataSkinEdids** = **Npc.Components** layer (see `--inspect-npc`).");
    Console.WriteLine(
        "KeywordsSorted = keyword EditorIDs sorted. **KeywordsEdidAndLocalizedName** = same order, `EDID` or `EDID (localized FULL)` when the **Keyword** record has a **Name**.");
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
        "slot\tNpcFormKey\tNpcEditorID\tAttackRaceEdid\tSkinWnamEdid\tTraitTemplateEdid\tComponentFullName\tFormLinkDataRaceEdids\tFormLinkDataSkinEdids\tNpcNameLocalized\tKeywordCount\tKeywordsSorted\tKeywordsEdidAndLocalizedName");
    foreach (var nfk in ordered)
    {
        if (!cache.TryResolve<INpcGetter>(nfk, out var npc))
        {
            Console.WriteLine($"?\t{nfk}\t(resolve failed)\t\t\t\t\t\t\t\t\t\t\t\t\t");
            continue;
        }

        var slot = TryExtractFaunaSlotLabel(npc.EditorID) ?? "";
        var atk = SanitizeTsvCell(FormatNpcAttackRaceEdid(cache, npc));
        var wnam = SanitizeTsvCell(FormatNpcSkinWnamEdid(cache, npc));
        var ttrait = SanitizeTsvCell(FormatNpcTraitTemplateEdid(cache, npc));
        var cfn = SanitizeTsvCell(FormatNpcComponentFullName(npc));
        var flr = SanitizeTsvCell(FormatNpcFormLinkDataRaceEdids(cache, npc));
        var fls = SanitizeTsvCell(FormatNpcFormLinkDataSkinArmorEdids(cache, npc));
        var npcLoc = SanitizeTsvCell(FormatNpcLocalizedName(cache, npc));
        var kws = npc.Keywords;
        if (kws is null || kws.Count == 0)
        {
            Console.WriteLine($"{slot}\t{npc.FormKey}\t{npc.EditorID}\t{atk}\t{wnam}\t{ttrait}\t{cfn}\t{flr}\t{fls}\t{npcLoc}\t0\t\t");
            continue;
        }

        var rows = new List<(string Edid, string WithName)>();
        foreach (var lk in kws)
        {
            if (lk.IsNull)
                continue;
            var edid = lk.FormKey.ToString();
            if (cache.TryResolve<IKeywordGetter>(lk.FormKey, out var kw))
            {
                if (kw.EditorID is not null)
                    edid = kw.EditorID;
                var loc = TryFormatTranslatedName(kw.Name);
                var withName = string.IsNullOrWhiteSpace(loc) ? edid : $"{edid} ({loc})";
                rows.Add((edid, withName));
            }
            else
                rows.Add((edid, edid));
        }

        rows.Sort((a, b) => string.Compare(a.Edid, b.Edid, StringComparison.OrdinalIgnoreCase));
        var joined = string.Join("; ", rows.Select(r => r.Edid));
        var joinedNamed = SanitizeTsvCell(string.Join("; ", rows.Select(r => r.WithName)));
        Console.WriteLine(
            $"{slot}\t{npc.FormKey}\t{npc.EditorID}\t{atk}\t{wnam}\t{ttrait}\t{cfn}\t{flr}\t{fls}\t{npcLoc}\t{rows.Count}\t{joined}\t{joinedNamed}");
    }

    return 0;
}

static string SanitizeTsvCell(string? s)
{
    if (string.IsNullOrEmpty(s))
        return "";
    return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

static string? TryResolveMajorEdid(ILinkCache cache, FormKey fk)
{
    if (fk == default || fk.IsNull)
        return null;
    if (cache.TryResolve<IMajorRecordGetter>(fk, out var maj) && maj.EditorID is not null)
        return maj.EditorID;
    return fk.ToString();
}

static string DescribeNpcObjectModProperty(ILinkCache cache, IAObjectModPropertyGetter<Npc.Property> p)
{
    var name = p.Property.ToString();
    if (p is IObjectModFormLinkIntPropertyGetter<Npc.Property> li)
    {
        var fk = li.Record.FormKey;
        if (fk.IsNull)
            return $"{name}=(null)";
        return $"{name}={TryResolveMajorEdid(cache, fk)}";
    }

    if (p is IObjectModFormLinkFloatPropertyGetter<Npc.Property> lf)
    {
        var fk = lf.Record.FormKey;
        if (fk.IsNull)
            return $"{name}=(null)";
        return $"{name}={TryResolveMajorEdid(cache, fk)}";
    }

    return name;
}

static string BuildObjectTemplatesCompactSummary(ILinkCache cache, INpcGetter npc, int maxTotalChars)
{
    var list = npc.ObjectTemplates;
    if (list is null || list.Count == 0)
        return "";

    var blocks = new List<string>();
    for (var i = 0; i < list.Count; i++)
    {
        var t = list[i];
        if (t is null)
            continue;
        var nm = TryFormatTranslatedName(t.Name);
        nm = string.IsNullOrEmpty(nm) ? "-" : nm.Replace('|', '/').Replace('\t', ' ');
        var block = new StringBuilder();
        block.Append('[').Append(i).Append(']').Append(nm).Append(':');
        block.Append(t.Default ? "Def" : "Alt").Append(':');
        block.Append("Lv").Append(t.LevelMin).Append('-').Append(t.LevelMax);
        block.Append(":Ad").Append(t.AddonIndex);
        var kwN = t.Keywords?.Count ?? 0;
        if (kwN > 0)
            block.Append(":Kw").Append(kwN);
        if (t.Properties is { Count: > 0 })
        {
            block.Append(':');
            foreach (var prop in t.Properties)
            {
                block.Append(DescribeNpcObjectModProperty(cache, prop)).Append(',');
            }

            if (block.Length > 0 && block[^1] == ',')
                block.Length--;
        }

        blocks.Add(block.ToString());
    }

    var joined = string.Join(" | ", blocks);
    if (joined.Length <= maxTotalChars)
        return joined;
    return joined[..maxTotalChars] + "…";
}

static string CollectNonSkinArmorEdidsFromNpcFormLinks(ILinkCache cache, INpcGetter npc, int maxDistinct)
{
    var seen = new HashSet<FormKey>();
    var edids = new List<string>();
    if (npc is not IFormLinkContainerGetter flc)
        return "";

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
            if (ed is null || ed.StartsWith("Skin_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seen.Add(fk))
                continue;
            edids.Add(ed);
            if (edids.Count >= maxDistinct)
                break;
        }
    }
    catch
    {
        /* EnumerateFormLinks can throw */
    }

    edids.Sort(StringComparer.OrdinalIgnoreCase);
    return string.Join("; ", edids);
}

/// <summary>
/// TSV: localized name hint, <see cref="INpcGetter.SkinToneIndex"/>, <see cref="INpcGetter.ObjectTemplates"/> (compact), non-<c>Skin_*</c> <see cref="IArmorGetter"/> from FormLinks.
/// </summary>
static int RunInspectPlanetFaunaExtrasTable(StarfieldExploreSession session, string hint, int listLimit)
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
        "**AttackRaceEdid** = `ATKR`. **SkinWnamEdid** = `WNAM`. **TraitTemplateEdid** = `TemplateActors.TraitTemplate`. **ComponentFullName** / **FormLinkDataRaceEdids** / **FormLinkDataSkinEdids** = **Npc.Components** (CK *Traits* may align here when `ATKR`/`WNAM` are empty).");
    Console.WriteLine(
        "NpcNameLocalized = best-effort display (**Name**, **ShortName**, **LongName**, **ObjectTemplates**, **KeywordsTemplate**, **DefaultTemplate** chain) + strings/BA2. NpcShortName / NpcLongName / NpcActivateTextOverride = raw fields on this row when present.");
    Console.WriteLine(
        "ObjectTemplatesCompact = **ObjectTemplate** blocks (variant levels, addon index, **Npc.Property** form links like Skin/DisplayName).");
    Console.WriteLine(
        "NonSkinArmorFormLinksEdids = **IArmorGetter** in **EnumerateFormLinks** whose EDID does **not** start with Skin_ (first 64 distinct, sorted).");
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
        "slot\tNpcFormKey\tNpcEditorID\tAttackRaceEdid\tSkinWnamEdid\tTraitTemplateEdid\tComponentFullName\tFormLinkDataRaceEdids\tFormLinkDataSkinEdids\tNpcNameLocalized\tNpcShortName\tNpcLongName\tNpcActivateTextOverride\tSkinToneIndex\tObjectTemplateCount\tObjectTemplatesCompact\tNonSkinArmorFormLinksEdids");
    foreach (var nfk in ordered)
    {
        if (!cache.TryResolve<INpcGetter>(nfk, out var npc))
        {
            Console.WriteLine($"?\t{nfk}\t(resolve failed)\t\t\t\t\t\t\t\t\t\t\t\t\t\t");
            continue;
        }

        var slot = TryExtractFaunaSlotLabel(npc.EditorID) ?? "";
        var atk = SanitizeTsvCell(FormatNpcAttackRaceEdid(cache, npc));
        var wnam = SanitizeTsvCell(FormatNpcSkinWnamEdid(cache, npc));
        var ttrait = SanitizeTsvCell(FormatNpcTraitTemplateEdid(cache, npc));
        var cfn = SanitizeTsvCell(FormatNpcComponentFullName(npc));
        var flr = SanitizeTsvCell(FormatNpcFormLinkDataRaceEdids(cache, npc));
        var fls = SanitizeTsvCell(FormatNpcFormLinkDataSkinArmorEdids(cache, npc));
        var locName = SanitizeTsvCell(FormatNpcLocalizedName(cache, npc));
        var shortN = SanitizeTsvCell(TryFormatTranslatedName(npc.ShortName));
        var longN = SanitizeTsvCell(TryFormatTranslatedName(npc.LongName));
        var act = SanitizeTsvCell(TryFormatTranslatedName(npc.ActivateTextOverride));
        var sti = npc.SkinToneIndex.HasValue ? npc.SkinToneIndex.Value.ToString(CultureInfo.InvariantCulture) : "";
        var ot = npc.ObjectTemplates;
        var otc = ot?.Count ?? 0;
        var ots = SanitizeTsvCell(BuildObjectTemplatesCompactSummary(cache, npc, 4000));
        var arm = SanitizeTsvCell(CollectNonSkinArmorEdidsFromNpcFormLinks(cache, npc, 64));

        Console.WriteLine(
            $"{slot}\t{npc.FormKey}\t{npc.EditorID}\t{atk}\t{wnam}\t{ttrait}\t{cfn}\t{flr}\t{fls}\t{locName}\t{shortN}\t{longN}\t{act}\t{sti}\t{otc}\t{ots}\t{arm}");
    }

    return 0;
}

}
