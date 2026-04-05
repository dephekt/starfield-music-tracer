using System.Globalization;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using StarfieldExplore.Game;

partial class Program
{
/// <summary>
/// Full-mod scan for <c>OutpostHarvesterFaunaScript</c> VMAD hosts, flattened ScriptObjectProperty / struct FormKeys (TSV),
/// distinct link targets, and FormLink backlinks — for comparing ESM-expressed data to in-game workshop fauna production.
/// </summary>
static int RunInspectFaunaProductionIndex(StarfieldExploreSession session, int maxBacklinksPerTarget)
{
    const string faunaScript = "OutpostHarvesterFaunaScript";

    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);

    Console.WriteLine(
        "Fauna production index: every major record whose **VirtualMachineAdapter** includes **" + faunaScript + "**. " +
        "TSV flattens **ScriptObjectProperty** and **StructList** members (e.g. **FaunaCreation**, **ResourceGlobals**) to FormKeys. " +
        "Backlinks = other majors whose **EnumerateFormLinks** reach those targets (expensive full-mod scan). " +
        "**OrganicResourceAV → ActorBase** mapping is still native (**GetActorBaseForResource**); this dump does not list it.");
    Console.WriteLine(
        maxBacklinksPerTarget > 0
            ? $"Backlink cap per target: --limit={maxBacklinksPerTarget}"
            : "Backlink cap: unlimited (--limit=0)");
    Console.WriteLine();

    var hosts = new List<(string Group, IMajorRecordGetter Rec)>();
    foreach (var pair in EnumerateMajorRecords(mod))
    {
        IVirtualMachineAdapterGetter? vmad;
        try
        {
            vmad = TryGetVirtualMachineAdapter(pair.Rec);
        }
        catch
        {
            continue;
        }

        if (!VmadHasScriptNamed(vmad, faunaScript))
            continue;
        hosts.Add(pair);
    }

    hosts.Sort((a, b) =>
    {
        var g = string.Compare(a.Group, b.Group, StringComparison.Ordinal);
        if (g != 0) return g;
        return string.Compare(a.Rec.EditorID, b.Rec.EditorID, StringComparison.OrdinalIgnoreCase);
    });

    Console.WriteLine($"Hosts with **{faunaScript}**: {hosts.Count}");
    foreach (var (group, rec) in hosts)
        Console.WriteLine($"  [{group}] {rec.FormKey}  EDID={rec.EditorID}{DisplayNameSuffixForMajor(rec)}");

    Console.WriteLine();
    Console.WriteLine("--- TSV: hostGroup\thostFormKey\thostEdid\tvmadPath\ttargetFormKey\ttargetSummary ---");

    var allTargets = new HashSet<FormKey>();
    foreach (var (group, rec) in hosts)
    {
        IVirtualMachineAdapterGetter? vmad;
        try
        {
            vmad = TryGetVirtualMachineAdapter(rec);
        }
        catch
        {
            continue;
        }

        var ent = TryGetVmadScriptEntry(vmad, faunaScript);
        if (ent is null) continue;

        var rows = new List<(string Path, FormKey FormKey)>();
        CollectObjectFormKeysFromScriptEntry(ent, rows, maxDepth: 10);
        foreach (var (_, fk) in rows)
            allTargets.Add(fk);

        var hostEdid = TsvCell(rec.EditorID);
        foreach (var (path, fk) in rows.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
            Console.WriteLine(
                string.Join(
                    '\t',
                    TsvCell(group),
                    rec.FormKey.ToString(),
                    hostEdid,
                    TsvCell(path),
                    fk.ToString(),
                    TsvCell(desc)));
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Distinct VMAD-linked FormKeys (all hosts): {allTargets.Count}");
    foreach (var fk in allTargets.OrderBy(x => x.ToString(), StringComparer.Ordinal))
    {
        var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
        Console.WriteLine($"  {fk}\t{TsvCell(desc)}");
    }

    Console.WriteLine();
    Console.WriteLine(
        "FormLink backlinks → targets above (referrer majors only; same scan as **--search-edid-substring** backlinks):");
    if (allTargets.Count == 0)
    {
        Console.WriteLine("  (no targets)");
        return 0;
    }

    var backlinks = BuildFormLinkBacklinksToFormKeysFullScan(mod, allTargets);
    foreach (var fk in allTargets.OrderBy(x => x.ToString(), StringComparer.Ordinal))
    {
        if (!backlinks.TryGetValue(fk, out var bl) || bl.Count == 0)
        {
            Console.WriteLine($"  {fk}: (no FormLink backlinks in enumerable majors)");
            continue;
        }

        Console.WriteLine($"  {fk}  ← {bl.Count} referrer(s):");
        var n = 0;
        foreach (var (g, referrer) in bl.OrderBy(x => x.Group, StringComparer.Ordinal)
                     .ThenBy(x => x.Rec.EditorID ?? "", StringComparer.OrdinalIgnoreCase))
        {
            if (maxBacklinksPerTarget > 0 && ++n > maxBacklinksPerTarget)
            {
                Console.WriteLine($"    … cap --limit={maxBacklinksPerTarget}");
                break;
            }

            Console.WriteLine(
                $"    [{g}] {referrer.FormKey}  EDID={referrer.EditorID}{DisplayNameSuffixForMajor(referrer)}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        "Globals with EditorID hinting outpost organic harvester tuning (same filter as **--inspect-outpost-harvesters**):");
    static bool GlobalOrganicHarvesterHint(string? e)
    {
        if (string.IsNullOrEmpty(e)) return false;
        if (!e.Contains("Harvester", StringComparison.OrdinalIgnoreCase)
            && !e.Contains("OrganicFauna", StringComparison.OrdinalIgnoreCase)
            && !e.Contains("OrganicFlora", StringComparison.OrdinalIgnoreCase))
            return false;
        return e.Contains("Outpost", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Fauna", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Flora", StringComparison.OrdinalIgnoreCase);
    }

    var gHits = new List<(FormKey Key, string? Edid, string DataStr)>();
    foreach (var g in mod.Globals)
    {
        if (!GlobalOrganicHarvesterHint(g.EditorID)) continue;
        string dataStr;
        try
        {
            dataStr = Convert.ToString(g.Data, CultureInfo.InvariantCulture) ?? "?";
        }
        catch
        {
            dataStr = "(unreadable)";
        }

        gHits.Add((g.FormKey, g.EditorID, dataStr));
    }

    gHits.Sort((a, b) => string.Compare(a.Edid, b.Edid, StringComparison.Ordinal));
    if (gHits.Count == 0)
        Console.WriteLine("  (none)");
    else
    {
        foreach (var (key, ed, dataStr) in gHits)
        {
            var fromVmad = allTargets.Contains(key) ? "  [also linked from fauna VMAD]" : "";
            Console.WriteLine($"  {key} EDID={ed}  Data={dataStr}{fromVmad}");
        }
    }

    Console.WriteLine();
    return 0;
}

static string TsvCell(string? s)
{
    if (string.IsNullOrEmpty(s)) return "";
    return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
}
