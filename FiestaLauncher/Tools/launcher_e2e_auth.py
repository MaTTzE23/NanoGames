import json
import os
import ssl
import sys
import urllib.request
from pathlib import Path


WATCH_PATHS = {
    "Nano.bin",
    "steam_api.dll",
    "BugTrap.dll",
    "d3dx9_25.dll",
    "server.json",
    r"ressystem\AbStateView.shn",
    r"ressystem\ActiveSkillView.shn",
    r"ressystem\CharacterTitleStateView.shn",
    r"ressystem\EffectViewInfo.shn",
    r"ressystem\ItemShopView.shn",
    r"ressystem\ItemViewInfo.shn",
    r"ressystem\MapViewInfo.shn",
    r"ressystem\MobViewInfo.shn",
    r"ressystem\NPCViewInfo.shn",
    r"ressystem\PassiveSkillView.shn",
    r"ressystem\ProduceView.shn",
    r"ressystem\CollectCardView.shn",
    r"ressystem\GTIView.shn",
    r"ressystem\ItemViewEquipTypeInfo.shn",
}


def post_json(url: str, body: dict) -> dict:
    request = urllib.request.Request(
        url,
        data=json.dumps(body).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    ssl_context = ssl._create_unverified_context()
    with urllib.request.urlopen(request, timeout=30, context=ssl_context) as response:
        return json.loads(response.read().decode("utf-8"))


def main() -> int:
    client_root = Path(r"C:\Program Files (x86)\NanOnline V2 (BETA)")
    manifest_path = Path(r"C:\inetpub\wwwroot\nano_site\patcher-manifest\manifest.json")
    output_path = Path(os.environ["TEMP"]) / "nanonline_launcher_e2e.json"

    config = json.loads((client_root / "server.json").read_text(encoding="utf-8"))
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

    expected_hashes = {}
    for entry in manifest.get("files", []):
        path = str(entry.get("path", "")).lstrip("\\/")
        if path in WATCH_PATHS:
            expected_hashes[path] = str(entry.get("sha256", ""))

    version = (client_root / "version.dat").read_text(encoding="utf-8", errors="ignore").strip() or "1.3.1"
    machine_id = "codex-e2e"

    login = post_json(
        config["LauncherLoginUrl"],
        {
            "Username": "CODEX",
            "Password": "CODEX",
            "MachineId": machine_id,
            "LauncherVersion": version,
        },
    )
    if not login.get("Success"):
        print("LOGIN_FAILED=" + str(login.get("Message", "Unknown error")))
        return 1

    start = post_json(
        config["LauncherStartUrl"],
        {
            "AccessToken": login["AccessToken"],
            "MachineId": machine_id,
            "LauncherVersion": version,
        },
    )
    if not start.get("Success"):
        print("START_FAILED=" + str(start.get("Message", "Unknown error")))
        return 1

    payload = {
        "ClientRoot": str(client_root),
        "Config": config,
        "ExpectedHashes": expected_hashes,
        "AccessToken": login["AccessToken"],
        "StartToken": start["StartToken"],
        "LoginHost": start["LoginHost"],
        "LoginPort": start["LoginPort"],
        "OskServer": start["OskServer"],
        "OskStore": start.get("OskStore") or config.get("OskStoreUrl") or "https://nanonline.net/",
        "Version": version,
    }
    output_path.write_text(json.dumps(payload), encoding="utf-8")

    print("LOGIN_SUCCESS=True")
    print("START_SUCCESS=True")
    print("PAYLOAD_FILE=" + str(output_path))
    print("WATCH_COUNT=" + str(len(expected_hashes)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
