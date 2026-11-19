#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include <libbluray/bluray.h>
#include "stream-probe.h"

struct track {
    BLURAY_STREAM_INFO info;
    char type;
    unsigned has_dolby_layer;
};

/* All primary PlayItems contribute tracks. A PID that changes meaning cannot
 * be represented as a single Emby media stream. */
static int add_tracks(struct track *tracks, const BLURAY_STREAM_INFO *streams,
                      unsigned count, char type)
{
    for (unsigned i = 0; i < count; i++) {
        const BLURAY_STREAM_INFO *stream = &streams[i];
        if (!stream->pid || stream->pid >= 0x1fff)
            return 0;
        struct track *track = &tracks[stream->pid];
        if (!track->type) {
            track->info = *stream;
            track->type = type;
            continue;
        }
        if (track->type != type || track->info.coding_type != stream->coding_type ||
            track->info.format != stream->format || track->info.rate != stream->rate)
            return 0;
        if (memcmp(track->info.lang, stream->lang, 3))
            memset(track->info.lang, 0, sizeof(track->info.lang));
        if (track->info.aspect != stream->aspect)
            track->info.aspect = 0;
        if (track->info.color_space != stream->color_space)
            track->info.color_space = 0;
        if (stream->dynamic_range_type > track->info.dynamic_range_type)
            track->info.dynamic_range_type = stream->dynamic_range_type;
        track->info.hdr_plus_flag |= stream->hdr_plus_flag;
    }
    return 1;
}

static int print_title(BLURAY *disc, const BLURAY_TITLE_INFO *title)
{
    struct track tracks[0x1fff] = {0};
    unsigned read_video = 0;
    for (uint32_t i = 0; i < title->clip_count; i++) {
        const BLURAY_CLIP_INFO *clip = &title->clips[i];
        if (!clip->pkt_count ||
            !add_tracks(tracks, clip->video_streams, clip->video_stream_count, 'v') ||
            !add_tracks(tracks, clip->audio_streams, clip->audio_stream_count, 'a') ||
            !add_tracks(tracks, clip->pg_streams, clip->pg_stream_count, 's'))
            return 0;
        read_video += clip->video_stream_count;
        if (clip->dv_stream_count && clip->video_stream_count == 1)
            tracks[clip->video_streams[0].pid].has_dolby_layer = 1;
    }
    if (!read_video || !title->duration)
        return 0;

    struct stream_probe *probe = open_stream_probe(disc, title->playlist);
    printf("{\"Playlist\":%" PRIu32 ",\"Duration90k\":%" PRIu64 ",\"Chapters90k\":[",
           title->playlist, title->duration);
    for (uint32_t index = 0; index < title->chapter_count; index++)
        printf("%s%" PRIu64, index ? "," : "", title->chapters[index].start);
    printf("],\"Tracks\":[");
    const char *separator = "";
    for (const char *type = "vas"; *type; type++) {
        for (unsigned pid = 1; pid < 0x1fff; pid++) {
            const struct track *track = &tracks[pid];
            if (track->type != *type)
                continue;
            const BLURAY_STREAM_INFO *stream = &track->info;
            char language[4] = {0};
            if (stream->lang[0] >= 'a' && stream->lang[0] <= 'z' &&
                stream->lang[1] >= 'a' && stream->lang[1] <= 'z' &&
                stream->lang[2] >= 'a' && stream->lang[2] <= 'z')
                memcpy(language, stream->lang, 3);
            printf("%s{\"Type\":\"%c\",\"Coding\":%u,\"Format\":%u,"
                   "\"Rate\":%u,\"Aspect\":%u,\"Language\":\"%s\"",
                   separator, *type, stream->coding_type, stream->format,
                   stream->rate, stream->aspect, language);
            if (*type == 'v')
                printf(",\"DynamicRange\":%u,\"DiscColorSpace\":%u,\"Hdr10Plus\":%u,\"DolbyLayer\":%u",
                       stream->dynamic_range_type, stream->color_space, stream->hdr_plus_flag,
                       track->has_dolby_layer);
            print_stream_sample(probe, stream);
            putchar('}');
            separator = ",";
        }
    }
    puts("]}");
    close_stream_probe(probe);
    return 1;
}

int main(int argc, char **argv)
{
    if (argc != 2) {
        fprintf(stderr, "Usage: bluray-main-title DISC_DIRECTORY_OR_ISO\n");
        return 2;
    }

    BLURAY *disc = bd_open(argv[1], NULL);
    if (!disc) {
        fprintf(stderr, "Cannot open the Blu-ray source.\n");
        return 1;
    }
    const BLURAY_DISC_INFO *info = bd_get_disc_info(disc);
    int main_title = -1;
    if (info && info->bluray_detected && !info->aacs_detected && !info->bdplus_detected &&
        bd_get_titles(disc, TITLES_RELEVANT, 0))
        main_title = bd_get_main_title(disc);

    BLURAY_TITLE_INFO *title = main_title < 0 ? NULL : bd_get_title_info(disc, (uint32_t)main_title, 0);
    int success = title && print_title(disc, title);
    if (title)
        bd_free_title_info(title);
    bd_close(disc);
    if (!success)
        fprintf(stderr, "No usable unencrypted Blu-ray main playlist.\n");
    return success ? 0 : 1;
}
