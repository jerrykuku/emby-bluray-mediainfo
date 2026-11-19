# Relink the static helper

Use Alpine 3.22 on the architecture of the helper. Install `build-base`, open the matching directory under `relink`, and run:

```sh
cc -static bluray-main-title.o stream-probe.o libbluray.a libudfread.a \
  libavformat.a libavcodec.a libavutil.a -pthread -ldl -lm -o bluray-main-title
```

To rebuild the libraries and helper from the included sources, install `pkgconf meson ninja` and run this from the `source` directory:

```sh
sh build-static-helper.sh ./third-party ./build
```
