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
| UGREEN NAS with RK3588 | `*-rk3588-arm64-static.zip` |
| Synology DSM native Emby | `*-synology-dsm-static.zip` |

On Synology, run `uname -m` and use the `aarch64` or `x86_64` folder.

1. Stop Emby. Copy `Emby.BlurayMainTitle.dll` and `bluray-main-title` into `plugins` in Emby's data directory (`/config/plugins` inside Docker).
2. Run `chmod +x bluray-main-title` in that directory, then start Emby.
3. Under **Scheduled Tasks → Library**, run **Fill Blu-ray main title media info**.

Tested with Emby 4.10.2.0 on Linux ARM64 and x86_64. Testing on Synology DSM and UGREEN UGOS is pending.

[Build and test (中文)](BUILD.md) · [MIT](LICENSE) · [Third-party licenses](THIRD_PARTY_NOTICES.md)
