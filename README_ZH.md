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

从 [Releases](https://github.com/jerrykuku/emby-bluray-mediainfo/releases) 下载对应平台的安装包。

| 平台 | 安装包 |
| --- | --- |
| Linux x86_64 | `*-linux-x86_64-static.zip` |
| Linux ARM64（`aarch64`） | `*-linux-arm64-static.zip` |
| Linux ARMv7（`armhf`） | `*-linux-armhf-static.zip` |
| 群晖 DSM 原生 Emby | `*-synology-dsm-static.zip` |

群晖运行 `uname -m` 后选择 `aarch64` 或 `x86_64` 目录。

1. 停止 Emby。把 `Emby.BlurayMainTitle.dll` 和 `bluray-main-title` 复制到 Emby 数据目录下的 `plugins`（Docker 容器内是 `/config/plugins`）。
2. 在该目录执行 `chmod +x bluray-main-title`，然后启动 Emby。
3. 在「计划任务 → 媒体库」运行 **Fill Blu-ray main title media info**。

## 兼容性

- 已验证 Emby 4.10.2.0，覆盖 Linux x86_64、ARM64，以及 QEMU ARMv7 虚拟机中的官方 armhf 包。
- 绿联 RK3588 NAS · 用户已验证，使用通用 Linux ARM64 包。
- 群晖 DSM 真机待测。

[构建与测试](BUILD.md) · [MIT](LICENSE) · [第三方许可证](THIRD_PARTY_NOTICES.md)
