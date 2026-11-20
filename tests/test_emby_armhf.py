"""Check Blu-ray folder and ISO backfill in an isolated armhf Emby 4.10.2.0."""

import http.client
import json
from pathlib import Path
import secrets
import sqlite3
import sys
import time
import urllib.error
import urllib.request
from xml.etree import ElementTree


def wait_for(check):
    deadline = time.monotonic() + 180
    while time.monotonic() < deadline:
        try:
            result = check()
            if result:
                return result
        except (urllib.error.URLError, http.client.RemoteDisconnected, TimeoutError, ConnectionResetError):
            pass
        time.sleep(1)
    raise RuntimeError(f"Timed out waiting for {check.__name__}")


def seed_api_key(database, key_file):
    # This is the key schema used by the pinned Emby 4.10.2.0 test server.
    key = secrets.token_hex(16)
    now = int(time.time())
    with sqlite3.connect(database) as db:
        db.executescript("""
            CREATE TABLE Devices2 (Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ReportedDeviceId TEXT NOT NULL, ReportedName TEXT NOT NULL,
                CustomName TEXT, Capabilities TEXT, IpAddress TEXT) STRICT;
            CREATE TABLE Tokens_2 (Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccessToken TEXT NOT NULL, DeviceId INT NOT NULL, AppName TEXT NOT NULL,
                AppVersion TEXT NOT NULL, UserId INT, IsActive INT NOT NULL,
                DateCreatedInt INT NOT NULL, DateLastActivityInt INT NOT NULL,
                FOREIGN KEY(DeviceId) REFERENCES Devices2(Id)) STRICT;
            INSERT INTO Devices2 (Id, ReportedDeviceId, ReportedName)
                VALUES (1, 'armhf-release-test', 'ARMv7 release test');
        """)
        db.execute("""INSERT INTO Tokens_2 (AccessToken, DeviceId, AppName, AppVersion,
            IsActive, DateCreatedInt, DateLastActivityInt)
            VALUES (?, 1, 'Blu-ray Release Test', '4.10.2.0', 1, ?, ?)""", (key, now, now))
    key_file.write_text(key, encoding="ascii")
    key_file.chmod(0o600)


def test_emby(endpoint, plugin_version, key):
    fixtures = {
        "Fixture Folder (2026)": "h264",
        "Fixture Image (2026).bluray.iso": "h264",
        "Fixture Blu-ray (2026).iso": "h264",
        "Fixture HDR10 (2026).bluray.iso": "hevc",
        "Fixture UHD (2026).ISO": "hevc",
        "Fixture Unsupported (2026).iso": None,
    }
    client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    headers = {
        "Content-Type": "application/json",
        "X-Emby-Token": key,
    }

    def request(path, data=None):
        payload = json.dumps(data).encode() if data is not None else None
        req = urllib.request.Request(endpoint + path, data=payload, headers=headers)
        with client.open(req, timeout=10) as response:
            body = response.read()
        return json.loads(body) if body else None

    info = wait_for(lambda: request("System/Info/Public"))
    assert info["Version"] == "4.10.2.0", info["Version"]
    plugins = request("Plugins")
    assert any(
        item.get("Name") == "Blu-ray Main Title Media Info" and item.get("Version") == plugin_version
        for item in plugins
    ), "Emby did not load the packaged plugin"
    print(f"Emby {info['Version']} armhf loaded plugin {plugin_version}", flush=True)

    options = {
        "PathInfos": [{"Path": "/fixtures"}],
        "EnableRealtimeMonitor": False,
        "MetadataFetchers": [],
        "ImageFetchers": [],
        "TypeOptions": [{"Type": "Movie", "MetadataFetchers": [], "ImageFetchers": []}],
    }
    request("Library/VirtualFolders?name=BluRayTest&collectionType=movies&refreshLibrary=true",
            {"LibraryOptions": options})

    def items():
        result = request("Items?Recursive=true&IncludeItemTypes=Movie&Fields=Path,MediaSources,MediaStreams,Chapters")
        return {item["Id"]: item for item in result["Items"]}

    def library_ready():
        result = items()
        scanning = any(
            task.get("Key") == "RefreshLibrary" and task["State"] != "Idle"
            for task in request("ScheduledTasks")
        )
        return result if len(result) == len(fixtures) and not scanning else None

    def plugin_task():
        return next(task for task in request("ScheduledTasks") if task["Key"] == "BluRayMainTitleMediaInfo")

    before = wait_for(library_ready)
    assert {Path(item["Path"]).name for item in before.values()} == fixtures.keys()
    assert all(not item["MediaSources"][0].get("MediaStreams") for item in before.values())
    task = plugin_task()
    assert task["State"] == "Idle"
    request("ScheduledTasks/Running/" + task["Id"], {})

    def task_finished():
        current = plugin_task()
        result = current.get("LastExecutionResult")
        return result if current["State"] == "Idle" and result != task.get("LastExecutionResult") else None

    result = wait_for(task_finished)
    assert result["Status"] == "Completed", result.get("ErrorMessage", result["Status"])

    after = items()
    assert before.keys() == after.keys()
    for item_id, item in after.items():
        old = before[item_id]["MediaSources"][0]
        source = item["MediaSources"][0]
        assert all(old[key] == source[key] for key in ("Id", "Path", "Container"))
        name = Path(source["Path"]).name
        codec = fixtures[name]
        if codec is None:
            assert not source.get("MediaStreams") and not item.get("RunTimeTicks") and not item.get("Chapters"), name
            print(f"{name}: skipped", flush=True)
            continue
        runtime = 80_080_000 if codec == "h264" else 30_000_000
        assert source["RunTimeTicks"] == item["RunTimeTicks"] == runtime, name
        tracks = [(track["Type"], track["Codec"], track.get("Language") or None)
                  for track in source["MediaStreams"]]
        expected_tracks = [("Video", codec, None), ("Audio", "ac3", "eng")]
        video = source["MediaStreams"][0]
        if codec == "h264":
            expected_tracks.append(("Subtitle", "pgssub", "zho"))
            assert (video["Width"], video["Height"]) == (1920, 1080), name
        else:
            assert (video["Width"], video["Height"], video["BitDepth"]) == (3840, 2160, 10), name
            assert video["ExtendedVideoType"] == "Hdr10" and video["ColorTransfer"] == "smpte2084", name
            assert [chapter["StartPositionTicks"] for chapter in item["Chapters"]] == [0, 10_000_000], name
        assert tracks == expected_tracks, (name, tracks)
        print(f"{name}: {video['Width']}x{video['Height']} {codec} {video['ExtendedVideoType']}, {runtime / 10_000_000:g} s", flush=True)


if __name__ == "__main__":
    if sys.argv[1] == "--seed":
        seed_api_key(Path(sys.argv[2]), Path(sys.argv[3]))
    else:
        project = Path(__file__).resolve().parents[1] / "src" / "Emby.BlurayMainTitle.csproj"
        version = ElementTree.parse(project).findtext("./PropertyGroup/Version")
        key = Path(sys.argv[2]).read_text(encoding="ascii")
        test_emby(f"http://127.0.0.1:{int(sys.argv[1])}/emby/", version + ".0", key)
