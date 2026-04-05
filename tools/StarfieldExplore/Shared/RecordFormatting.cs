using System.Collections;
using System.Globalization;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;

partial class Program
{
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
        return $"MiscItem  EDID={dictMisc.EditorID}";
    if (constructibleByFormKey?.TryGetValue(fk, out var dictCobj) == true)
        return $"ConstructibleObject  EDID={dictCobj.EditorID}";
    if (cache.TryResolve<IItemGetter>(fk, out var item))
        return $"Item ({item.GetType().Name})  EDID={item.EditorID}";
    if (cache.TryResolve<IMiscItemGetter>(fk, out var misc))
        return $"MiscItem  EDID={misc.EditorID}";
    if (cache.TryResolve<IResourceGetter>(fk, out var res))
        return $"Resource  EDID={res.EditorID}  ResourceType={res.ResourceType}";
    if (cache.TryResolve<IIngestibleGetter>(fk, out var ing))
        return $"Ingestible  EDID={ing.EditorID}";
    if (cache.TryResolve<IConstructibleObjectGetter>(fk, out var cobj))
        return $"ConstructibleObject  EDID={cobj.EditorID}";
    if (cache.TryResolve<IMajorRecordGetter>(fk, out var maj))
        return $"{maj.GetType().Name}  EDID={maj.EditorID}";
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
                    return $"group={prop.Name}  CLR={maj.GetType().Name}  EDID={maj.EditorID}";
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
