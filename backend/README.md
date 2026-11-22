# ATLAS backend

The backend owns sessions, passwords, permission resolution, organization
authorization, LDAP and all SQL Server operations. The frontend contains API
adapters and rendering code.

## Projects

- `Atlas.Api`: HTTP controllers, authentication handler, CSRF and exception handling.
- `Atlas.Application`: DTOs, repository contracts, authentication/workspace/LDAP services and access policies.
- `Atlas.Domain`: entities and persisted relationships.
- `Atlas.Infrastructure`: Dapper repositories, atomic writers, migration runner and cryptography/LDAP transport.
- `Atlas.DbTool`: migration, seed, repository/schema verification and source import CLI.
- `Atlas.Tests`: unit, SQL Server, HTTP/security and live LDAP integration tests.

## Local SQL Server

Install SQL Server 2022 Developer or Express with the Database Engine service.
Use SQL Server Configuration Manager to enable the instance and TCP/IP when
needed. For a local Windows workstation, Windows authentication avoids storing
SQL passwords. Create `ATLAS_V2` using SSMS or:

```powershell
sqlcmd -S 'localhost\SQL2022DEV' -E -b -Q 'CREATE DATABASE [ATLAS_V2];'
$env:ConnectionStrings__Atlas = 'Server=localhost\SQL2022DEV;Database=ATLAS_V2;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet restore backend/Atlas.slnx --disable-parallel -m:1
dotnet build backend/Atlas.slnx -m:1
dotnet run --no-build --project backend/Atlas.DbTool -- migrate
dotnet run --no-build --project backend/Atlas.DbTool -- verify-schema
```

Run commands from the repository root. The database tool creates schema inside
an existing database. Use an account with DDL permissions for migrations and
a separate least-privilege application identity in production. Do not use
`TrustServerCertificate=True` in production; install a trusted server certificate.

The API Development configuration and database CLI default to
`localhost\SQL2022DEV/ATLAS_V2` with integrated authentication. Production
requires `ConnectionStrings__Atlas` (or `ATLAS_DB_CONNECTION`).
Store SQL authentication credentials in environment variables or a secret
manager when integrated authentication cannot be used.

For local API secrets, .NET user secrets are supported by the API project:

```powershell
dotnet user-secrets set 'Ldap:EncryptionKey' '<your stable random secret>' --project backend/Atlas.Api
```

Replace the placeholder with a secret; never commit it. Environment variable
`LDAP_CONFIG_ENCRYPTION_KEY` is also supported.

## Migrations and seeding

Add numbered SQL files under `database/Migrations`, rebuild, then run
`migrate`. Applied scripts are recorded in `dbo.__AtlasMigrations`; each
script and its history insertion commit in one transaction. Run one migration
process at a time. Do not edit applied migrations or use `GO` batch separators.

The schema includes all 21 original entities, plus `user_setting` for profile,
notification and integration preferences. UUIDs use UNIQUEIDENTIFIER, booleans
use BIT, timestamps use UTC DATETIMEOFFSET, and dates use DATE. Enum strings
use CHECK constraints. JSON preferences use NVARCHAR(MAX) with ISJSON checking.
Task references retain their existing values, with a minimum four-digit width
for new values; references above 9999 are not truncated.

Development seed command:

```powershell
# Set ATLAS_DEMO_PASSWORD securely in this process first (at least 12 characters).
dotnet run --no-build --project backend/Atlas.DbTool -- seed
```

The password is never logged. Existing users are preserved. `seed --force`
is destructive to the selected development workspace and should only be used
on disposable data. Never use it against source data or a production destination.

## Start and HTTP behavior

```powershell
pnpm api:dev
```

The launch profile sets Development and port 5080. A built DLL launched directly
must use `backend/Atlas.Api` as its working directory so configuration loads.
For production use `dotnet publish backend/Atlas.Api -c Release`, configure the
hosting process securely, and serve the frontend over HTTPS. Expose the backend
only to the frontend proxy or a trusted internal network.

- `GET /health`: liveness.
- `GET /health/db`: Dapper connectivity check; failures return generic 503.
- `GET /openapi/v1.json`: development API contract.
- `GET /health/migrations`: development migration diagnostics.

Production cookies are Secure and HttpOnly, with SameSite=Lax. Sessions use
random opaque tokens; only SHA-256 token digests are stored for new sessions.
The session is validated against SQL Server for each authenticated request.
All unsafe API methods require the ASP.NET antiforgery cookie plus the
`X-ATLAS-CSRF` request header. Login and LDAP login have per-IP process-local
rate limits. No CORS access is enabled; browsers use the same-origin Next proxy.

API responses use Cache-Control: no-store. Password-confirmed self-account
deletion revokes sessions and transfers projects within one transaction; it
rejects deletion of the last active administrator or accounts with no eligible
project successor. Directory-only accounts use administrator-assisted removal.

