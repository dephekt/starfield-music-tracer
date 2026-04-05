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

/// <summary>Hex dump for Mutagen/Noggog binary subfields (e.g. Transform BNAM) in overlay mode.</summary>
static string FormatBinarySubfieldDebug(object? field)
{
    if (field == null) return "(null)";
    if (field is byte[] arr)
        return arr.Length == 0 ? "(empty)" : Convert.ToHexString(arr);
    if (field is ReadOnlyMemory<byte> rom0)
        return rom0.Length == 0 ? "(empty)" : Convert.ToHexString(rom0.Span);

    const BindingFlags bf = BindingFlags.Public | BindingFlags.Instance;
    var memProp = field.GetType().GetProperty("Memory", bf);
    if (memProp?.GetValue(field) is ReadOnlyMemory<byte> rom2)
        return rom2.Length == 0 ? "(empty)" : Convert.ToHexString(rom2.Span);

    // Noggog ReadOnlyMemorySlice<byte> exposes ToArray(); Span is not reflection-friendly.
    var toArray = field.GetType().GetMethod("ToArray", bf, Type.EmptyTypes);
    if (toArray?.Invoke(field, null) is byte[] copied)
        return copied.Length == 0 ? "(empty)" : Convert.ToHexString(copied);

    return field.ToString() ?? "?";
}

static IVirtualMachineAdapterGetter? TryGetVirtualMachineAdapter(IMajorRecordGetter rec)
{
    var p = rec.GetType().GetProperty("VirtualMachineAdapter", BindingFlags.Public | BindingFlags.Instance);
    return p?.GetValue(rec) as IVirtualMachineAdapterGetter;
}

/// <summary>Expand <see cref="ScriptStructListProperty"/> (e.g. FaunaCreation): each struct has Members as nested script properties.</summary>
static void DumpScriptStructListExpanded(
    IScriptPropertyGetter structListProp,
    string indent,
    int depth,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    if (depth > 5) return;
    const BindingFlags bf = BindingFlags.Public | BindingFlags.Instance;
    var structsProp = structListProp.GetType().GetProperty("Structs", bf);
    if (structsProp?.GetValue(structListProp) is not IList structList) return;

    for (var si = 0; si < structList.Count; si++)
    {
        var entry = structList[si];
        if (entry is null) continue;
        Console.WriteLine($"{indent}struct[{si}] ({entry.GetType().Name})");
        var memProp = entry.GetType().GetProperty("Members", bf);
        if (memProp?.GetValue(entry) is not IList members) continue;

        for (var mi = 0; mi < members.Count; mi++)
        {
            if (members[mi] is not IScriptPropertyGetter msp) continue;
            var mLine = FormatScriptPropertyLine(msp, cache, miscByFormKey, constructibleByFormKey);
            Console.WriteLine($"{indent}  m[{mi}] {mLine}");
            if (msp is IFormLinkContainerGetter mflc)
            {
                var linkN = 0;
                try
                {
                    foreach (var raw in mflc.EnumerateFormLinks(true))
                    {
                        if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var subPath)) continue;
                        if (fk == default || fk.IsNull) continue;
                        var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
                        var hint = string.IsNullOrEmpty(subPath) ? "" : $"{subPath} → ";
                        Console.WriteLine($"{indent}    nested {hint}{fk}  ({desc})");
                        if (++linkN >= 8) break;
                    }

                    if (linkN >= 8)
                        Console.WriteLine($"{indent}    … nested cap 8");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{indent}    nested links: ({ex.GetType().Name}: {ex.Message})");
                }
            }

            if (msp.GetType().Name.Contains("StructList", StringComparison.Ordinal))
                DumpScriptStructListExpanded(msp, indent + "  ", depth + 1, cache, miscByFormKey, constructibleByFormKey);
        }
    }
}

