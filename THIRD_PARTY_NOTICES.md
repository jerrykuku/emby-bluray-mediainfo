# 第三方许可证

MIT 适用于本仓库的自有代码。第三方组件保留原许可证。

静态探测程序使用以下库：

- libbluray 1.5.0 和 libudfread 1.2.0，LGPL 2.1 或更新版本。[VideoLAN 源码](https://download.videolan.org/pub/videolan/)
- FFmpeg 8.1.3，LGPL 2.1 或更新版本。[FFmpeg 源码与许可证](https://ffmpeg.org/legal.html)
- musl 1.2.5，MIT。[musl 源码](https://musl.libc.org/)
- GCC 运行库，适用 GCC Runtime Library Exception。[例外条款](https://www.gnu.org/licenses/gcc-exception-3.1.html)

每个 [Release](https://github.com/jerrykuku/emby-bluray-main-title/releases/tag/v0.2.0-beta) ZIP 都附带对应的库源码、许可证、构建脚本和可重新链接的对象文件。重新链接命令见包内 `source/RELINK.md`。

`MediaBrowser.Server.Core` 通过 NuGet 供编译使用。Emby Server 提供运行时 SDK。 [Emby SDK](https://www.nuget.org/packages/MediaBrowser.Server.Core/4.9.1.90)
