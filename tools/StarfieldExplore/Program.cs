using System.Collections;
using System.Globalization;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;

static string DataDir() =>
    Environment.GetEnvironmentVariable("STARFIELD_DATA")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".steam/steam/steamapps/common/Starfield/Data");

var (listLimit, targetEdids, inspectToken) = ParseArgs(args);
var dataDir = DataDir();

if (inspectToken is not null)
{
    if (inspectToken.StartsWith("cobj:", StringComparison.Ordinal))
        RunInspectCobj(dataDir, inspectToken[5..]);
    else if (inspectToken.StartsWith("resource:", StringComparison.Ordinal))
        RunInspectResource(dataDir, inspectToken[9..]);
    else if (inspectToken.StartsWith("planetflora-misc:", StringComparison.Ordinal))
        RunInspectPlanetFloraForMisc(dataDir, inspectToken["planetflora-misc:".Length..]);
    else if (inspectToken.StartsWith("planetflora-misc-substr:", StringComparison.Ordinal))
        RunInspectPlanetFloraMiscSubstr(dataDir, inspectToken["planetflora-misc-substr:".Length..]);
    else if (inspectToken.StartsWith("cobjs-for-output-misc:", StringComparison.Ordinal))
        RunInspectCobjsForOutputMisc(dataDir, inspectToken["cobjs-for-output-misc:".Length..]);
    else if (inspectToken.StartsWith("resourcegen-resource:", StringComparison.Ordinal))
        RunInspectResourceGenForResource(dataDir, inspectToken["resourcegen-resource:".Length..]);
    else if (inspectToken.StartsWith("planet-survey:", StringComparison.Ordinal))
        RunInspectPlanetSurvey(dataDir, inspectToken["planet-survey:".Length..]);
    else if (inspectToken == "husbandry")
        RunInspectHusbandry(dataDir);
    else if (inspectToken == "outpost-harvesters")
        RunInspectOutpostHarvesters(dataDir);
    else if (inspectToken == "outpost-husbandry-cells")
        RunInspectOutpostHusbandryCells(dataDir);
    else if (inspectToken == "pen-herd-planets")
        RunInspectPenHerdPlanets(dataDir);
    else if (inspectToken == "pen-fauna-script-trace")
        RunInspectPenFaunaScriptTrace(dataDir);
    else
    {
        Console.Error.WriteLine($"Unknown inspect token: {inspectToken}");
        return 1;
    }

    return 0;
}

if (targetEdids.Count == 0)
{
    var env = Environment.GetEnvironmentVariable("STARFIELD_TARGET_EDIDS");
    targetEdids = string.IsNullOrWhiteSpace(env)
        ? ["Chem_Craft_Amp", "Aid_Craft_PenicillinX"]
        : env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
var esm = Path.Combine(dataDir, "Starfield.esm");
if (!File.Exists(esm))
{
    Console.Error.WriteLine($"Starfield.esm not found: {esm}");
    return 1;
}

Console.WriteLine($"Loading (overlay) {esm} …");
var path = ModPath.FromPath(esm);
using var mod = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
var cache = mod.ToImmutableLinkCache();
var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
var ingestibleByFormKey = mod.Ingestibles.ToDictionary(x => x.FormKey);
var leveledItemByFormKey = mod.LeveledItems.ToDictionary(x => x.FormKey);
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
    "Use --inspect-husbandry, --inspect-outpost-harvesters, --inspect-outpost-husbandry-cells, --inspect-pen-herd-planets, --inspect-pen-fauna-script-trace (fauna pen VMAD → quest / scanner).");
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
Console.WriteLine($"Flora / loot list cap per section: {listLimit} (use --limit=N or --limit=0 for no cap).");
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
            listLimit))
        return 1;
    Console.WriteLine();
}

Console.WriteLine(
    "Outpost husbandry — see --inspect-husbandry, --inspect-outpost-harvesters, --inspect-outpost-husbandry-cells, --inspect-pen-herd-planets, --inspect-pen-fauna-script-trace, research/outpost-organic-husbandry.md, research/tooling-catalog.md");

return 0;

