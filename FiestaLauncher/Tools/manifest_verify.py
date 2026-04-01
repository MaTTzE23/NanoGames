from __future__ import annotations

from pathlib import Path

from common import build_parser, print_json, verify_manifest


def main() -> int:
    parser = build_parser("Validate manifest structure and hash consistency.")
    args = parser.parse_args()

    payload = verify_manifest(Path(args.client_root), Path(args.manifest))
    print_json(payload, args.pretty)
    return 0 if payload["manifest_status"] == "pass" and payload["hash_consistency"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
