using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.BlurayMainTitle;

public sealed class ScanTask : IScheduledTask
{
    private readonly ILibraryManager library;
    private readonly IItemRepository repository;
    private readonly IMediaSourceManager mediaSources;
    private readonly ILogger logger;
    private readonly string helper;

    public ScanTask(ILibraryManager library, IItemRepository repository, IMediaSourceManager mediaSources,
        IApplicationPaths paths, ILogManager logs)
    {
        this.library = library;
        this.repository = repository;
        this.mediaSources = mediaSources;
        logger = logs.GetLogger(nameof(BlurayMainTitle));
        helper = Path.Combine(paths.PluginsPath, "bluray-main-title");
    }

    public string Key => "BluRayMainTitleMediaInfo";
    public string Name => "Fill Blu-ray main title media info";
    public string Description => "Fills missing Blu-ray streams, HDR, runtime and chapter information.";
    public string Category => "Library";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        if (!File.Exists(helper))
            throw new FileNotFoundException("Install bluray-main-title in the Emby plugins directory.");

        var videos = library.GetItemList(new InternalItemsQuery
        {
            Recursive = true,
            Containers = new[] { "bluray", "blurayiso", "iso" }
        }).OfType<Video>().ToArray();

        for (var index = 0; index < videos.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var video = videos[index];
            try
            {
                await Update(video, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or Win32Exception or JsonException or TimeoutException)
            {
                logger.Warn("Skipping disc item {0} ({1}).", video.InternalId, ex.Message);
            }
            progress.Report(100.0 * (index + 1) / videos.Length);
        }
        progress.Report(100);
    }

