# Migrations on an existing database

Migrations are applied automatically at startup by `MigrationBootstrapper.Run`, called from
`Program.Main` before anything else resolves a database context.

## The one awkward case, handled automatically

The schema used to be created by `Database.EnsureCreated()`, which records nothing in
`__EFMigrationsHistory`. On a database that predates migrations the tables therefore exist while the
history is empty, and calling `Migrate()` would fail on the first `CREATE TABLE`.

`MigrationBootstrapper` detects exactly that shape (no migration history, but the `Users` table exists) and
records the baseline as applied instead of replaying it. That is correct by construction: the baseline
migration was generated from that very schema. It logs a warning when it does so, and it is idempotent.

A fresh database needs none of this: `Migrate()` creates it and applies every migration, baseline included.

This path is covered by `SyncStreamAPI.Tests/Integration/MigrationBootstrapperTests.cs`, which runs against
a real PostgreSQL server when `SYNCSTREAM_TEST_DB` is set and skips otherwise. It reproduces the
`EnsureCreated` shape and asserts the adoption works, so this is verified behaviour rather than a claim.

## Confirming the baseline matches what is deployed

`InitialBaseline` was generated with `Npgsql.EnableLegacyTimestampBehavior` on, so its timestamp columns are
`timestamp without time zone`, matching a database created under Npgsql 5. Worth confirming once against the
live database:

```sql
SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE data_type LIKE 'timestamp%'
ORDER BY table_name, column_name;
```

Every row should say `timestamp without time zone`. If any says `timestamp with time zone`, stop: the
baseline does not describe that database and needs regenerating against it.

## Doing it by hand instead

If you would rather adopt the baseline manually and remove the automatic path:

```sql
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    character varying(150) NOT NULL,
    "ProductVersion" character varying(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260819001533_InitialBaseline', '9.0.4')
ON CONFLICT ("MigrationId") DO NOTHING;
```

Then `dotnet dotnet-ef migrations list` should show `InitialBaseline` without `(Pending)`.

## Known follow-up: timestamps

`Npgsql.EnableLegacyTimestampBehavior` is set in `DataContext/NpgsqlCompatibility.cs` to keep the
pre-Npgsql-6 `DateTime` mapping. Removing it is a migration of its own, and it needs a decision per column,
because the existing values are not all in the same zone:

| Column | Written as |
| --- | --- |
| `DbFile.Created`, `DbFile.DateToBeDeleted` | UTC (`DateTime.UtcNow`) |
| `DbRememberToken.Created` | UTC (`DateTime.UtcNow`) |
| `DbConversation.LastUpdated` | server local time (`DateTime.Now`) |
| `DbMessage.Time` | server local time (`DateTime.Now`) |

So the conversion is `AT TIME ZONE 'UTC'` for the first two groups and `AT TIME ZONE '<the server's zone>'`
for the last two. Confirm the deployment's timezone before writing it, and change the two `DateTime.Now`
call sites to `DateTime.UtcNow` in the same commit so new rows stop adding to the mess.