static (int listLimit, List<string> targetEdids, string? inspectToken) ParseArgs(string[] args)
{
    var limit = 25;
    var targets = new List<string>();
    string? inspectToken = null;
    foreach (var a in args)
    {
        if (a is "--help" or "-h")
        {
            Console.WriteLine("""
                StarfieldExplore — craft BOM + flora + creature loot (DeathItem); --inspect-husbandry for outpost organic modules.

                Usage:
                  dotnet run -- [options] [IngestibleEditorID ...]

                Options:
                  --limit=N           Max flora / loot NPC lines per bucket (default 25; 0 = unlimited)
                  --inspect-cobj=EDID     Print one COBJ's ConstructableComponents and exit
                  --inspect-resource=EDID     Print IResourceGetter Produce + List (LeveledItem) FormKey and exit
                  --planetflora-misc=EDID         List PlanetFlora rows for that Resource misc EditorID and exit
                  --planetflora-misc-substr=S     List misc EditorIDs used as PlanetFlora.Resource containing S and exit
                  --cobjs-for-output-misc=EDID    List COBJs whose CreatedObject is this misc and exit
                  --resourcegen-resource=EDID     Full RGD scan + biome hits + planet FormLink referrers for this Resource EditorID and exit
                  --planet-survey=HINT            Planet EditorID substring or FormKey fragment (e.g. Altair, 05E05C); dump biomes→RGD→resources + filtered Resource links under planet
                  --inspect-husbandry             Dump outpost organic fauna/flora FormLists, builder COBJs, and key Furniture; exit
                  --inspect-outpost-harvesters    Dump harvester Transforms, backlinking PackIn/Activator/Furniture, VMAD + verbose FormLinks, Globals/Curves/GameSettings; exit
                  --inspect-outpost-husbandry-cells  Organic tier PackIn → linked storage CELL → Persistent/Temporary placed (VMAD, base form, FormLinks); Container keyword/VMAD pass; exit
                  --inspect-pen-herd-planets      Planet fauna → INpcSpawn (Npc | LeveledNpc expanded) → herd keywords; Coverage stats + optional Race→herd bridge heuristic; exit
                  --inspect-pen-fauna-script-trace  OutpostHarvesterFaunaScript VMAD → linked quest / faction / HandScannerTarget + SQ_Parent quest VMAD/objectives (no .pex); exit
                  --help                          This text

                Default targets if none given: Chem_Craft_Amp, Aid_Craft_PenicillinX
                Override list: STARFIELD_TARGET_EDIDS=Edid1,Edid2
                """);
            Environment.Exit(0);
        }

        if (a.StartsWith("--inspect-cobj=", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "cobj:" + a[15..];
            continue;
        }

        if (a.StartsWith("--inspect-resource=", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "resource:" + a[19..];
            continue;
        }

        if (a.StartsWith("--planetflora-misc=", StringComparison.OrdinalIgnoreCase) &&
            !a.StartsWith("--planetflora-misc-substr=", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "planetflora-misc:" + a["--planetflora-misc=".Length..];
            continue;
        }

        if (a.StartsWith("--planetflora-misc-substr=", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "planetflora-misc-substr:" + a["--planetflora-misc-substr=".Length..];
            continue;
        }

        if (a.StartsWith("--cobjs-for-output-misc=", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "cobjs-for-output-misc:" + a["--cobjs-for-output-misc=".Length..];
            continue;
        }

        if (a.StartsWith("--resourcegen-resource=", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "resourcegen-resource:" + a["--resourcegen-resource=".Length..];
            continue;
        }

        if (a.StartsWith("--planet-survey=", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "planet-survey:" + a["--planet-survey=".Length..];
            continue;
        }

        if (string.Equals(a, "--inspect-husbandry", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "husbandry";
            continue;
        }

        if (string.Equals(a, "--inspect-outpost-harvesters", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "outpost-harvesters";
            continue;
        }

        if (string.Equals(a, "--inspect-outpost-husbandry-cells", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "outpost-husbandry-cells";
            continue;
        }

        if (string.Equals(a, "--inspect-pen-herd-planets", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "pen-herd-planets";
            continue;
        }

        if (string.Equals(a, "--inspect-pen-fauna-script-trace", StringComparison.OrdinalIgnoreCase))
        {
            inspectToken = "pen-fauna-script-trace";
            continue;
        }

        if (a.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(a.AsSpan(8), out var n))
        {
            limit = n;
            continue;
        }

        if (!a.StartsWith('-'))
            targets.Add(a);
    }

    return (limit, targets, inspectToken);
}

static void RunInspectCobjsForOutputMisc(string dataDir, string miscEdid)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);
    var misc = mod.MiscItems.FirstOrDefault(m => m.EditorID == miscEdid);
    if (misc is null)
    {
        Console.Error.WriteLine($"MiscItem {miscEdid} not found.");
        Environment.Exit(1);
    }

    var fk = misc.FormKey;
    var hits = mod.ConstructibleObjects.Where(c => c.CreatedObject.FormKey == fk).ToList();
    Console.WriteLine($"COBJs with CreatedObject -> {miscEdid} ({fk}): {hits.Count}");
    foreach (var c in hits)
    {
        Console.WriteLine($"  {c.FormKey} EDID={c.EditorID}");
        foreach (var line in c.ConstructableComponents ?? [])
        {
            var comp = line.Component;
            if (comp is null || comp.IsNull) continue;
            Console.WriteLine(
                $"    <- {comp.FormKey}  ({DescribeComponent(cache, comp.FormKey, miscByFormKey, constructibleByFormKey)})");
        }
    }
}

static void RunInspectResourceGenForResource(string dataDir, string resourceEdid)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
    var res = mod.Resources.FirstOrDefault(x => x.EditorID == resourceEdid);
    if (res is null)
    {
        Console.Error.WriteLine($"Resource {resourceEdid} not found.");
        Environment.Exit(1);
    }

    Console.WriteLine($"=== Resource {resourceEdid} ({res.FormKey}) — ResourceGenerationData ===");
    var resourceToRgd = BuildResourceToRgdFormKeysFullScan(mod);
    resourceToRgd.TryGetValue(res.FormKey, out var rgdKeySet);
    rgdKeySet ??= [];
    var rgdByKey = mod.ResourceGenerationData.ToDictionary(x => x.FormKey);
    Console.WriteLine(
        $"Distinct ResourceGenerationData records listing this resource in Items[].Resource: {rgdKeySet.Count} (of {mod.ResourceGenerationData.Count()} total RGD in ESM)");
    foreach (var rfk in rgdKeySet.OrderBy(x => x.ToString(), StringComparer.Ordinal))
    {
        rgdByKey.TryGetValue(rfk, out var rgd);
        var rowCount = 0;
        if (rgd?.Items is not null)
        {
            foreach (var it in rgd.Items)
            {
                if (it is null || it.Resource.IsNull) continue;
                if (it.Resource.FormKey == res.FormKey)
                    rowCount++;
            }
        }

        Console.WriteLine($"  RGD {rfk}  EDID={rgd?.EditorID}  ({rowCount} item row(s) for this resource)");
    }

    Console.WriteLine();
    Console.WriteLine("PlanetBiome + IBiomeGetter ResourceGeneration → RGD (same as main trace index):");
    var map = BuildBiomeResourceGenByResourceFormKey(mod, cache);
    if (!map.TryGetValue(res.FormKey, out var rows) || rows.Count == 0)
        Console.WriteLine($"  (none)");
    else
    {
        Console.WriteLine($"  {rows.Count} biome row(s):");
        foreach (var row in rows.Take(50))
            Console.WriteLine($"    Planet {row.PlanetKey} EDID={row.PlanetEdid}  Biome EDID={row.BiomeEdid}");
        if (rows.Count > 50)
            Console.WriteLine($"    … {rows.Count - 50} more");
    }

    Console.WriteLine();
    Console.WriteLine(
        "Planets with any FormLink (EnumerateFormLinks nested) to those RGD FormKeys — catches SurfaceTree / Details / etc., not only Biomes:");
    var planetRefs = FindPlanetsWithFormLinksToKeys(mod, rgdKeySet);
    if (planetRefs.Count == 0)
        Console.WriteLine("  (none — RGD may be unused, or links live outside IPlanet)");
    else
    {
        foreach (var pr in planetRefs.OrderBy(p => p.PlanetEdid, StringComparer.Ordinal).Take(40))
        {
            var sample = string.Join(" | ", pr.PathHints.Take(4));
            var more = pr.PathHints.Count > 4 ? $" (+{pr.PathHints.Count - 4} more link paths)" : "";
            Console.WriteLine(
                $"  Planet {pr.PlanetKey} EDID={pr.PlanetEdid}  links: {pr.PathHints.Count}  e.g. {sample}{more}");
        }

        if (planetRefs.Count > 40)
            Console.WriteLine($"  … {planetRefs.Count - 40} more planets");
    }
}

static void PrintRgdResourceLines(ILinkCache cache, FormKey rgdFk, string prefix)
{
    if (!cache.TryResolve<IResourceGenerationDataGetter>(rgdFk, out var rgd))
    {
        Console.WriteLine($"{prefix} → {rgdFk} (unresolved RGD)");
        return;
    }

    Console.WriteLine($"{prefix} → RGD {rgdFk} EDID={rgd.EditorID}");
    var items = rgd.Items;
    if (items is null)
    {
        Console.WriteLine($"{prefix}    Items: (null)");
        return;
    }

    foreach (var item in items)
    {
        if (item is null || item.Resource.IsNull) continue;
        var rf = item.Resource.FormKey;
        cache.TryResolve<IResourceGetter>(rf, out var res);
        Console.WriteLine($"{prefix}    Resource {rf}  EDID={res?.EditorID}");
    }
}

static bool ResourceEdidLooksLikeSurveyInteresting(string? edid)
{
    if (string.IsNullOrEmpty(edid)) return false;
    return edid.Contains("Argon", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Water", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("H2O", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Uranium", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Uran", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Benz", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("Aromatic", StringComparison.OrdinalIgnoreCase)
        || edid.Contains("C6H", StringComparison.OrdinalIgnoreCase);
}

static void RunInspectPlanetSurvey(string dataDir, string hint)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
    var h = hint.Trim();
    if (h.Length == 0)
    {
        Console.Error.WriteLine("Empty planet hint.");
        Environment.Exit(1);
    }

    var matches = mod.Planets
        .Where(p =>
            p.EditorID?.Contains(h, StringComparison.OrdinalIgnoreCase) == true
            || p.FormKey.ToString().Contains(h, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (matches.Count == 0)
    {
        Console.WriteLine($"No planets matching hint \"{h}\" (EditorID substring or FormKey string fragment).");
        return;
    }

    Console.WriteLine($"Planets matching \"{h}\": {matches.Count}");
    foreach (var planet in matches)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Planet {planet.FormKey}  EDID={planet.EditorID} ===");
        Console.WriteLine("--- Biome → ResourceGeneration → RGD.Items.Resource ---");
        var biomes = planet.Biomes;
        if (biomes is null || biomes.Count == 0)
        {
            Console.WriteLine("(no biomes)");
        }
        else
        {
            for (var bi = 0; bi < biomes.Count; bi++)
            {
                var biome = biomes[bi];
                string? biomeEdid = null;
                if (!biome.Biome.IsNull && cache.TryResolve<IBiomeGetter>(biome.Biome.FormKey, out var br))
                    biomeEdid = br.EditorID;
                Console.WriteLine($"  [{bi}] PlanetBiome  Biome={biomeEdid}  ({biome.Biome.FormKey})");
                var rgPlanetBiome = biome.ResourceGeneration;
                if (rgPlanetBiome.IsNull)
                    Console.WriteLine("      PlanetBiome.ResourceGeneration: (null)");
                else
                    PrintRgdResourceLines(cache, rgPlanetBiome.FormKey, "      PlanetBiome.ResourceGeneration");

                if (!biome.Biome.IsNull && cache.TryResolve<IBiomeGetter>(biome.Biome.FormKey, out var biomeRec))
                {
                    var rgList = biomeRec.ResourceGeneration;
                    if (rgList is null || rgList.Count == 0)
                        Console.WriteLine("      IBiomeGetter.ResourceGeneration: (empty)");
                    else
                    {
                        for (var ri = 0; ri < rgList.Count; ri++)
                        {
                            var link = rgList[ri];
                            if (link.IsNull)
                            {
                                Console.WriteLine($"      IBiomeGetter.ResourceGeneration[{ri}]: (null link)");
                                continue;
                            }

                            PrintRgdResourceLines(
                                cache,
                                link.FormKey,
                                $"      IBiomeGetter.ResourceGeneration[{ri}]");
                        }
                    }
                }
            }
        }

        Console.WriteLine(
            "--- IResourceGetter under planet EnumerateFormLinks(true), filtered (Argon/Water/Uran/Benz/Aromatic/C6H…) ---");
        if (planet is not IFormLinkContainerGetter flc)
        {
            Console.WriteLine("(planet does not implement IFormLinkContainerGetter)");
            continue;
        }

        var seenRes = new HashSet<FormKey>();
        try
        {
            foreach (var raw in flc.EnumerateFormLinks(true))
            {
                if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out _)) continue;
                if (!cache.TryResolve<IResourceGetter>(fk, out var res)) continue;
                if (!ResourceEdidLooksLikeSurveyInteresting(res.EditorID)) continue;
                if (!seenRes.Add(fk)) continue;
                Console.WriteLine($"  {fk}  EDID={res.EditorID}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"(EnumerateFormLinks failed: {ex.Message})");
        }
    }
}

static void RunInspectHusbandry(string dataDir)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
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

static void RunInspectOutpostHarvesters(string dataDir)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
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

static void RunInspectOutpostHusbandryCells(string dataDir)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    string[] organicPackInEdids =
    [
        "OutpostPI_BuilderOrganicFauna01",
        "OutpostPI_BuilderOrganicFauna02",
        "OutpostPI_BuilderOrganicFauna03",
        "OutpostPI_BuilderOrganicFlora01",
        "OutpostPI_BuilderOrganicFlora02",
        "OutpostPI_BuilderOrganicFlora03",
    ];

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
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
}

/// <summary>
/// Trace <c>OutpostHarvesterFaunaScript</c> VMAD on organic fauna builder containers → linked <see cref="IQuestGetter"/> / faction / scanner form,
/// then dump quest adapter + objectives (static ESM data). Eligibility logic lives in compiled Papyrus + scan API, not in planet PCM alone.
/// </summary>
static void RunInspectPenFaunaScriptTrace(string dataDir)
{
    const string faunaScript = "OutpostHarvesterFaunaScript";

    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
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
        return;
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
}

/// <summary><see cref="IKeywordGetter"/> FormKeys whose EditorID starts with ActorTypeHerd (vanilla pen <c>FaunaCreation</c> <c>CreatureKeyword</c> targets).</summary>
static Dictionary<FormKey, string?> BuildActorTypeHerdKeywordEdidByFormKey(IStarfieldModGetter mod)
{
    var map = new Dictionary<FormKey, string?>();
    foreach (var k in mod.Keywords)
    {
        var e = k.EditorID;
        if (e is null || !e.StartsWith("ActorTypeHerd", StringComparison.OrdinalIgnoreCase)) continue;
        map[k.FormKey] = e;
    }

    return map;
}

/// <summary>
/// Planet biome fauna links often resolve to leveled or variant <see cref="INpcGetter"/> rows that omit herd keywords;
/// those keywords may live on <see cref="IRaceGetter"/>, on <see cref="ITemplateActorsGetter.KeywordsTemplate"/>, or on the <see cref="INpcGetter.DefaultTemplate"/> chain.
/// </summary>
static void AddHerdKeywordsFromFaunaNpcAndAncestors(
    INpcGetter npc,
    ILinkCache cache,
    IReadOnlySet<FormKey> herdKeySet,
    HashSet<FormKey> herdsOnPlanet,
    HashSet<FormKey> visitedNpcFormKeys)
{
    if (!visitedNpcFormKeys.Add(npc.FormKey)) return;

    foreach (var lk in npc.Keywords ?? [])
    {
        if (!lk.IsNull && herdKeySet.Contains(lk.FormKey))
            herdsOnPlanet.Add(lk.FormKey);
    }

    if (!npc.Race.IsNull && cache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race))
    {
        foreach (var lk in race.Keywords ?? [])
        {
            if (!lk.IsNull && herdKeySet.Contains(lk.FormKey))
                herdsOnPlanet.Add(lk.FormKey);
        }
    }

    var ta = npc.TemplateActors;
    if (ta is not null && !ta.KeywordsTemplate.IsNull &&
        cache.TryResolve<INpcGetter>(ta.KeywordsTemplate.FormKey, out var keywordsTemplateNpc))
        AddHerdKeywordsFromFaunaNpcAndAncestors(keywordsTemplateNpc, cache, herdKeySet, herdsOnPlanet, visitedNpcFormKeys);

    if (!npc.DefaultTemplate.IsNull && cache.TryResolve<INpcGetter>(npc.DefaultTemplate.FormKey, out var parent))
        AddHerdKeywordsFromFaunaNpcAndAncestors(parent, cache, herdKeySet, herdsOnPlanet, visitedNpcFormKeys);
}

/// <summary>
/// Planet biome fauna rows are typed in Mutagen as <see cref="IFormLinkGetter{INpcGetter}"/>, but the same <see cref="FormKey"/> can resolve to
/// <see cref="ILeveledNpcGetter"/> at runtime (<see cref="INpcSpawnGetter"/> is implemented by both <see cref="Npc"/> and <see cref="LeveledNpc"/>).
/// </summary>
static void AddHerdFromPlanetFaunaSpawnTarget(
    FormKey spawnTargetFk,
    ILinkCache cache,
    IReadOnlySet<FormKey> herdKeySet,
    HashSet<FormKey> herdsOnPlanet,
    HashSet<FormKey> visitedNpcFormKeys,
    HashSet<FormKey> visitedLeveledNpcFormKeys,
    HashSet<FormKey> expandedNpcFormKeysCollector)
{
    if (cache.TryResolve<INpcGetter>(spawnTargetFk, out var npc))
    {
        expandedNpcFormKeysCollector.Add(npc.FormKey);
        AddHerdKeywordsFromFaunaNpcAndAncestors(npc, cache, herdKeySet, herdsOnPlanet, visitedNpcFormKeys);
        return;
    }

    if (cache.TryResolve<ILeveledNpcGetter>(spawnTargetFk, out var lev))
        AddHerdFromLeveledNpcForPlanetFauna(
            lev, cache, herdKeySet, herdsOnPlanet, visitedNpcFormKeys, visitedLeveledNpcFormKeys, expandedNpcFormKeysCollector);
}

static void AddHerdFromLeveledNpcForPlanetFauna(
    ILeveledNpcGetter lev,
    ILinkCache cache,
    IReadOnlySet<FormKey> herdKeySet,
    HashSet<FormKey> herdsOnPlanet,
    HashSet<FormKey> visitedNpcFormKeys,
    HashSet<FormKey> visitedLeveledNpcFormKeys,
    HashSet<FormKey> expandedNpcFormKeysCollector)
{
    if (!visitedLeveledNpcFormKeys.Add(lev.FormKey)) return;

    foreach (var row in lev.Entries ?? [])
    {
        if (row.Reference.IsNull) continue;
        AddHerdFromPlanetFaunaSpawnTarget(
            row.Reference.FormKey,
            cache,
            herdKeySet,
            herdsOnPlanet,
            visitedNpcFormKeys,
            visitedLeveledNpcFormKeys,
            expandedNpcFormKeysCollector);
    }
}

/// <summary>Resolves the same <see cref="INpcSpawn"/> graph as <see cref="AddHerdFromPlanetFaunaSpawnTarget"/> but only collects leaf <see cref="INpcGetter"/> FormKeys.</summary>
static void CollectNpcFormKeysFromFaunaSpawnTarget(
    FormKey spawnTargetFk,
    ILinkCache cache,
    HashSet<FormKey> visitedLeveledNpcFormKeys,
    HashSet<FormKey> outNpcFormKeys)
{
    if (cache.TryResolve<INpcGetter>(spawnTargetFk, out var npc))
    {
        outNpcFormKeys.Add(npc.FormKey);
        return;
    }

    if (!cache.TryResolve<ILeveledNpcGetter>(spawnTargetFk, out var lev)) return;
    if (!visitedLeveledNpcFormKeys.Add(lev.FormKey)) return;

    foreach (var row in lev.Entries ?? [])
    {
        if (row.Reference.IsNull) continue;
        CollectNpcFormKeysFromFaunaSpawnTarget(row.Reference.FormKey, cache, visitedLeveledNpcFormKeys, outNpcFormKeys);
    }
}

static void RunInspectPenHerdPlanets(string dataDir)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();

    var herdKwEdid = BuildActorTypeHerdKeywordEdidByFormKey(mod);
    var herdKeySet = herdKwEdid.Keys.ToHashSet();
    var herdToPlanets = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var hk in herdKeySet)
        herdToPlanets[hk] = [];

    var planetToHerds = new Dictionary<FormKey, HashSet<FormKey>>();
    var planetFaunaNpcFormKeys = new HashSet<FormKey>();
    var planetsWithFaunaRows = 0;
    var faunaEntryRows = 0;
    var faunaTopLevelNpc = 0;
    var faunaTopLevelLeveledNpc = 0;
    var faunaUnresolvedSpawnTarget = 0;

    foreach (var planet in mod.Planets)
    {
        var herdsOnPlanet = new HashSet<FormKey>();
        var biomes = planet.Biomes;
        if (biomes is null) continue;
        var anyFaunaRow = false;
        foreach (var pb in biomes)
        {
            var fauna = pb.Fauna;
            if (fauna is null || fauna.Count == 0) continue;
            foreach (var link in fauna)
            {
                if (link.IsNull) continue;
                anyFaunaRow = true;
                faunaEntryRows++;
                var fk = link.FormKey;
                if (cache.TryResolve<INpcGetter>(fk, out _))
                    faunaTopLevelNpc++;
                else if (cache.TryResolve<ILeveledNpcGetter>(fk, out _))
                    faunaTopLevelLeveledNpc++;
                else
                    faunaUnresolvedSpawnTarget++;

                var visitedNpc = new HashSet<FormKey>();
                var visitedLeveled = new HashSet<FormKey>();
                AddHerdFromPlanetFaunaSpawnTarget(
                    fk,
                    cache,
                    herdKeySet,
                    herdsOnPlanet,
                    visitedNpc,
                    visitedLeveled,
                    planetFaunaNpcFormKeys);
            }
        }

        if (!anyFaunaRow) continue;
        planetsWithFaunaRows++;
        if (herdsOnPlanet.Count == 0) continue;
        planetToHerds[planet.FormKey] = herdsOnPlanet;
        foreach (var h in herdsOnPlanet)
            herdToPlanets[h].Add(planet.FormKey);
    }

    Console.WriteLine(
        "Fauna pen herd tiers vs planet data: vanilla **`FaunaCreation`** uses **`CreatureKeyword`** = **`ActorTypeHerdLarge`** / **`Medium`** / **`Small`**. " +
        "Here: **`Planet` → `PlanetBiome.Fauna`** (form links typed **`INpcGetter`**, but each **`FormKey`** is resolved as **`INpcSpawn`**: **`Npc`** or **`LeveledNpc`**, **`LeveledNpc`** expanded recursively) → herd keywords on each resolved **`Npc`** (same NPC/race/KeywordsTemplate/DefaultTemplate rules). " +
        "Does **not** model full-scan unlock (player progression).");
    Console.WriteLine();

    if (herdKwEdid.Count == 0)
    {
        Console.WriteLine("(no ActorTypeHerd* keywords in Keywords group — unexpected)");
        return;
    }

    var npcFormKeysWithHerdKeyword = new HashSet<FormKey>();
    foreach (var n in mod.Npcs)
    {
        var tierScratch = new HashSet<FormKey>();
        var visitedNpc = new HashSet<FormKey>();
        AddHerdKeywordsFromFaunaNpcAndAncestors(n, cache, herdKeySet, tierScratch, visitedNpc);
        if (tierScratch.Count > 0)
            npcFormKeysWithHerdKeyword.Add(n.FormKey);
    }

    var planetFaunaListedWithHerd = planetFaunaNpcFormKeys.Count(npcFormKeysWithHerdKeyword.Contains);

    Console.WriteLine(
        $"Coverage: distinct **`Npc`** FormKeys reachable from planet fauna (after **`LeveledNpc`** expansion): {planetFaunaNpcFormKeys.Count}  |  " +
        $"NPC records in plugin with ≥1 ActorTypeHerd* (same NPC/race/KeywordsTemplate/DefaultTemplate rules): {npcFormKeysWithHerdKeyword.Count}  |  " +
        $"intersection: {planetFaunaListedWithHerd}");
    if (planetFaunaListedWithHerd == 0 && npcFormKeysWithHerdKeyword.Count > 0)
        Console.WriteLine(
            "(Herd keywords exist on some NPCs, but not on any planet fauna–reachable **`Npc`** in this plugin — pen logic may use a different graph, or DLC/overrides.)");
    Console.WriteLine();

    var raceToHerdTiers = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var herdNpcFk in npcFormKeysWithHerdKeyword)
    {
        if (!cache.TryResolve<INpcGetter>(herdNpcFk, out var hn) || hn.Race.IsNull) continue;
        var raceFk = hn.Race.FormKey;
        if (!raceToHerdTiers.TryGetValue(raceFk, out var tierSet))
            raceToHerdTiers[raceFk] = tierSet = [];
        var scratch = new HashSet<FormKey>();
        var vn = new HashSet<FormKey>();
        AddHerdKeywordsFromFaunaNpcAndAncestors(hn, cache, herdKeySet, scratch, vn);
        foreach (var t in scratch)
            tierSet.Add(t);
    }

    var planetToHerdsRaceBridge = new Dictionary<FormKey, HashSet<FormKey>>();
    var herdToPlanetsRace = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var hk in herdKeySet)
        herdToPlanetsRace[hk] = [];

    foreach (var planet in mod.Planets)
    {
        var npcsHere = new HashSet<FormKey>();
        foreach (var pb in planet.Biomes ?? [])
        {
            foreach (var link in pb.Fauna ?? [])
            {
                if (link.IsNull) continue;
                var vl = new HashSet<FormKey>();
                CollectNpcFormKeysFromFaunaSpawnTarget(link.FormKey, cache, vl, npcsHere);
            }
        }

        if (npcsHere.Count == 0) continue;
        var tiers = new HashSet<FormKey>();
        foreach (var nfk in npcsHere)
        {
            if (!cache.TryResolve<INpcGetter>(nfk, out var pn) || pn.Race.IsNull) continue;
            if (raceToHerdTiers.TryGetValue(pn.Race.FormKey, out var fromRace))
            {
                foreach (var t in fromRace)
                    tiers.Add(t);
            }
        }

        if (tiers.Count == 0) continue;
        planetToHerdsRaceBridge[planet.FormKey] = tiers;
        foreach (var t in tiers)
            herdToPlanetsRace[t].Add(planet.FormKey);
    }

    var racesWithHerd = raceToHerdTiers.Count;
    var racesOnPlanetFauna = new HashSet<FormKey>();
    foreach (var nfk in planetFaunaNpcFormKeys)
    {
        if (cache.TryResolve<INpcGetter>(nfk, out var pn) && !pn.Race.IsNull)
            racesOnPlanetFauna.Add(pn.Race.FormKey);
    }

    var raceOverlapCount = racesOnPlanetFauna.Count(raceToHerdTiers.ContainsKey);

    Console.WriteLine(
        "Race bridge (heuristic): if any **`Npc`** with **`ActorTypeHerd*`** shares a **`Race`** FormKey with a planet fauna **`Npc`**, union those herd tiers onto the planet. " +
        "Not guaranteed to match runtime pen filtering (same race can split herd behavior).");
    Console.WriteLine(
        $"  Races that carry herd tiers (via herd-tagged NPCs): {racesWithHerd}  |  distinct races on planet fauna NPCs: {racesOnPlanetFauna.Count}  |  overlapping races: {raceOverlapCount}");
    Console.WriteLine(
        $"  Planets with ≥1 tier via race bridge: {planetToHerdsRaceBridge.Count}");
    Console.WriteLine();
    Console.WriteLine("  Per herd keyword — planets (race bridge):");
    foreach (var hk in herdKwEdid.Keys.OrderBy(k => herdKwEdid[k] ?? "", StringComparer.Ordinal))
        Console.WriteLine($"    {herdKwEdid[hk]}  →  {herdToPlanetsRace[hk].Count} planet(s)");
    Console.WriteLine();
    Console.WriteLine("  Sample planets (up to 20 by EditorID), race-bridge tiers:");
    foreach (var planet in mod.Planets
        .Where(p => planetToHerdsRaceBridge.ContainsKey(p.FormKey))
        .OrderBy(p => p.EditorID, StringComparer.Ordinal)
        .Take(20))
    {
        var names = planetToHerdsRaceBridge[planet.FormKey]
            .OrderBy(k => herdKwEdid[k] ?? "", StringComparer.Ordinal)
            .Select(k => herdKwEdid[k] ?? k.ToString());
        Console.WriteLine($"    {planet.EditorID}  ({planet.FormKey})  →  {string.Join(", ", names)}");
    }

    if (planetToHerdsRaceBridge.Count == 0)
        Console.WriteLine("    (none — no race overlap between planet fauna NPCs and herd-tagged NPCs.)");
    Console.WriteLine();

    Console.WriteLine("ActorTypeHerd* keywords:");
    foreach (var kv in herdKwEdid.OrderBy(x => x.Value, StringComparer.Ordinal))
        Console.WriteLine($"  {kv.Key}  {kv.Value}");

    Console.WriteLine();
    Console.WriteLine(
        $"Planets with ≥1 PlanetBiome.Fauna row: {planetsWithFaunaRows}  |  fauna entry rows (non-null link): {faunaEntryRows}  |  " +
        $"top-level **`INpcSpawn`** → **`Npc`**: {faunaTopLevelNpc}  |  → **`LeveledNpc`**: {faunaTopLevelLeveledNpc}  |  unresolved: {faunaUnresolvedSpawnTarget}");
    Console.WriteLine($"Planets with ≥1 fauna row carrying ActorTypeHerd* (after resolution): {planetToHerds.Count}");

    Console.WriteLine();
    Console.WriteLine("Per herd keyword — how many planets have ≥1 fauna row whose NPC/race/template carries that keyword:");
    foreach (var hk in herdKwEdid.Keys.OrderBy(k => herdKwEdid[k] ?? "", StringComparer.Ordinal))
    {
        var ed = herdKwEdid[hk];
        Console.WriteLine($"  {ed}  →  {herdToPlanets[hk].Count} planet(s)");
    }

    Console.WriteLine();
    Console.WriteLine("Sample planets (up to 40 by EditorID) with herd tiers present:");
    var sample = mod.Planets
        .Where(p => planetToHerds.ContainsKey(p.FormKey))
        .OrderBy(p => p.EditorID, StringComparer.Ordinal)
        .Take(40)
        .ToList();
    foreach (var planet in sample)
    {
        var tierNames = planetToHerds[planet.FormKey]
            .OrderBy(k => herdKwEdid[k] ?? "", StringComparer.Ordinal)
            .Select(k => herdKwEdid[k] ?? k.ToString());
        Console.WriteLine($"  {planet.EditorID}  ({planet.FormKey})  →  {string.Join(", ", tierNames)}");
    }

    if (sample.Count == 0)
        Console.WriteLine("  (none — see Coverage line above; planet fauna rows may not reference herd-tagged NPCs in this plugin.)");

    Console.WriteLine();
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

static void RunInspectPlanetFloraMiscSubstr(string dataDir, string substr)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var floraEdid = mod.Florae.ToDictionary(x => x.FormKey, x => x.EditorID);
    var map = BuildPlanetFloraByResourceMisc(mod, floraEdid);
    var miscByKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var hits = new List<string>();
    foreach (var fk in map.Keys)
    {
        if (!miscByKey.TryGetValue(fk, out var misc)) continue;
        var e = misc.EditorID;
        if (e is not null && e.Contains(substr, StringComparison.OrdinalIgnoreCase))
            hits.Add($"{fk}  {e}  ({map[fk].Count} rows)");
    }

    hits.Sort(StringComparer.Ordinal);
    Console.WriteLine($"PlanetFlora.Resource misc EditorIDs containing \"{substr}\" ({hits.Count}):");
    foreach (var h in hits)
        Console.WriteLine($"  {h}");
}

static void RunInspectPlanetFloraForMisc(string dataDir, string miscEdid)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var floraEdid = mod.Florae.ToDictionary(x => x.FormKey, x => x.EditorID);
    var map = BuildPlanetFloraByResourceMisc(mod, floraEdid);
    var misc = mod.MiscItems.FirstOrDefault(m => m.EditorID == miscEdid);
    if (misc is null)
    {
        Console.Error.WriteLine($"MiscItem {miscEdid} not found.");
        Environment.Exit(1);
    }

    if (!map.TryGetValue(misc.FormKey, out var rows))
    {
        Console.WriteLine($"No PlanetFlora rows with Resource -> {miscEdid} ({misc.FormKey}).");
        return;
    }

    Console.WriteLine($"PlanetFlora rows for misc {miscEdid} ({misc.FormKey}): {rows.Count}");
    foreach (var row in rows.Take(40))
        Console.WriteLine($"  Flora {row.FloraKey} EDID={row.FloraEdid}  Planet {row.PlanetKey} EDID={row.PlanetEdid}");
    if (rows.Count > 40)
        Console.WriteLine($"  … {rows.Count - 40} more");
}

static void RunInspectResource(string dataDir, string resourceEdid)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);
    var r = mod.Resources.FirstOrDefault(x => x.EditorID == resourceEdid);
    if (r is null)
    {
        Console.Error.WriteLine($"Resource {resourceEdid} not found.");
        Environment.Exit(1);
    }

    Console.WriteLine($"Resource {r.FormKey} EDID={r.EditorID} ResourceType={r.ResourceType}");
    Console.WriteLine($"  Produce: {(r.Produce.IsNull ? "(null)" : r.Produce.FormKey.ToString())}");
    Console.WriteLine($"  List:    {(r.List.IsNull ? "(null)" : r.List.FormKey.ToString())}");
    var kws = r.Keywords;
    if (kws is { Count: > 0 })
    {
        Console.WriteLine($"  Keywords ({kws.Count}):");
        foreach (var kw in kws.Take(30))
        {
            if (kw.IsNull) continue;
            if (cache.TryResolve<IKeywordGetter>(kw.FormKey, out var kg))
                Console.WriteLine($"    {kw.FormKey}  EDID={kg.EditorID}");
            else
                Console.WriteLine($"    {kw.FormKey}");
        }

        if (kws.Count > 30)
            Console.WriteLine($"    … {kws.Count - 30} more");
    }
    if (!r.List.IsNull && cache.TryResolve<ILeveledItemGetter>(r.List.FormKey, out var lev))
    {
        Console.WriteLine($"  List entries ({lev.Entries?.Count ?? 0}):");
        var entries = lev.Entries;
        if (entries is not null)
        {
            foreach (var e in entries.Take(40))
            {
                var rr = e?.Reference;
                if (rr is null || rr.IsNull) continue;
                var fk = rr.FormKey;
                Console.WriteLine(
                    $"    - {fk}  ({DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey)})");
            }

            if (entries.Count > 40)
                Console.WriteLine($"    … {entries.Count - 40} more");
        }
    }
}

