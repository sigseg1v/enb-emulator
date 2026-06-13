#!/usr/bin/env python3
"""Merge launch-recipe settings into FreyaLauncher.settings.json.

Usage: merge-settings.py <settings-path> <overlay-json>

The play-local / play-online just recipes used to regenerate the settings
file from scratch on every launch, which silently wiped every user-owned
toggle the launcher had persisted (most visibly UseClientMods, the
"Enable Lua Mods" checkbox -- enabling it survived exactly one session).
This script instead loads the existing file (tolerating a missing or
corrupt one), overwrites ONLY the keys the recipe owns (connection
target, client path, auth port), and preserves everything else
(UseClientMods, FormMainPositionX/Y, SettingsVersion, ...).
"""
import json
import sys


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__.strip().splitlines()[2], file=sys.stderr)
        return 2
    path, overlay_json = sys.argv[1], sys.argv[2]

    overlay = json.loads(overlay_json)
    if not isinstance(overlay, dict):
        print("merge-settings: overlay must be a JSON object", file=sys.stderr)
        return 2

    try:
        with open(path, encoding="utf-8") as f:
            settings = json.load(f)
        if not isinstance(settings, dict):
            settings = {}
    except (OSError, ValueError):
        settings = {}

    settings.update(overlay)

    with open(path, "w", encoding="utf-8") as f:
        json.dump(settings, f, indent=2)
        f.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
