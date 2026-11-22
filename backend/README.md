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
by default; it can be enabled through `Atlas__AllowSelfRegistration`.

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
