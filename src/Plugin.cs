using MediaBrowser.Common.Plugins;

namespace Emby.BlurayMainTitle;

public sealed class Plugin : BasePlugin
{
    public override Guid Id => new("9542cc6a-9812-46b9-bf48-ff363a25f13e");
    public override string Name => "Blu-ray Main Title Media Info";
    public override string Description => "Fills missing media info from BDMV folders and Blu-ray ISO playlists.";
}
