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
static int RunInspectHusbandry(StarfieldExploreSession session)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);

    Console.WriteLine(
        "Note: OutpostBuilderOrganic_FaunaList / _FloraList name **fauna**/**flora** but list **tier COBJ recipes** for the " +
        "organic builder, not individual creatures or plants. Creature pen rules likely live on placed modules + scripts.");
    Console.WriteLine();
    Console.WriteLine("=== OutpostBuilderOrganic_FaunaList ===");
    var faunaList = mod.FormLists.FirstOrDefault(f => f.EditorID == "OutpostBuilderOrganic_FaunaList");
    if (faunaList is null)
        Console.WriteLine("(not found)");
    else
        PrintFormListEntries(cache, faunaList, miscByFormKey, constructibleByFormKey);

    Console.WriteLine();
    Console.WriteLine("=== OutpostBuilderOrganic_FloraList ===");
    var floraList = mod.FormLists.FirstOrDefault(f => f.EditorID == "OutpostBuilderOrganic_FloraList");
    if (floraList is null)
        Console.WriteLine("(not found)");
    else
        PrintFormListEntries(cache, floraList, miscByFormKey, constructibleByFormKey);

    Console.WriteLine();
    Console.WriteLine("=== COBJ (organic fauna + flora builder recipes) ===");
    foreach (var c in mod.ConstructibleObjects.OrderBy(x => x.EditorID, StringComparer.Ordinal))
    {
        var ed = c.EditorID;
        if (ed is null) continue;
        var organic = ed.Contains("Outpost_Builder_Organic", StringComparison.OrdinalIgnoreCase)
            || ed.Contains("Outpost_BuilderOrganic", StringComparison.OrdinalIgnoreCase);
        if (!organic) continue;
        Console.WriteLine($"{c.FormKey} EDID={ed}  CreatedObject={c.CreatedObject.FormKey}");
        foreach (var line in c.ConstructableComponents ?? [])
        {
            var comp = line.Component;
            if (comp is null || comp.IsNull) continue;
            Console.WriteLine(
                $"    <- {comp.FormKey}  ({DescribeComponent(cache, comp.FormKey, miscByFormKey, constructibleByFormKey)})");
        }
    }

    Console.WriteLine();
    Console.WriteLine("=== Placed module (CreatedObject) — record group + sample FormLinks ===");
    foreach (var c in mod.ConstructibleObjects.OrderBy(x => x.EditorID, StringComparer.Ordinal))
    {
        var ed = c.EditorID;
        if (ed is null) continue;
        if (!ed.StartsWith("co_Outpost_Builder_OrganicFauna", StringComparison.OrdinalIgnoreCase)
            && !ed.StartsWith("co_Outpost_BuilderOrganicFlora", StringComparison.OrdinalIgnoreCase))
            continue;
        if (c.CreatedObject.IsNull) continue;
        var placed = c.CreatedObject.FormKey;
        var located = FindMajorRecordGroup(mod, placed);
        Console.WriteLine($"{ed}  ->  {placed}  |  {located ?? "unresolved group"}");
        if (cache.TryResolve<IMajorRecordGetter>(placed, out var maj) && maj is IFormLinkContainerGetter flc)
        {
            var n = 0;
            foreach (var raw in flc.EnumerateFormLinks(true))
            {
                if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var path)) continue;
                if (fk == default || fk.IsNull) continue;
                var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
                if (desc.Contains("unresolved Null", StringComparison.OrdinalIgnoreCase)) continue;
                var hint = string.IsNullOrEmpty(path) ? "" : $"{path} → ";
                Console.WriteLine($"    link {hint}{fk}  ({desc})");
                if (++n >= 20) break;
            }

            if (n == 0)
                Console.WriteLine("    (no FormLinks resolved on this record)");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        $"Furniture group count: {mod.Furniture.Count()} — EditorID is often empty in binary overlay here, so pen/greenhouse " +
        "furniture is easier to reach via COBJ CreatedObject above.");

    return 0;
}