static void DumpVirtualMachineAdapter(
    IVirtualMachineAdapterGetter? vmad,
    string indent,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    if (vmad is null)
    {
        Console.WriteLine($"{indent}(no VirtualMachineAdapter subrecord)");
        return;
    }

    try
    {
        Console.WriteLine($"{indent}VMAD Version={vmad.Version} ObjectFormat={vmad.ObjectFormat} Scripts={vmad.Scripts.Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}VMAD (header unreadable: {ex.Message})");
        return;
    }

    for (var si = 0; si < vmad.Scripts.Count; si++)
    {
        var script = vmad.Scripts[si];
        string? sn;
        object? flags;
        try
        {
            sn = script.Name;
            flags = script.Flags;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{indent}  [{si}] (script unreadable: {ex.Message})");
            continue;
        }

        Console.WriteLine($"{indent}  [{si}] Script={sn}  Flags={flags}");
        IReadOnlyList<IScriptPropertyGetter> props;
        try
        {
            props = script.Properties;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{indent}      (properties unreadable: {ex.Message})");
            continue;
        }

        for (var pi = 0; pi < props.Count; pi++)
        {
            var line = FormatScriptPropertyLine(props[pi], cache, miscByFormKey, constructibleByFormKey);
            Console.WriteLine($"{indent}      prop[{pi}] {line}");
            if (props[pi] is IFormLinkContainerGetter pflc)
            {
                var n = 0;
                try
                {
                    foreach (var raw in pflc.EnumerateFormLinks(true))
                    {
                        if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var subPath)) continue;
                        if (fk == default || fk.IsNull) continue;
                        var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
                        var hint = string.IsNullOrEmpty(subPath) ? "" : $"{subPath} → ";
                        Console.WriteLine($"{indent}        nested {hint}{fk}  ({desc})");
                        if (++n >= 12) break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{indent}        nested links: ({ex.GetType().Name}: {ex.Message})");
                }

                if (n >= 12)
                    Console.WriteLine($"{indent}        … nested cap 12");
            }

            if (props[pi].GetType().Name.Contains("StructList", StringComparison.Ordinal))
                DumpScriptStructListExpanded(props[pi], $"{indent}      ", 0, cache, miscByFormKey, constructibleByFormKey);
        }
    }
}

static FormKey? TryGetScriptObjectFormKeyByName(
    IVirtualMachineAdapterGetter? vmad,
    string scriptName,
    string propertyName)
{
    if (vmad is null) return null;
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

        if (!string.Equals(sn, scriptName, StringComparison.OrdinalIgnoreCase)) continue;
        IReadOnlyList<IScriptPropertyGetter> props;
        try
        {
            props = vmad.Scripts[si].Properties;
        }
        catch
        {
            return null;
        }

        for (var pi = 0; pi < props.Count; pi++)
        {
            string? pn;
            try
            {
                pn = props[pi].Name;
            }
            catch
            {
                continue;
            }

            if (!string.Equals(pn, propertyName, StringComparison.Ordinal)) continue;
            var p = props[pi];
            if (!p.GetType().Name.Contains("Object", StringComparison.Ordinal)) return null;
            var obj = p.GetType().GetProperty("Object")?.GetValue(p);
            if (obj is null) return null;
            if (obj.GetType().GetProperty("FormKey")?.GetValue(obj) is FormKey fk && fk != default && !fk.IsNull)
                return fk;
            return null;
        }

        return null;
    }

    return null;
}

static void DumpQuestVirtualMachineAdapter(
    IQuestGetter quest,
    string indent,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    IQuestAdapterGetter? qa;
    try
    {
        qa = quest.VirtualMachineAdapter;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}(quest VirtualMachineAdapter unreadable: {ex.Message})");
        return;
    }

    if (qa is null)
    {
        Console.WriteLine($"{indent}(no quest VirtualMachineAdapter)");
        return;
    }

    try
    {
        Console.WriteLine(
            $"{indent}Quest VMAD: Fragments={qa.Fragments.Count}  ExtraBindDataVersion={qa.ExtraBindDataVersion}  Versioning={qa.Versioning}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}Quest VMAD (header: {ex.Message})");
        return;
    }

    for (var fi = 0; fi < qa.Fragments.Count; fi++)
    {
        var frag = qa.Fragments[fi];
        Console.WriteLine($"{indent}  fragment[{fi}] {frag?.GetType().Name ?? "null"}");
    }

    IScriptEntryGetter? ent;
    try
    {
        ent = qa.Script;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}  (quest Script entry unreadable: {ex.Message})");
        return;
    }

    if (ent is null)
    {
        Console.WriteLine($"{indent}  (no primary Quest Script entry on adapter)");
        return;
    }

    IReadOnlyList<IScriptPropertyGetter> qprops;
    try
    {
        qprops = ent.Properties;
        Console.WriteLine($"{indent}  Quest script entry: Name={ent.Name}  Properties={qprops.Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}  (quest script properties unreadable: {ex.Message})");
        return;
    }

    for (var pi = 0; pi < qprops.Count; pi++)
    {
        var line = FormatScriptPropertyLine(qprops[pi], cache, miscByFormKey, constructibleByFormKey);
        Console.WriteLine($"{indent}    prop[{pi}] {line}");
        if (qprops[pi] is IFormLinkContainerGetter pflc)
        {
            var n = 0;
            try
            {
                foreach (var raw in pflc.EnumerateFormLinks(true))
                {
                    if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var subPath)) continue;
                    if (fk == default || fk.IsNull) continue;
                    var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
                    var hint = string.IsNullOrEmpty(subPath) ? "" : $"{subPath} → ";
                    Console.WriteLine($"{indent}      nested {hint}{fk}  ({desc})");
                    if (++n >= 12) break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{indent}      nested links: ({ex.GetType().Name}: {ex.Message})");
            }

            if (n >= 12)
                Console.WriteLine($"{indent}      … nested cap 12");
        }

        if (qprops[pi].GetType().Name.Contains("StructList", StringComparison.Ordinal))
            DumpScriptStructListExpanded(qprops[pi], $"{indent}      ", 0, cache, miscByFormKey, constructibleByFormKey);
    }
}

