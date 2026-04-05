using System.IO.Abstractions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order.DI;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Noggog;

namespace StarfieldExplore.Game;

public static class StarfieldSessionFactory
{
    /// <summary>Resolve <see cref="Language"/> from env; returns null if unset (session still uses English for strings).</summary>
    public static Language? TryParseTargetLanguageFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("STARFIELD_TARGET_LANGUAGE")?.Trim();
        if (string.IsNullOrEmpty(raw))
            return null;
        if (Enum.TryParse<Language>(raw, ignoreCase: true, out var lang))
            return lang;
        Console.Error.WriteLine($"STARFIELD_TARGET_LANGUAGE={raw} is not a known Mutagen Language; using English.");
        return Language.English;
    }

    /// <summary>
    /// Requires <c>STARFIELD_PLUGINS_TXT</c> or <c>STARFIELD_LOAD_ORDER</c>. Builds full <see cref="GameEnvironment"/> with string/BA2 wiring for Linux.
    /// </summary>
    public static bool TryCreate(string dataDirectory, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out StarfieldExploreSession? session, out string error)
    {
        session = null;
        error = "";

        if (!Directory.Exists(dataDirectory))
        {
            error = $"Data folder not found: {dataDirectory}";
            return false;
        }

        var esmPath = Path.Combine(dataDirectory, "Starfield.esm");
        if (!File.Exists(esmPath))
        {
            error = $"Starfield.esm not found: {esmPath}";
            return false;
        }

        var pluginsTxt = Environment.GetEnvironmentVariable("STARFIELD_PLUGINS_TXT")?.Trim();
        var loSpec = Environment.GetEnvironmentVariable("STARFIELD_LOAD_ORDER");
        var hasPlugins = !string.IsNullOrEmpty(pluginsTxt);
        var hasLo = !string.IsNullOrWhiteSpace(loSpec);

        if (!hasPlugins && !hasLo)
        {
            error =
                "Set STARFIELD_PLUGINS_TXT (full path to Plugins.txt — capital P on Linux) or STARFIELD_LOAD_ORDER (comma-separated plugin filenames) so load order and string BA2 resolution match the game.";
            return false;
        }

        if (hasPlugins && !File.Exists(pluginsTxt!))
        {
            error = $"STARFIELD_PLUGINS_TXT does not exist: {pluginsTxt}";
            return false;
        }

        ModKey[]? modKeys = null;
        if (hasLo)
        {
            modKeys = loSpec!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => ModKey.FromFileName(s))
                .ToArray();
        }

        var fs = IFileSystemExt.DefaultFilesystem;
        DirectoryPath dataPath = dataDirectory;
        var loForStrings = StringArchivePaths.CreateLoadOrderListingsForStringArchives(fs, modKeys, pluginsTxt);
        var archiveForStrings = StringArchivePaths.CreateArchivePathsForStringLookup(fs, dataPath, loForStrings);

        var optionalLang = TryParseTargetLanguageFromEnvironment();
        var effectiveLang = optionalLang ?? Language.English;
        TranslatedString.DefaultLanguage = effectiveLang;

        var stringsRead = new StringsReadParameters
        {
            ApplicableArchivePathsOverride = archiveForStrings,
            TargetLanguage = effectiveLang,
            NonLocalizedEncodingOverride = MutagenEncoding._utf8,
        };

        IGameEnvironment<IStarfieldMod, IStarfieldModGetter> env;
        try
        {
            var builder = GameEnvironment.Typical.Builder<IStarfieldMod, IStarfieldModGetter>(GameRelease.Starfield)
                .WithTargetDataFolder(dataDirectory)
                .WithStringParameters(stringsRead);

            if (hasPlugins)
                builder = builder.WithResolver(t =>
                    t == typeof(IPluginListingsPathContext)
                        ? new PluginListingsPathInjection(new FilePath(pluginsTxt!))
                        : null);

            if (modKeys is not null)
                builder = builder.WithLoadOrder(modKeys);

            env = builder.Build();
        }
        catch (Exception ex)
        {
            error = $"GameEnvironment.Build failed: {ex.Message}";
            return false;
        }

        var starfieldKey = ModKey.FromFileName("Starfield.esm");
        IStarfieldModGetter? starfieldMod = null;
        foreach (var listing in env.LoadOrder.ListedOrder)
        {
            if (listing.ModKey != starfieldKey)
                continue;
            starfieldMod = listing.Mod;
            break;
        }

        if (starfieldMod is null)
        {
            env.Dispose();
            error = $"Starfield.esm is not present in the resolved load order (ModKey {starfieldKey}).";
            return false;
        }

        session = new StarfieldExploreSession(dataDirectory, env, starfieldMod, effectiveLang);
        return true;
    }
}
