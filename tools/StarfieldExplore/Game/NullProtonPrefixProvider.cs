using Mutagen.Bethesda;
using Mutagen.Bethesda.Installs.DI;

namespace StarfieldExplore.Game;

/// <summary>INI resolution for archive lists does not need Proton paths when game dir is injected.</summary>
internal sealed class NullProtonPrefixProvider : IProtonPrefixProvider
{
    public string? TryGetProtonLocalAppData(GameRelease release) => null;
    public string? TryGetProtonMyDocuments(GameRelease release) => null;
}
