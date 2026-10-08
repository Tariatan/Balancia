# Balancia project memory

Last tidied: 2026-10-08. Scope: this repository only.
Historical checks below are recorded evidence, not checks rerun during cleanup.

## Read first

- [Technical Design Description](docs/Technical_Design_Description.md) owns
  product behavior and design decisions.
- [AGENTS.md](AGENTS.md) owns operational rules, including versioning and
  documentation ownership; [README.md](README.md) owns build/run commands.
- [CHANGELOG.md](CHANGELOG.md) records releases and implemented changes.
- [PLAN.md](docs/PLAN.md) is maintained exclusively by the user.

## Decisions worth retaining

- **Usage agreement:** Windows is the only writer; Android is a read-only
  balance/history viewer. Stale snapshots and manual refresh are acceptable.
  Google Drive is user-managed snapshot transport. Android transport/device
  acceptance remains open. See the TDD's Android context.
- **Encryption boundary (2026-10-03):** the owner chose to leave local databases,
  recovery copies, and CSV exports plaintext. Transported snapshots and rolling
  backups are encrypted; canceling key setup pauses exports without a plaintext
  fallback. Changing the passphrase affects future exports, while older files
  retain their old phrase. See the TDD's snapshot lifecycle and Desktop 2.9.0 /
  Storage 4.0.0 entries in the changelog.
- **Dialog layout decision (2026-09-28):** fixed-shape Settings, Error, and CSV
  preview dialogs moved to AXAML. The shared `EditDialog` remains code-built
  because each caller supplies different fields. Client-derivable validation is
  shown inline before saving; uniqueness and concurrent-edit checks remain in
  Storage. Source: [MainWindow.Dialogs.cs](src/Balancia.Desktop/MainWindow.Dialogs.cs).
- **Date navigation correction (2026-10-02):** the user requested Up / wheel up
  to advance one day and Down / wheel down to go back. Native checks confirmed
  Up moved October 1 to 2 and wheel up moved October 2 to 3 after the correction.
  See the TDD's Window Composition section and Desktop 2.1.0 changelog entry.
- **Repository reference (2026-09-17):** Git was initialized on `main`; the
  recorded private repository is [Tariatan/Balancia](https://github.com/Tariatan/Balancia).

## Recorded verification

### Encryption and diagnostics — 2026-10-03

- Locked restore and the full Release solution build passed with zero warnings
  or errors; 79 Core and 100 Storage tests passed, including privacy,
  correlation, and logging severity checks. Earlier encryption coverage recorded
  79 Core / 95 Storage tests and synthetic DPAPI save/load/tampered-cache checks.
- Actual shutdown methods were exercised through a source-linked synthetic
  harness: ten encrypted backups, one overwritten snapshot, retention after
  successful export, and preservation on missing/disposed keys and failures.
- Native Windows startup, remembered-key load, and normal-close logging passed.
  Settings/passphrase layout and Cancel were checked with a separate database.
- Android 0.3.1 APK was published and hash-checked (application version 5,
  target API 36). This established packaging only; device behavior remains open.
- Sources: the prior repository handoff; Desktop 2.9.0–2.9.1, Storage
  4.0.0–4.0.1, and Android 0.3.0–0.3.1 entries in [CHANGELOG.md](CHANGELOG.md).

### CSV — 2026-10-02 to 2026-10-03

- Nine-column ledger export/import was covered by fresh-ledger roundtrip,
  unchanged reimport, invalid format/IDs/amounts, and atomic rollback checks;
  82 Storage tests, Desktop Release build, and diff checks passed on October 2.
  The owner separately confirmed the Windows CSV export/import workflow.
- Reminder rows were added on October 3; locked restore and Release build passed.
  Their runtime roundtrip remains unverified. Sources: the prior handoff and
  Desktop 2.8.0 / Storage 3.3.0 changelog entries.

### Desktop interaction — 2026-09-28 to 2026-10-05

- Settings extraction was manually verified on September 28. Later settings
  persistence consolidation and version-footer layout still need interaction
  review; the earlier check does not cover those revisions.
- Category `#` button accessibility and an empty-list click were checked in a
  separate sample ledger on October 4; populated parent/child selection remains
  unverified. Locked restore, full Release build, and diff check passed.
- Other recorded Desktop changes passed Release builds and, where recorded,
  locked restore/diff checks. Outstanding native checks are grouped below rather
  than repeated per release. Source: the prior repository handoff.

## Open verification and next step

- **First:** verify Windows passphrase Save, encrypted export, and restore via
  the picker; then run [Android device acceptance](docs/ANDROID.md) for initial
  pairing, same-key reimport, restart, changed phrase, wrong phrase, Cancel,
  damaged archives, legacy plaintext import, runtime logs, and Keystore outcomes.
- **Categories and transactions:** populated parent/child filtering; repeated
  Add another transaction entry after Category reset; Description suggestion
  click/Tab acceptance. The earlier UI tool could not reliably target the owned
  Description dialog, so opening it was not an interaction pass.
- **CSV and dialogs:** reminder roundtrip; Error and CSV preview dialog click-through.
- **Dashboard:** Trend/Timeline hover tooltips; Last 30 days startup fallback and
  filters; reminder colors, totals, current/next-month branch switching and early
  payment advancement; default-account highlight; localized Today label.
- **Settings:** consolidated persistence and the fixed version footer beside Close
  (Release build passed on 2026-10-05; native layout review remains pending).
- **Logging:** midnight rolling and retention were configured but not verified
  by shifting time. Android document-provider compatibility remains device-dependent.

## Maintenance notes

- The obsolete `scripts/Check-Harness.ps1` is absent, confirmed on 2026-10-08.
  Use current README commands instead of historical harness instructions.
- Cleanup consolidated repeated release metadata and implementation descriptions
  into links above. The old claim that Storage "remains 3.0.0" was superseded by
  later releases; current metadata is in the project files and changelog.
- Keep this handoff focused on decisions, evidence limits, and the next action;
  add updates only when the user requests them.
