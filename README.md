# Emby Blu-ray Main Title Media Info

给 Emby 里的 BDMV 和蓝光 ISO 补齐片长、章节、HDR10、杜比视界、音轨和字幕信息。保留原片源路径和已有轨道索引。

## 下载

[0.2.0 Beta](https://github.com/jerrykuku/emby-bluray-main-title/releases/tag/v0.2.0-beta) 提供绿联 RK3588 ARM64、Linux x86_64 和群晖原生 Emby 安装包。群晖用 `uname -m` 确认架构后选择 `aarch64` 或 `x86_64` 目录。

## 安装

1. 停止 Emby，把压缩包里的 `Emby.BlurayMainTitle.dll` 和 `bluray-main-title` 复制到 Emby 数据目录的 `plugins`。Docker 对应容器内的 `/config/plugins`。
2. 确认 `bluray-main-title` 有执行权限，再启动 Emby。
3. 在「计划任务 → 媒体库」运行 **Fill Blu-ray main title media info**。

原盘需未加密。`smb://` 和 `nfs://` 条目需要 Emby 所在系统有对应挂载。已在 Linux ARM64 和 x86_64 的 Emby 4.10.2.0 上验证，群晖 DSM 和绿联 UGOS 真机待测。

[构建与测试](BUILD.md) · [MIT](LICENSE) · [第三方许可证](THIRD_PARTY_NOTICES.md)
