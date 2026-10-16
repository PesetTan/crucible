# Private catalog snapshots

The owner's private [PesetTan/static-data repository](https://github.com/PesetTan/static-data) contains a consistent `standard_cards.sqlite` snapshot, the public Reanimator archive, and historical catalog backups. Access requires explicit GitHub permission; the public Crucible repository and desktop distributions do not grant access.

For Codex Cloud, include both repositories in the environment, verify the data with `python3 /workspace/static-data/scripts/verify.py`, and set `MTG_CARD_DB=/workspace/static-data/standard_cards.sqlite`. Substitute the actual data checkout path if setup uses another directory. Publish or republish the environment before starting a new task. See the private repository's README for setup and refresh instructions and the [Codex Cloud environment guide](https://learn.chatgpt.com/docs/environments/cloud-environments).

The existing local catalog at `../../standard_cards.sqlite` is preserved in place and remains the development default. The `../../static-data/standard_cards.sqlite` file is an independent backup API snapshot. The archive and old recovery databases now live under `../../static-data/`; source catalog refreshes do not automatically update the committed snapshot. Keep `data/simulator.sqlite`, replays, and project evidence in their current locations.

The snapshots include personal account context and are kept private. They are separate from the clean starter catalog shipped with desktop packages. Read snapshots with SQLite `mode=ro`; use a separate working catalog for explicit attribute edits. Simulation imports remain read-only.
