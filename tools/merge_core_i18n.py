#!/usr/bin/env python3
"""Merge reviewed translations into a core locale and require English parity."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any


MISSING = object()
PLACEHOLDER = re.compile(r"\{\d+(?::[^{}]+)?\}")


def merge_node(english: Any, current: Any, overlay: Any, path: str) -> Any:
    if isinstance(english, dict):
        if current is MISSING:
            current = {}
        if overlay is MISSING:
            overlay = {}
        if not isinstance(current, dict) or not isinstance(overlay, dict):
            raise ValueError(f"Expected an object at {path or '<root>'}")
        unknown = set(overlay) - set(english)
        if unknown:
            raise ValueError(f"Unknown translation keys at {path or '<root>'}: {sorted(unknown)}")
        return {
            key: merge_node(
                value,
                current.get(key, MISSING),
                overlay.get(key, MISSING),
                f"{path}.{key}" if path else key,
            )
            for key, value in english.items()
        }

    if isinstance(english, list):
        selected = overlay if overlay is not MISSING else current
        if selected is MISSING:
            raise ValueError(f"Missing translation array: {path}")
        if not isinstance(selected, list) or len(selected) != len(english):
            raise ValueError(f"Translation array shape differs at {path}")
        return [
            merge_node(source, translated, MISSING, f"{path}[{index}]")
            for index, (source, translated) in enumerate(zip(english, selected))
        ]

    selected = overlay if overlay is not MISSING else current
    if selected is MISSING:
        raise ValueError(f"Missing translation: {path}")
    if not isinstance(selected, type(english)):
        raise ValueError(f"Translation type differs at {path}")
    if isinstance(english, str):
        expected = sorted(PLACEHOLDER.findall(english))
        actual = sorted(PLACEHOLDER.findall(selected))
        if expected != actual:
            raise ValueError(
                f"Placeholder mismatch at {path}: expected {expected}, found {actual}"
            )
    return selected


def extract_missing_node(english: Any, current: Any) -> Any:
    if isinstance(english, dict):
        current_object = current if isinstance(current, dict) else {}
        return {
            key: extracted
            for key, value in english.items()
            if (extracted := extract_missing_node(value, current_object.get(key, MISSING)))
            is not MISSING
        }

    if isinstance(english, list):
        if not isinstance(current, list) or len(current) != len(english):
            return english
        extracted_items = [
            extract_missing_node(value, current[index])
            for index, value in enumerate(english)
        ]
        return MISSING if all(item is MISSING for item in extracted_items) else english

    return english if current is MISSING else MISSING


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("locale", help="Locale name, for example ru-ru")
    parser.add_argument("overlay", type=Path, nargs="?", help="Reviewed partial translation JSON")
    parser.add_argument(
        "--create-overlay",
        type=Path,
        metavar="OUTPUT",
        help="Write the locale's missing English resources to OUTPUT instead of merging",
    )
    args = parser.parse_args()

    repository_root = Path(__file__).parents[1]
    language_directory = repository_root / "src" / "ZTMZ.PacenoteTool.I18N" / "lang"
    english_path = language_directory / "en-us.json"
    locale_path = language_directory / f"{args.locale.lower()}.json"

    english = json.loads(english_path.read_text(encoding="utf-8-sig"))
    current = json.loads(locale_path.read_text(encoding="utf-8-sig"))

    if args.create_overlay:
        extracted = extract_missing_node(english, current)
        if extracted is MISSING:
            extracted = {}
        args.create_overlay.write_text(
            json.dumps(extracted, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        print(f"Wrote missing {args.locale} resources to {args.create_overlay}")
        return

    if args.overlay is None:
        parser.error("overlay is required unless --create-overlay is used")

    overlay = json.loads(args.overlay.read_text(encoding="utf-8-sig"))
    merged = merge_node(english, current, overlay, "")
    locale_path.write_text(
        json.dumps(merged, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Merged complete {args.locale} resources into {locale_path}")


if __name__ == "__main__":
    main()
