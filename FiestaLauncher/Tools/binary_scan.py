from __future__ import annotations

import argparse
import json
from pathlib import Path

from common import print_json, sha256_file


DEFAULT_BINARY = Path(r"C:\inetpub\wwwroot\nano_site\downloads\client\Nano.bin")
DEFAULT_NEEDLES = [
    "auth.nanonline.net",
    "stubedore.t",
    "/user/v1/getInfo",
    "-osk_server",
    "-osk_token",
    "-osk_store",
    "-steam_login",
]


def find_occurrences(raw: bytes, needle: str) -> list[dict[str, object]]:
    hits: list[dict[str, object]] = []
    for encoding in ("ascii", "utf-16le"):
        encoded = needle.encode(encoding, errors="ignore")
        start = 0
        while encoded:
            offset = raw.find(encoded, start)
            if offset < 0:
                break
            hits.append({"encoding": encoding, "offset": offset, "needle": needle})
            start = offset + 1
    return hits


def main() -> int:
    parser = argparse.ArgumentParser(description="Scan Nano.bin for legacy host and auth markers.")
    parser.add_argument("--binary", default=str(DEFAULT_BINARY))
    parser.add_argument("--needle", action="append", dest="needles")
    parser.add_argument("--pretty", action="store_true")
    args = parser.parse_args()

    binary_path = Path(args.binary)
    raw = binary_path.read_bytes()
    needles = args.needles or DEFAULT_NEEDLES

    findings: list[dict[str, object]] = []
    for needle in needles:
        findings.extend(find_occurrences(raw, needle))

    payload = {
        "binary": str(binary_path),
        "size": binary_path.stat().st_size,
        "sha256": sha256_file(binary_path),
        "needles": needles,
        "findings": sorted(findings, key=lambda item: (str(item["needle"]), int(item["offset"]))),
    }
    print_json(payload, args.pretty)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
