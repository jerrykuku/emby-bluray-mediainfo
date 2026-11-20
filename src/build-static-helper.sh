#!/bin/sh
set -eu
export LC_ALL=C

if [ "$#" -ne 2 ] || [ "$(uname -s)" != Linux ]; then
    printf 'Usage on Linux: %s SOURCE_ARCHIVES_DIR OUTPUT_DIR\n' "$0" >&2
    exit 2
fi
abi_flags=
case "$(uname -m)" in
    aarch64) cpu=armv8-a; machine=AArch64 ;;
    armv7l|armv8l) cpu=armv7-a; machine=ARM; abi_flags=' -mfpu=vfpv3-d16 -mfloat-abi=hard' ;;
    x86_64) cpu=x86-64; machine='Advanced Micro Devices X86-64' ;;
    *) printf 'This build supports Linux aarch64, ARMv7 armhf and x86_64.\n' >&2; exit 2 ;;
esac

source_dir=$(CDPATH= cd "$(dirname "$0")" && pwd)
archives=$(realpath "$1")
mkdir -p "$2"
output=$(realpath "$2")
bluray=libbluray-1.5.0
udfread=libudfread-1.2.0
ffmpeg=ffmpeg-8.1.3

(cd "$archives" && sha256sum -c "$source_dir/source-archives.sha256")

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
for name in "$udfread" "$bluray" "$ffmpeg"; do
    tar -xf "$archives/$name.tar.xz" -C "$work"
done
prefix=$work/prefix
export CC=cc CFLAGS="-O2 -march=$cpu$abi_flags" PKG_CONFIG_PATH="$prefix/lib/pkgconfig"

meson setup "$work/udf-build" "$work/$udfread" --prefix="$prefix" --libdir=lib \
    --default-library=static --buildtype=release
meson compile -C "$work/udf-build" -j 3
meson install -C "$work/udf-build"
meson setup "$work/bd-build" "$work/$bluray" --prefix="$prefix" --libdir=lib \
    --default-library=static --buildtype=release --wrap-mode=nodownload -Denable_tools=false \
    -Dbdj_jar=disabled -Dfontconfig=disabled -Dfreetype=disabled -Dlibxml2=disabled
meson compile -C "$work/bd-build" -j 3
meson install -C "$work/bd-build"

(
    cd "$work/$ffmpeg"
    ./configure --prefix="$prefix" --cc=cc --extra-cflags="$CFLAGS" \
        --enable-static --disable-shared --disable-programs --disable-doc --disable-network \
        --disable-autodetect --disable-asm --disable-pthreads --disable-debug \
        --disable-avdevice --disable-avfilter --disable-swscale --disable-swresample \
        --disable-everything --enable-demuxer=mpegts --enable-bsf=extract_extradata \
        --enable-parser=h264,hevc,mpegvideo,vc1,dca,ac3,mlp \
        --enable-decoder=h264,hevc,mpeg2video,vc1,dca,ac3,eac3,truehd,pcm_bluray
    make -j3
    make install
)

cc -std=c11 -Wall -Wextra -Werror $CFLAGS -I"$prefix/include" \
    -c "$source_dir/bluray-main-title.c" -o "$work/bluray-main-title.o"
cc -std=c11 -Wall -Wextra -Werror $CFLAGS -I"$prefix/include" \
    -c "$source_dir/stream-probe.c" -o "$work/stream-probe.o"
cc -static "$work/bluray-main-title.o" "$work/stream-probe.o" \
    -L"$prefix/lib" -lbluray -ludfread -lavformat -lavcodec -lavutil -pthread -ldl -lm \
    -o "$work/bluray-main-title"

readelf -h "$work/bluray-main-title" | grep -q "Machine:.*$machine"
if [ "$machine" = ARM ]; then
    readelf -h "$work/bluray-main-title" | grep -q 'hard-float ABI'
fi
if readelf -l "$work/bluray-main-title" | grep -q 'INTERP'; then
    printf 'Expected a static helper.\n' >&2
    exit 1
fi

cp "$work/bluray-main-title" "$work/bluray-main-title.o" "$work/stream-probe.o" \
    "$prefix/lib/libbluray.a" "$prefix/lib/libudfread.a" "$prefix/lib/libavformat.a" \
    "$prefix/lib/libavcodec.a" "$prefix/lib/libavutil.a" "$output/"
