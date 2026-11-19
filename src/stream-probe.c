#include "stream-probe.h"

#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>


#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/dovi_meta.h>
#include <libavutil/pixdesc.h>

#define SAMPLE_BYTES (8 * 1024 * 1024)
#define BUFFER_BYTES (32 * 1024)

struct stream_probe {
    BLURAY *disc;
    AVFormatContext *format;
    AVIOContext *io;
    int remaining;
};

static int read_sample(void *opaque, uint8_t *buffer, int length)
{
    struct stream_probe *probe = opaque;
    if (length > probe->remaining)
        length = probe->remaining;
    if (!length)
        return AVERROR_EOF;
    int read = bd_read(probe->disc, buffer, length);
    if (read <= 0)
        return AVERROR_EOF;
    probe->remaining -= read;
    return read;
}

void close_stream_probe(struct stream_probe *probe)
{
    if (!probe)
        return;
    avformat_close_input(&probe->format);
    if (probe->io) {
        av_freep(&probe->io->buffer);
        avio_context_free(&probe->io);
    }
    free(probe);
}

struct stream_probe *open_stream_probe(BLURAY *disc, uint32_t playlist)
{
    if (!bd_select_playlist(disc, playlist))
        return NULL;
    struct stream_probe *probe = calloc(1, sizeof(*probe));
    if (!probe)
        return NULL;
    probe->disc = disc;
    probe->remaining = SAMPLE_BYTES;
    uint8_t *buffer = av_malloc(BUFFER_BYTES);
    if (!buffer)
        goto error;
    probe->io = avio_alloc_context(buffer, BUFFER_BYTES, 0, probe, read_sample, NULL, NULL);
    if (!probe->io) {
        av_free(buffer);
        goto error;
    }
    probe->format = avformat_alloc_context();
    if (!probe->format)
        goto error;
    probe->format->pb = probe->io;
    probe->format->flags |= AVFMT_FLAG_CUSTOM_IO;
    probe->format->probesize = SAMPLE_BYTES;
    probe->format->max_analyze_duration = 3 * AV_TIME_BASE;
    av_log_set_level(AV_LOG_QUIET);
    if (avformat_open_input(&probe->format, NULL, av_find_input_format("mpegts"), NULL) < 0)
        goto error;
    avformat_find_stream_info(probe->format, NULL);
    return probe;
error:
    close_stream_probe(probe);
    return NULL;
}

static enum AVCodecID declared_codec(uint8_t coding)
{
    switch (coding) {
    case 0x01: return AV_CODEC_ID_MPEG1VIDEO;
    case 0x02: return AV_CODEC_ID_MPEG2VIDEO;
    case 0x1b: return AV_CODEC_ID_H264;
    case 0x24: return AV_CODEC_ID_HEVC;
    case 0xea: return AV_CODEC_ID_VC1;
    case 0x80: return AV_CODEC_ID_PCM_BLURAY;
    case 0x81: return AV_CODEC_ID_AC3;
    case 0x82: case 0x85: case 0x86: return AV_CODEC_ID_DTS;
    case 0x83: return AV_CODEC_ID_TRUEHD;
    case 0x84: return AV_CODEC_ID_EAC3;
    default: return AV_CODEC_ID_NONE;
    }
}

static void number_field(const char *name, int64_t value)
{
    if (value > 0)
        printf(",\"%s\":%" PRId64, name, value);
}

static void string_field(const char *name, const char *value)
{
    if (!value || !*value || !strcmp(value, "unknown"))
        return;
    printf(",\"%s\":\"", name);
    for (const unsigned char *ch = (const unsigned char *)value; *ch; ch++) {
        if (*ch < 32)
            printf("\\u%04x", *ch);
        else {
            if (*ch == '"' || *ch == '\\')
                putchar('\\');
            putchar(*ch);
        }
    }
    putchar('"');
}

void print_stream_sample(const struct stream_probe *probe, const BLURAY_STREAM_INFO *track)
{
    enum AVCodecID expected = declared_codec(track->coding_type);
    if (!probe || expected == AV_CODEC_ID_NONE)
        return;
    for (unsigned i = 0; i < probe->format->nb_streams; i++) {
        const AVStream *stream = probe->format->streams[i];
        const AVCodecParameters *details = stream->codecpar;
        if (stream->id != track->pid || details->codec_id != expected)
            continue;
        number_field("Width", details->width);
        number_field("Height", details->height);
        number_field("SampleRate", details->sample_rate);
        number_field("Channels", details->ch_layout.nb_channels);
        number_field("BitRate", details->bit_rate);
        string_field("Profile", avcodec_profile_name(expected, details->profile));
        if (details->ch_layout.order != AV_CHANNEL_ORDER_UNSPEC) {
            char layout[80];
            if (av_channel_layout_describe(&details->ch_layout, layout, sizeof(layout)) > 0)
                string_field("ChannelLayout", layout);
        }
        int bit_depth = details->bits_per_raw_sample;
        if (details->codec_type == AVMEDIA_TYPE_VIDEO) {
            const AVPixFmtDescriptor *pixels = av_pix_fmt_desc_get(details->format);
            if (pixels && pixels->nb_components) {
                if (!bit_depth)
                    bit_depth = pixels->comp[0].depth;
                string_field("PixelFormat", pixels->name);
            }
            string_field("ColorPrimaries", av_color_primaries_name(details->color_primaries));
            string_field("ColorTransfer", av_color_transfer_name(details->color_trc));
            string_field("ColorSpace", av_color_space_name(details->color_space));
            const AVPacketSideData *metadata = av_packet_side_data_get(details->coded_side_data,
                details->nb_coded_side_data, AV_PKT_DATA_DOVI_CONF);
            if (metadata && metadata->size >= sizeof(AVDOVIDecoderConfigurationRecord)) {
                const AVDOVIDecoderConfigurationRecord *dv = (const void *)metadata->data;
                number_field("DolbyProfile", dv->dv_profile);
                number_field("DolbyCompatibility", dv->dv_bl_signal_compatibility_id);
            }
        } else if (expected == AV_CODEC_ID_PCM_BLURAY && !bit_depth) {
            bit_depth = details->bits_per_coded_sample;
        }
        number_field("BitDepth", bit_depth);
        return;
    }
}
