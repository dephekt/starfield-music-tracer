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
static int RunInspectGameEnvironment(StarfieldExploreSession session)
{
    var env = session.Environment;
    var pluginsTxt = Environment.GetEnvironmentVariable("STARFIELD_PLUGINS_TXT")?.Trim();
    var loSpec = Environment.GetEnvironmentVariable("STARFIELD_LOAD_ORDER");

    Console.WriteLine($"WithTargetDataFolder: {session.DataDirectory}");
    if (!string.IsNullOrWhiteSpace(loSpec))
    {
        var n = loSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        Console.WriteLine($"STARFIELD_LOAD_ORDER → {n} explicit plugin name(s) (**WithLoadOrder**)");
    }

    if (!string.IsNullOrEmpty(pluginsTxt))
    {
        Console.WriteLine(
            "STARFIELD_PLUGINS_TXT → **IPluginListingsPathContext** via **PluginListingsPathInjection** " +
            "(archive / string listing pipeline).");
        Console.WriteLine($"  {pluginsTxt}");
    }

    Console.WriteLine("Starfield.esm: ESM-shaped record scan uses session.StarfieldEsm (listing entry above).");
    Console.WriteLine($"LinkCache: {session.LinkCache.GetType().Name}");
    Console.WriteLine($"Effective target language: {session.TargetLanguage}");

    Console.WriteLine();
    Console.WriteLine($"Resolved load order — {env.LoadOrder.Count} plugin(s):");
    foreach (var l in env.LoadOrder.ListedOrder)
        Console.WriteLine($"  {l.ModKey.FileName}");

    if (!string.IsNullOrEmpty(pluginsTxt))
        Console.WriteLine($"LoadOrderFilePath (context): {env.LoadOrderFilePath}");

    Console.WriteLine();
    TryPrintSampleLocalizedName(env);
    return 0;
}

static void TryPrintSampleLocalizedName(IGameEnvironment<IStarfieldMod, IStarfieldModGetter> env)
{
    IIngestibleGetter? amp = null;
    foreach (var ing in env.LoadOrder.PriorityOrder.Ingestible().WinningOverrides())
    {
        if (string.Equals(ing.EditorID, "Chem_Craft_Amp", StringComparison.Ordinal))
        {
            amp = ing;
            break;
        }
    }

    if (amp is null)
    {
        Console.WriteLine("Sample **Chem_Craft_Amp**: not found in winning overrides.");
        return;
    }

    try
    {
        var s = amp.Name?.String ?? "";
        Console.WriteLine(
            string.IsNullOrEmpty(s)
                ? "Sample **Chem_Craft_Amp** → Name.String is empty (strings path or language may still be wrong)."
                : $"Sample **Chem_Craft_Amp** → Name.String = \"{s}\"");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Sample **Chem_Craft_Amp** → Name.String threw: {ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine(
        "If still empty: try **StringsFolderOverride** / **BsaFolderOverride** on **StringsReadParameters** (Data folder).");
}

}