static string FormatScriptPropertyLine(
    IScriptPropertyGetter prop,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    string? name;
    try
    {
        name = prop.Name;
    }
    catch
    {
        name = "?";
    }

    var tn = prop.GetType().Name;
    try
    {
        if (tn.Contains("Float", StringComparison.Ordinal))
        {
            var d = prop.GetType().GetProperty("Data")?.GetValue(prop);
            return $"{tn}  {name}={d}";
        }

        if (tn.Contains("Object", StringComparison.Ordinal))
        {
            var obj = prop.GetType().GetProperty("Object")?.GetValue(prop);
            if (obj is null)
                return $"{tn}  {name}=(null)";
            var fkProp = obj.GetType().GetProperty("FormKey");
            if (fkProp?.GetValue(obj) is FormKey fk && fk != default && !fk.IsNull)
            {
                var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
                return $"{tn}  {name} → {fk}  ({desc})";
            }

            return $"{tn}  {name}={obj}";
        }

        if (tn.Contains("Int", StringComparison.Ordinal) && prop.GetType().GetProperty("Data") is { } ip)
        {
            var v = ip.GetValue(prop);
            return $"{tn}  {name}={v}";
        }

        if (tn.Contains("String", StringComparison.Ordinal) && prop.GetType().GetProperty("Data") is { } sp)
        {
            var v = sp.GetValue(prop);
            return $"{tn}  {name}={v}";
        }

        if (tn.Contains("StructList", StringComparison.Ordinal)
            && prop.GetType().GetProperty("Structs")?.GetValue(prop) is IList structs)
            return $"{tn}  {name}  (Structs count={structs.Count})";

        if (tn.Contains("List", StringComparison.Ordinal))
        {
            var listProp = prop.GetType().GetProperty("Items") ?? prop.GetType().GetProperty("Data");
            if (listProp?.GetValue(prop) is IList list)
                return $"{tn}  {name}  (count={list.Count})";
        }
    }
    catch (Exception ex)
    {
        return $"{tn}  {name}  (read error: {ex.Message})";
    }

    return $"{tn}  {name}";
}

/// <summary>Every EnumerateFormLinks item, including rows where FormKey extraction fails (verbose / “vlinks”).</summary>
static void DumpEnumerateFormLinksVerbose(
    IFormLinkContainerGetter flc,
    string indent,
    int cap,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    var n = 0;
    try
    {
        foreach (var raw in flc.EnumerateFormLinks(true))
        {
            if (n >= cap)
            {
                Console.WriteLine($"{indent}… verbose cap {cap}");
                return;
            }

            if (TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var path))
            {
                var hint = string.IsNullOrEmpty(path) ? "" : $"{path} → ";
                if (fk == default || fk.IsNull)
                    Console.WriteLine($"{indent}[{n}] {hint}(null FormKey)");
                else
                {
                    var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
                    Console.WriteLine($"{indent}[{n}] {hint}{fk}  ({desc})");
                }
            }
            else
                Console.WriteLine($"{indent}[{n}] (no FormKey) {raw?.GetType().Name ?? "null"}: {raw}");

            n++;
        }

        if (n == 0)
            Console.WriteLine($"{indent}(no EnumerateFormLinks items)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}(EnumerateFormLinks verbose: {ex.GetType().Name}: {ex.Message})");
    }
}

