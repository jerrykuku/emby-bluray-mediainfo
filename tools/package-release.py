#!/usr/bin/env python3
from hashlib import file_digest
from pathlib import Path
from shutil import copy2, copytree, make_archive
import sys
from tarfile import open as open_tar
from tempfile import TemporaryDirectory
from xml.etree import ElementTree

SOURCE = Path("src")
VERSION = ElementTree.parse(SOURCE / "Emby.BlurayMainTitle.csproj").findtext(
    "./PropertyGroup/Version"
)
DLL = "Emby.BlurayMainTitle.dll"
HELPER = "bluray-main-title"
PLATFORMS = {
    "linux-x86_64-static": ("x86_64",),
    "rk3588-arm64-static": ("aarch64",),
    "synology-dsm-static": ("x86_64", "aarch64"),
}
LICENSES = {
    "libbluray-1.5.0": ("COPYING", "libbluray-COPYING"),
    "libudfread-1.2.0": ("COPYING", "libudfread-COPYING"),
    "ffmpeg-8.1.3": ("COPYING.LGPLv2.1", "ffmpeg-LGPL-2.1"),
}


def package(inputs, docs, output):
    output.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory() as work:
        work = Path(work)
        source = work / "source"
        copytree(SOURCE, source, ignore=lambda _, names: {".gitignore", "bin", "obj"}.intersection(names))
        copytree(docs, source / "third-party")
        for archive, (license_path, target) in LICENSES.items():
            with open_tar(docs / (archive + ".tar.xz")) as tar:
                license_file = tar.extractfile(f"{archive}/{license_path}")
                (source / "third-party" / target).write_bytes(license_file.read())
        copy2("tools/RELINK.md", source)

        for platform, architectures in PLATFORMS.items():
            stem = f"emby-bluray-mediainfo-{VERSION}-{platform}"
            root = work / stem
            copytree(source, root / "source")
            for name in ("LICENSE", "THIRD_PARTY_NOTICES.md"):
                copy2(name, root)
            (root / "README.md").write_text(
                "# emby-bluray-mediainfo\n\n"
                "Copy both files in `plugins` to Emby's plugin directory. "
                "On Synology choose the directory matching `uname -m`.\n\n"
                "[Installation](https://github.com/jerrykuku/emby-bluray-mediainfo#install)"
                " · [Relinking](source/RELINK.md)\n",
                encoding="utf-8",
            )
            for arch in architectures:
                native = inputs / f"native-{arch}"
                plugins = (root / arch if len(architectures) > 1 else root) / "plugins"
                plugins.mkdir(parents=True)
                copy2(inputs / "managed" / DLL, plugins)
                copy2(native / HELPER, plugins)
                relink = root / "source" / "relink" / arch
                copytree(native, relink, ignore=lambda _, names: {HELPER}.intersection(names))
            with (root / "SHA256SUMS").open("w", encoding="utf-8") as manifest:
                for path in sorted(root.rglob("*")):
                    if path.is_file() and path.name != "SHA256SUMS":
                        with path.open("rb") as item:
                            digest = file_digest(item, "sha256").hexdigest()
                        manifest.write(f"{digest}  {path.relative_to(root).as_posix()}\n")
            archive = make_archive(str(output / stem), "zip", work, stem)
            print(archive, flush=True)


if __name__ == "__main__":
    package(*(Path(arg) for arg in sys.argv[1:]))
