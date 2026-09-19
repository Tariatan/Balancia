# Android viewer checkpoint

Balancia keeps Windows as the only writer. The Android companion will receive a
validated `.balancia` snapshot and expose balances plus read-only transaction
search. It must never open or synchronize the live Windows SQLite file.

The .NET Android workload is installed (manifest 36.1.69) and the project now
targets `net10.0-android` with Android API 36. The first viewer slice lets the
user choose a document, copies it into app-private staging, validates the archive
and SQLite database, promotes the embedded database only after validation, and
shows a read-only net-worth/monthly-summary screen. It never writes to the
snapshot or exposes ledger editing. Device testing on the Samsung S20 FE remains.
The viewer also provides a description search over the staged read-only history,
returning up to 50 newest matches.
