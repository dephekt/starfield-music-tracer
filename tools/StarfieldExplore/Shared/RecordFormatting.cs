using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;

partial class Program
{
/// <summary>
/// Best-effort display string for an <see cref="INpcGetter"/>: <c>Name</c>, <c>ShortName</c>, <c>LongName</c>,
/// first non-empty <c>ObjectTemplates</c> entry <c>Name</c>, then <c>TemplateActors.KeywordsTemplate</c> and
/// <c>DefaultTemplate</c> ancestors (many fauna rows omit top-level <c>Name</c>).
/// </summary>
static string FormatNpcLocalizedName(ILinkCache cache, INpcGetter npc)
{
    try
    {
        var self = TryNpcSelfDisplayString(npc);
        if (!string.IsNullOrWhiteSpace(self))
            return self!;

        var ta = npc.TemplateActors;
        if (ta is not null && !ta.KeywordsTemplate.IsNull
            && cache.TryResolve<INpcGetter>(ta.KeywordsTemplate.FormKey, out var ktn))
        {
            var fromKw = TryNpcSelfDisplayString(ktn);
            if (!string.IsNullOrWhiteSpace(fromKw))
                return fromKw!;
        }

        var seen = new HashSet<FormKey>();
        INpcGetter? cur = npc;
        var depth = 0;
        while (cur is not null && depth < 8 && seen.Add(cur.FormKey))
        {
            if (cur.DefaultTemplate.IsNull)
                break;
            if (!cache.TryResolve<INpcGetter>(cur.DefaultTemplate.FormKey, out var parent))
                break;
            var ps = TryNpcSelfDisplayString(parent);
            if (!string.IsNullOrWhiteSpace(ps))
                return ps!;
            cur = parent;
            depth++;
        }

        if (npc.Name is null)
            return "(null Name on row — no Short/Long/ObjectTemplate name; templates above may still carry UI text)";
        return "(empty — check STARFIELD_INI / string BA2 / STARFIELD_TARGET_LANGUAGE)";
    }
    catch (Exception ex)
    {
        return $"({ex.GetType().Name}: {ex.Message})";
    }
}

static string? TryNpcSelfDisplayString(INpcGetter npc)
{
    foreach (var g in new[] { npc.Name, npc.ShortName, npc.LongName })
    {
        var s = TryFormatTranslatedName(g);
        if (!string.IsNullOrWhiteSpace(s))
            return s;
    }

    if (npc.ObjectTemplates is null)
        return null;
    foreach (var ot in npc.ObjectTemplates)
    {
        var s = TryFormatTranslatedName(ot?.Name);
        if (!string.IsNullOrWhiteSpace(s))
            return s;
    }

    return null;
}

/// <summary><see cref="INpcGetter.AttackRace"/> (ATKR) — CK “Traits” species race for CCT rows (e.g. OctopedeARace).</summary>
static string FormatNpcAttackRaceEdid(ILinkCache cache, INpcGetter npc)
{
    if (npc.AttackRace.IsNull)
        return "";
    if (cache.TryResolve<IRaceGetter>(npc.AttackRace.FormKey, out var r) && r.EditorID is not null)
        return r.EditorID;
    return npc.AttackRace.FormKey.ToString();
}

/// <summary><see cref="INpcGetter.Skin"/> armor (WNAM) — CK “Traits” skin (e.g. Skin_OctopedeA…).</summary>
static string FormatNpcSkinWnamEdid(ILinkCache cache, INpcGetter npc)
{
    if (npc.Skin.IsNull)
        return "";
    if (cache.TryResolve<IArmorGetter>(npc.Skin.FormKey, out var a) && a.EditorID is not null)
        return a.EditorID;
    return npc.Skin.FormKey.ToString();
}

/// <summary><c>TemplateActors.TraitTemplate</c> when set on the NPC.</summary>
static string FormatNpcTraitTemplateEdid(ILinkCache cache, INpcGetter npc)
{
    var ta = npc.TemplateActors;
    if (ta is null || ta.TraitTemplate.IsNull)
        return "";
    var fk = ta.TraitTemplate.FormKey;
    if (cache.TryResolve<INpcGetter>(fk, out var n) && n.EditorID is not null)
        return n.EditorID;
    if (cache.TryResolve<IMajorRecordGetter>(fk, out var maj) && maj.EditorID is not null)
        return maj.EditorID;
    return fk.ToString();
}

/// <summary>First non-empty <see cref="IFullNameComponentGetter.Name"/> on <see cref="INpcGetter.Components"/> (CK often surfaces this when the Npc row’s top-level FULL is empty).</summary>
static string FormatNpcComponentFullName(INpcGetter npc)
{
    if (npc.Components is not { Count: > 0 })
        return "";

    foreach (var c in npc.Components)
    {
        if (c is not IFullNameComponentGetter fn)
            continue;
        var s = TryFormatTranslatedName(fn.Name);
        if (!string.IsNullOrWhiteSpace(s))
            return s;
    }

    return "";
}

/// <summary><see cref="FormLinkDataComponent"/> <c>LinkedForm</c> entries that resolve to <see cref="IRaceGetter"/> (semicolon-separated, sorted distinct).</summary>
static string FormatNpcFormLinkDataRaceEdids(ILinkCache cache, INpcGetter npc)
{
    var list = new List<string>();
    if (npc.Components is not { Count: > 0 })
        return "";

    foreach (var c in npc.Components)
    {
        if (c is not IFormLinkDataComponentGetter fld || fld.Links is null)
            continue;
        foreach (var link in fld.Links)
        {
            if (link.LinkedForm.IsNull)
                continue;
            if (cache.TryResolve<IRaceGetter>(link.LinkedForm.FormKey, out var r) && r.EditorID is not null)
                list.Add(r.EditorID);
        }
    }

    if (list.Count == 0)
        return "";
    return string.Join(
        ';',
        list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
}

/// <summary><see cref="FormLinkDataComponent"/> <c>LinkedForm</c> armors; prefers EDIDs containing <c>Skin</c> (semicolon-separated, sorted distinct).</summary>
static string FormatNpcFormLinkDataSkinArmorEdids(ILinkCache cache, INpcGetter npc)
{
    var list = new List<string>();
    if (npc.Components is not { Count: > 0 })
        return "";

    foreach (var c in npc.Components)
    {
        if (c is not IFormLinkDataComponentGetter fld || fld.Links is null)
            continue;
        foreach (var link in fld.Links)
        {
            if (link.LinkedForm.IsNull)
                continue;
            if (cache.TryResolve<IArmorGetter>(link.LinkedForm.FormKey, out var a) && a.EditorID is not null)
                list.Add(a.EditorID);
        }
    }

    if (list.Count == 0)
        return "";
    var skinish = list.Where(e => e.Contains("Skin", StringComparison.OrdinalIgnoreCase)).ToList();
    var use = skinish.Count > 0 ? skinish : list;
    return string.Join(
        ';',
        use.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
}

/// <summary>Resolved FULL/display string, or empty if missing, blank, or resolution throws.</summary>
static string TryFormatTranslatedName(ITranslatedStringGetter? name)
{
    if (name is null) return "";
    try
    {
        var s = name.String;
        return string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
    }
    catch
    {
        return "";
    }
}

/// <summary>Console suffix <c>  Name="…"</c> when a translated name resolves; empty otherwise.</summary>
static string TranslatedNameSuffix(ITranslatedNamedGetter? named)
{
    var s = TryFormatTranslatedName(named?.Name);
    if (string.IsNullOrEmpty(s)) return "";
    return "  Name=\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>Suffix from a bare <see cref="ITranslatedStringGetter"/> (e.g. script property display).</summary>
static string TranslatedNameSuffix(ITranslatedStringGetter? nameGetter)
{
    var s = TryFormatTranslatedName(nameGetter);
    if (string.IsNullOrEmpty(s)) return "";
    return "  Name=\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

static string DisplayNameSuffixForMajor(IMajorRecordGetter? maj)
{
    if (maj is ITranslatedNamedGetter tn)
        return TranslatedNameSuffix(tn);
    return "";
}

/// <summary>Hex dump for Mutagen/Noggog binary subfields (e.g. Transform BNAM).</summary>
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

    var toArray = field.GetType().GetMethod("ToArray", bf, Type.EmptyTypes);
    if (toArray?.Invoke(field, null) is byte[] copied)
        return copied.Length == 0 ? "(empty)" : Convert.ToHexString(copied);

    return field.ToString() ?? "?";
}

/// <summary>Best-effort scalar dump for records (typed subfields may throw).</summary>
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

/// <summary>COBJ components in Starfield are often <see cref="IResourceGetter"/>, not misc items.</summary>
static string DescribeComponent(
    ILinkCache cache,
    FormKey fk,
    IReadOnlyDictionary<FormKey, IMiscItemGetter>? miscByFormKey = null,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter>? constructibleByFormKey = null)
{
    if (miscByFormKey?.TryGetValue(fk, out var dictMisc) == true)
        return $"MiscItem  EDID={dictMisc.EditorID}{TranslatedNameSuffix(dictMisc)}";
    if (constructibleByFormKey?.TryGetValue(fk, out var dictCobj) == true)
        return $"ConstructibleObject  EDID={dictCobj.EditorID}";
    if (cache.TryResolve<IMiscItemGetter>(fk, out var misc))
        return $"MiscItem  EDID={misc.EditorID}{TranslatedNameSuffix(misc)}";
    if (cache.TryResolve<IIngestibleGetter>(fk, out var ing))
        return $"Ingestible  EDID={ing.EditorID}{TranslatedNameSuffix(ing)}";
    if (cache.TryResolve<IFloraGetter>(fk, out var flora))
        return $"Flora  EDID={flora.EditorID}{TranslatedNameSuffix(flora)}";
    if (cache.TryResolve<INpcGetter>(fk, out var npc))
        return $"Npc  EDID={npc.EditorID}{TranslatedNameSuffix(npc)}";
    if (cache.TryResolve<IResourceGetter>(fk, out var res))
        return $"Resource  EDID={res.EditorID}  ResourceType={res.ResourceType}{TranslatedNameSuffix(res)}";
    if (cache.TryResolve<IConstructibleObjectGetter>(fk, out var cobj))
        return $"ConstructibleObject  EDID={cobj.EditorID}";
    if (cache.TryResolve<IItemGetter>(fk, out var item))
    {
        var suf = item is ITranslatedNamedGetter tn ? TranslatedNameSuffix(tn) : "";
        return $"Item ({item.GetType().Name})  EDID={item.EditorID}{suf}";
    }

    if (cache.TryResolve<IQuestGetter>(fk, out var quest))
        return $"Quest  EDID={quest.EditorID}{TranslatedNameSuffix(quest)}";
    if (cache.TryResolve<IContainerGetter>(fk, out var cont))
        return $"Container  EDID={cont.EditorID}{TranslatedNameSuffix(cont)}";
    if (cache.TryResolve<IKeywordGetter>(fk, out var kw))
        return $"Keyword  EDID={kw.EditorID}{TranslatedNameSuffix(kw)}";
    if (cache.TryResolve<IMajorRecordGetter>(fk, out var maj))
        return $"{maj.GetType().Name}  EDID={maj.EditorID}{DisplayNameSuffixForMajor(maj)}";
    return $"unresolved {fk}";
}

/// <summary>
/// Starfield <see cref="IResourceGetter.Produce"/> often points at records outside <see cref="IMiscItemGetter"/> alone.
/// Scan every enumerable major-record group on the loaded mod to find which property holds <paramref name="target"/>.
/// </summary>
static string? FindMajorRecordGroup(IStarfieldModGetter mod, FormKey target)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
    foreach (var prop in mod.GetType().GetProperties(flags))
    {
        if (prop.GetIndexParameters().Length != 0) continue;
        if (!typeof(IEnumerable).IsAssignableFrom(prop.PropertyType)) continue;
        if (prop.PropertyType == typeof(string)) continue;
        object? val;
        try
        {
            val = prop.GetValue(mod);
        }
        catch
        {
            continue;
        }

        if (val is not IEnumerable seq) continue;
        try
        {
            foreach (var item in seq)
            {
                if (item is IMajorRecordGetter maj && maj.FormKey == target)
                    return $"group={prop.Name}  CLR={maj.GetType().Name}  EDID={maj.EditorID}{DisplayNameSuffixForMajor(maj)}";
            }
        }
        catch
        {
            continue;
        }
    }

    return null;
}

}