static void RunInspectCobj(string dataDir, string cobjEdid)
{
    var esm = Path.Combine(dataDir, "Starfield.esm");
    if (!File.Exists(esm))
    {
        Console.Error.WriteLine($"Starfield.esm not found: {esm}");
        Environment.Exit(1);
    }

    using var mod = StarfieldMod.CreateFromBinaryOverlay(ModPath.FromPath(esm), StarfieldRelease.Starfield);
    var cache = mod.ToImmutableLinkCache();
    var constructibleByFormKey = mod.ConstructibleObjects.ToDictionary(x => x.FormKey);
    var miscByFormKey = mod.MiscItems.ToDictionary(x => x.FormKey);
    var c = mod.ConstructibleObjects.FirstOrDefault(x => x.EditorID == cobjEdid);
    if (c is null)
    {
        Console.Error.WriteLine($"ConstructibleObject {cobjEdid} not found.");
        Environment.Exit(1);
    }

    Console.WriteLine($"COBJ {c.FormKey} EDID={c.EditorID} CreatedObject={c.CreatedObject.FormKey}");
    var n = c.ConstructableComponents?.Count ?? 0;
    Console.WriteLine($"ConstructableComponents count: {n}");
    foreach (var line in c.ConstructableComponents ?? [])
    {
        var comp = line.Component;
        if (comp is null || comp.IsNull) continue;
        var fk = comp.FormKey;
        Console.WriteLine($"  - {fk}  ({DescribeComponent(cache, fk, miscByFormKey, constructibleByFormKey)})");
    }
}

