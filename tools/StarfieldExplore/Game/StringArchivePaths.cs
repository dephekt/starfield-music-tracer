using System.IO.Abstractions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives.DI;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Inis.DI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Order.DI;
using Mutagen.Bethesda.Plugins.Utility;
using Noggog;

namespace StarfieldExplore.Game;

/// <summary>Builds <see cref="IGetApplicableArchivePaths"/> for string BA2 discovery without Windows LocalAppData.</summary>
internal static class StringArchivePaths
{
    internal static ILoadOrderListingsProvider CreateLoadOrderListingsForStringArchives(
        IFileSystem fs,
        ModKey[]? explicitModOrder,
        string? pluginsTxtPath)
    {
        var release = GameRelease.Starfield;
        var releaseCtx = new GameReleaseInjection(release);

        if (explicitModOrder is not null)
        {
            return new LoadOrderListingsInjection(
                explicitModOrder.Select(m => (ILoadOrderListingGetter)new LoadOrderListing(m, enabled: true)).ToArray());
        }

        if (!string.IsNullOrEmpty(pluginsTxtPath))
        {
            var rawReader = new PluginRawListingsReader(
                fs,
                new PluginListingsParser(
                    new PluginListingCommentTrimmer(),
                    new LoadOrderListingParser(new HasEnabledMarkersProvider(releaseCtx))));
            var enabled = new EnabledPluginListingsProvider(
                fs,
                rawReader,
                new PluginListingsPathInjection(new FilePath(pluginsTxtPath)));
            return new LoadOrderListingsInjection(enabled.Get().ToArray());
        }

        return new LoadOrderListingsInjection(new LoadOrderListing(ModKey.FromFileName("Starfield.esm"), enabled: true));
    }

    internal static IGetApplicableArchivePaths CreateArchivePathsForStringLookup(
        IFileSystem fs,
        DirectoryPath dataPath,
        ILoadOrderListingsProvider listingsForArchiveSort)
    {
        var release = GameRelease.Starfield;
        var releaseCtx = new GameReleaseInjection(release);
        var dataDirProvider = new DataDirectoryInjection(dataPath);
        var gameDirLookup = new GameDirectoryLookupInjection(release, dataPath.Directory);
        var archiveExt = new ArchiveExtensionProvider(releaseCtx);
        return new GetApplicableArchivePaths(
            fs,
            new CheckArchiveApplicability(archiveExt),
            dataDirProvider,
            archiveExt,
            new CachedArchiveListingDetailsProvider(
                listingsForArchiveSort,
                new GetArchiveIniListings(
                    fs,
                    new IniPathProvider(
                        releaseCtx,
                        new IniPathLookup(
                            gameDirLookup,
                            new NullProtonPrefixProvider()))),
                new ArchiveNameFromModKeyProvider(releaseCtx)));
    }
}
