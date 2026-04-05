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
static int DispatchInspect(StarfieldExploreSession session, string inspectToken, int listLimit)
{
    if (inspectToken.StartsWith("cobj:", StringComparison.Ordinal))
        return RunInspectCobj(session, inspectToken[5..]);
    if (inspectToken.StartsWith("resource:", StringComparison.Ordinal))
        return RunInspectResource(session, inspectToken[9..]);
    if (inspectToken.StartsWith("planetflora-misc:", StringComparison.Ordinal))
        return RunInspectPlanetFloraForMisc(session, inspectToken["planetflora-misc:".Length..]);
    if (inspectToken.StartsWith("planetflora-misc-substr:", StringComparison.Ordinal))
        return RunInspectPlanetFloraMiscSubstr(session, inspectToken["planetflora-misc-substr:".Length..]);
    if (inspectToken.StartsWith("cobjs-for-output-misc:", StringComparison.Ordinal))
        return RunInspectCobjsForOutputMisc(session, inspectToken["cobjs-for-output-misc:".Length..]);
    if (inspectToken.StartsWith("resourcegen-resource:", StringComparison.Ordinal))
        return RunInspectResourceGenForResource(session, inspectToken["resourcegen-resource:".Length..]);
    if (inspectToken.StartsWith("planet-survey:", StringComparison.Ordinal))
        return RunInspectPlanetSurvey(session, inspectToken["planet-survey:".Length..]);
    if (inspectToken.StartsWith("planet-fauna:", StringComparison.Ordinal))
        return RunInspectPlanetFauna(session, inspectToken["planet-fauna:".Length..], listLimit);
    if (inspectToken.StartsWith("planet-flora:", StringComparison.Ordinal))
        return RunInspectPlanetFlora(session, inspectToken["planet-flora:".Length..], listLimit);
    if (inspectToken.StartsWith("planet-fauna-detail:", StringComparison.Ordinal))
        return RunInspectPlanetFaunaDetail(session, inspectToken["planet-fauna-detail:".Length..], listLimit);
    if (inspectToken.StartsWith("planet-fauna-skin-table:", StringComparison.Ordinal))
        return RunInspectPlanetFaunaSkinTable(session, inspectToken["planet-fauna-skin-table:".Length..], listLimit);
    if (inspectToken.StartsWith("planet-fauna-loot-table:", StringComparison.Ordinal))
        return RunInspectPlanetFaunaLootTable(session, inspectToken["planet-fauna-loot-table:".Length..], listLimit);
    if (inspectToken.StartsWith("planet-fauna-keyword-table:", StringComparison.Ordinal))
        return RunInspectPlanetFaunaKeywordTable(session, inspectToken["planet-fauna-keyword-table:".Length..], listLimit);
    if (inspectToken.StartsWith("planet-fauna-extras-table:", StringComparison.Ordinal))
        return RunInspectPlanetFaunaExtrasTable(session, inspectToken["planet-fauna-extras-table:".Length..], listLimit);
    if (inspectToken.StartsWith("search-edid-substring:", StringComparison.Ordinal))
        return RunSearchEdidSubstring(session, inspectToken["search-edid-substring:".Length..], listLimit, 30);
    if (inspectToken.StartsWith("inspect-npc:", StringComparison.Ordinal))
        return RunInspectNpc(session, inspectToken["inspect-npc:".Length..]);
    if (inspectToken == "game-environment")
        return RunInspectGameEnvironment(session);
    if (inspectToken == "husbandry")
        return RunInspectHusbandry(session);
    if (inspectToken == "outpost-harvesters")
        return RunInspectOutpostHarvesters(session);
    if (inspectToken == "outpost-husbandry-cells")
        return RunInspectOutpostHusbandryCells(session);
    if (inspectToken == "pen-herd-planets")
        return RunInspectPenHerdPlanets(session);
    if (inspectToken == "pen-fauna-script-trace")
        return RunInspectPenFaunaScriptTrace(session);
    if (inspectToken == "pen-fauna-tiers")
        return RunInspectPenFaunaTiers(session);
    if (inspectToken.StartsWith("planet-fauna-pen-bridge:", StringComparison.Ordinal))
        return RunInspectPlanetFaunaPenBridge(session, inspectToken["planet-fauna-pen-bridge:".Length..]);

    Console.Error.WriteLine($"Unknown inspect token: {inspectToken}");
    return 1;
}

}
