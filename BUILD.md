# 构建与测试

在仓库根目录运行以下命令。插件使用 .NET 8 SDK 构建，目标框架为 .NET 6。

```sh
dotnet build -c Release src/Emby.BlurayMainTitle.csproj
dotnet run -c Release --project tests/emby_bluray_paths
```

静态探测程序在对应架构的 Alpine 3.22 上构建。安装 `build-base pkgconf meson ninja`，并把以下源包放到 `source-archives`。

- [`libbluray-1.5.0.tar.xz`](https://download.videolan.org/pub/videolan/libbluray/1.5.0/libbluray-1.5.0.tar.xz)
- [`libudfread-1.2.0.tar.xz`](https://download.videolan.org/pub/videolan/libudfread/libudfread-1.2.0.tar.xz)
- [`ffmpeg-8.1.3.tar.xz`](https://ffmpeg.org/releases/ffmpeg-8.1.3.tar.xz)

```sh
sh src/build-static-helper.sh ./source-archives ./static-helper
python3 src/test_probe.py ./static-helper/bluray-main-title
```

探测测试需要带 `libx264` 和 `libx265` 的 FFmpeg 及 tsMuxeR 2.7.0。双层杜比视界测试可通过 `--dovi-sample` 指定 FFmpeg FATE 的 `mov/dovi-p7.mp4`。

DLL 在 `src/bin/Release/net6.0/`，静态探测程序在 `static-helper/`。
