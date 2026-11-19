using Emby.BlurayMainTitle;

var active = new[]
{
    "32 1 0:27 / / rw - ext4 /dev/disk rw",
    @"51 32 0:61 / /mnt/emby\040concerts ro - cifs //nas/演唱会 ro",
    "52 32 0:62 / /mnt/nfs ro - nfs4 nas:/volume1 ro",
    @"53 32 0:63 / /mnt/nfs-movies ro - nfs4 nas:/volume1/Film\040Archive ro",
    "54 32 0:64 / /mnt/ip6 ro - nfs [2001:db8::3]:/films ro"
};
var auto = "55 32 0:65 / /mnt/auto rw - autofs systemd-1 rw";
var ready = "56 55 0:66 / /mnt/auto ro - nfs4 nas:/volume4/记录片 ro";
var fstab = new[] { "nas:/volume4/记录片 /mnt/auto nfs ro,x-systemd.automount 0 0" };

void Check(bool condition, string scenario)
{
    if (!condition)
        throw new Exception(scenario);
}

MountedDiscPath? Resolve(string url) => MountedDiscPath.Resolve(url, active, Array.Empty<string>());

var smb = Resolve("SMB://NAS/%E6%BC%94%E5%94%B1%E4%BC%9A/Adele%20Live/BDMV/");
Check(smb?.LocalPath == "/mnt/emby concerts/Adele Live/BDMV", "SMB URI and mount escaping");
Check(smb!.Confirm("/mnt/emby concerts/Adele Live", active), "matched active SMB mount");
Check(!smb.Contains("/mnt/emby concerts-neighbor/Film"), "mount point path boundary");
Check(!smb.Confirm(smb.LocalPath, active.Append("57 51 0:67 / /mnt/emby\\040concerts/Adele\\040Live ro - tmpfs tmpfs ro")),
    "reject an unrelated nested mount");
Check(Resolve("smb://nas:445/演唱会/Film.bluray.iso")?.LocalPath == "/mnt/emby concerts/Film.bluray.iso", "SMB ISO and default port");
Check(Resolve("smb://nas/演唱会2/Film") is null, "SMB full share name");
Check(Resolve("smb://nas-other/演唱会/Film") is null, "SMB full hostname");
foreach (var value in new[] { "../Film", "%2E%2e/Film", "Film%2FBDMV", "Film%5CBDMV", "Film%00", "Film?param=x", "Film#tag" })
    Check(Resolve("smb://nas/演唱会/" + value) is null, "reject invalid path " + value);
Check(Resolve("smb://user:credential@nas/演唱会/Film") is null, "credential-bearing URI");
Check(Resolve("smb://nas:1445/演唱会/Film") is null, "alternate port differs from mount address");

var nfs = Resolve("nfs://nas:/volume1/Film%20Archive/原盘.iso");
Check(nfs?.LocalPath == "/mnt/nfs-movies/原盘.iso" && nfs.Confirm(nfs.LocalPath, active), "NFS longest export prefix");
Check(Resolve("nfs://NAS/volume1/Film%20Archives/Film")?.LocalPath == "/mnt/nfs/Film Archives/Film", "NFS export component boundary");
Check(Resolve("nfs://nas/Volume1/Film") is null, "NFS exports are case-sensitive");
Check(Resolve("nfs://[2001:db8::3]/films/Test")?.LocalPath == "/mnt/ip6/Test", "NFS IPv6 mount");
Check(Resolve("nfs://nas/volume10/Film") is null, "unmounted NFS export");

var pending = MountedDiscPath.Resolve("nfs://nas/volume4/记录片/Test/BDMV", active.Append(auto), fstab);
Check(pending?.LocalPath == "/mnt/auto/Test/BDMV", "configured kernel automount");
Check(!pending!.Confirm(pending.LocalPath, active.Append(auto)), "automount must attach before writeback");
Check(pending.Confirm(pending.LocalPath, active.Append(auto).Append(ready)), "same NFS source attached");
Check(!pending.Confirm(pending.LocalPath, active.Append(auto).Append(ready.Replace("记录片", "演唱会"))), "refuse a changed mount source");
Check(MountedDiscPath.Resolve("nfs://nas/volume4/记录片/Test", active, fstab) is null, "fstab alone does not establish a mount");
Check(MountedDiscPath.Resolve("smb://nas/演唱会/Film", active.Append(active[1].Replace("/mnt/emby\\040concerts", "/mnt/other")), fstab) is null,
    "refuse equally-specific ambiguous mounts");
Console.WriteLine("SMB/NFS URI and Linux mount resolution checks passed.");
