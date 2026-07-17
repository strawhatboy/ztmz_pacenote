# Diagnostics

Open `About` and select `Diagnostics` to inspect the active game, UDP receiver, audio playback engine, Web Dashboard, and the selected game's prerequisite check. Refreshing diagnostics is read-only; it does not repair game files or restart any listener.

`Create Support Bundle` writes a ZIP file to the application's `support` data folder. The ZIP contains:

- `diagnostics.json`: a runtime snapshot with user-profile and ZTMZ data-folder paths redacted.
- The seven most recent log files, limited to the most recent 5 MiB of each file.

The bundle does not include raw game XML/INI/JSON configuration files, user configuration, audio packages, recordings, or replay data. It is created locally and is never uploaded automatically.
