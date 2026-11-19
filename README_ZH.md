<div align="center">

# emby-bluray-mediainfo

给 Emby 里的蓝光 BDMV 目录和 ISO 镜像补齐主影片的媒体信息。

[English](README.md) · **简体中文**

</div>

## 功能

- 补齐片长、章节、HDR10、杜比视界、音轨和字幕信息。
- 保留原片源路径和已有轨道索引。
- 支持未加密原盘。`smb://` 和 `nfs://` 路径需要 Emby 能访问对应的挂载目录。

## 安装

从 [0.2.0 Beta](https://github.com/jerrykuku/emby-bluray-mediainfo/releases/tag/v0.2.0-beta) 下载对应平台的安装包。

| 平台 | 安装包 |
| --- | --- |
| Linux x86_64 | [Linux x86_64](https://github.com/jerrykuku/emby-bluray-mediainfo/releases/download/v0.2.0-beta/Emby-BlurayMainTitle-0.2.0-linux-x86_64-static.zip) |
| 绿联 RK3588 NAS | [RK3588 ARM64](https://github.com/jerrykuku/emby-bluray-mediainfo/releases/download/v0.2.0-beta/Emby-BlurayMainTitle-0.2.0-rk3588-arm64-static.zip) |
| 群晖 DSM 原生 Emby | [群晖 DSM](https://github.com/jerrykuku/emby-bluray-mediainfo/releases/download/v0.2.0-beta/Emby-BlurayMainTitle-0.2.0-synology-dsm-static.zip) |

群晖运行 `uname -m` 后选择 `aarch64` 或 `x86_64` 目录。

1. 停止 Emby。把 `Emby.BlurayMainTitle.dll` 和 `bluray-main-title` 复制到 Emby 数据目录下的 `plugins`（Docker 容器内是 `/config/plugins`）。
2. 在该目录执行 `chmod +x bluray-main-title`，然后启动 Emby。
3. 在「计划任务 → 媒体库」运行 **Fill Blu-ray main title media info**。

已在 Linux ARM64 和 x86_64 的 Emby 4.10.2.0 上验证，群晖 DSM 和绿联 UGOS 真机待测。

[构建与测试](BUILD.md) · [MIT](LICENSE) · [第三方许可证](THIRD_PARTY_NOTICES.md)