    private async Task Update(Video video, CancellationToken cancellationToken)
    {
        var itemPath = video.Path;
        var container = video.Container;
        if (string.IsNullOrEmpty(itemPath))
            return;

        MountedDiscPath? mounted = null;
        if (OperatingSystem.IsLinux() && MountedDiscPath.IsNetworkUri(itemPath))
            mounted = MountedDiscPath.Resolve(itemPath) ??
                throw new InvalidDataException("No matching Linux mount for the SMB or NFS source.");
        else if (!video.IsFileProtocol || !Path.IsPathFullyQualified(itemPath))
            return;

        var path = Path.TrimEndingDirectorySeparator(mounted?.LocalPath ?? itemPath);
        if (container == "bluray" &&
            string.Equals(Path.GetFileName(path), "BDMV", StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path)!;
        if ((container is "blurayiso" or "iso") && !path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            return;
        if (mounted is not null && !mounted.Contains(path))
            return;

        var info = await Probe(path, cancellationToken).ConfigureAwait(false);
        if (mounted is not null && !mounted.Confirm(path))
            throw new InvalidDataException("The Linux mount serving the disc changed during the probe.");
        var streams = info.Tracks.Select(track => track.ToMediaStream()).OfType<MediaStream>().ToList();
        if (!streams.Any(stream => stream.Type == MediaStreamType.Video))
            throw new InvalidDataException("No supported video track in the main playlist.");

        var current = library.GetItemById(video.InternalId) as Video;
        if (current?.Path != itemPath || current.Container != container)
            return;
        var existing = GetStreams(current, cancellationToken);
        var embedded = existing.Where(stream => !stream.IsExternal).ToArray();
        bool streamsChanged;
        if (embedded.Length > 0)
        {
            streamsChanged = EnrichExisting(embedded, streams);
            streams = existing;
        }
        else
        {
            // Preserve external subtitle indices.
            var occupiedIndices = existing.Select(stream => stream.Index).ToHashSet();
            var index = 0;
            foreach (var stream in streams)
            {
                while (occupiedIndices.Contains(index))
                    index++;
                stream.Index = index++;
            }
            mediaSources.NormalizeMediaStreams(streams);
            streams.AddRange(existing);
            streamsChanged = true;
        }

        bool runtimeChanged = current.RunTimeTicks.GetValueOrDefault() <= 0;
        if (runtimeChanged)
        {
            current.RunTimeTicks = info.RuntimeTicks;
            library.UpdateItems(new List<BaseItem> { current }, current.GetParent(), ItemUpdateType.MetadataImport,
                setDateLastSaved: false, saveMetadata: false, metadataRefreshOptions: null, cancellationToken);
        }
        if (streamsChanged)
            repository.SaveMediaStreams(current.InternalId, streams, cancellationToken);
        var chapters = info.Chapters();
        bool chaptersChanged = chapters.Count > 0 && repository.GetChapter(current.InternalId, 0, cancellationToken) is null;
        if (chaptersChanged)
            repository.SaveChapters(current.InternalId, chapters);
        if (streamsChanged || runtimeChanged || chaptersChanged)
            logger.Info("Filled Blu-ray item {0} from {1:D5}.mpls.", current.InternalId, info.Playlist);
    }

    private static bool EnrichExisting(MediaStream[] existing, List<MediaStream> probed)
    {
        static bool SameTrack(MediaStream first, MediaStream second) =>
            first.Type == second.Type && string.Equals(first.Codec, second.Codec, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(first.Language) || string.IsNullOrEmpty(second.Language) ||
             string.Equals(first.Language, second.Language, StringComparison.OrdinalIgnoreCase));

        bool changed = false;
        foreach (var stream in existing)
        {
            var matches = probed.Where(track => SameTrack(stream, track)).Take(2).ToArray();
            if (matches.Length != 1 || existing.Count(other => SameTrack(other, matches[0])) != 1)
                continue;
            var sample = matches[0];
            changed |= FillMissing(stream.Width, sample.Width, value => stream.Width = value);
            changed |= FillMissing(stream.Height, sample.Height, value => stream.Height = value);
            changed |= FillMissing(stream.BitDepth, sample.BitDepth, value => stream.BitDepth = value);
            changed |= FillMissing(stream.BitRate, sample.BitRate, value => stream.BitRate = value);
            changed |= FillMissing(stream.RealFrameRate, sample.RealFrameRate, value => stream.RealFrameRate = value);
            changed |= FillMissing(stream.Profile, sample.Profile, value => stream.Profile = value);
            changed |= FillMissing(stream.PixelFormat, sample.PixelFormat, value => stream.PixelFormat = value);
            changed |= FillMissing(stream.ColorPrimaries, sample.ColorPrimaries, value => stream.ColorPrimaries = value);
            changed |= FillMissing(stream.ColorTransfer, sample.ColorTransfer, value => stream.ColorTransfer = value);
            changed |= FillMissing(stream.ColorSpace, sample.ColorSpace, value => stream.ColorSpace = value);
            changed |= FillMissing(stream.Channels, sample.Channels, value => stream.Channels = value);
            changed |= FillMissing(stream.ChannelLayout, sample.ChannelLayout, value => stream.ChannelLayout = value);
            changed |= FillMissing(stream.SampleRate, sample.SampleRate, value => stream.SampleRate = value);
            if (sample.ExtendedVideoType != ExtendedVideoTypes.None &&
                (stream.ExtendedVideoType == ExtendedVideoTypes.None ||
                 stream.ExtendedVideoType == ExtendedVideoTypes.Hdr10 &&
                    sample.ExtendedVideoType is ExtendedVideoTypes.Hdr10Plus or ExtendedVideoTypes.DolbyVision))
            {
                stream.ExtendedVideoType = sample.ExtendedVideoType;
                stream.ExtendedVideoSubType = sample.ExtendedVideoSubType;
                changed = true;
            }
            else if (stream.ExtendedVideoType == sample.ExtendedVideoType &&
                stream.ExtendedVideoSubType == ExtendedVideoSubTypes.None &&
                sample.ExtendedVideoSubType != ExtendedVideoSubTypes.None)
            {
                stream.ExtendedVideoSubType = sample.ExtendedVideoSubType;
                changed = true;
            }
        }
        return changed;
    }

    private static bool FillMissing<T>(T current, T proposed, Action<T> assign)
    {
        if (current is not (null or "" or "unknown" or "unspecified") || proposed is null)
            return false;
        assign(proposed);
        return true;
    }

    private List<MediaStream> GetStreams(Video video, CancellationToken cancellationToken) =>
        repository.GetMediaStreams(new MediaStreamQuery { ItemId = video.InternalId }, cancellationToken);

    private async Task<DiscInfo> Probe(string path, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // The Linux helper links against the distribution's libraries. Emby's
        // private glibc can be older than the distribution's libbluray dependencies.
        if (OperatingSystem.IsLinux())
            start.Environment.Remove("LD_LIBRARY_PATH");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new IOException("Could not start disc probe.");
        var output = process.StandardOutput.ReadToEndAsync();
        var diagnostics = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Disc probe exceeded 45 seconds.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            await diagnostics.ConfigureAwait(false);
        }
        var json = await output.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException("No usable unencrypted Blu-ray main playlist.");
        var info = JsonSerializer.Deserialize<DiscInfo>(json);
        return info is { Duration90k: > 0, Tracks.Length: > 0 }
            ? info : throw new InvalidDataException("Invalid Blu-ray probe output.");
    }
}