static bool IsOrganicOutpostHarvesterTransformEdid(string? e) =>
    !string.IsNullOrEmpty(e)
    && (e.StartsWith("Outpost_HarvesterFauna", StringComparison.OrdinalIgnoreCase)
        || e.StartsWith("Outpost_HarvesterFlora", StringComparison.OrdinalIgnoreCase));

static int RunInspectOutpostHarvesters(StarfieldExploreSession session)
{
    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);

    var harvesters = mod.Transforms
        .Where(t => IsOrganicOutpostHarvesterTransformEdid(t.EditorID))
        .OrderBy(t => t.EditorID, StringComparer.Ordinal)
        .ToList();

    var harvesterKeys = harvesters.Select(h => h.FormKey).ToHashSet();
    var backlinks = BuildBacklinksToFormKeys(mod, harvesterKeys);

    Console.WriteLine(
        "Organic outpost harvesters: ITransform (fauna pen / flora greenhouse). EnumerateFormLinks surfaces outputs and " +
        "related forms; BNAM/ENAM are record-specific subfields. Referrers = PackIn/Activator/Furniture whose EnumerateFormLinks " +
        "reach this Transform; VMAD on those records (scripts + Object properties + nested links).");
    Console.WriteLine($"Matching Transforms: {harvesters.Count}");
    foreach (var tr in harvesters)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {tr.FormKey}  EDID={tr.EditorID}  ({tr.GetType().Name}) ===");
        try
        {
            Console.WriteLine($"  BNAM hex={FormatBinarySubfieldDebug(tr.BNAM)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BNAM: ({ex.GetType().Name}: {ex.Message})");
        }

        try
        {
            Console.WriteLine($"  ENAM hex={FormatBinarySubfieldDebug(tr.ENAM)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ENAM: ({ex.GetType().Name}: {ex.Message})");
        }

        DumpPrimitivePublicProperties(tr, "  prop ", 24);

        if (tr is IFormLinkContainerGetter flcTr)
        {
            Console.WriteLine("  EnumerateFormLinks(true) — resolved (skip unresolved Null):");
            var n = 0;
            foreach (var raw in flcTr.EnumerateFormLinks(true))
            {
                if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var path)) continue;
                if (fk == default || fk.IsNull) continue;
                var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
                if (desc.Contains("unresolved Null", StringComparison.OrdinalIgnoreCase)) continue;
                var hint = string.IsNullOrEmpty(path) ? "" : $"{path} → ";
                Console.WriteLine($"    {hint}{fk}  ({desc})");
                if (++n >= 60) break;
            }

            if (n >= 60)
                Console.WriteLine("    … cap 60");

            Console.WriteLine("  EnumerateFormLinks(true) — verbose (all items, incl. unparsed):");
            DumpEnumerateFormLinksVerbose(flcTr, "    ", 40, cache, miscByFormKey, constructibleByFormKey);
        }
        else
            Console.WriteLine("  (does not implement IFormLinkContainerGetter)");

        if (backlinks.TryGetValue(tr.FormKey, out var refs) && refs.Count > 0)
        {
            Console.WriteLine($"  Referrers linking here ({refs.Count} record(s) across PackIn / Activator / Furniture):");
            foreach (var (group, rec) in refs.OrderBy(x => x.Group, StringComparer.Ordinal).ThenBy(x => x.Rec.EditorID, StringComparer.Ordinal))
            {
                Console.WriteLine(
                    $"    {group}  {rec.FormKey}  EDID={rec.EditorID}  ({rec.GetType().Name}){DisplayNameSuffixForMajor(rec)}");
                var vmad = TryGetVirtualMachineAdapter(rec);
                DumpVirtualMachineAdapter(vmad, "      ", cache, miscByFormKey, constructibleByFormKey);
                if (rec is IFormLinkContainerGetter flcRef)
                {
                    Console.WriteLine("      Referrer EnumerateFormLinks verbose (first 48):");
                    DumpEnumerateFormLinksVerbose(flcRef, "        ", 48, cache, miscByFormKey, constructibleByFormKey);
                }
            }
        }
        else
            Console.WriteLine("  Referrers: (none found in PackIn / Activator / Furniture EnumerateFormLinks)");
    }

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

    Console.WriteLine();
    Console.WriteLine("=== Globals (EditorID suggests outpost organic harvester tuning) ===");
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
        Console.WriteLine("  (none — tuning may be script-only or use different EditorID patterns)");
    else
    {
        foreach (var (key, ed, dataStr) in gHits)
            Console.WriteLine($"  {key} EDID={ed}  Data={dataStr}");
    }

    Console.WriteLine();
    Console.WriteLine("=== CurveTables (EditorID contains Harvester) ===");
    var cList = mod.CurveTables
        .Where(c => c.EditorID?.Contains("Harvester", StringComparison.OrdinalIgnoreCase) == true)
        .OrderBy(c => c.EditorID, StringComparer.Ordinal)
        .ToList();
    if (cList.Count == 0)
        Console.WriteLine("  (none)");
    else
    {
        foreach (var ct in cList)
            Console.WriteLine($"  {ct.FormKey} EDID={ct.EditorID}");
    }

    Console.WriteLine();
    Console.WriteLine("=== GameSettings (EditorID contains Harvester or Outpost), first 80 ===");
    var gsList = mod.GameSettings
        .Where(g => g.EditorID?.Contains("Harvester", StringComparison.OrdinalIgnoreCase) == true
            || g.EditorID?.Contains("Outpost", StringComparison.OrdinalIgnoreCase) == true)
        .OrderBy(g => g.EditorID, StringComparer.Ordinal)
        .Take(80)
        .ToList();
    if (gsList.Count == 0)
        Console.WriteLine("  (none)");
    else
    {
        foreach (var gs in gsList)
        {
            Console.WriteLine($"  {gs.FormKey} EDID={gs.EditorID}");
            DumpPrimitivePublicProperties(gs, "    ", 12);
        }
    }

    return 0;
}

