from __future__ import annotations

import argparse
import socket
import urllib.error
import urllib.request
from pathlib import Path

from common import DEFAULT_CLIENT_ROOT, DEFAULT_MANIFEST_PATH, print_json
from client_audit import audit_client
from manifest_verify import verify_manifest


AUTH_HOST = "auth.nanonline.net"
GETINFO_URL = "http://auth.nanonline.net/user/v1/getInfo?realm=live&token=test&sig="


def check_dns() -> tuple[bool, str]:
    try:
        ip = socket.gethostbyname(AUTH_HOST)
        return True, ip
    except OSError as exc:
        return False, str(exc)


def check_getinfo() -> tuple[bool, str]:
    request = urllib.request.Request(GETINFO_URL, method="GET")
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            body = response.read(256).decode("utf-8", errors="ignore")
            return response.status in (200, 401), body
    except urllib.error.HTTPError as exc:
        body = exc.read(256).decode("utf-8", errors="ignore")
        return exc.code == 401, body
    except Exception as exc:
        return False, str(exc)


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the NanOnline release gates.")
    parser.add_argument("--client-root", default=str(DEFAULT_CLIENT_ROOT))
    parser.add_argument("--manifest", default=str(DEFAULT_MANIFEST_PATH))
    parser.add_argument("--world-join-confirmed", action="store_true")
    parser.add_argument("--pretty", action="store_true")
    args = parser.parse_args()

    client_root = Path(args.client_root)
    manifest_path = Path(args.manifest)
    audit = audit_client(client_root, manifest_path)
    manifest = verify_manifest(client_root, manifest_path)
    dns_ok, dns_info = check_dns()
    getinfo_ok, getinfo_info = check_getinfo()

    required_files = ["NanOnlineLauncher.exe", "Nano.exe", "NanoSecurityTray.exe", "Nano.bin", "server.json"]
    missing_runtime_files = [name for name in required_files if not (client_root / name).exists()]

    gates = [
        {
            "gate_name": "auth_dns",
            "passed": dns_ok,
            "blocking_reason": "" if dns_ok else dns_info,
        },
        {
            "gate_name": "auth_getinfo",
            "passed": getinfo_ok,
            "blocking_reason": "" if getinfo_ok else getinfo_info,
        },
        {
            "gate_name": "manifest_consistency",
            "passed": manifest["manifest_status"] == "pass" and manifest["hash_consistency"] == "pass",
            "blocking_reason": "" if manifest["manifest_status"] == "pass" and manifest["hash_consistency"] == "pass" else "Manifest and live client differ.",
        },
        {
            "gate_name": "runtime_files_present",
            "passed": not missing_runtime_files,
            "blocking_reason": "" if not missing_runtime_files else f"Missing files: {', '.join(missing_runtime_files)}",
        },
        {
            "gate_name": "unexpected_client_files",
            "passed": not audit["unexpected_files"],
            "blocking_reason": "" if not audit["unexpected_files"] else f"Unexpected files: {', '.join(audit['unexpected_files'][:10])}",
        },
        {
            "gate_name": "world_join_canary",
            "passed": bool(args.world_join_confirmed),
            "blocking_reason": "" if args.world_join_confirmed else "World-join canary not confirmed yet.",
        },
    ]

    payload = {
        "status": "pass" if all(gate["passed"] for gate in gates) else "fail",
        "gates": gates,
        "dns_info": dns_info,
        "getinfo_info": getinfo_info,
        "client_audit": audit,
        "manifest_verify": manifest,
    }
    print_json(payload, args.pretty)
    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
