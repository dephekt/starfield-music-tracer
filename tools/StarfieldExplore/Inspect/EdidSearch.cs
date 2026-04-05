using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using StarfieldExplore.Game;

partial class Program
{
/// <summary>
/// Find major records whose <see cref="IMajorRecordGetter.EditorID"/> contains a substring; then list <see cref="IFormLinkContainerGetter"/> backlinks across the whole mod.
/// </summary>
static int RunSearchEdidSubstring(StarfieldExploreSession session, string substr, int maxHits, int maxBacklinksPerTarget)
{
    var s = substr.Trim();
    if (s.Length == 0)
    {
        Console.Error.WriteLine("Empty substring.");
        return 1;
    }

    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var cmp = StringComparison.OrdinalIgnoreCase;
    var hits = new List<(string Group, IMajorRecordGetter Rec)>();
    foreach (var pair in EnumerateMajorRecords(mod))
    {
        var ed = pair.Rec.EditorID;
        if (ed is null || ed.IndexOf(s, cmp) < 0) continue;
        hits.Add(pair);
    }

    hits.Sort((a, b) =>
    {
        var g = string.Compare(a.Group, b.Group, StringComparison.Ordinal);
        if (g != 0) return g;
        return string.Compare(a.Rec.EditorID, b.Rec.EditorID, StringComparison.OrdinalIgnoreCase);
    });

    var totalFound = hits.Count;
    if (maxHits > 0 && hits.Count > maxHits)
        hits = hits.Take(maxHits).ToList();

    Console.WriteLine(
        $"EditorID contains \"{s}\": {totalFound} major record(s) on **Starfield.esm** listing (showing {hits.Count}{(maxHits > 0 && totalFound > maxHits ? $"; cap --limit={maxHits}" : "")}).");
    Console.WriteLine(
        "Use this to locate **Skin_** / **CCT** rows, then read **FormLink backlinks** (which records point at them).");
    Console.WriteLine();

    foreach (var (group, rec) in hits)
    {
        var desc = DescribeComponent(cache, rec.FormKey);
        Console.WriteLine($"  [{group}] {rec.FormKey}  EDID={rec.EditorID}{DisplayNameSuffixForMajor(rec)}");
        Console.WriteLine($"           ({desc})");
    }

    if (hits.Count == 0)
        return 0;

    Console.WriteLine();
    Console.WriteLine(
        $"FormLink backlinks (full mod scan; up to {maxBacklinksPerTarget} referrers per hit; expensive on large plugins):");

    var targetSet = hits.Select(h => h.Rec.FormKey).ToHashSet();
    var backlinks = BuildFormLinkBacklinksToFormKeysFullScan(mod, targetSet);

    foreach (var (group, rec) in hits)
    {
        var fk = rec.FormKey;
        if (!backlinks.TryGetValue(fk, out var bl) || bl.Count == 0)
        {
            Console.WriteLine($"  {fk}  EDID={rec.EditorID}: (no FormLink backlinks found in enumerable majors)");
            continue;
        }

        Console.WriteLine($"  {fk}  EDID={rec.EditorID}  ← {bl.Count} referrer(s):");
        var n = 0;
        foreach (var (g, referrer) in bl.OrderBy(x => x.Group, StringComparer.Ordinal)
                     .ThenBy(x => x.Rec.EditorID ?? "", StringComparer.OrdinalIgnoreCase))
        {
            if (++n > maxBacklinksPerTarget)
            {
                Console.WriteLine($"    … {bl.Count - maxBacklinksPerTarget} more referrer(s)");
                break;
            }

            Console.WriteLine(
                $"    [{g}] {referrer.FormKey}  EDID={referrer.EditorID}{DisplayNameSuffixForMajor(referrer)}");
        }
    }

    return 0;
}

}
