#ifndef EMBY_BLURAY_STREAM_PROBE_H
#define EMBY_BLURAY_STREAM_PROBE_H

#include <libbluray/bluray.h>

struct stream_probe;
struct stream_probe *open_stream_probe(BLURAY *disc, uint32_t playlist);
void print_stream_sample(const struct stream_probe *probe, const BLURAY_STREAM_INFO *track);
void close_stream_probe(struct stream_probe *probe);

#endif
