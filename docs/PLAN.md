# Implementation plan and checkpoint

Last updated: 2026-10-03.

## ToDO list

- Verify Windows passphrase setup/save, automatic export after restart, and restore
  through the file picker with the owner's data. Layout and cancellation were
  checked with a separate synthetic database; setup/save was not automated.
- Verify Android API 36 pairing on the Samsung device: wrong phrase/cancel,
  successful import, restart, subsequent automatic unlock, passphrase change,
  and legacy plaintext import (see docs/ANDROID.md). Check app-private logs for
  these outcomes and remembered-key failures with Android 0.3.1.

## Completed

- Encryption/snapshot/backup logging coverage added (2026-10-03), including an
  Android daily file sink, correlated validation/replacement milestones, key
  cache outcomes, expected rejection levels, and privacy regression checks.
- Locked restore and Release solution build passed with no warnings/errors;
  79 Core and 100 Storage tests passed. Native Windows startup/key-load/close
  logs and synthetic shutdown success/failure/skip/retention logs checked.
  Android runtime log checks remain pending.

- Encrypted snapshots and rolling backups implemented (2026-10-03). Local
  databases/recovery copies/CSV remain plaintext; Windows DPAPI and Android
  Keystore protect remembered derived keys. Legacy snapshots stay readable.
- Locked restore, Release solution build, 79 Core tests and 95 Storage tests
  passed. Synthetic Windows DPAPI and shutdown-method smoke checks passed,
  including ten-file retention, fixed snapshot overwrite, and failed/missing-key
  preservation. Android device acceptance remains pending above.
