from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


DEFAULT_CLIENT_ROOT = Path(r"C:\inetpub\wwwroot\nano_site\downloads\client")
DEFAULT_MANIFEST_PATH = Path(r"C:\inetpub\wwwroot\nano_site\patcher-manifest\manifest.json")


def build_parser(description: str) -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("--client-root", default=str(DEFAULT_CLIENT_ROOT))
    parser.add_argument("--manifest", default=str(DEFAULT_MANIFEST_PATH))
    parser.add_argument("--pretty", action="store_true")
    return parser


def normalize_relative_path(path: str) -> str:
    return path.replace("/", "\\").strip("\\")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_manifest(manifest_path: Path) -> dict[str, Any]:
    return json.loads(manifest_path.read_text(encoding="utf-8"))


def iter_client_files(client_root: Path) -> list[Path]:
    return sorted(path for path in client_root.rglob("*") if path.is_file())


def to_relative_path(client_root: Path, path: Path) -> str:
    return normalize_relative_path(str(path.relative_to(client_root)))


def matches_cleanup_path(relative_path: str, cleanup_paths: list[str]) -> bool:
    normalized = normalize_relative_path(relative_path)
    for cleanup_path in cleanup_paths:
        candidate = normalize_relative_path(cleanup_path)
        if normalized == candidate or normalized.startswith(candidate + "\\"):
            return True
    return False


def audit_client(client_root: Path, manifest_path: Path) -> dict[str, Any]:
    manifest = load_manifest(manifest_path)
    manifest_files = {
        normalize_relative_path(file_info["path"]): file_info
        for file_info in manifest.get("files", [])
    }
    cleanup_paths = [normalize_relative_path(value) for value in manifest.get("cleanup_paths", [])]

    disk_files = {
        to_relative_path(client_root, path): path
        for path in iter_client_files(client_root)
    }

    missing_files: list[str] = []
    hash_mismatches: list[dict[str, Any]] = []
    for relative_path, file_info in manifest_files.items():
        disk_path = disk_files.get(relative_path)
        if disk_path is None:
            missing_files.append(relative_path)
            continue

        actual_hash = sha256_file(disk_path)
        expected_hash = str(file_info.get("sha256", "")).lower()
        if actual_hash.lower() != expected_hash:
            hash_mismatches.append(
                {
                    "path": relative_path,
                    "expected": expected_hash,
                    "actual": actual_hash.lower(),
                }
            )

    unexpected_files = sorted(
        relative_path
        for relative_path in disk_files
        if relative_path not in manifest_files and not matches_cleanup_path(relative_path, cleanup_paths)
    )

    existing_cleanup_targets = sorted(
        cleanup_path
        for cleanup_path in cleanup_paths
        if (client_root / cleanup_path).exists()
        or any(
            relative_path == cleanup_path or relative_path.startswith(cleanup_path + "\\")
            for relative_path in disk_files
        )
    )

    status = "pass" if not missing_files and not hash_mismatches else "fail"
    return {
        "status": status,
        "client_root": str(client_root),
        "manifest_path": str(manifest_path),
        "manifest_file_count": len(manifest_files),
        "disk_file_count": len(disk_files),
        "missing_files": missing_files,
        "unexpected_files": unexpected_files,
        "hash_mismatches": hash_mismatches,
        "cleanup_targets": existing_cleanup_targets,
    }


def verify_manifest(client_root: Path, manifest_path: Path) -> dict[str, Any]:
    manifest = load_manifest(manifest_path)
    audit = audit_client(client_root, manifest_path)
    entry_executable = normalize_relative_path(str(manifest.get("entry_executable", "")))
    manifest_file_paths = {
        normalize_relative_path(file_info["path"])
        for file_info in manifest.get("files", [])
    }

    entry_status = "pass" if entry_executable in manifest_file_paths else "fail"
    hash_status = "pass" if not audit["hash_mismatches"] and not audit["missing_files"] else "fail"
    cleanup_status = "pass" if isinstance(manifest.get("cleanup_paths", []), list) else "fail"
    manifest_status = "pass" if entry_status == "pass" and cleanup_status == "pass" else "fail"

    return {
        "manifest_status": manifest_status,
        "entry_executable_status": entry_status,
        "entry_executable": entry_executable,
        "hash_consistency": hash_status,
        "cleanup_consistency": cleanup_status,
        "missing_files": audit["missing_files"],
        "hash_mismatches": audit["hash_mismatches"],
        "cleanup_targets": audit["cleanup_targets"],
    }


def print_json(payload: dict[str, Any], pretty: bool) -> None:
    if pretty:
        print(json.dumps(payload, indent=2, ensure_ascii=False))
        return

    print(json.dumps(payload, ensure_ascii=False))
