using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;

namespace StarfieldExplore.Game;

/// <summary>
/// One <see cref="GameEnvironment"/> load: full link cache, Linux-safe strings, and the Starfield.esm mod for ESM-shaped scans.
/// </summary>
public sealed class StarfieldExploreSession : IDisposable
{
    public string DataDirectory { get; }
    public IGameEnvironment<IStarfieldMod, IStarfieldModGetter> Environment { get; }
    public IStarfieldModGetter StarfieldEsm { get; }
    public Language TargetLanguage { get; }

    public ILinkCache LinkCache => Environment.LinkCache;

    public StarfieldExploreSession(
        string dataDirectory,
        IGameEnvironment<IStarfieldMod, IStarfieldModGetter> environment,
        IStarfieldModGetter starfieldEsm,
        Language targetLanguage)
    {
        DataDirectory = dataDirectory;
        Environment = environment;
        StarfieldEsm = starfieldEsm;
        TargetLanguage = targetLanguage;
    }

    public void Dispose() => Environment.Dispose();
}