Persist and protect ASP.NET Data Protection keys. Windows local development uses
the user's DPAPI-protected key repository. Multiple production API instances
need a shared, protected key ring so antiforgery tokens work across instances.
Configure network/TLS termination, trusted proxy topology, monitoring, backup
and restore policies for the deployment environment.

## Authorization

`AccessGuard` centrally enforces the original 21 permission names and exact
Lab/Section matches. Legacy member-role fallback is preserved when no explicit
role assignment exists. Role names containing 'admin' retain the original
administrator organization bypass. Explicit permission checks still use the
resolved permission set.

Scope checks apply to both existing records and requested destinations. Missing
organization assignments do not grant unscoped access. Nonadministrators see
only their own organization in the workspace. Administrative changes use
serializable transactions and audit records; system role names/deletion,
self-role changes and last active administrator removal/demotion are protected.

Task creation locks its parent and calculates ordering inside the insertion
transaction; concurrent creations receive distinct positions. LDAP configuration,
mapping replacements and connection-test status commit with their audit entries.

Passwords retain Better Auth scrypt compatibility (NFKC, N=16384, r=16, p=1,
64-byte output). New identity IDs use random UUIDs. Registration remains disabled
in production by default; local Development configuration enables it. Set
`Atlas__AllowSelfRegistration=true` to enable it in another environment.
The `/sign-up` page uses the existing registration endpoint and signs new users
in with the Users role. Email verification delivery is not configured.

## LDAP

