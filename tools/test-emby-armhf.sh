#!/bin/sh
set -eu

package=$(realpath "$1")
plugins=$(realpath "$2")
fixtures=$(realpath "$3")

printf '%s  %s\n' be2c943a3e1f55b46d87a39b123027d843b52743f8c8b07047b01fb5bce6f41a "$package" | sha256sum -c -
container=$(docker run -d --platform linux/arm/v7 --cpus 2 --memory 2600m \
    -p 127.0.0.1::8096 \
    --mount "type=bind,src=$package,dst=/emby.deb,readonly" \
    --mount "type=bind,src=$plugins,dst=/plugin,readonly" \
    --mount "type=bind,src=$fixtures,dst=/fixtures,readonly" \
    debian:bookworm-slim sh -ec '
        test "$(dpkg --print-architecture)" = armhf
        test "$(dpkg-deb -f /emby.deb Architecture)" = armhf
        dpkg-deb -x /emby.deb /
        mkdir -p /config/plugins /config/config
        cp -p /plugin/Emby.BlurayMainTitle.dll /plugin/bluray-main-title /config/plugins/
        printf "%s\n" "<ServerConfiguration><EnableRemoteAccess>false</EnableRemoteAccess><EnableUPnP>false</EnableUPnP></ServerConfiguration>" > /config/config/system.xml
        EMBY_DATA=/config exec /opt/emby-server/bin/emby-server
    ')
trap 'docker rm -f "$container" >/dev/null' EXIT
address=$(docker port "$container" 8096/tcp)
if ! python3 tests/test_emby_armhf.py "${address##*:}"; then
    docker logs "$container" 2>&1 |
        grep -Ei 'Architecture:|Operating system:|Unhandled exception|Segmentation fault|error while loading shared libraries|QEMU|library not found' || true
    exit 1
fi