static List<FormKey> CollectCellFormKeysFromPackIn(IPackInGetter pk, ILinkCache cache)
{
    var set = new HashSet<FormKey>();
    if (pk is not IFormLinkContainerGetter flc) return [];

    try
    {
        foreach (var raw in flc.EnumerateFormLinks(true))
        {
            if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out _)) continue;
            if (fk == default || fk.IsNull) continue;
            if (cache.TryResolve<ICellGetter>(fk, out _))
                set.Add(fk);
        }
    }
    catch
    {
        /* skip */
    }

    return set.OrderBy(x => x.ToString(), StringComparer.Ordinal).ToList();
}

static void DumpPlacedListForHusbandryCell(
    string sectionTitle,
    IReadOnlyList<IPlacedGetter> placed,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey,
    HashSet<FormKey> organicContainerBases)
{
    Console.WriteLine($"  {sectionTitle}: {placed.Count} reference(s)");
    for (var i = 0; i < placed.Count; i++)
    {
        var pl = placed[i];
        Console.WriteLine();
        if (pl is not IPlacedObjectGetter po)
        {
            Console.WriteLine($"    [{i}] {pl.FormKey}  ({pl.GetType().Name}, not IPlacedObjectGetter)");
            continue;
        }

        var baseFk = po.Base.FormKey;
        var baseDesc = DescribeComponent(cache, baseFk, miscByFormKey, constructibleByFormKey);
        var kind = "";
        if (cache.TryResolve<INpcGetter>(baseFk, out _)) kind += " NPC";
        if (cache.TryResolve<IFloraGetter>(baseFk, out _)) kind += " Flora";
        if (cache.TryResolve<IContainerGetter>(baseFk, out var cPre))
        {
            kind += " Container";
            if (cPre.EditorID?.Contains("OutpostBuilderOrganic", StringComparison.OrdinalIgnoreCase) == true)
                organicContainerBases.Add(baseFk);
        }

        if (cache.TryResolve<IActivatorGetter>(baseFk, out _)) kind += " Activator";

        Console.WriteLine($"    [{i}] Placed {po.FormKey}  EDID={po.EditorID ?? "(empty)"}");
        Console.WriteLine($"        Base {baseFk}  ({baseDesc}){kind}");

        var vmadPl = TryGetVirtualMachineAdapter(po);
        if (vmadPl is not null)
        {
            Console.WriteLine("        Placed VMAD:");
            DumpVirtualMachineAdapter(vmadPl, "          ", cache, miscByFormKey, constructibleByFormKey);
        }

        if (po is IFormLinkContainerGetter pflc)
        {
            Console.WriteLine("        FormLinks (first 28):");
            DumpResolvedFormLinksCap(pflc, "          ", 28, cache, miscByFormKey, constructibleByFormKey);
        }
    }
}

