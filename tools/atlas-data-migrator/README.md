# PostgreSQL source transfer

This standalone tool is the only remaining PostgreSQL driver consumer. It is
not part of frontend or API application dependencies. The source is read in one
REPEATABLE READ, READ ONLY transaction; SQL Server access occurs only through
Dapper in Atlas.DbTool. No plaintext data export file is created.

Run from the repository root:

```powershell
pnpm --dir tools/atlas-data-migrator install --frozen-lockfile
dotnet build backend/Atlas.slnx -m:1
$env:ConnectionStrings__Atlas = '<SQL Server connection for an empty destination>'
pnpm db:migrate
# Supply DATABASE_URL securely, or use an appropriately protected source env file.
node --env-file=.backups/source.env tools/atlas-data-migrator/migrate-data.mjs
node --env-file=.backups/source.env tools/atlas-data-migrator/migrate-data.mjs --verify
```

The destination database must exist. Migrations seed reference roles and
permissions; import replaces only those reference seed rows inside its
transaction before restoring the original IDs and relationships. Any existing
application records cause import to fail. It never overwrites a populated
destination. Use a fresh database for a new migration attempt.

All 21 original tables are transferred in dependency order. Source IDs,
password hashes, encrypted LDAP values and audit records are preserved.
Bounded NVARCHAR widths are checked using UTF-16 byte lengths. SQL Server
constraints and uniqueness rules remain enabled. A bad width, type, foreign key,
uniqueness conflict or record count aborts and rolls back the entire import.
Destination collation differs from PostgreSQL; incompatible distinct source
values must be resolved deliberately before import, never truncated or merged.

Old sessions are deliberately expired. Users authenticate again; new .NET
sessions use hashed tokens and a different cookie. The task reference sequence
is advanced past the largest migrated reference. UTC timestamps and calendar
dates are preserved with explicit conversions.

The reader keeps PostgreSQL dates and timestamps as text until the .NET importer
parses them, retaining timestamp microseconds instead of passing through
JavaScript Date. For a rehearsal made with an older millisecond-only reader,
the explicit `--repair-timestamp-precision` option restores only timestamps
which still exactly match the earlier truncated value. It uses primary-key
compare-and-set updates in one transaction and verifies every original field
before committing; changed destination values cause verification to fail and
roll back. This option is unnecessary for new imports.

`--verify` is read-only on both sides. It checks every source row against all
destination column values, including case-sensitive text and UTF-16 byte
lengths. It permits newer destination records created after cutover. The
documented exception is session expiration, which must be in the past.
Diagnostics contain table names and counts, not source credentials or row values.

Treat source environment files, dump files and original volumes as sensitive.
Keep them out of git and restrict access. The local migration used an isolated
copy of the stopped original Docker volume, mounting the original read-only
during copying. The original volume and a successfully restored custom-format
dump remain available until operational sign-off. Never delete or reseed the
source database.

The local recovery copy is stopped after validation. Start only that copy with
`docker start atlas-migration-source` when re-verifying local source data, then
stop it again with `docker stop atlas-migration-source`. These commands do not
start or change the original volume.

For large production datasets this in-memory snapshot tool needs capacity
planning; it holds source rows in memory in the reader and importer. Schedule a
write freeze and an appropriate maintenance window, rehearse on a protected
copy, verify data, and back up the final SQL Server destination before switching
production traffic.