/// <summary>
/// <see cref="IPlanetBiomeGetter.Flora"/> entries pair a <see cref="IFloraGetter"/> with a yield <see cref="IMiscItemGetter"/> (Resource field).
/// This is how vanilla ties “which plant” to “which stackable material” for PCM / survey data (not <see cref="IFloraGetter.Ingredient"/> alone).
/// </summary>
static Dictionary<FormKey, List<(FormKey FloraKey, string? FloraEdid, FormKey PlanetKey, string? PlanetEdid)>> BuildPlanetFloraByResourceMisc(
    IStarfieldModGetter mod,
    IReadOnlyDictionary<FormKey, string?> floraEdidByFormKey)
{
    var map = new Dictionary<FormKey, List<(FormKey, string?, FormKey, string?)>>();
    foreach (var planet in mod.Planets)
    {
        var biomes = planet.Biomes;
        if (biomes is null) continue;
        foreach (var biome in biomes)
        {
            var pfl = biome.Flora;
            if (pfl is null) continue;
            foreach (var pf in pfl)
            {
                if (pf.Resource.IsNull || pf.Flora.IsNull) continue;
                var miscFk = pf.Resource.FormKey;
                var floraFk = pf.Flora.FormKey;
                floraEdidByFormKey.TryGetValue(floraFk, out var floraEdid);
                if (!map.TryGetValue(miscFk, out var rows))
                {
                    rows = [];
                    map[miscFk] = rows;
                }

                rows.Add((floraFk, floraEdid, planet.FormKey, planet.EditorID));
            }
        }
    }

    return map;
}

