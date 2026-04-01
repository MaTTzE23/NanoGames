from __future__ import annotations

from pathlib import Path

from common import audit_client, build_parser, print_json


def main() -> int:
    parser = build_parser("Audit the live NanOnline client against the manifest.")
    args = parser.parse_args()

    payload = audit_client(Path(args.client_root), Path(args.manifest))
    print_json(payload, args.pretty)
    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
