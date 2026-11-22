# ATLAS v2.0 — ADFS Task Lifecycle and Assignment System

ATLAS keeps its Next.js 16 / React 19 interface and uses an ASP.NET Core API,
Dapper, and Microsoft SQL Server for authentication, authorization, and data.

| Layer | Implementation |
| --- | --- |
| Frontend | Next.js App Router, React, TypeScript, Tailwind, Base UI/shadcn |
| API | ASP.NET Core, C#, database-backed sessions, CSRF protection |
| Data access | Dapper with Microsoft.Data.SqlClient |
| Application database | Locally installed SQL Server for development |
| Directory authentication | LDAPS / StartTLS through System.DirectoryServices.Protocols |

Dashboard, projects, Kanban, task ordering, calendar, team administration,
RBAC, Lab/Section scope, LDAP configuration and audit pages retain their routes
and layouts. Profile, password, notification and integration preferences use
the API. Account deletion verifies the current password, transfers project
ownership transactionally and protects the last active administrator.
External service OAuth/synchronization and the original sample API-key
widgets were not implemented by the original application; this migration does
not add external provider integrations or turn sample keys into API credentials.

## Run locally

Install .NET 10, Node.js/pnpm and SQL Server 2022 Developer or Express. Create
an empty database named `ATLAS_V2` in your local instance using SSMS, then
configure its connection in your shell:

```powershell
$env:ConnectionStrings__Atlas = 'Server=localhost\SQL2022DEV;Database=ATLAS_V2;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
pnpm install --frozen-lockfile
dotnet restore backend/Atlas.slnx --disable-parallel -m:1
pnpm db:migrate
```

For a new demo workspace only, set `ATLAS_DEMO_PASSWORD` through your shell or
secret manager to a password of at least 12 characters and run `pnpm db:seed`.
Seeding preserves existing users unless explicitly invoked with `--force`.
Migrated accounts retain their original password hashes and IDs; users sign in
again because old sessions are deliberately revoked.

In separate shells:

```powershell
pnpm api:dev
pnpm dev
```

The API listens at `http://127.0.0.1:5080`; the frontend uses
`http://localhost:3000`. Copy [.env.example](.env.example) to `.env.local` if
you need to change `ATLAS_API_URL`. That URL is server-only: browsers call
same-origin `/api/atlas/*`, and Next.js proxies requests to the API.
No browser or Node.js code connects to SQL Server.

Development defaults use Windows authentication against
`localhost\SQL2022DEV/ATLAS_V2`. Override the connection for another instance.
Production has no default connection string: supply secure configuration.

## Validation

```powershell
pnpm api:build
pnpm build
pnpm typecheck
pnpm lint
pnpm db:verify
pnpm test:backend
pnpm test:ui
```

Backend integration tests require a separate migrated and seeded database
named `ATLAS_MigrationVerification` by default, plus Docker for the disposable
LDAP fixture. Set `ATLAS_TEST_CONNECTION` to use another dedicated database
whose name contains `Verification`. See the [backend guide](backend/README.md)
for setup and deployment requirements.

Stop the API before rebuilding or running backend tests on Windows to avoid
locking its executable. To test the production frontend after a build, set
`ATLAS_UI_TEST_PRODUCTION=true` before running `pnpm test:ui`.

Browser tests require the API running and use port 3100 for Next.js. They
create and remove temporary application records. Set `ATLAS_UI_TEST_EMAIL`
and `ATLAS_UI_TEST_PASSWORD` for your test workspace; the defaults match the
original migrated local demo account. Use a disposable local workspace for tests.

`pnpm api:types` regenerates TypeScript contracts from the running development
API. Runtime Zod validation checks the responses used by existing UI components.

## Migration and recovery

[progress.md](progress.md) records the eight stages, tests, file groups,
decisions and historical failures.
[The source transfer tool](tools/atlas-data-migrator/README.md) reads a
PostgreSQL snapshot in a read-only transaction and streams it to the Dapper
importer. Its optional source driver is isolated from application dependencies.

The original PostgreSQL Docker volume was preserved. A database dump, archived
source configuration and the original tracked repository are also retained in
the local ignored `.backups` folder with a restricted Windows ACL. The dump
was restored into a separate verification database successfully. Keep these
backups until operational sign-off. Do not delete or modify the original volume.

## API Management

Administrators can use **Administration → API Management** for Scalar API
documentation, live statistics, searchable activity/application logs, diagnostic
details, exports and monitoring configuration. Start at
`/settings/api-management`. Apply database migrations before running the updated
API. See the [backend implementation and deployment guide](backend/README.md#api-management-and-diagnostics-center)
for storage, redaction, tracing, configuration and verification details.

## Attribution

The interface is based on the open-source
[shadcn Dashboard + Landing Page Template](https://github.com/shadcnstore/shadcn-dashboard-landing-template)
by [ShadcnStore](https://shadcnstore.com), extended for ATLAS.