static int RunInspectOutpostHusbandryCells(StarfieldExploreSession session)
{
    string[] organicPackInEdids =
    [
        "OutpostPI_BuilderOrganicFauna01",
        "OutpostPI_BuilderOrganicFauna02",
        "OutpostPI_BuilderOrganicFauna03",
        "OutpostPI_BuilderOrganicFlora01",
        "OutpostPI_BuilderOrganicFlora02",
        "OutpostPI_BuilderOrganicFlora03",
    ];

    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);

    Console.WriteLine(
        "Outpost organic husbandry: tier PackIn → EnumerateFormLinks → storage ICellGetter → Persistent / Temporary placed IPlacedObjectGetter. " +
        "Highlights OutpostBuilderOrganic* Container bases (keywords + VMAD). Production recipes may still be script/fragment-heavy.");
    Console.WriteLine();

    var organicContainerBases = new HashSet<FormKey>();

    foreach (var edid in organicPackInEdids)
    {
        var pk = mod.PackIns.FirstOrDefault(p => p.EditorID == edid);
        if (pk is null)
        {
            Console.WriteLine($"=== PackIn EDID={edid}  (NOT FOUND) ===");
            Console.WriteLine();
            continue;
        }

        var cellKeys = CollectCellFormKeysFromPackIn(pk, cache);
        Console.WriteLine($"=== PackIn {pk.FormKey}  EDID={edid}{TranslatedNameSuffix(pk)} ===");
        if (cellKeys.Count == 0)
        {
            Console.WriteLine("  (no ICellGetter FormKey in PackIn EnumerateFormLinks)");
            Console.WriteLine();
            continue;
        }

        Console.WriteLine($"  Linked CELL(s): {string.Join(", ", cellKeys)}");
        foreach (var ck in cellKeys)
        {
            if (!cache.TryResolve<ICellGetter>(ck, out var cell))
            {
                Console.WriteLine($"  --- CELL {ck} (TryResolve failed) ---");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"  --- CELL {cell.FormKey}  EDID={cell.EditorID}{TranslatedNameSuffix(cell)} ---");
            try
            {
                DumpPlacedListForHusbandryCell(
                    "Persistent",
                    cell.Persistent,
                    cache,
                    miscByFormKey,
                    constructibleByFormKey,
                    organicContainerBases);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Persistent: ({ex.GetType().Name}: {ex.Message})");
            }

            try
            {
                DumpPlacedListForHusbandryCell(
                    "Temporary",
                    cell.Temporary,
                    cache,
                    miscByFormKey,
                    constructibleByFormKey,
                    organicContainerBases);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Temporary: ({ex.GetType().Name}: {ex.Message})");
            }
        }

        Console.WriteLine();
    }

    Console.WriteLine("=== Distinct OutpostBuilderOrganic* Container bases seen on placed (keywords + VMAD) ===");
    if (organicContainerBases.Count == 0)
        Console.WriteLine("  (none — check placed Base lines above)");
    else
    {
        foreach (var bf in organicContainerBases.OrderBy(x => x.ToString(), StringComparer.Ordinal))
        {
            if (!cache.TryResolve<IContainerGetter>(bf, out var cont))
            {
                Console.WriteLine($"{bf}  (not resolved as IContainerGetter)");
                continue;
            }

            Console.WriteLine();
            DumpContainerKeywordsAndVmad(cont, "  ", cache, miscByFormKey, constructibleByFormKey);
        }
    }

    Console.WriteLine();

    return 0;
}

