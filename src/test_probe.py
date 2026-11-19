"""Generate an unencrypted multi-clip Blu-ray and compare directory and ISO probes."""

import argparse
import json
import shutil
import subprocess
import tempfile
from pathlib import Path


def run(*arguments, **kwargs):
    return subprocess.run(arguments, check=True, capture_output=True, text=True, timeout=45, **kwargs)


def test(helper, ffmpeg, tsmuxer, root):
    clip = root / "clip.m2ts"
    run(ffmpeg, "-hide_banner", "-loglevel", "error",
        "-f", "lavfi", "-i", "color=c=green:size=1920x1080:rate=24000/1001",
        "-f", "lavfi", "-i", "sine=frequency=400:sample_rate=48000",
        "-t", "8", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "48", "-sc_threshold", "0",
        "-profile:v", "high", "-level:v", "4.1", "-c:a", "ac3", "-ac", "2",
        "-mpegts_m2ts_mode", "1", clip)
    subtitle = root / "subtitle.srt"
    subtitle.write_text("1\n00:00:01,000 --> 00:00:06,000\nSynthetic subtitle\n", encoding="utf-8")
    meta = root / "test.meta"
    meta.write_text(
        "MUXOPT --blu-ray --split-duration=2s --mplsOffset=123\n"
        f'V_MPEG4/ISO/AVC, "{clip}", track=4113\n'
        f'A_AC3, "{clip}", track=4352, lang=eng\n'
        f'S_TEXT/UTF8, "{subtitle}", video-width=1920, video-height=1080, fps=23.976, lang=zho\n',
        encoding="utf-8",
    )
    folder = root / "原盘 BDMV"
    iso = root / "原盘 image.bluray.iso"
    run(tsmuxer, meta, folder)
    run(tsmuxer, meta, iso)
    clips = list((folder / "BDMV" / "STREAM").glob("*.m2ts"))
    assert len(clips) > 1, "Test disc must have a multi-clip playlist"
    directory_title = json.loads(run(helper, folder).stdout)
    iso_title = json.loads(run(helper, iso).stdout)
    assert directory_title == iso_title
    assert iso_title["Playlist"] == 123
    assert abs(iso_title["Duration90k"] / 90_000 - 8) < 0.1
    assert [(track["Type"], track["Coding"], track["Language"]) for track in iso_title["Tracks"]] == [
        ("v", 0x1b, ""), ("a", 0x81, "eng"), ("s", 0x90, "zho"),
    ]
    video, audio, _ = iso_title["Tracks"]
    assert (video["Width"], video["Height"], video["BitDepth"]) == (1920, 1080, 8)
    assert (audio["Channels"], audio["SampleRate"]) == (2, 48000)
    bad_disc = root / "unreadable.bluray.iso"
    bad_disc.write_bytes(b"Not a Blu-ray image")
    for unsupported in (bad_disc, clip, root, root / "missing.iso"):
        result = subprocess.run([helper, unsupported], capture_output=True, timeout=10)
        assert result.returncode != 0 and not result.stdout, unsupported
    return folder, iso


def test_uhd(helper, ffmpeg, tsmuxer, root, dovi_sample):
    video = root / "uhd.hevc"
    audio = root / "surround.ac3"
    run(ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:size=3840x2160:rate=24",
        "-t", "3", "-vf", "format=yuv420p10le", "-c:v", "libx265", "-preset", "ultrafast", "-x265-params",
        "pools=2:frame-threads=1:log-level=error:repeat-headers=1:keyint=24:scenecut=0:hdr10=1:"
        "colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:"
        "master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,50):max-cll=1000,400",
        video)
    run(ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=channel_layout=5.1:sample_rate=48000",
        "-t", "3", "-c:a", "ac3", "-b:a", "640k", audio)

    def mux_disc(name, enhancement=None):
        meta = root / f"{name}.meta"
        clips = f'V_MPEGH/ISO/HEVC, "{video}", fps=24\n'
        if enhancement:
            clips += f'V_MPEGH/ISO/HEVC, "{enhancement}", fps=24\n'
        meta.write_text("MUXOPT --blu-ray-v3 --custom-chapters=00:00:00.000;00:00:01.000\n"
                        + clips + f'A_AC3, "{audio}", lang=eng\n', encoding="utf-8")
        folder, iso = root / name, root / f"{name}.bluray.iso"
        run(tsmuxer, meta, folder)
        run(tsmuxer, meta, iso)
        result = json.loads(run(helper, folder).stdout)
        assert result == json.loads(run(helper, iso).stdout)
        return result

    hdr = mux_disc("HDR10")
    assert hdr["Chapters90k"] == [0, 90000]
    uhd, surround = hdr["Tracks"]
    assert uhd["DynamicRange"] == 1
    assert (uhd["Width"], uhd["Height"], uhd["BitDepth"]) == (3840, 2160, 10)
    assert (uhd["ColorPrimaries"], uhd["ColorTransfer"]) == ("bt2020", "smpte2084")
    assert (surround["Channels"], surround["SampleRate"], surround["BitRate"]) == (6, 48000, 640000)
    if dovi_sample:
        enhancement = root / "el.hevc"
        run(ffmpeg, "-hide_banner", "-loglevel", "error", "-i", dovi_sample, "-map", "0:v:1",
            "-c:v", "copy", "-bsf:v", "hevc_mp4toannexb", enhancement)
        dolby = mux_disc("Dolby Vision", enhancement)
        assert dolby["Tracks"][0]["DolbyLayer"] == 1
        assert len(dolby["Tracks"]) == 2, "Dolby enhancement data is not a second user video track"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("helper", type=Path)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--tsmuxer", default="tsMuxeR")
    parser.add_argument("--dovi-sample", type=Path, help="Optional FFmpeg FATE mov/dovi-p7.mp4 fixture")
    parser.add_argument("--emby-fixtures", type=Path, help="Save the SDR discs for the Emby integration test")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory() as directory:
        folder, iso = test(args.helper.resolve(), args.ffmpeg, args.tsmuxer, Path(directory))
        test_uhd(args.helper.resolve(), args.ffmpeg, args.tsmuxer, Path(directory), args.dovi_sample)
        if args.emby_fixtures:
            args.emby_fixtures.mkdir(parents=True)
            shutil.move(folder, args.emby_fixtures / "Fixture Folder (2026)")
            shutil.move(iso, args.emby_fixtures / "Fixture Image (2026).bluray.iso")
    print("BDMV, Blu-ray ISO, multi-clip runtime, track metadata, HDR and invalid inputs passed.")
