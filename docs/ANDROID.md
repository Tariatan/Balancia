# Android viewer checkpoint

Balancia keeps Windows as the only writer. The Android companion will receive a
validated `.balancia` snapshot and expose balances plus read-only transaction
search. It must never open or synchronize the live Windows SQLite file.

The .NET Android workload is installed (manifest 36.1.69) and the project now
targets `net10.0-android` with Android API 36. It is a native Android shell only;
the viewer UI and document-provider flow are still next. The storage snapshot
format and `LedgerStore.ValidateSnapshot` provide the validation boundary that
the viewer will reuse. The next implementation slice stages a snapshot, validates
it, and promotes it to an app-local copy only after integrity, dataset, and schema
checks succeed.
