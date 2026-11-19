#!/bin/sh
set -eu

package=$(realpath "$1")
plugins=$(realpath "$2")
fixtures=$(realpath "$3")
kernel=$(realpath "$4")
modules=$(realpath "$5")

printf '%s  %s\n' \
    be2c943a3e1f55b46d87a39b123027d843b52743f8c8b07047b01fb5bce6f41a "$package" \
    0ce5af84e060b8ac831806f7e9901bcfe47863ea81307ff8f8ab2dd4b02269c9 "$kernel" \
    3b60e6cb7ca18f9fb07cca83812a22b10ee3b01888f3b4231e7607283e135b96 "$modules" | sha256sum -c -

work=$(mktemp -d)
container=
vm=
trap '
    if [ -n "$vm" ]; then kill "$vm" 2>/dev/null || true; wait "$vm" 2>/dev/null || true; fi
    if [ -n "$container" ]; then docker rm "$container" >/dev/null; fi
    rm -rf "$work"
' EXIT

root=$work/root
mkdir "$root"
container=$(docker create --platform linux/arm/v7 alpine:3.22)
docker export "$container" | tar -xf - -C "$root"
docker rm "$container" >/dev/null
container=
ar -p "$package" data.tar.gz | tar -xzf - -C "$root"
gzip -dc "$modules" | (cd "$root" && cpio -id 'lib/modules/*')
mkdir -p "$root/config/plugins" "$root/config/config"
cp -p "$plugins/Emby.BlurayMainTitle.dll" "$plugins/bluray-main-title" "$root/config/plugins/"
cp -R "$fixtures" "$root/fixtures"
printf '%s\n' '<ServerConfiguration><EnableRemoteAccess>false</EnableRemoteAccess><EnableUPnP>false</EnableUPnP></ServerConfiguration>' > "$root/config/config/system.xml"

cat > "$root/init" <<'GUEST'
#!/bin/sh
set -eu
mount -t devtmpfs devtmpfs /dev
exec >/dev/console 2>&1
mount -t proc proc /proc
mount -t sysfs sysfs /sys
modprobe virtio_mmio
modprobe virtio_net
ip link set lo up
ip link set eth0 up
ip addr add 10.0.2.15/24 dev eth0
test "$(uname -m)" = armv7l
EMBY_DATA=/config exec /opt/emby-server/bin/emby-server
GUEST
chmod +x "$root/init"
(cd "$root" && find . -print0 | cpio -0 -o -H newc | gzip -1 > "$work/rootfs.gz")

port=18983
qemu-system-arm -M virt -cpu cortex-a15 -smp 2 -m 1536 -nographic -no-reboot \
    -kernel "$kernel" -initrd "$work/rootfs.gz" -append 'console=ttyAMA0 rdinit=/init panic=-1' \
    -netdev "user,id=net0,restrict=on,hostfwd=tcp:127.0.0.1:$port-:8096" \
    -device virtio-net-device,netdev=net0 > "$work/console.log" 2>&1 &
vm=$!
if ! python3 tests/test_emby_armhf.py "$port"; then
    grep -Ei 'Run /init|OS/Process|Unhandled exception|error while loading|kernel panic|No such file|qemu-system-arm:' "$work/console.log" || true
    exit 1
fi
