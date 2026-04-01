from __future__ import annotations

import argparse
import json
import os
import re
from datetime import datetime, timedelta
from pathlib import Path
from typing import Iterable

from common import print_json


LOGIN_LOG_DIR = Path(r"C:\FiestaServer\Server\Login\DebugMessage")
WORLD_LOG_DIR = Path(r"C:\FiestaServer\Server\WorldManager\DebugMessage")
IIS_LOG_DIR = Path(r"C:\inetpub\logs\LogFiles\W3SVC1")
CLIENT_ROOT = Path(r"C:\inetpub\wwwroot\nano_site\downloads\client")
LOCAL_LOG_ROOT = Path(os.environ.get("LOCALAPPDATA", r"C:\Users\Administrator\AppData\Local")) / "NanOnline" / "Logs"

LOGIN_LINE_RE = re.compile(r"^\d+\s+(?P<dt>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})")
IIS_LINE_RE = re.compile(r"^(?P<date>\d{4}-\d{2}-\d{2}) (?P<time>\d{2}:\d{2}:\d{2}) ")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Correlate launcher and server logs around a timestamp.")
    parser.add_argument("--timestamp", help="Local timestamp like 2026-03-31T14:24:46", required=True)
    parser.add_argument("--window-minutes", type=int, default=5)
    parser.add_argument("--pretty", action="store_true")
    return parser.parse_args()


def read_tail(path: Path, count: int = 120) -> list[str]:
    if not path.exists():
        return []
    return path.read_text(encoding="utf-8", errors="ignore").splitlines()[-count:]


def filter_server_log(lines: Iterable[str], center: datetime, window: timedelta) -> list[str]:
    matches: list[str] = []
    for line in lines:
        match = LOGIN_LINE_RE.match(line)
        if not match:
            continue
        dt = datetime.strptime(match.group("dt"), "%Y-%m-%d %H:%M:%S")
        if abs(dt - center) <= window:
            matches.append(line)
    return matches


def filter_iis_log(lines: Iterable[str], center: datetime, window: timedelta) -> list[str]:
    matches: list[str] = []
    interesting = (
        "/api/launcher/login.php",
        "/api/launcher/start.php",
        "/api/launcher/heartbeat.php",
        "/api/launcher/security_event.php",
        "/user/v1/getInfo",
    )
    for line in lines:
        if not any(marker in line for marker in interesting):
            continue
        match = IIS_LINE_RE.match(line)
        if not match:
            continue
        dt = datetime.strptime(
            f"{match.group('date')} {match.group('time')}",
            "%Y-%m-%d %H:%M:%S",
        )
        if abs(dt - center) <= window:
            matches.append(line)
    return matches


def main() -> int:
    args = parse_args()
    center = datetime.fromisoformat(args.timestamp.replace("Z", "+00:00")).replace(tzinfo=None)
    window = timedelta(minutes=max(1, args.window_minutes))

    login_log = next(iter(sorted(LOGIN_LOG_DIR.glob("Msg_*.txt"), reverse=True)), None)
    world_log = next(iter(sorted(WORLD_LOG_DIR.glob("Msg_*.txt"), reverse=True)), None)
    iis_log = next(iter(sorted(IIS_LOG_DIR.glob("u_ex*.log"), reverse=True)), None)

    payload = {
        "timestamp": center.isoformat(sep=" "),
        "window_minutes": args.window_minutes,
        "launcher_logs": {
            "nano_security_tray.log": read_tail(LOCAL_LOG_ROOT / "nano_security_tray.log"),
            "nano_wrapper.log": read_tail(LOCAL_LOG_ROOT / "nano_wrapper.log"),
        },
        "iis": filter_iis_log(read_tail(iis_log, 500) if iis_log else [], center, window),
        "login_server": filter_server_log(read_tail(login_log, 500) if login_log else [], center, window),
        "world_manager": filter_server_log(read_tail(world_log, 500) if world_log else [], center, window),
    }
    print_json(payload, args.pretty)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
