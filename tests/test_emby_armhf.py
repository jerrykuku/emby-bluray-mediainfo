"""Check that an isolated armhf Emby 4.10.2.0 fills SDR BDMV and ISO entries."""

import http.client
import json
from pathlib import Path
import secrets
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


def test_emby(endpoint, plugin_version):
    headers = {
        "Content-Type": "application/json",
        "X-Emby-Authorization": 'MediaBrowser Client="Blu-ray Test", Device="Loopback", DeviceId="armhf-test", Version="1.0"',
    }

    def request(path, data=None):
        payload = json.dumps(data).encode() if data is not None else None
        req = urllib.request.Request(endpoint + path, data=payload, headers=headers)
        with urllib.request.urlopen(req, timeout=10) as response:
            body = response.read()
        return json.loads(body) if body else None

    info = wait_for(lambda: request("System/Info/Public"))
    assert info["Version"] == "4.10.2.0", info["Version"]
    credentials = {"Username": "BlurayFixture", "Pw": secrets.token_urlsafe(24)}
    request("Startup/User", {"Name": credentials["Username"], "Password": credentials["Pw"]})
    request("Startup/RemoteAccess", {"EnableRemoteAccess": False, "EnableAutomaticPortMapping": False})
    request("Startup/Complete", {})
    headers["X-Emby-Token"] = request("Users/AuthenticateByName", credentials)["AccessToken"]

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
        result = request("Items?Recursive=true&IncludeItemTypes=Movie&Fields=Path,MediaSources,MediaStreams")
        return {item["Id"]: item for item in result["Items"]}

    def library_ready():
        result = items()
        scanning = any(
            task.get("Key") == "RefreshLibrary" and task["State"] != "Idle"
            for task in request("ScheduledTasks")
        )
        return result if len(result) == 2 and not scanning else None

    def plugin_task():
        return next(task for task in request("ScheduledTasks") if task["Key"] == "BluRayMainTitleMediaInfo")

    before = wait_for(library_ready)
    assert {Path(item["Path"]).name for item in before.values()} == {
        "Fixture Folder (2026)", "Fixture Image (2026).bluray.iso"
    }
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
        assert source["RunTimeTicks"] == item["RunTimeTicks"] == 80_080_000
        tracks = [(track["Type"], track["Codec"], track.get("Language") or None)
                  for track in source["MediaStreams"]]
        assert tracks == [("Video", "h264", None), ("Audio", "ac3", "eng"), ("Subtitle", "pgssub", "zho")]
        print(f"{Path(source['Path']).name}: 8.008 s, H.264, AC-3, and PGS", flush=True)


if __name__ == "__main__":
    project = Path(__file__).resolve().parents[1] / "src" / "Emby.BlurayMainTitle.csproj"
    version = ElementTree.parse(project).findtext("./PropertyGroup/Version")
    test_emby(f"http://127.0.0.1:{int(sys.argv[1])}/emby/", version + ".0")
