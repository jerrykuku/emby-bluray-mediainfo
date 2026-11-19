using System.Text.RegularExpressions;

namespace Emby.BlurayMainTitle;

// Resolves only Linux mounts in the server's own mount namespace.
internal sealed record MountedDiscPath(string LocalPath, string MountPoint, string Scheme, string Source)
{
    private const string MountInfo = "/proc/self/mountinfo";

    public static bool IsNetworkUri(string path) =>
        path.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("nfs://", StringComparison.OrdinalIgnoreCase);

    public static MountedDiscPath? Resolve(string path) =>
        Resolve(path, File.ReadLines(MountInfo), File.Exists("/etc/fstab") ? File.ReadLines("/etc/fstab") : Array.Empty<string>());

    internal static MountedDiscPath? Resolve(string path, IEnumerable<string> mountInfo, IEnumerable<string> fstab)
    {
        int separator = path.IndexOf("://", StringComparison.Ordinal);
        if (separator < 0 || !IsNetworkUri(path) || path.IndexOfAny(new[] { '?', '#', '\\' }) >= 0)
            return null;

        var location = ParseLocation(path[..separator].ToLowerInvariant(), path[(separator + 3)..], uri: true);
        if (location is null)
            return null;

        var mounts = ReadMounts(mountInfo).ToLookup(mount => mount.MountPoint, StringComparer.Ordinal);
        var active = mounts.Select(group => GetActiveMount(group)).OfType<Mount>();
        var automatic = fstab.Select(Fields)
            .Where(fields => fields.Length >= 4 && !fields[0].StartsWith('#') &&
                             fields[3].Split(',').Contains("x-systemd.automount"))
            .Select(fields => new Mount("/", Unescape(fields[1]), fields[2], Unescape(fields[0])))
            .Where(mount => mounts[mount.MountPoint].Any() &&
                            mounts[mount.MountPoint].All(existing => existing.FileSystem == "autofs"));

        var matches = active.Concat(automatic)
            .Where(mount => mount.Root == "/" && Path.IsPathFullyQualified(mount.MountPoint))
            .Select(mount => (Mount: mount, Location: mount.GetRemoteLocation()))
            .Where(candidate => candidate.Location?.IsPrefixOf(location) == true)
            .OrderByDescending(candidate => candidate.Location!.Parts.Length)
            .Take(2).ToArray();

        if (matches.Length == 0 ||
            matches.Length == 2 && matches[0].Location!.Parts.Length == matches[1].Location!.Parts.Length)
            return null;

        var (selected, prefix) = matches[0];
        var resolved = Path.Combine(new[] { selected.MountPoint }
            .Concat(location.Parts.Skip(prefix!.Parts.Length)).ToArray());
        return new MountedDiscPath(resolved, selected.MountPoint, prefix.Scheme, selected.Source);
    }

    public bool Contains(string path) => IsAtOrBelow(MountPoint, path);

    public bool Confirm(string path) => Confirm(path, File.ReadLines(MountInfo));

    internal bool Confirm(string path, IEnumerable<string> mountInfo)
    {
        // An automount must have attached the expected share by the time the
        // helper returns. A nested or overlaid mount must not supply the bytes.
        var covering = ReadMounts(mountInfo).Where(mount => IsAtOrBelow(mount.MountPoint, path))
            .GroupBy(mount => mount.MountPoint, StringComparer.Ordinal)
            .OrderByDescending(group => group.Key.Length).FirstOrDefault();
        var active = covering is null ? null : GetActiveMount(covering);
        return active is { Root: "/" } && active.MountPoint == MountPoint && active.Source == Source &&
            active.GetRemoteLocation()?.Scheme == Scheme;
    }

    private static bool IsAtOrBelow(string root, string path) =>
        path == root || path.StartsWith(root.EndsWith('/') ? root : root + "/", StringComparison.Ordinal);

    private static Mount? GetActiveMount(IEnumerable<Mount> mounts)
    {
        var active = mounts.Where(mount => mount.FileSystem != "autofs").Take(2).ToArray();
        return active.Length == 1 ? active[0] : null;
    }

    private static IEnumerable<Mount> ReadMounts(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var fields = Fields(line);
            int separator = Array.IndexOf(fields, "-");
            if (separator >= 6 && separator + 2 < fields.Length)
                yield return new Mount(Unescape(fields[3]), Unescape(fields[4]), fields[separator + 1], Unescape(fields[separator + 2]));
        }
    }

    private static string[] Fields(string line) =>
        line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    private static string Unescape(string value) =>
        Regex.Replace(value, @"\\(040|011|012|134)", match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString());

    private static RemoteLocation? ParseLocation(string scheme, string address, bool uri)
    {
        int slash = address.IndexOf('/');
        if (slash < 0)
            return null;
        var authority = address[..slash];
        if (scheme == "nfs" && authority.EndsWith(':'))
            authority = authority[..^1];
        if (authority.IndexOfAny(new[] { '@', '%', '\\', '?', '#' }) >= 0 ||
            !Uri.TryCreate(scheme + "://" + authority + "/", UriKind.Absolute, out var host) ||
            host.Host.Length == 0 || host.Port != -1 && host.Port != (scheme == "smb" ? 445 : 2049))
            return null;

        var suffix = address[(slash + 1)..].TrimEnd('/');
        var parts = suffix.Length == 0 ? Array.Empty<string>() : suffix.Split('/');
        if (uri)
            parts = parts.Select(Uri.UnescapeDataString).ToArray();
        if ((scheme == "smb" && parts.Length == 0) || parts.Any(part => part.Length == 0 ||
            part is "." or ".." || part.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0))
            return null;
        return new RemoteLocation(scheme, host.IdnHost, parts);
    }

    private sealed record Mount(string Root, string MountPoint, string FileSystem, string Source)
    {
        public RemoteLocation? GetRemoteLocation() => FileSystem switch
        {
            "cifs" or "smb3" when Source.StartsWith("//", StringComparison.Ordinal) =>
                ParseLocation("smb", Source[2..], uri: false),
            "nfs" or "nfs4" when Source.Contains(":/", StringComparison.Ordinal) =>
                ParseLocation("nfs", Source, uri: false),
            _ => null
        };
    }

    private sealed record RemoteLocation(string Scheme, string Host, string[] Parts)
    {
        public bool IsPrefixOf(RemoteLocation other)
        {
            if (Scheme != other.Scheme || !Host.Equals(other.Host, StringComparison.OrdinalIgnoreCase) ||
                Parts.Length > other.Parts.Length)
                return false;
            for (var index = 0; index < Parts.Length; index++)
            {
                var comparison = Scheme == "smb" && index == 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!Parts[index].Equals(other.Parts[index], comparison))
                    return false;
            }
            return true;
        }
    }
}