/// <summary>Records whose <see cref="IFormLinkContainerGetter.EnumerateFormLinks"/> (recursive) touches a harvester Transform FormKey.</summary>
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

/// <summary>Best-effort scalar dump for overlay records (typed subfields may throw).</summary>
static void DumpPrimitivePublicProperties(object rec, string indent, int maxLines)
{
    const BindingFlags f = BindingFlags.Public | BindingFlags.Instance;
    var n = 0;
    foreach (var p in rec.GetType().GetProperties(f).OrderBy(x => x.Name, StringComparer.Ordinal))
    {
        if (p.GetIndexParameters().Length != 0) continue;
        var pt = p.PropertyType;
        if (pt != typeof(string) && pt != typeof(float) && pt != typeof(double) && pt != typeof(int) && pt != typeof(uint)
            && pt != typeof(long) && pt != typeof(ulong) && pt != typeof(byte) && pt != typeof(sbyte)
            && pt != typeof(short) && pt != typeof(ushort) && pt != typeof(bool))
            continue;
        object? v;
        try
        {
            v = p.GetValue(rec);
        }
        catch
        {
            continue;
        }

        Console.WriteLine($"{indent}{p.Name}={v}");
        if (++n >= maxLines) return;
    }
}

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
                    $"    {group}  {rec.FormKey}  EDID={rec.EditorID}  ({rec.GetType().Name})");
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

static void DumpContainerKeywordsAndVmad(
    IContainerGetter cont,
    string indent,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    Console.WriteLine($"{indent}Container {cont.FormKey}  EDID={cont.EditorID}");
    try
    {
        var kws = cont.Keywords;
        if (kws is null || kws.Count == 0)
            Console.WriteLine($"{indent}  Keywords: (none or null in overlay)");
        else
        {
            Console.WriteLine($"{indent}  Keywords ({kws.Count}):");
            foreach (var lk in kws)
            {
                if (lk.IsNull) continue;
                if (cache.TryResolve<IKeywordGetter>(lk.FormKey, out var kw))
                    Console.WriteLine($"{indent}    {lk.FormKey}  {kw.EditorID}");
                else
                    Console.WriteLine($"{indent}    {lk.FormKey}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}  Keywords: ({ex.GetType().Name}: {ex.Message})");
    }

    try
    {
        if (cont.Items is not null && cont.Items.Count > 0)
            Console.WriteLine($"{indent}  Default.Items: {cont.Items.Count} entr(y/ies) (overlay)");
        else
            Console.WriteLine($"{indent}  Default.Items: (null or empty — typical in binary overlay)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}  Items: ({ex.GetType().Name}: {ex.Message})");
    }

    DumpVirtualMachineAdapter(cont.VirtualMachineAdapter, indent + "  ", cache, miscByFormKey, constructibleByFormKey);
}

static void DumpResolvedFormLinksCap(
    IFormLinkContainerGetter flc,
    string indent,
    int cap,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    var n = 0;
    try
    {
        foreach (var raw in flc.EnumerateFormLinks(true))
        {
            if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var path)) continue;
            if (fk == default || fk.IsNull) continue;
            var desc = DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey);
            var hint = string.IsNullOrEmpty(path) ? "" : $"{path} → ";
            Console.WriteLine($"{indent}{hint}{fk}  ({desc})");
            if (++n >= cap) break;
        }

        if (n >= cap)
            Console.WriteLine($"{indent}… cap {cap}");
        else if (n == 0)
            Console.WriteLine($"{indent}(no resolved FormLinks)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{indent}(EnumerateFormLinks: {ex.GetType().Name}: {ex.Message})");
    }
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
        Console.WriteLine($"=== PackIn {pk.FormKey}  EDID={edid} ===");
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
            Console.WriteLine($"  --- CELL {cell.FormKey}  EDID={cell.EditorID} ---");
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
        Console.WriteLine($"  {c.FormKey}  EDID={c.EditorID}");

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
        $"Quest record: {quest.FormKey}  EDID={quest.EditorID}  Stages={quest.Stages?.Count ?? 0}  Objectives={quest.Objectives?.Count ?? 0}");
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
                    kw = $"{t.Keyword.FormKey}  EDID={kg.EditorID}";
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
    Console.WriteLine($"FormList {fl.FormKey}  {n} entr(y/ies):");
    if (items is null) return;
    foreach (var it in items)
    {
        if (it.IsNull) continue;
        var fk = it.FormKey;
        Console.WriteLine($"  {fk}  ({DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey)})");
    }
}

}