LDAP transport uses Microsoft's
[LdapConnection API](https://learn.microsoft.com/en-us/dotnet/api/system.directoryservices.protocols.ldapconnection?view=net-10.0).
LDAPS and StartTLS support service-account lookup followed by a separate user
bind. Filters are escaped and attribute names validated. TLS custom-CA validation
checks both certificate trust and hostname. RequireSecure blocks plain LDAP.
Keep ValidateTls enabled outside intentional isolated test environments.

Bind credentials and new CA certificates are encrypted with AES-256-GCM. The
envelope is the existing base64 `iv.tag.ciphertext` format; SHA-256 derives the
key from the configured secret. No built-in encryption key is used. Existing
plaintext CA values are accepted only for migration compatibility and are
encrypted when saved again. API responses return only a configured marker.

Preserve the old encryption key material when migrating existing ciphertext.
The legacy `BETTER_AUTH_SECRET` fallback is supported only when explicitly
configured for existing values. For rotation, configure the new current key and
put old materials in `Ldap__PreviousEncryptionKeys`, separated by semicolons.
Re-enter/re-save bind credentials and CA material with the new key, verify
directory access, and then remove previous keys.

Enabled group mappings select an ATLAS role after directory authentication.
Legacy Member maps to Users and Administrator maps to Administrators. Identity
linking/account provisioning is transactional. Auto-provisioning/approval
settings are enforced, and LDAP accounts require a Lab and Section assignment
before a session is issued. Configuration, connection tests, mapping changes
and authentication events are audited without secrets.

## Automated verification

Create `ATLAS_MigrationVerification`, apply migrations and seed it with a
test-only password:

```powershell
$env:ConnectionStrings__Atlas = 'Server=localhost\SQL2022DEV;Database=ATLAS_MigrationVerification;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project backend/Atlas.DbTool -- migrate
# Supply ATLAS_DEMO_PASSWORD before seeding this dedicated test database.
dotnet run --project backend/Atlas.DbTool -- seed
dotnet run --project backend/Atlas.DbTool -- verify-schema
dotnet run --project backend/Atlas.DbTool -- verify
pnpm test:backend
```

Tests use `ATLAS_TEST_CONNECTION`, defaulting to the same verification instance.
They reject a database whose name does not contain Verification. Repository
verification exercises every entity group, transaction rollback, pagination,
filtering, scope isolation, injection attempts and task-reference boundaries.

`scripts/test-backend.ps1` starts the disposable localhost LDAP fixture and
runs all .NET tests. The fixture uses the pinned legacy osixia/openldap image
only for testing, never as a production directory recommendation. Its bundled
CA is expired; the script generates a current localhost test certificate and
uses it through explicit custom trust without changing machine trust stores.
Ports 1389/1636 and the `atlas-ldap-verification` container are test-only.
For infrastructure-independent unit tests, use
`dotnet test --filter 'Category!=LdapIntegration'`; the remaining HTTP/repository
tests still require SQL Server.

The original application's external OAuth/synchronization and sample API-key
widgets were placeholders. Their configuration UI remains; this backend migration
does not implement external provider integrations or make sample keys usable as
authentication credentials. Production directory-specific Active Directory
configuration must be checked in its actual deployment environment.

## API Management and Diagnostics Center

Administrators with `admin.access` can open **Administration → API Management**.
The primary dashboard is `/settings/api-management`; its documentation,
activity, diagnostics and configuration pages append `/documentation`,
`/activity`, `/diagnostics` and `/configuration`. Updating monitoring settings
also requires `admin.settings`. These checks run in the API as well as the UI.
The existing session, role resolver, CSRF protection and dashboard shell are reused.

### Installation and documentation

Run `pnpm install --frozen-lockfile`, restore/build the backend, and run
`pnpm db:migrate` before starting the updated API. Migration
`005_api_observability.sql` adds `api_diagnostic_record` and
`api_monitoring_config`; it does not alter application identity or business data.
The diagnostic table indexes stream/timestamp, request ID, correlation ID,
user ID, endpoint and HTTP status. The configuration table has one JSON row.

Scalar is bundled through the official `@scalar/api-reference-react` package.
Installed versions: Scalar React `0.9.95`; OpenTelemetry hosting, ASP.NET,
HttpClient, SQL Client and OTLP exporter packages `1.21.0`. Package lockfiles
record the complete resolved dependency graph.
It uses the existing ASP.NET OpenAPI generator rather than a copied endpoint
list. Controller signatures, DTOs and response metadata supply schemas; schema
and operation transformers add authentication, common errors, required identity
fields and illustrative request/error response examples. Legacy anonymous
responses have explicit response metadata. Dynamic `JsonElement` settings and
patch payloads retain generic JSON schemas. Add typed DTOs or
`ProducesResponseType` metadata when extending endpoints; do not maintain a
separate specification.

The protected browser URLs are:

- `/api/atlas/admin/api-management/openapi/v1.json`
- `/api/atlas/admin/api-management/openapi/v1.yaml`

Their direct backend equivalents replace `/api/atlas` with `/api`. The legacy
`/openapi/v1.json` remains development-only and now also requires an administrator.
Downloads and documentation reads are audited. Scalar sends cookies only to
the same-origin application proxy, obtains a fresh CSRF token for unsafe
requests, and rejects external request targets and redirects. Authentication
persistence, telemetry, external fonts and developer sharing tools are disabled.
Executing an endpoint retains all existing backend permission and scope checks.
The API type-generation CLI now needs an authorized session when reading OpenAPI.

### Collection, storage and security

`ApiActivityMiddleware` surrounds exception handling, authentication,
authorization, rate limiting and controllers. It captures actual status codes,
duration, response byte counts, safe header metadata, browser family, immediate
peer IP, user ID, route template and correlation information. Unknown paths are
stored as `/[unmatched]`; route parameter values are omitted. Forwarded client
IP headers are not trusted automatically.

Each request receives a new UUID request ID and a UUID correlation ID, returned
in `X-Request-ID` and `X-Correlation-ID`. Inbound correlation IDs are replaced
unless the immediate peer is explicitly trusted and the supplied value is a valid
nonempty UUID. The IDs flow through `HttpContext`, logging scopes and Activity
tags. Existing inbound W3C trace context is identified separately from trust in
the correlation header. Outbound named HttpClient `atlas` forwards the correlation
ID only to explicitly allowlisted hosts.

Redaction occurs before enqueueing or persistence. Request/response bodies are
off by default and limited to workspace/project/task routes. Authentication,
password, LDAP, settings and diagnostics bodies are never collected. Even when
enabled, arbitrary strings and nontechnical primitive values are redacted;
only named technical numeric/boolean fields survive. Sensitive field names have
an immutable built-in denylist plus administrator additions. Header capture is
limited to normalized content type and validated content length. Query values
are always redacted and unknown query keys are replaced. JSON depth, collection
lengths and captured bytes are bounded. Cookies, credentials, arbitrary headers,
raw SQL and exception messages are not persisted.

`MonitoringRuntime` uses a bounded 4,096-record channel and batched background
SQL writes. Request completion does not wait for database ingestion. Overflow
and write failures increment visible ingestion counters; they never fail normal
requests. Shutdown attempts a five-second drain. This is best-effort diagnostic
storage, not a durable compliance audit queue. A process crash or outage can
lose queued records. Review dropped/failure counters when sizing the deployment.

The existing `ILogger` pipeline remains active. A provider persists a logically
separate `application` stream in the same diagnostic table, with severity,
component, event ID, safe known event descriptions and optional exception type.
Interpolated arguments and unknown message text are omitted. API activity and
application events are independently searchable; request details load related
application events and spans using the correlation ID. There is no business
background-job infrastructure in the existing application to instrument.

### Statistics, searches and retention

All dashboard counts, success/error rates, averages, percentile calculations,
time buckets, endpoint rankings, method groups and status groups come from SQL
queries over collected activity records. No example dashboard data is seeded.
Reporting windows cover 15 minutes through 30 days or a custom range up to
31 days. Recent activity appears below the overview; charts follow the table.
The UI refreshes every 15 seconds and cancels obsolete filter requests.

`/api/admin/api-management` exposes activity search/detail, statistics,
ingestion health, configuration and export endpoints. Filtering and pagination
run on the server; sort columns are allowlisted and query values parameterized.
Request/correlation IDs, user, method, endpoint, status, stream, date range,
success/failure, slow requests and authentication/authorization failures are
searchable. A request detail includes safe request/response metadata, redacted
optional bodies, exception type, method-only stack frames and related trace spans.

JSON and CSV exports reuse the selected filters and sort order. Each export is
capped at 1,000 rows and rate-limited to five per minute per user/IP partition.
CSV fields are quoted and formula-escaped. Exports, sensitive record reads,
searches and configuration changes use the existing RBAC audit table;
configuration persistence and its audit insertion share a transaction.
Retention defaults to 30 days. The worker purges expired diagnostic rows in
bounded 2,000-row batches (up to 16 batches every 15 seconds). RBAC security audits retain their existing
retention behavior. Disabling activity collection does not delete history.

### Tracing and deployment configuration

OpenTelemetry packages instrument ASP.NET incoming requests, HttpClient calls
and SQL Server commands. `Atlas.Application` adds internal workspace operation
spans. Sampled span durations and parent/span IDs are available in the trace
stream. An allowlist processor strips sensitive tags and baggage before storage
and OTLP export. Exception event recording is disabled. Future custom spans must
use static operation names and never attach credentials, SQL text, user payloads
or exception messages/events.

Tracing is disabled by default and has a separate dynamic enable switch and
sampling ratio in API Configuration. No collector is needed for local span
storage. To export traces, set `OTEL_EXPORTER_OTLP_ENDPOINT` on the API host
and restart it; standard OpenTelemetry OTLP protocol/header environment settings
apply. Collector availability and authentication are deployment responsibilities.
Export transport is disabled when no collector endpoint is configured.

Optional API host settings:

- `Observability:TrustedCorrelationPeers`: array of trusted immediate IP peers.
  A trusted proxy must strip or replace untrusted incoming correlation headers.
- `Observability:CorrelationHosts`: array of outbound hostnames allowed to receive
  correlation headers through the named `atlas` HttpClient.
- Indexed environment forms use `Observability__TrustedCorrelationPeers__0` and
  `Observability__CorrelationHosts__0` (and subsequent indexes).

All other logging/capture/retention/sampling settings are persisted through API
Configuration. Defaults require no new secrets or environment variables.
No collector or LDAP test service was provisioned by this feature.

### Implementation and verification

New backend files: `Atlas.Application/Observability/*`,
`Atlas.Api/Observability/*`, `Atlas.Api/Controllers/MonitoringController.cs`,
`Atlas.Infrastructure/Repositories/MonitoringStore.cs`, the migration above,
and `Atlas.Tests/MonitoringTests.cs`.
Modified backend integration points: API `Program.cs`, project/lock files,
controller response metadata, the exception handler, workspace
service spans and infrastructure registration.
New frontend files: `src/lib/api/monitoring.ts`, the
`src/app/(dashboard)/settings/api-management` route tree and components,
and `tests/ui/api-management.spec.ts`.
Modified frontend integration points: sidebar navigation, global CSS layer order,
API client cancellation support, package manifest and pnpm lockfile.

Verification commands:

```powershell
pnpm build
pnpm typecheck
dotnet build backend/Atlas.slnx -m:1
dotnet vstest backend/Atlas.Tests/bin/Debug/net10.0/Atlas.Tests.dll --TestCaseFilter:'Category!=LdapIntegration'
$env:ATLAS_UI_TEST_PRODUCTION = 'true'
pnpm exec playwright test tests/ui/api-management.spec.ts tests/ui/registration.spec.ts tests/ui/smoke.spec.ts --workers=2
```

Monitoring integration tests use the isolated Verification database, check
administrator restrictions, JSON/YAML documentation, real SQL metrics and P95,
filtering/pagination/export, configuration auditing, redaction, sampled spans,
authentication/authorization/validation/404/rate-limit recording and storage
failure isolation. Browser tests exercise the real dashboard, request details,
exports, settings/mobile layout, Scalar GET and CSRF-protected POST execution,
and existing login/registration flows. Live LDAP tests require the separate
fixture described above.

Validation on the local implementation: frontend production build and targeted
ESLint checks passed; all 25 backend tests outside `LdapIntegration` passed;
all 12 production-browser API Management, registration and smoke tests passed.
No browser JavaScript errors occurred in the API Management checks. Live LDAP
fixture tests and delivery to an external OTLP collector were not run.
