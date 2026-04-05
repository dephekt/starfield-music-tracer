namespace StarfieldExplore.Cli;

public static class CliArguments
{
    public sealed record CliOptions(int ListLimit, List<string> TargetEdids, string? InspectToken);

    /// <summary>Parses argv. When <paramref name="showHelp"/> is true, print <see cref="WriteHelp"/> and exit 0 from Main.</summary>
    public static CliOptions Parse(string[] args, out bool showHelp)
    {
        showHelp = false;
        var limit = 25;
        var targets = new List<string>();
        string? inspectToken = null;
        foreach (var a in args)
        {
            if (a is "--help" or "-h")
            {
                showHelp = true;
                return new CliOptions(limit, targets, null);
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

            if (a.StartsWith("--planet-fauna=", StringComparison.OrdinalIgnoreCase))
            {
                inspectToken = "planet-fauna:" + a["--planet-fauna=".Length..];
                continue;
            }

            if (a.StartsWith("--inspect-npc=", StringComparison.OrdinalIgnoreCase))
            {
                inspectToken = "inspect-npc:" + a["--inspect-npc=".Length..];
                continue;
            }

            if (string.Equals(a, "--inspect-game-environment", StringComparison.OrdinalIgnoreCase))
            {
                inspectToken = "game-environment";
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

        return new CliOptions(limit, targets, inspectToken);
    }

    public static void WriteHelp()
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
              --planet-fauna=HINT             Same planet matching as --planet-survey; list PlanetBiome.Fauna spawns (Npc + expanded LeveledNpc) per biome + unique leaf Npc summary (--limit caps summary)
              --inspect-npc=HINT              Npc EditorID substring or FormKey fragment; Name, Race, DefaultTemplate chain, Keywords, DeathItem (classic creatures); CCT planet fauna often DummyRace+CCT_Creature — see banner in output
              --inspect-game-environment      Print resolved GameEnvironment: plugins, load order path, link cache, target language, sample localized name (requires STARFIELD_PLUGINS_TXT or STARFIELD_LOAD_ORDER)
              --inspect-husbandry             Dump outpost organic fauna/flora FormLists, builder COBJs, and key Furniture; exit
              --inspect-outpost-harvesters    Dump harvester Transforms, backlinking PackIn/Activator/Furniture, VMAD + verbose FormLinks, Globals/Curves/GameSettings; exit
              --inspect-outpost-husbandry-cells  Organic tier PackIn → linked storage CELL → Persistent/Temporary placed (VMAD, base form, FormLinks); Container keyword/VMAD pass; exit
              --inspect-pen-herd-planets      Planet fauna → INpcSpawn (Npc | LeveledNpc expanded) → herd keywords; Coverage stats + optional Race→herd bridge heuristic; exit
              --inspect-pen-fauna-script-trace  OutpostHarvesterFaunaScript VMAD → linked quest / faction / HandScannerTarget + SQ_Parent quest VMAD/objectives (no .pex); exit
              --help                          This text

            Data: STARFIELD_DATA (folder containing Starfield.esm). Load order: set STARFIELD_PLUGINS_TXT (full path to Plugins.txt) or STARFIELD_LOAD_ORDER (comma-separated plugin filenames). Optional: STARFIELD_TARGET_LANGUAGE (Mutagen Language enum name) for string resolution.

            Default targets if none given: Chem_Craft_Amp, Aid_Craft_PenicillinX
            Override list: STARFIELD_TARGET_EDIDS=Edid1,Edid2
            """);
    }
}
