using System.Collections;
using System.Globalization;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Starfield;

partial class Program
{
/// <summary>One <c>FaunaCreation</c> struct slot from <see cref="IVirtualMachineAdapterGetter"/> (herd keyword + count).</summary>
readonly record struct FaunaCreationSlotExtract(int SlotIndex, FormKey? CreatureKeyword, int? CreateCount);

static int? TryReadScriptIntPropertyData(IScriptPropertyGetter msp)
{
    const BindingFlags bf = BindingFlags.Public | BindingFlags.Instance;
    var dp = msp.GetType().GetProperty("Data", bf);
    if (dp?.GetValue(msp) is not { } v)
        return null;
    return v switch
    {
        int i => i,
        uint u => (int)u,
        short s => s,
        ushort us => us,
        long l => (int)l,
        _ => null,
    };
}

/// <summary>Parse <paramref name="structListPropertyName"/> on <paramref name="scriptName"/> (e.g. <c>FaunaCreation</c> on <c>OutpostHarvesterFaunaScript</c>).</summary>
static IReadOnlyList<FaunaCreationSlotExtract> TryExtractFaunaCreationSlots(
    IVirtualMachineAdapterGetter? vmad,
    string scriptName,
    string structListPropertyName = "FaunaCreation")
{
    var rows = new List<FaunaCreationSlotExtract>();
    if (vmad is null)
        return rows;

    const BindingFlags bf = BindingFlags.Public | BindingFlags.Instance;
    for (var si = 0; si < vmad.Scripts.Count; si++)
    {
        IScriptEntryGetter scriptEnt;
        try
        {
            scriptEnt = vmad.Scripts[si];
        }
        catch
        {
            continue;
        }

        string? sn;
        try
        {
            sn = scriptEnt.Name;
        }
        catch
        {
            continue;
        }

        if (!string.Equals(sn, scriptName, StringComparison.OrdinalIgnoreCase))
            continue;

        IReadOnlyList<IScriptPropertyGetter> props;
        try
        {
            props = scriptEnt.Properties;
        }
        catch
        {
            continue;
        }

        for (var pi = 0; pi < props.Count; pi++)
        {
            var p = props[pi];
            string? pn;
            try
            {
                pn = p.Name;
            }
            catch
            {
                continue;
            }

            if (!string.Equals(pn, structListPropertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!p.GetType().Name.Contains("StructList", StringComparison.Ordinal))
                continue;
            var structsProp = p.GetType().GetProperty("Structs", bf);
            if (structsProp?.GetValue(p) is not IList structList)
                continue;

            for (var st = 0; st < structList.Count; st++)
            {
                var entry = structList[st];
                if (entry is null)
                    continue;
                FormKey? ck = null;
                int? cc = null;
                var memProp = entry.GetType().GetProperty("Members", bf);
                if (memProp?.GetValue(entry) is not IList members)
                    continue;

                for (var mi = 0; mi < members.Count; mi++)
                {
                    if (members[mi] is not IScriptPropertyGetter msp)
                        continue;
                    string? mn;
                    try
                    {
                        mn = msp.Name;
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.Equals(mn, "CreatureKeyword", StringComparison.OrdinalIgnoreCase)
                        && msp.GetType().Name.Contains("Object", StringComparison.Ordinal))
                    {
                        var obj = msp.GetType().GetProperty("Object")?.GetValue(msp);
                        if (obj?.GetType().GetProperty("FormKey")?.GetValue(obj) is FormKey fk && fk != default && !fk.IsNull)
                            ck = fk;
                    }
                    else if (string.Equals(mn, "createCount", StringComparison.OrdinalIgnoreCase))
                        cc = TryReadScriptIntPropertyData(msp);
                }

                rows.Add(new FaunaCreationSlotExtract(st, ck, cc));
            }
        }
    }

    return rows;
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

static void DumpContainerKeywordsAndVmad(
    IContainerGetter cont,
    string indent,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey)
{
    Console.WriteLine($"{indent}Container {cont.FormKey}  EDID={cont.EditorID}{TranslatedNameSuffix(cont)}");
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
                    Console.WriteLine($"{indent}    {lk.FormKey}  {kw.EditorID}{TranslatedNameSuffix(kw)}");
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
    IReadOnlyDictionary<FormKey, IMiscItemGetter>? miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter>? constructibleByFormKey)
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

}