/// <summary>
/// Append resource FormKeys from one <see cref="IResourceGenerationDataGetter"/> into the planet/biome index.
/// </summary>
static void AppendBiomeResourceGenFromRgd(
    Dictionary<FormKey, List<(FormKey PlanetKey, string? PlanetEdid, string? BiomeEdid)>> map,
    ILinkCache cache,
    FormKey rgdFk,
    FormKey planetKey,
    string? planetEdid,
    string? biomeEdid)
{
    if (!cache.TryResolve<IResourceGenerationDataGetter>(rgdFk, out var rgd)) return;
    var items = rgd.Items;
    if (items is null) return;
    foreach (var item in items)
    {
        if (item is null || item.Resource.IsNull) continue;
        var resFk = item.Resource.FormKey;
        if (!map.TryGetValue(resFk, out var rows))
        {
            rows = [];
            map[resFk] = rows;
        }

        rows.Add((planetKey, planetEdid, biomeEdid));
    }
}

/// <summary>
/// For each <see cref="IResourceGetter"/> FormKey from <see cref="IPlanetBiomeGetter.ResourceGeneration"/> and from
/// <see cref="IBiomeGetter.ResourceGeneration"/> (list of RGD links) on the referenced biome,
/// → <see cref="IResourceGenerationDataGetter.Items"/>[].<see cref="IResourceGenerationDataItemGetter.Resource"/>.
/// Survey-style inorganics usually live on <see cref="IBiomeGetter"/>, not <see cref="IPlanetBiomeGetter"/>.
/// </summary>
static Dictionary<FormKey, List<(FormKey PlanetKey, string? PlanetEdid, string? BiomeEdid)>> BuildBiomeResourceGenByResourceFormKey(
    IStarfieldModGetter mod,
    ILinkCache cache)
{
    var map = new Dictionary<FormKey, List<(FormKey, string?, string?)>>();
    foreach (var planet in mod.Planets)
    {
        var biomes = planet.Biomes;
        if (biomes is null) continue;
        foreach (var biome in biomes)
        {
            IBiomeGetter? biomeRec = null;
            if (!biome.Biome.IsNull && cache.TryResolve<IBiomeGetter>(biome.Biome.FormKey, out var resolved))
                biomeRec = resolved;
            var biomeEdid = biomeRec?.EditorID;

            var rgPb = biome.ResourceGeneration;
            if (!rgPb.IsNull)
                AppendBiomeResourceGenFromRgd(map, cache, rgPb.FormKey, planet.FormKey, planet.EditorID, biomeEdid);

            var rgList = biomeRec?.ResourceGeneration;
            if (rgList is null) continue;
            foreach (var link in rgList)
            {
                if (link.IsNull) continue;
                AppendBiomeResourceGenFromRgd(map, cache, link.FormKey, planet.FormKey, planet.EditorID, biomeEdid);
            }
        }
    }

    return map;
}

