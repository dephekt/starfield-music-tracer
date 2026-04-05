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
using StarfieldExplore.Cli;
using StarfieldExplore.Game;

static string DataDir() =>
    Environment.GetEnvironmentVariable("STARFIELD_DATA")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".steam/steam/steamapps/common/Starfield/Data");

var opts = CliArguments.Parse(args, out var showHelp);
if (showHelp)
{
    CliArguments.WriteHelp();
    return 0;
}

var dataDir = DataDir();
if (!StarfieldSessionFactory.TryCreate(dataDir, out var session, out var factoryError))
{
    Console.Error.WriteLine(factoryError);
    return 1;
}

using (session)
{
    if (opts.InspectToken is not null)
        return DispatchInspect(session, opts.InspectToken, opts.ListLimit);

    var targetEdids = opts.TargetEdids;
    if (targetEdids.Count == 0)
    {
        var env = Environment.GetEnvironmentVariable("STARFIELD_TARGET_EDIDS");
        targetEdids = string.IsNullOrWhiteSpace(env)
            ? ["Chem_Craft_Amp", "Aid_Craft_PenicillinX"]
            : env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    var mod = session.StarfieldEsm;
    var cache = session.LinkCache;
    Console.WriteLine("Loading GameEnvironment (Starfield.esm from resolved load order) …");
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);

    var floraEdidByFormKey = mod.Florae.ToDictionary(x => x.FormKey, x => x.EditorID);
    var planetFloraByResourceMisc = BuildPlanetFloraByResourceMisc(mod, floraEdidByFormKey);
    var pfRows = planetFloraByResourceMisc.Values.Sum(l => l.Count);
    Console.WriteLine(
        $"PlanetFlora rows indexed: {pfRows} across {planetFloraByResourceMisc.Count} distinct Resource misc FormKeys.");
    var biomeResourceGenByResource = BuildBiomeResourceGenByResourceFormKey(mod, cache);
    var brRows = biomeResourceGenByResource.Values.Sum(l => l.Count);
    Console.WriteLine(
        $"Planet biome ResourceGeneration rows: {brRows} across {biomeResourceGenByResource.Count} distinct IResourceGetter FormKeys.");
    Console.WriteLine($"ResourceGenerationData records in ESM: {mod.ResourceGenerationData.Count()}");
    Console.WriteLine(
        "Outpost organic fauna/flora: FormLists OutpostBuilderOrganic_FaunaList / _FloraList list tier COBJs (not creature/plant whitelists). " +
        "Use --inspect-husbandry, --inspect-outpost-harvesters, --inspect-outpost-husbandry-cells, --inspect-pen-herd-planets, --inspect-pen-fauna-script-trace, --inspect-fauna-production-index (full VMAD/backlink dump for fauna harvester script).");
    var lootNpcsByItemKey = BuildLootNpcIndex(mod, cache);
    var cobjOutputToInputs = BuildCobjOutputToInputs(mod);

    Console.WriteLine();
    Console.WriteLine(
        "Planet flora index: Planet → Biome → PlanetFlora (Flora + Resource misc). " +
        "Matches INARA-style “gathered from flora” for planetary spawns.");
    Console.WriteLine(
        "Inorganics index: Planet → PlanetBiome + IBiomeGetter → ResourceGeneration → ResourceGenerationData.Items.Resource " +
        "(survey data is usually on the Biome record, not PlanetBiome).");
    Console.WriteLine(
        "Creature loot index: Npc.DeathItem → LeveledItem (recursive) → item-like forms. " +
        "Outpost organic recipes: --inspect-outpost-husbandry-cells (CELL + placed); --inspect-outpost-harvesters (Transforms + Globals).");
    Console.WriteLine($"Flora / loot list cap per section: {opts.ListLimit} (use --limit=N or --limit=0 for no cap).");
    Console.WriteLine();

    foreach (var edid in targetEdids)
    {
        if (!TraceCraftTarget(
                mod,
                cache,
                edid,
                miscByFormKey,
                constructibleByFormKey,
                planetFloraByResourceMisc,
                biomeResourceGenByResource,
                lootNpcsByItemKey,
                cobjOutputToInputs,
                opts.ListLimit))
            return 1;
        Console.WriteLine();
    }

    Console.WriteLine(
        "Outpost husbandry — see --inspect-husbandry, --inspect-outpost-harvesters, --inspect-outpost-husbandry-cells, --inspect-pen-herd-planets, --inspect-pen-fauna-script-trace, research/outpost-organic-husbandry.md, research/tooling-catalog.md");

    return 0;
}

