#!/bin/sh
set -eu

arch=$1
bundles=$2
case "$arch" in
    x86_64) set -- linux-x86_64-static synology-dsm-static ;;
    aarch64) set -- linux-arm64-static synology-dsm-static ;;
    armv7l) set -- linux-armhf-static ;;
    *) exit 2 ;;
esac

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
for package do
    set -- "$bundles"/*-"$package".zip
    test "$#" -eq 1
    archive=$1
    root=$work/$(basename "$archive" .zip)
    unzip -q "$archive" -d "$work"
    (cd "$root" && sha256sum -c SHA256SUMS)
    plugins=$root/plugins
    if [ "$package" = synology-dsm-static ]; then
        plugins=$root/$arch/plugins
    fi
    test -x "$plugins/bluray-main-title"
    (
        cd "$root/source/relink/$arch"
        cc -static bluray-main-title.o stream-probe.o libbluray.a libudfread.a \
            libavformat.a libavcodec.a libavutil.a -pthread -ldl -lm -o bluray-main-title
        cmp bluray-main-title "$plugins/bluray-main-title"
    )
    printf 'Relinked %s (%s)\n' "$package" "$arch"
done