/// <summary>
/// Full scan of <see cref="IStarfieldModGetter.ResourceGenerationData"/>: each distinct
/// <see cref="IResourceGenerationDataGetter"/> FormKey that lists <paramref name="resourceFk"/> in <c>Items[].Resource</c>.
/// </summary>
static Dictionary<FormKey, HashSet<FormKey>> BuildResourceToRgdFormKeysFullScan(IStarfieldModGetter mod)
{
    var map = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var rgd in mod.ResourceGenerationData)
    {
        var items = rgd.Items;
        if (items is null) continue;
        foreach (var item in items)
        {
            if (item is null) continue;
            if (item.Resource.IsNull) continue;
            var rf = item.Resource.FormKey;
            if (!map.TryGetValue(rf, out var set))
            {
                set = [];
                map[rf] = set;
            }

            set.Add(rgd.FormKey);
        }
    }

    return map;
}

/// <summary>
/// Planets whose <see cref="IFormLinkContainerGetter.EnumerateFormLinks"/> (recursive) touches any FormKey in <paramref name="targetKeys"/>.
/// </summary>
static List<(FormKey PlanetKey, string? PlanetEdid, List<string> PathHints)> FindPlanetsWithFormLinksToKeys(
    IStarfieldModGetter mod,
    IReadOnlySet<FormKey> targetKeys)
{
    var list = new List<(FormKey, string?, List<string>)>();
    if (targetKeys.Count == 0) return list;

    foreach (var planet in mod.Planets)
    {
        if (planet is not IFormLinkContainerGetter flc) continue;
        var hints = new List<string>();
        try
        {
            foreach (var raw in flc.EnumerateFormLinks(true))
            {
                if (!TryGetFormKeyFromLinkEnumerationItem(raw, out var fk, out var pathHint)) continue;
                if (!targetKeys.Contains(fk)) continue;
                var label = string.IsNullOrEmpty(pathHint) ? fk.ToString() : $"{pathHint} → {fk}";
                hints.Add(label);
            }
        }
        catch
        {
            hints.Add("(EnumerateFormLinks threw — skipped rest for this planet)");
        }

        if (hints.Count > 0)
            list.Add((planet.FormKey, planet.EditorID, hints));
    }

    return list;
}

/// <summary>
/// Mutagen’s link enumerator yields an internal item type; resolve a <see cref="FormKey"/> and optional path hint via reflection.
/// </summary>
static bool TryGetFormKeyFromLinkEnumerationItem(object? item, out FormKey fk, out string? pathHint)
{
    fk = default;
    pathHint = null;
    if (item is null) return false;
    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
    var t = item.GetType();
    foreach (var p in t.GetProperties(flags))
    {
        if (p.PropertyType == typeof(string)
            && p.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
            && p.GetValue(item) is string s
            && !string.IsNullOrEmpty(s))
            pathHint = s;
    }

    foreach (var p in t.GetProperties(flags))
    {
        var val = p.GetValue(item);
        if (val is null) continue;
        var vt = val.GetType();
        var isNullProp = vt.GetProperty("IsNull");
        if (isNullProp?.PropertyType == typeof(bool) && (bool)isNullProp.GetValue(val)! == true) continue;
        var fkProp = vt.GetProperty("FormKey");
        if (fkProp?.GetValue(val) is FormKey f && f != default)
        {
            fk = f;
            return true;
        }
    }

    foreach (var p in t.GetProperties(flags))
    {
        if (p.PropertyType != typeof(FormKey)) continue;
        if (p.GetValue(item) is FormKey f2 && f2 != default)
        {
            fk = f2;
            return true;
        }
    }

    return false;
}

