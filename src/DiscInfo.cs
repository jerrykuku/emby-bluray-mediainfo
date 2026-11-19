using MediaBrowser.Model.Entities;

namespace Emby.BlurayMainTitle;

internal sealed record DiscInfo(int Playlist, long Duration90k, DiscTrack[] Tracks, long[]? Chapters90k)
{
    public static long ToTicks(long clockValue) => (long)Math.Round(clockValue * (decimal)TimeSpan.TicksPerSecond / 90_000);
    public long RuntimeTicks => ToTicks(Duration90k);

    public List<ChapterInfo> Chapters() => (Chapters90k ?? Array.Empty<long>())
        .Where(value => value >= 0 && value < Duration90k).Distinct().OrderBy(value => value)
        .Select((value, index) => new ChapterInfo
        {
            StartPositionTicks = ToTicks(value),
            Name = $"Chapter {index + 1}"
        }).ToList();
}

internal sealed record DiscTrack(string Type, int Coding, int Format, int Rate, int Aspect, string Language)
{
    public int DynamicRange { get; init; }
    public int DiscColorSpace { get; init; }
    public int Hdr10Plus { get; init; }
    public int DolbyLayer { get; init; }
    public int DolbyProfile { get; init; }
    public int DolbyCompatibility { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? BitDepth { get; init; }
    public int? Channels { get; init; }
    public int? SampleRate { get; init; }
    public long? BitRate { get; init; }
    public string? Profile { get; init; }
    public string? PixelFormat { get; init; }
    public string? ChannelLayout { get; init; }
    public string? ColorPrimaries { get; init; }
    public string? ColorTransfer { get; init; }
    public string? ColorSpace { get; init; }

    public MediaStream? ToMediaStream()
    {
        var codec = Coding switch
        {
            0x01 => "mpeg1video",
            0x02 => "mpeg2video",
            0x1b => "h264",
            0x24 => "hevc",
            0xea => "vc1",
            0x80 => "pcm_bluray",
            0x81 => "ac3",
            0x82 => "dts",
            0x83 => "truehd",
            0x84 => "eac3",
            0x85 or 0x86 => "dts",
            0x90 => "pgssub",
            0x92 => "hdmv_text_subtitle",
            _ => null
        };
        if (codec is null)
            return null;

        var stream = new MediaStream
        {
            Codec = codec,
            Language = string.IsNullOrEmpty(Language) ? null : Language,
            BitDepth = BitDepth,
            BitRate = BitRate is > 0 and <= int.MaxValue ? (int)BitRate.Value : null,
            Profile = Profile
        };
        switch (Type)
        {
            case "v":
                stream.Type = MediaStreamType.Video;
                stream.Height = Height ?? (Format switch
                {
                    1 or 3 => 480,
                    2 or 7 => 576,
                    4 or 6 => 1080,
                    5 => 720,
                    8 => 2160,
                    _ => null
                });
                stream.Width = Width ?? (Format switch { 5 => 1280, 8 => 3840, _ => null });
                stream.IsInterlaced = Format is 1 or 2 or 4;
                stream.AspectRatio = Aspect switch { 2 => "4:3", 3 => "16:9", _ => null };
                stream.RealFrameRate = Rate switch
                {
                    1 => 24000f / 1001,
                    2 => 24,
                    3 => 25,
                    4 => 30000f / 1001,
                    6 => 50,
                    7 => 60000f / 1001,
                    _ => null
                };
                SetVideoProperties(stream);
                break;
            case "a":
                stream.Type = MediaStreamType.Audio;
                stream.Profile ??= Coding switch { 0x85 => "DTS-HD HRA", 0x86 => "DTS-HD MA", _ => null };
                stream.Channels = Channels ?? (Format switch { 1 => 1, 3 => 2, _ => null });
                stream.SampleRate = SampleRate ?? (Rate switch { 1 => 48000, 4 => 96000, 5 => 192000, _ => null });
                stream.ChannelLayout = ChannelLayout;
                break;
            case "s":
                stream.Type = MediaStreamType.Subtitle;
                break;
            default:
                return null;
        }
        return stream;
    }

    private void SetVideoProperties(MediaStream stream)
    {
        stream.PixelFormat = PixelFormat;
        stream.ColorSpace = ColorSpace;
        stream.ColorPrimaries = ColorPrimaries ?? (DiscColorSpace switch { 1 => "bt709", 2 => "bt2020", _ => null });
        stream.ColorTransfer = ColorTransfer;
        if (DolbyProfile > 0 || DynamicRange == 2 || DolbyLayer != 0)
        {
            stream.ExtendedVideoType = ExtendedVideoTypes.DolbyVision;
            stream.ExtendedVideoSubType = (DolbyProfile, DolbyCompatibility) switch
            {
                (5, _) => ExtendedVideoSubTypes.DoviProfile50,
                (7, 6) => ExtendedVideoSubTypes.DoviProfile76,
                (8, 1) => ExtendedVideoSubTypes.DoviProfile81,
                (8, 2) => ExtendedVideoSubTypes.DoviProfile82,
                (8, 4) => ExtendedVideoSubTypes.DoviProfile84,
                _ when DolbyLayer != 0 => ExtendedVideoSubTypes.DoviProfile76,
                _ => ExtendedVideoSubTypes.None
            };
        }
        else if (Hdr10Plus != 0)
        {
            stream.ExtendedVideoType = ExtendedVideoTypes.Hdr10Plus;
            stream.ExtendedVideoSubType = ExtendedVideoSubTypes.Hdr10Plus0;
            stream.ColorTransfer ??= "smpte2084";
        }
        else if (ColorTransfer == "arib-std-b67")
        {
            stream.ExtendedVideoType = ExtendedVideoTypes.HyperLogGamma;
            stream.ExtendedVideoSubType = ExtendedVideoSubTypes.HyperLogGamma;
        }
        else if (DynamicRange == 1 ||
            ColorTransfer == "smpte2084" && stream.ColorPrimaries == "bt2020" && BitDepth >= 10)
        {
            stream.ExtendedVideoType = ExtendedVideoTypes.Hdr10;
            stream.ExtendedVideoSubType = ExtendedVideoSubTypes.Hdr10;
            stream.ColorTransfer ??= "smpte2084";
        }
    }
}
