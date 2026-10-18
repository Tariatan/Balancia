# Android viewer checkpoint

Balancia keeps Windows as the only writer. The Android companion receives a validated `.balancia` snapshot and exposes balances plus read-only transaction search. It never opens or synchronizes the live Windows SQLite file, and it opens its own copy through the read-only `LedgerReader`, so the project has no write operation to call.

The .NET Android workload is installed (manifest 36.1.x) and the project targets `net10.0-android` with Android API 36. The viewer lets the user choose a document, copies it into app-private staging, validates the archive and SQLite database, promotes the embedded database only after validation, and shows a read-only screen with net worth, per-account balances, current-month income and expenses, the transaction count, and the time of the import. Search lists the newest 50 matches in a scrollable view and says how many matches exist when there are more.

The application id is `com.triatan.balancia.android` (Android 0.4.0 and later). An earlier install under `com.companyname.Balancia.Android` is a different app: uninstall it, and import a snapshot once in the new app (the first import asks for the passphrase again). The manifest sets `allowBackup="false"` and requests no permissions, so the working viewer database is not copied to Google backup and the app has no network access.

Encrypted snapshots (Desktop 2.9.0 / Storage 4.0.0 and later) require Android 0.3.0 or later. On first import, enter the exact passphrase configured in Windows Settings. Android derives the portable key, authenticates the entire archive, validates its database, then replaces the local viewer copy. The remembered key is wrapped by Android Keystore in app-private `NoBackupFilesDir`; subsequent imports using the same export key unlock automatically. The saved local viewer copy also reopens after restarting the app.

A wrong phrase or damaged file keeps the previous database intact and shows an error. Canceling unlock leaves it unchanged. Reinstalling the app, invalidating the device key, or changing the Windows export phrase requires entering the phrase again. Plaintext snapshots are rejected as an unsupported format (Android 0.3.2). Like a Windows restore, the import rejects a snapshot of a different ledger dataset and an older revision of the same dataset, and the current copy stays (Android 0.4.0); only an unreadable current copy is replaced. To start over with a different ledger, clear the app's storage in Android settings first. Local viewer databases remain plaintext, as agreed; this feature protects the transported snapshot.

Android 0.3.1 added daily diagnostics in app-private `NoBackupFilesDir/log/balancia-yyyyMMdd.log`, retaining 180 daily files since Android 0.5.0. Logs cover saved-copy startup, snapshot selection/copy, unlock/cancel/retry, validation and replacement, remembered-key storage, and search results count/failures. They exclude passphrases, keys, financial contents, document URIs, and search text. These files are not included in snapshots or OS backups.

Build the API 36 APK with the installed workload:

```powershell
dotnet restore Balancia.slnx --locked-mode
dotnet publish src/Balancia.Android/Balancia.Android.csproj -c Release --no-restore -p:AndroidPackageFormats=apk
```

Device acceptance: import an encrypted snapshot; verify wrong phrase and Cancel retain the previous copy; enter the correct phrase; restart; import a fresh snapshot with the same export key and verify no prompt; change the Windows passphrase and verify one new prompt; also verify that a legacy plaintext snapshot is rejected as unsupported and leaves the previous copy intact. Then import an older snapshot of the same ledger, and a snapshot of a different ledger, and verify both are rejected with the current copy intact. Search for a common word with more than 50 matches and verify the list scrolls and shows the "newest 50 of N" note. Confirm the package id, that the app asks for no permissions, and that the app's data is excluded from device backup. Check device logs during this sequence: expected unlock/validation rejections should be Information, and Keystore cache failures should be Warning.