/// <summary>
/// For each COBJ output (<see cref="IConstructibleObjectGetter.CreatedObject"/>), all <see cref="ConstructableComponents"/> FormKeys.
/// Used to walk backward: harvest ingredients often match an *input* to a refinery recipe, not the chemlab resource/misc directly.
/// </summary>
static Dictionary<FormKey, HashSet<FormKey>> BuildCobjOutputToInputs(IStarfieldModGetter mod)
{
    var map = new Dictionary<FormKey, HashSet<FormKey>>();
    foreach (var c in mod.ConstructibleObjects)
    {
        var created = c.CreatedObject;
        if (created.IsNull) continue;
        var o = created.FormKey;
        if (!map.TryGetValue(o, out var set))
        {
            set = [];
            map[o] = set;
        }

        foreach (var line in c.ConstructableComponents ?? [])
        {
            var comp = line.Component?.FormKey;
            if (comp.HasValue)
                set.Add(comp.Value);
        }
    }

    return map;
}

/// <summary>
/// Union of <paramref name="seeds"/> with every FormKey that appears as a COBJ component feeding into any form already in the set (fixpoint).
/// </summary>
static HashSet<FormKey> ExpandPrecursorFormKeys(
    IEnumerable<FormKey> seeds,
    IReadOnlyDictionary<FormKey, HashSet<FormKey>> cobjOutputToInputs)
{
    var expanded = new HashSet<FormKey>();
    var queue = new Queue<FormKey>();
    foreach (var s in seeds)
    {
        if (expanded.Add(s))
            queue.Enqueue(s);
    }

    while (queue.Count > 0)
    {
        var k = queue.Dequeue();
        if (!cobjOutputToInputs.TryGetValue(k, out var inputs)) continue;
        foreach (var inn in inputs)
        {
            if (expanded.Add(inn))
                queue.Enqueue(inn);
        }
    }

    return expanded;
}

/// <summary>
/// Maps item-like FormKey (misc, ingestible, resource, …) to NPCs whose <see cref="INpcGetter.DeathItem"/> expands to that form.
/// </summary>
static Dictionary<FormKey, List<(FormKey NpcKey, string? Edid)>> BuildLootNpcIndex(
    IStarfieldModGetter mod,
    ILinkCache cache)
{
    var map = new Dictionary<FormKey, List<(FormKey, string?)>>();
    foreach (var npc in mod.Npcs)
    {
        var death = npc.DeathItem;
        if (death.IsNull) continue;

        var itemKeys = new HashSet<FormKey>();
        var levVisited = new HashSet<FormKey>();
        ExpandItemKeysFromFormKey(death.FormKey, cache, levVisited, itemKeys);

        foreach (var fk in itemKeys)
        {
            if (!map.TryGetValue(fk, out var list))
            {
                list = [];
                map[fk] = list;
            }

            list.Add((npc.FormKey, npc.EditorID));
        }
    }

    return map;
}

static void ExpandItemKeysFromFormKey(
    FormKey fk,
    ILinkCache cache,
    HashSet<FormKey> leveledVisited,
    HashSet<FormKey> itemLikeKeys)
{
    if (cache.TryResolve<ILeveledItemGetter>(fk, out var lev))
    {
        if (!leveledVisited.Add(fk)) return;
        var entries = lev.Entries;
        if (entries is null) return;
        foreach (var entry in entries)
        {
            var r = entry?.Reference;
            if (r is null || r.IsNull) continue;
            ExpandItemKeysFromFormKey(r.FormKey, cache, leveledVisited, itemLikeKeys);
        }

        return;
    }

    if (cache.TryResolve<IMiscItemGetter>(fk, out _)
        || cache.TryResolve<IIngestibleGetter>(fk, out _)
        || cache.TryResolve<IResourceGetter>(fk, out _))
        itemLikeKeys.Add(fk);
}

static bool TraceCraftTarget(
    IStarfieldModGetter mod,
    ILinkCache cache,
    string targetIngestibleEdid,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey,
    IReadOnlyDictionary<FormKey, List<(FormKey FloraKey, string? FloraEdid, FormKey PlanetKey, string? PlanetEdid)>> planetFloraByResourceMisc,
    IReadOnlyDictionary<FormKey, List<(FormKey PlanetKey, string? PlanetEdid, string? BiomeEdid)>> biomeResourceGenByResource,
    IReadOnlyDictionary<FormKey, List<(FormKey NpcKey, string? Edid)>> lootNpcsByItemKey,
    IReadOnlyDictionary<FormKey, HashSet<FormKey>> cobjOutputToInputs,
    int listLimit)
{
    var ingestible = mod.Ingestibles.FirstOrDefault(i => i.EditorID == targetIngestibleEdid);
    if (ingestible is null)
    {
        Console.Error.WriteLine($"Ingestible {targetIngestibleEdid} not found.");
        return false;
    }

    Console.WriteLine($"=== {targetIngestibleEdid} ===");
    Console.WriteLine($"Ingestible: {ingestible.FormKey}  EDID={ingestible.EditorID}");

    var cobj = mod.ConstructibleObjects.FirstOrDefault(c => c.CreatedObject.FormKey == ingestible.FormKey);
    if (cobj is null)
    {
        Console.Error.WriteLine($"No ConstructibleObject with CreatedObject -> {ingestible.FormKey}.");
        return false;
    }

    Console.WriteLine($"ConstructibleObject: {cobj.FormKey}  EDID={cobj.EditorID}");
    var wb = cobj.WorkbenchKeyword.IsNull
        ? "(null)"
        : cobj.WorkbenchKeyword.TryResolve<IKeywordGetter>(cache, out var kw)
            ? $"{cobj.WorkbenchKeyword.FormKey}  EDID={kw.EditorID}"
            : cobj.WorkbenchKeyword.FormKey.ToString();
    Console.WriteLine($"  WorkbenchKeyword: {wb}");
    Console.WriteLine($"  CreatedObject:     {cobj.CreatedObject.FormKey}");
    Console.WriteLine("  Components (ConstructableComponents):");

    var componentKeys = new List<FormKey>();
    foreach (var line in cobj.ConstructableComponents ?? [])
    {
        var comp = line.Component ?? throw new InvalidOperationException("COBJ line missing Component");
        componentKeys.Add(comp.FormKey);
        Console.WriteLine($"    - {comp.FormKey}  ({DescribeComponent(cache, comp.FormKey, miscByFormKey, constructibleByFormKey)})");
    }

    Console.WriteLine();
    Console.WriteLine("(Quantities: repeated ConstructableComponents rows for the same FormKey.)");
    var qty = (cobj.ConstructableComponents ?? [])
        .Select(x => x.Component?.FormKey)
        .Where(fk => fk.HasValue)
        .Select(fk => fk!.Value)
        .GroupBy(k => k)
        .Select(g => (g.Key, g.Count()))
        .ToList();
    Console.WriteLine("  Quantities by FormKey:");
    foreach (var (fk, n) in qty)
        Console.WriteLine($"    x{n}  {fk}");

    Console.WriteLine();
    Console.WriteLine("Gather hints (flora + planet resource gen + creature loot; not vendors / outpost husbandry):");

    foreach (var fk in componentKeys.Distinct())
    {
        Console.WriteLine($"  Component {fk}:");
        var gather = ResolveGatherKeys(fk, cache, miscByFormKey, constructibleByFormKey, mod);
        if (gather.ResourceNote is not null)
            Console.WriteLine($"    {gather.ResourceNote}");

        var keysToTry = gather.Keys.ToList();
        if (keysToTry.Count == 0)
        {
            Console.WriteLine("    (no gather lookup keys — investigate record type)");
            continue;
        }

        var planetFloraByFloraKey = new Dictionary<FormKey, (string? FloraEdid, HashSet<string?> Planets)>();
        foreach (var lk in keysToTry)
        {
            if (!planetFloraByResourceMisc.TryGetValue(lk, out var rows)) continue;
            foreach (var row in rows)
            {
                if (!planetFloraByFloraKey.TryGetValue(row.FloraKey, out var agg))
                {
                    agg = (row.FloraEdid, []);
                    planetFloraByFloraKey[row.FloraKey] = agg;
                }

                agg.Planets.Add(row.PlanetEdid);
                if (agg.FloraEdid is null && row.FloraEdid is not null)
                    planetFloraByFloraKey[row.FloraKey] = (row.FloraEdid, agg.Planets);
            }
        }

        if (planetFloraByFloraKey.Count > 0)
        {
            Console.WriteLine(
                "    Flora (planet biome spawn; IPlanetFlora.Resource misc matches gather key — INARA-style):");
            PrintLimited(
                planetFloraByFloraKey
                    .OrderBy(kv => kv.Value.FloraEdid, StringComparer.Ordinal)
                    .Select(kv =>
                    {
                        var planetSample = string.Join(", ", kv.Value.Planets.Where(p => !string.IsNullOrEmpty(p)).Take(4));
                        var more = kv.Value.Planets.Count > 4 ? $" +{kv.Value.Planets.Count - 4} planets" : "";
                        return $"      Flora {kv.Key}  EDID={kv.Value.FloraEdid}  [planets: {planetSample}{more}]";
                    }),
                listLimit);
        }
        else
            Console.WriteLine("    Flora (planet PCM): (no PlanetFlora.Resource hit for these gather keys)");

        var resourceGenByPlanet = new Dictionary<FormKey, (string? PlanetEdid, HashSet<string?> Biomes)>();
        foreach (var lk in keysToTry)
        {
            if (!biomeResourceGenByResource.TryGetValue(lk, out var rgRows)) continue;
            foreach (var row in rgRows)
            {
                if (!resourceGenByPlanet.TryGetValue(row.PlanetKey, out var agg))
                {
                    agg = (row.PlanetEdid, []);
                    resourceGenByPlanet[row.PlanetKey] = agg;
                }

                agg.Biomes.Add(row.BiomeEdid);
                if (agg.PlanetEdid is null && row.PlanetEdid is not null)
                    resourceGenByPlanet[row.PlanetKey] = (row.PlanetEdid, agg.Biomes);
            }
        }

        if (resourceGenByPlanet.Count > 0)
        {
            Console.WriteLine(
                "    Planet / biome resource generation (ResourceGenerationData.Items.Resource; inorganics / survey):");
            PrintLimited(
                resourceGenByPlanet
                    .OrderBy(kv => kv.Value.PlanetEdid, StringComparer.Ordinal)
                    .Select(kv =>
                    {
                        var biomeSample = string.Join(", ", kv.Value.Biomes.Where(b => !string.IsNullOrEmpty(b)).Take(6));
                        var more = kv.Value.Biomes.Count > 6 ? $" +{kv.Value.Biomes.Count - 6} biomes" : "";
                        return $"      Planet {kv.Key}  EDID={kv.Value.PlanetEdid}  [biomes: {biomeSample}{more}]";
                    }),
                listLimit);
        }
        else
            Console.WriteLine(
                "    Planet / biome resource generation: (no ResourceGenerationData.Items.Resource hit for these gather keys)");

        var precursorKeys = ExpandPrecursorFormKeys(keysToTry, cobjOutputToInputs);
        var ingredientHits = new Dictionary<FormKey, (FormKey FloraKey, string? Edid)>();
        foreach (var flora in mod.Florae)
        {
            var ing = flora.Ingredient;
            if (ing.IsNull) continue;
            if (!precursorKeys.Contains(ing.FormKey)) continue;
            ingredientHits.TryAdd(flora.FormKey, (flora.FormKey, flora.EditorID));
        }

        if (ingredientHits.Count > 0)
        {
            Console.WriteLine(
                "    Flora (Flora.Ingredient in COBJ precursor chain; Flora.Production is seasonal weights only):");
            PrintLimited(
                ingredientHits.Values
                    .OrderBy(f => f.Edid, StringComparer.Ordinal)
                    .Select(f => $"      Flora {f.FloraKey}  EDID={f.Edid}"),
                listLimit);
        }

        var lootNpcs = new Dictionary<FormKey, (FormKey NpcKey, string? Edid)>();
        foreach (var lookupKey in keysToTry)
        {
            if (!lootNpcsByItemKey.TryGetValue(lookupKey, out var npcs)) continue;
            foreach (var n in npcs)
                lootNpcs.TryAdd(n.NpcKey, n);
        }

        if (lootNpcs.Count > 0)
        {
            Console.WriteLine(
                "    Looted from creature (Npc.DeathItem → LeveledItem → item; not outpost husbandry whitelist):");
            PrintLimited(
                lootNpcs.Values
                    .OrderBy(n => n.Edid, StringComparer.Ordinal)
                    .Select(n => $"      Npc {n.NpcKey}  EDID={n.Edid}"),
                listLimit);
        }
        else
            Console.WriteLine("    Creature loot: (no Npc.DeathItem expansion hits these keys)");

        if (gather.GasOrExtractorLikely && resourceGenByPlanet.Count == 0)
            Console.WriteLine(
                "    Note: component looks like gas/inorganic; if no ResourceGeneration rows above, check other bodies or mod load order.");
    }

    return true;
}