/// <summary>
/// Trace <c>OutpostHarvesterFaunaScript</c> VMAD on organic fauna builder containers → linked <see cref="IQuestGetter"/> / faction / scanner form,
/// then dump quest adapter + objectives (static ESM data). Eligibility logic lives in compiled Papyrus + scan API, not in planet PCM alone.
/// </summary>
static int RunInspectPenFaunaScriptTrace(StarfieldExploreSession session)
{
    const string faunaScript = "OutpostHarvesterFaunaScript";

    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);

    Console.WriteLine(
        "Fauna pen script trace (ESM-only): **`OutpostHarvesterFaunaScript`** on **`OutpostBuilderOrganicFauna*`** containers. " +
        "Summarizes VMAD **ScriptObjectProperty** links, then the **`OutpostFauna`** quest record and its quest VMAD / objectives. " +
        "Planet eligibility is **not** expressed as “herd keyword on **`PlanetBiome.Fauna`** NPC” in vanilla; VMAD links **quest**, **faction**, **`FaunaCreation`** struct list, and **HandScannerTarget** (see below — vanilla resolves it as **ActorValueInformation**, not an NPC).");
    Console.WriteLine();

    var faunaContainers = new List<IContainerGetter>();
    foreach (var c in mod.Containers)
    {
        IVirtualMachineAdapterGetter? vmad;
        try
        {
            vmad = c.VirtualMachineAdapter;
        }
        catch
        {
            continue;
        }

        if (vmad is null) continue;
        var match = false;
        for (var si = 0; si < vmad.Scripts.Count; si++)
        {
            string? sn;
            try
            {
                sn = vmad.Scripts[si].Name;
            }
            catch
            {
                continue;
            }

            if (string.Equals(sn, faunaScript, StringComparison.OrdinalIgnoreCase))
            {
                match = true;
                break;
            }
        }

        if (match)
            faunaContainers.Add(c);
    }

    Console.WriteLine($"Containers with **{faunaScript}**: {faunaContainers.Count}");
    foreach (var c in faunaContainers.OrderBy(x => x.EditorID, StringComparer.Ordinal))
        Console.WriteLine($"  {c.FormKey}  EDID={c.EditorID}{TranslatedNameSuffix(c)}");

    Console.WriteLine();
    FormKey? questFk = null;
    FormKey? scannerFk = null;
    FormKey? factionFk = null;
    foreach (var c in faunaContainers)
    {
        var vm = c.VirtualMachineAdapter;
        if (questFk is null)
            questFk = TryGetScriptObjectFormKeyByName(vm, faunaScript, "OutpostFauna");
        if (scannerFk is null)
            scannerFk = TryGetScriptObjectFormKeyByName(vm, faunaScript, "HandScannerTarget");
        if (factionFk is null)
            factionFk = TryGetScriptObjectFormKeyByName(vm, faunaScript, "OutpostFaunaFaction");
    }

    Console.WriteLine("VMAD links (first non-null across fauna containers):");
    Console.WriteLine(
        $"  OutpostFauna (quest)     → {(questFk is null ? "(missing)" : $"{questFk}  ({DescribeComponent(cache, questFk.Value, miscByFormKey, constructibleByFormKey)})")}");
    Console.WriteLine(
        $"  OutpostFaunaFaction      → {(factionFk is null ? "(missing)" : $"{factionFk}  ({DescribeComponent(cache, factionFk.Value, miscByFormKey, constructibleByFormKey)})")}");
    Console.WriteLine(
        $"  HandScannerTarget        → {(scannerFk is null ? "(missing)" : $"{scannerFk}  ({DescribeComponent(cache, scannerFk.Value, miscByFormKey, constructibleByFormKey)})")}");

    if (scannerFk is not null)
    {
        var located = FindMajorRecordGroup(mod, scannerFk.Value);
        Console.WriteLine(
            located is null
                ? $"  HandScannerTarget: not found in enumerable major-record groups on this mod (may be non-major / alias-only / overlay quirk)."
                : $"  HandScannerTarget resolved in mod enumeration: {located}");
    }

    Console.WriteLine();
    if (questFk is null || !cache.TryResolve<IQuestGetter>(questFk.Value, out var quest))
    {
        Console.WriteLine("(Could not resolve **OutpostFauna** link as **IQuestGetter** — stop.)");
        return 0;
    }

    Console.WriteLine(
        $"Quest record: {quest.FormKey}  EDID={quest.EditorID}{TranslatedNameSuffix(quest)}  Stages={quest.Stages?.Count ?? 0}  Objectives={quest.Objectives?.Count ?? 0}");
    try
    {
        var summary = quest.Summary;
        if (!string.IsNullOrWhiteSpace(summary))
            Console.WriteLine($"  Summary: {summary}");
    }
    catch
    {
        /* TranslatedString etc. */
    }

    Console.WriteLine();
    Console.WriteLine("Quest objectives (index, target count, target keywords — no display text):");
    var objs = quest.Objectives;
    if (objs is null || objs.Count == 0)
        Console.WriteLine("  (none)");
    else
    {
        foreach (var o in objs)
        {
            var targets = o.Targets;
            var tc = targets?.Count ?? 0;
            Console.WriteLine($"  Objective Index={o.Index}  Targets={tc}");
            if (targets is null) continue;
            for (var ti = 0; ti < targets.Count; ti++)
            {
                var t = targets[ti];
                string kw = "(no Keyword)";
                if (!t.Keyword.IsNull && cache.TryResolve<IKeywordGetter>(t.Keyword.FormKey, out var kg))
                    kw = $"{t.Keyword.FormKey}  EDID={kg.EditorID}{TranslatedNameSuffix(kg)}";
                else if (!t.Keyword.IsNull)
                    kw = t.Keyword.FormKey.ToString();
                var condN = 0;
                try
                {
                    condN = t.Conditions?.Count ?? 0;
                }
                catch
                {
                    condN = -1;
                }

                Console.WriteLine($"    target[{ti}]  AliasID={t.AliasID}  Keyword={kw}  Conditions={condN}");
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine("Quest VirtualMachineAdapter (fragments + embedded script properties):");
    DumpQuestVirtualMachineAdapter(quest, "  ", cache, miscByFormKey, constructibleByFormKey);

    Console.WriteLine();
    Console.WriteLine(
        "Conclusion: **`FaunaCreation`** on the **container** defines herd **slots** (keyword + **createCount**). " +
        "**`OutpostFauna`** points at a **quest** (vanilla EDID often **`SQ_Parent`** on the base form — shared parent pattern). " +
        "**`HandScannerTarget`** is a **ScriptObjectProperty** to **`ActorValueInformation`** `HandScannerTarget` (scanner-related **actor value**, not a creature formlink). " +
        "The **`OutpostFauna` → `SQ_Parent`** quest row on disk is an **empty shell** (0 stages/objectives, no quest VMAD properties here) — typical **parent quest** pattern; fauna logic is in **compiled `OutpostHarvesterFaunaScript`** + runtime state. " +
        "Planet/terminal eligibility is **not** derivable from **`PlanetBiome.Fauna`** ∩ **`ActorTypeHerd*`** alone.");
    Console.WriteLine();

    return 0;
}

static void PrintFormListEntries(
    ILinkCache cache,
    IFormListGetter fl,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    var items = fl.Items;
    var n = items?.Count ?? 0;
    Console.WriteLine($"FormList {fl.FormKey}{TranslatedNameSuffix(fl)}  {n} entr(y/ies):");
    if (items is null) return;
    foreach (var it in items)
    {
        if (it.IsNull) continue;
        var fk = it.FormKey;
        Console.WriteLine($"  {fk}  ({DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey)})");
    }
}

}
