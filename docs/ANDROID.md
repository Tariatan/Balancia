# Android viewer checkpoint

Balancia keeps Windows as the only writer. The Android companion will receive a
validated `.balancia` snapshot and expose balances plus read-only transaction
search. It must never open or synchronize the live Windows SQLite file.

The .NET Android workload is installed (manifest 36.1.69) and the project now
targets `net10.0-android` with Android API 36. The first viewer slice lets the
user choose a document, copies it into app-private staging, validates the archive
and SQLite database, promotes the embedded database only after validation, and
shows a read-only net-worth/monthly-summary screen. It never writes to the
snapshot or exposes ledger editing.

Encrypted snapshots (Desktop 2.9.0 / Storage 4.0.0 and later) require Android
0.3.0 or later. On first import, enter the exact passphrase configured in Windows
Settings. Android derives the portable key, authenticates the entire archive,
validates its database, then replaces the local viewer copy. The remembered key
is wrapped by Android Keystore in app-private `NoBackupFilesDir`; subsequent
imports using the same export key unlock automatically. The saved local viewer
copy also reopens after restarting the app.

A wrong phrase or damaged file keeps the previous database intact and shows an
error. Canceling unlock leaves it unchanged. Reinstalling the app, invalidating
the device key, or changing the Windows export phrase requires entering the
phrase again. Older plaintext snapshots still import. Local viewer databases
remain plaintext, as agreed; this feature protects the transported snapshot.

Android 0.3.1 adds daily diagnostics in app-private
`NoBackupFilesDir/log/balancia-yyyyMMdd.log`, retaining ten daily files. Logs cover
saved-copy startup, snapshot selection/copy, unlock/cancel/retry, validation and
replacement, remembered-key storage, and search results count/failures. They
exclude passphrases, keys, financial contents, document URIs, and search text.
These files are not included in snapshots or OS backups.

Build the API 36 APK with the installed workload:

```powershell
dotnet restore Balancia.slnx --locked-mode
dotnet publish src/Balancia.Android/Balancia.Android.csproj -c Release --no-restore -p:AndroidPackageFormats=apk
```

Device acceptance: import an encrypted snapshot; verify wrong phrase and Cancel
retain the previous copy; enter the correct phrase; restart; import a fresh
snapshot with the same export key and verify no prompt; change the Windows
passphrase and verify one new prompt; also import a legacy plaintext snapshot.
Check device logs during this sequence: expected unlock/validation rejections
should be Information, and Keystore cache failures should be Warning.
