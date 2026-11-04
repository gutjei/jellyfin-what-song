using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SongPause;

public sealed class Plugin(IApplicationPaths paths, IXmlSerializer serializer)
    : BasePlugin<BasePluginConfiguration>(paths, serializer)
{
    public override string Name => "Song Pause";
    public override Guid Id => new("7bd9d031-4b68-47c2-a04b-fb2e1987ae71");
    public override string Description => "Show songs from a companion timeline when video is paused.";
}
