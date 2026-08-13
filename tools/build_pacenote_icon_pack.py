#!/usr/bin/env python3
"""Build the versioned runtime pacenote icon pack from canonical artwork."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

from PIL import Image


SAFE_TOKEN = re.compile(r"^[a-z0-9_]+$")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-icons", type=Path, required=True)
    parser.add_argument("--source-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--size", type=int, default=128)
    parser.add_argument("--version", type=int, default=1)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    if args.size <= 0:
        raise ValueError("Icon size must be positive")

    records = json.loads(args.source_manifest.read_text(encoding="utf-8"))
    if not isinstance(records, list):
        raise ValueError("Source manifest must be a JSON array")

    icons_dir = args.output / "icons"
    icons_dir.mkdir(parents=True, exist_ok=True)

    icons: dict[str, dict[str, str]] = {}
    aliases: dict[str, str] = {}

    for record in records:
        canonical = record["primary_filename"]
        if not SAFE_TOKEN.fullmatch(canonical):
            raise ValueError(f"Unsafe canonical token: {canonical!r}")
        if canonical in icons:
            raise ValueError(f"Duplicate canonical token: {canonical}")

        source_path = args.source_icons / f"{canonical}.png"
        destination_path = icons_dir / f"{canonical}.png"
        if not source_path.is_file():
            raise FileNotFoundError(source_path)

        with Image.open(source_path) as source:
            image = source.convert("RGBA")
            if image.size != (args.size, args.size):
                image = image.resize((args.size, args.size), Image.Resampling.LANCZOS)
            image.save(destination_path, format="PNG", optimize=True, compress_level=9)

        digest = hashlib.sha256(destination_path.read_bytes()).hexdigest()
        icons[canonical] = {
            "file": f"icons/{canonical}.png",
            "sha256": digest,
        }

        for alias in record["aliases"]:
            normalized_alias = alias.lower()
            if not SAFE_TOKEN.fullmatch(normalized_alias):
                raise ValueError(f"Unsafe token alias: {alias!r}")
            existing = aliases.get(normalized_alias)
            if existing is not None and existing != canonical:
                raise ValueError(
                    f"Alias {normalized_alias!r} maps to both {existing!r} and {canonical!r}"
                )
            aliases[normalized_alias] = canonical

    expected_files = {f"{canonical}.png" for canonical in icons}
    for stale_path in icons_dir.glob("*.png"):
        if stale_path.name not in expected_files:
            stale_path.unlink()

    manifest = {
        "version": args.version,
        "iconSize": args.size,
        "format": "png",
        "icons": dict(sorted(icons.items())),
        "aliases": dict(sorted(aliases.items())),
    }
    (args.output / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )

    total_bytes = sum(path.stat().st_size for path in icons_dir.glob("*.png"))
    print(
        json.dumps(
            {
                "icons": len(icons),
                "aliases": len(aliases),
                "size": args.size,
                "bytes": total_bytes,
                "output": str(args.output),
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