static (HashSet<FormKey> Keys, string? ResourceNote, bool GasOrExtractorLikely) ResolveGatherKeys(
    FormKey componentFk,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey,
    IStarfieldModGetter mod)
{
    var keys = new HashSet<FormKey> { componentFk };
    string? note = null;
    var gas = false;

    if (cache.TryResolve<IResourceGetter>(componentFk, out var res) && !res.Produce.IsNull)
    {
        var produceFk = res.Produce.FormKey;
        var produceLabel = FormatProduceLabel(produceFk, cache, miscByFormKey, constructibleByFormKey, mod);
        note = $"Resource chain: {res.EditorID}  -> Produce {produceFk} ({produceLabel})";

        keys.Add(produceFk);
        if (constructibleByFormKey.TryGetValue(produceFk, out var nestedCobj))
        {
            var created = nestedCobj.CreatedObject.FormKey;
            keys.Add(created);
            var createdDesc = DescribeComponent(cache, created, miscByFormKey, constructibleByFormKey);
            note += $"; refined misc {created} ({createdDesc})";
            AddOrganPartHarvestMiscKeys(keys, miscByFormKey, created);
        }

        if (res.EditorID?.Contains("Inorg", StringComparison.OrdinalIgnoreCase) == true
            || res.EditorID?.Contains("Argon", StringComparison.OrdinalIgnoreCase) == true)
            gas = true;
    }

    return (keys, note, gas);
}

/// <summary>
/// Planet PCM uses per-organ miscs (e.g. <c>OrgCommonToxin_Leaf</c>) as <see cref="IPlanetFloraGetter.Resource"/>,
/// while chemlab/refinery uses the stackable base misc (<c>OrgCommonToxin</c>). Link them by EditorID prefix <c>{base}_</c>.
/// </summary>
static void AddOrganPartHarvestMiscKeys(
    HashSet<FormKey> keys,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    FormKey refinedStackableMiscFk)
{
    if (!miscByFormKey.TryGetValue(refinedStackableMiscFk, out var baseMisc)) return;
    var bn = baseMisc.EditorID;
    if (string.IsNullOrEmpty(bn)) return;
    var prefix = bn + "_";
    foreach (var m in miscByFormKey.Values)
    {
        if (m.EditorID?.StartsWith(prefix, StringComparison.Ordinal) == true)
            keys.Add(m.FormKey);
    }
}

static string FormatProduceLabel(
    FormKey produceFk,
    ILinkCache cache,
    IReadOnlyDictionary<FormKey, IMiscItemGetter> miscByFormKey,
    IReadOnlyDictionary<FormKey, IConstructibleObjectGetter> constructibleByFormKey,
    IStarfieldModGetter mod)
{
    if (miscByFormKey.TryGetValue(produceFk, out var pm))
        return $"MiscItem EDID={pm.EditorID}";
    if (constructibleByFormKey.TryGetValue(produceFk, out var refineCobj))
        return $"ConstructibleObject EDID={refineCobj.EditorID}  -> CreatedObject {refineCobj.CreatedObject.FormKey}";
    if (!constructibleByFormKey.ContainsKey(produceFk))
    {
        var located = FindMajorRecordGroup(mod, produceFk);
        if (located is not null)
            return $"{DescribeComponent(cache, produceFk, miscByFormKey, constructibleByFormKey)}  |  {located}";
    }

    return DescribeComponent(cache, produceFk, miscByFormKey, constructibleByFormKey);
}

static void PrintLimited(IEnumerable<string> lines, int limit)
{
    if (limit == 0)
    {
        foreach (var line in lines)
            Console.WriteLine(line);
        return;
    }

    var n = 0;
    foreach (var line in lines)
    {
        if (n >= limit)
        {
            Console.WriteLine($"      … (cap {limit}; use --limit=0 for full list)");
            return;
        }

        Console.WriteLine(line);
        n++;
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
