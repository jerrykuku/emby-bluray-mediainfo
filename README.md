<div align="center">

# emby-bluray-mediainfo

An Emby plugin that fills in media info for the main title of Blu-ray BDMV folders and ISO images.

**English** · [简体中文](README_ZH.md)

</div>

## Features

- Adds runtime, chapters, HDR10 and Dolby Vision details, audio tracks, and subtitles.
- Keeps the original media path and existing stream indexes.
- Reads unencrypted discs. `smb://` and `nfs://` paths need a matching mount accessible to Emby.

## Install

Download the package for your server from [Releases](https://github.com/jerrykuku/emby-bluray-mediainfo/releases).

| Platform | Package |
| --- | --- |
| Linux x86_64 | `*-linux-x86_64-static.zip` |
| Linux ARM64 (`aarch64`) | `*-linux-arm64-static.zip` |
| Linux ARMv7 (`armhf`) | `*-linux-armhf-static.zip` |
| Synology DSM native Emby | `*-synology-dsm-static.zip` |

On Synology, run `uname -m` and use the `aarch64` or `x86_64` folder.

1. Stop Emby. Copy `Emby.BlurayMainTitle.dll` and `bluray-main-title` into `plugins` in Emby's data directory (`/config/plugins` inside Docker).
2. Run `chmod +x bluray-main-title` in that directory, then start Emby.
3. Under **Scheduled Tasks → Library**, run **Fill Blu-ray main title media info**.

## Compatibility

- Emby 4.10.2.0 on Linux x86_64 and ARM64, and in a QEMU ARMv7 VM with the official armhf package.
- UGREEN RK3588 NAS · verified by the user with the Linux ARM64 package.
- Synology DSM hardware testing is pending.

[Build and test (中文)](BUILD.md) · [MIT](LICENSE) · [Third-party licenses](THIRD_PARTY_NOTICES.md)
