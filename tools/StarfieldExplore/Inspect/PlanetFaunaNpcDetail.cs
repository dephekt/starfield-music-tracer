using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using StarfieldExplore.Game;

partial class Program
{
/// <summary>
/// For each unique leaf <see cref="INpcGetter"/> from <see cref="IPlanetBiomeGetter.Fauna"/>: CCT/template chain, keywords, FormLinks, <see cref="INpcGetter.DeathItem"/> expansion.
/// </summary>
static int RunInspectPlanetFaunaDetail(StarfieldExploreSession session, string hint, int listLimit)
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
        "Per Npc: Name (if any), Race, KeywordsTemplate, DefaultTemplate chain (with names), EnumerateFormLinks cap, DeathItem → leveled → leaf misc/ingestible/resource.");
    Console.WriteLine("CCT slots: expect **CCT_DummyRace** / **CCT_Creature** — colloquial species = skin/CCT data or wikis, not PCM EditorID.");
    Console.WriteLine();

    var ordered = allLeafNpcs
        .OrderBy(nfk =>
            cache.TryResolve<INpcGetter>(nfk, out var n) && n.EditorID is not null
                ? n.EditorID
                : nfk.ToString(),
            StringComparer.OrdinalIgnoreCase)
        .ToList();

    var total = ordered.Count;
    if (listLimit > 0 && ordered.Count > listLimit)
    {
        Console.WriteLine($"--limit={listLimit}: detailing first {listLimit} of {total} Npc(s) (use --limit=0 for all).");
        Console.WriteLine();
        ordered = ordered.Take(listLimit).ToList();
    }

    foreach (var nfk in ordered)
    {
        if (!cache.TryResolve<INpcGetter>(nfk, out var npc))
        {
            Console.WriteLine($"=== {nfk}  (INpcGetter resolve failed) ===");
            Console.WriteLine();
            continue;
        }

        Console.WriteLine($"=== Npc {npc.FormKey}  EDID={npc.EditorID}{TranslatedNameSuffix(npc)} ===");
        Console.WriteLine($"  Name (localized line): {FormatNpcLocalizedName(cache, npc)}");

        if (npc.Race.IsNull)
            Console.WriteLine("  Race: (null)");
        else if (cache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race))
        {
            Console.WriteLine($"  Race: {npc.Race.FormKey}  EDID={race.EditorID}{TranslatedNameSuffix(race)}");
            var re = race.EditorID ?? "";
            if (re.Contains("CCT_Dummy", StringComparison.OrdinalIgnoreCase)
                || re.Contains("DummyRace", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    "  Note: **RNAM** is CCT dummy — use **AttackRace** + **Skin** + **Components** below (CK **Traits** tab).");
            }
        }
        else
            Console.WriteLine($"  Race: {npc.Race.FormKey}  (resolve failed)");

        PrintNpcCctIdentityInspect(cache, npc);
        PrintNpcComponentTraitsInspect(cache, npc);

        var ta = npc.TemplateActors;
        if (ta is not null && !ta.KeywordsTemplate.IsNull)
        {
            if (cache.TryResolve<INpcGetter>(ta.KeywordsTemplate.FormKey, out var ktn))
                Console.WriteLine(
                    $"  TemplateActors.KeywordsTemplate: {ta.KeywordsTemplate.FormKey}  EDID={ktn.EditorID}{TranslatedNameSuffix(ktn)}");
            else
                Console.WriteLine($"  TemplateActors.KeywordsTemplate: {ta.KeywordsTemplate.FormKey}  (not an Npc?)");
        }

        Console.WriteLine("  DefaultTemplate chain (Npc → Npc, max depth 10):");
        PrintNpcTemplateChain(cache, npc, 10);

        var kws = npc.Keywords;
        if (kws is { Count: > 0 })
        {
            Console.WriteLine($"  Keywords ({kws.Count}, first 48):");
            foreach (var lk in kws.Take(48))
            {
                if (lk.IsNull) continue;
                if (cache.TryResolve<IKeywordGetter>(lk.FormKey, out var kw))
                    Console.WriteLine($"    {lk.FormKey}  EDID={kw.EditorID}{TranslatedNameSuffix(kw)}");
                else
                    Console.WriteLine($"    {lk.FormKey}");
            }

            if (kws.Count > 48)
                Console.WriteLine($"    … {kws.Count - 48} more");
        }
        else
            Console.WriteLine("  Keywords: (none on this Npc)");

        if (npc is IFormLinkContainerGetter flc)
        {
            Console.WriteLine("  EnumerateFormLinks (resolved, cap 48):");
            DumpResolvedFormLinksCap(flc, "    ", 48, cache, null, null);
        }
        else
            Console.WriteLine("  EnumerateFormLinks: (record does not implement IFormLinkContainerGetter)");

        PrintNpcDeathItemLootLeaves(cache, npc);
        Console.WriteLine();
    }

    return 0;
}

}
