# ATLAS v2.0 Migration Progress

## Project Information

- **Project:** ATLAS - ADFS Task Lifecycle and Assignment System
- **Original Architecture:** Next.js 16 + React 19 + PostgreSQL + Drizzle ORM + Better Auth
- **Current Architecture:** Next.js 16 + React 19 + ASP.NET Core + Dapper + SQL Server
- **Target Architecture:** Next.js 16 + React 19 + ASP.NET Core Web API + Dapper + Microsoft SQL Server
- **Migration Type:** Database and backend architecture migration
- **Status:** Completed
- **Current Stage:** Stage 8 — completed; all eight stages locally verified
- **Completed:** 2026-11-22
- **Application Database:** ATLAS_V2 on localhost\SQL2022DEV
- **Started:** 2026-11-21

## Overall Progress

| Stage | Description | Status |
| --- | --- | --- |
| 1 | .NET backend and SQL Server connection | Completed |
| 2 | Database schema and migrations | Completed |
| 3 | Dapper repositories | Completed |
| 4 | Authentication and sessions | Completed |
| 5 | RBAC and Lab/Section security | Completed |
| 6 | LDAP migration | Completed |
| 7 | Next.js API integration | Completed |
| 8 | Testing and legacy removal | Completed |

---

## Stage 1: .NET Backend and SQL Server Connection

**Status:** Completed

### Tasks

- [x] Inspect existing application architecture.
- [x] Create ASP.NET Core Web API project.
- [x] Configure C# project structure.
- [x] Install Dapper.
- [x] Install Microsoft.Data.SqlClient.
- [x] Configure dependency injection.
- [x] Implement database connection factory.
- [x] Configure SQL Server connection strings.
- [x] Connect successfully to local SQL Server.
- [x] Verify connection with a database health check.
- [x] Build and validate backend.

### Acceptance Criteria

- [x] ASP.NET Core backend starts successfully.
- [x] Backend connects to local SQL Server.
- [x] Connection configuration uses environment variables or secure configuration.
- [x] No hardcoded database credentials.
- [x] Existing frontend continues to build. (`pnpm build` verified in the revalidation session.)

### Verification

- `dotnet build backend/Atlas.slnx` -> succeeded, 0 errors.
- `dotnet run --project Atlas.Api --urls http://localhost:5080` -> listening.
- `GET /health` -> `{"status":"ok","service":"atlas-api"}`.
- `GET /health/db` -> `{"status":"ok","database":"ATLAS","server":"localhost\\SQL2022DEV"}`.
- Database `ATLAS` created on `localhost\SQL2022DEV` (Windows integrated authentication).

### Environment Verified

- .NET SDK 11.0.100 (targeting `net10.0`), ASP.NET Core runtime 10.0.15 / 11.0.0.
- SQL Server 2022 Developer (`localhost\SQL2022DEV`) and Express (`localhost\SQLEXPRESS`) are running.
- `sqlcmd` 16.0 available; Windows integrated authentication verified against `localhost\SQL2022DEV`.
- NuGet.org reachable.
- Node v25.2.1, pnpm 10.33.0.
- Local PostgreSQL Docker container is **not currently running**; required later for the data migration utility.

### Notes

- Existing codebase inspected: schema (`src/db/schema/*`), queries (`src/db/queries/flowboard.ts`),
  server actions (`src/db/actions/*`), auth (`src/lib/auth.ts`), RBAC (`src/lib/rbac.ts`),
  org scoping (`src/lib/org.ts`), LDAP (`src/lib/ldap.ts`, `src/app/api/auth/ldap/route.ts`),
  seed scripts, and Playwright smoke test.
- Better Auth password format confirmed from `@better-auth/utils`: scrypt with
  `N=16384, r=16, p=1, dkLen=64`, salt = ASCII bytes of a 32-character hex string,
  stored as `hexSalt:hexKey`, password normalized to NFKC first.
- Better Auth session cookie name is `better-auth.session_token`. The .NET backend will
  issue its own cookie (`atlas.session_token`) with a deliberately designed session model,
  preserving password verification so existing users can sign in.

---

## Stage 2: Database Schema and Migrations

**Status:** Completed

### Tasks

- [x] Inventory all existing Drizzle schema definitions.
- [x] Map PostgreSQL data types to SQL Server.
- [x] Create SQL Server database schema.
- [x] Implement primary keys and foreign keys.
- [x] Implement indexes and constraints.
- [x] Configure default values.
- [x] Implement SQL Server migration tooling.
- [x] Create initial migration.
- [x] Implement migration history tracking.
- [x] Implement development seed scripts.
- [x] Validate schema creation.

### Acceptance Criteria

- [x] All existing database entities are represented.
- [x] Relationships and constraints are preserved for the migrated source dataset; full transactional import passed with all existing constraints enforced.
- [x] SQL Server migrations execute successfully.
- [x] Seed scripts execute successfully.

### Verification

- `dotnet run --project Atlas.DbTool -- migrate` -> applied `001_initial_schema.sql`, `002_reference_data.sql`.
- `dotnet run --project Atlas.DbTool -- status` -> both `[applied]`.
- `dotnet run --project Atlas.DbTool -- seed` -> users=8, members=8, projects=6, tasks=36, accounts=8.
- SQL verification: 8 users, 8 members, 6 projects, 36 tasks, 21 permissions, 3 roles; task references `TSK-0001...`.
- Migration history table `dbo.__AtlasMigrations` in place.
- Reference roles match drizzle/0003: Users, Supervisors, Administrators with the same permission sets.

---

## Stage 3: Dapper Repositories

**Status:** Completed

### Tasks

- [x] Implement user repository.
- [x] Implement session repository.
- [x] Implement account repository.
- [x] Implement project repository.
- [x] Implement task repository.
- [x] Implement member repository.
- [x] Implement project membership repository.
- [x] Implement Lab and Section repositories.
- [x] Implement organization assignment repository.
- [x] Implement role repository.
- [x] Implement permission repository.
- [x] Implement user-role repository.
- [x] Implement role-permission repository.
- [x] Implement RBAC audit repository.
- [x] Implement organization audit repository.
- [x] Implement LDAP repositories.
- [x] Validate CRUD operations.
- [x] Validate transactions.
- [x] Validate pagination and filtering.

### Acceptance Criteria

- [x] All required repositories are implemented.
- [x] Queries use parameterized SQL.
- [x] Asynchronous database operations are used.
- [x] Transactions protect multi-step operations.
- [x] Repository tests pass.

---

## Stage 4: Authentication and Sessions

**Status:** Completed

### Tasks

- [x] Audit existing Better Auth implementation.
- [x] Design .NET authentication architecture.
- [x] Implement secure password verification.
- [x] Implement login.
- [x] Implement logout.
- [x] Implement session creation.
- [x] Implement session validation.
- [x] Implement session expiration.
- [x] Implement session revocation.
- [x] Implement password changes.
- [x] Preserve user and account relationships.
- [x] Implement secure HttpOnly cookies.
- [x] Implement CSRF protections.
- [x] Test authentication flows.

### Acceptance Criteria

- [x] Existing users can authenticate using the migrated authentication system.
- [x] Session lifecycle works correctly.
- [x] Passwords and secrets are handled securely.
- [x] Authentication is enforced by the backend.

---

## Stage 5: RBAC and Lab/Section Security

**Status:** Completed

### Tasks

- [x] Inventory all 21 existing permission keys.
- [x] Preserve permission names and semantics.
- [x] Implement role resolution.
- [x] Implement permission resolution.
- [x] Implement legacy role fallback.
- [x] Implement administrator privileges.
- [x] Implement Lab scoping.
- [x] Implement Section scoping.
- [x] Implement organization assignment checks.
- [x] Implement requireOrgScope equivalent.
- [x] Implement centralized authorization policies.
- [x] Implement authorization denial auditing.
- [x] Test cross-Lab access restrictions.
- [x] Test cross-Section access restrictions.
- [x] Test administrator bypass rules.

### Acceptance Criteria

- [x] All 21 permissions behave as before.
- [x] Backend enforces authorization.
- [x] Cross-organization access is prevented.
- [x] Administrator exceptions preserve existing behavior.
- [x] Audit records are generated correctly.

---

## Stage 6: LDAP Migration

**Status:** Completed

### Tasks

- [x] Inspect existing ldapts integration.
- [x] Select appropriate .NET LDAP library.
- [x] Implement LDAP configuration management.
- [x] Implement LDAP connection testing.
- [x] Implement LDAP authentication.
- [x] Implement LDAP identity management.
- [x] Implement group mappings.
- [x] Preserve AES-256-GCM encryption.
- [x] Preserve CA certificate protection.
- [x] Enforce secure LDAP requirements.
- [x] Implement LDAP audit logging.
- [x] Validate LDAP functionality.

### Acceptance Criteria

- [x] LDAP functionality remains available.
- [x] Sensitive credentials are encrypted at rest.
- [x] Secrets are never exposed to the frontend.
- [x] Secure LDAP requirements are enforced.

---

## Stage 7: Next.js API Integration

**Status:** Completed

### Tasks

- [x] Create centralized TypeScript API client.
- [x] Configure backend API URL.
- [x] Implement authentication integration.
- [x] Migrate dashboard data loading.
- [x] Migrate project functionality.
- [x] Migrate Kanban functionality.
- [x] Migrate task functionality.
- [x] Migrate calendar functionality.
- [x] Migrate team management.
- [x] Migrate profile and settings.
- [x] Migrate RBAC administration.
- [x] Migrate LDAP administration.
- [x] Migrate audit log interfaces.
- [x] Replace database-dependent Server Actions.
- [x] Validate frontend workflows.

### Acceptance Criteria

- [x] Frontend communicates with the .NET backend.
- [x] Existing pages and workflows remain functional.
- [x] Type safety is maintained.
- [x] Browser cannot access SQL Server directly.
- [x] Backend authorization protects every relevant operation.

---

## Stage 8: Testing and Legacy Removal

**Status:** Completed

### Tasks

- [x] Run backend build.
- [x] Run frontend build.
- [x] Run TypeScript checks.
- [x] Run lint.
- [x] Run backend unit tests.
- [x] Run SQL Server integration tests.
- [x] Run Playwright UI tests.
- [x] Test authentication.
- [x] Test RBAC.
- [x] Test Lab/Section security.
- [x] Test LDAP.
- [x] Test all Kanban workflows.
- [x] Validate audit logs.
- [x] Validate data integrity.
- [x] Remove Drizzle dependencies.
- [x] Remove PostgreSQL drivers.
- [x] Remove obsolete PostgreSQL scripts.
- [x] Remove PostgreSQL Docker configuration.
- [x] Search for remaining PostgreSQL and Drizzle references.
- [x] Update documentation.
- [x] Complete final migration review.

### Acceptance Criteria

- [x] All critical application workflows pass.
- [x] No active application database access uses Drizzle.
- [x] No active application database access uses PostgreSQL.
- [x] SQL Server is the only application database.
- [x] Dapper is the active database access layer.
- [x] Existing functionality and security are preserved.

---

## Architecture Decisions

| # | Decision | Rationale |
| --- | --- | --- |
| AD-1 | Backend split into `Atlas.Api`, `Atlas.Application`, `Atlas.Infrastructure`, `Atlas.Domain` | Requested separation of concerns; keeps controllers thin and SQL isolated. |
| AD-2 | Custom embedded-SQL migration runner with `__AtlasMigrations` history table | No external migration dependency; deterministic ordering and transaction per script. |
| AD-3 | SQL Server enums represented as `NVARCHAR` + `CHECK` constraints | Preserves exact PostgreSQL enum string values without extra joins. |
| AD-4 | Session cookie `atlas.session_token`, opaque token delivered by HttpOnly cookie and SHA-256 digest stored in `session` | Deliberate replacement for Better Auth sessions; avoids trusting Better Auth cookie signing. |
| AD-5 | Better Auth scrypt password verification implemented in .NET (BouncyCastle) | Existing users keep working without a password reset. |
| AD-6 | LDAP secrets keep the existing AES-256-GCM `iv.tag.ciphertext` base64 format | Existing encrypted values remain readable; key stays in `LDAP_CONFIG_ENCRYPTION_KEY`. |
| AD-7 | Frontend talks to backend through a Next.js rewrite (`/api/atlas/*` → .NET `/api/*`) | Same-origin cookies, no CORS/cookie-domain complexity for the browser. |

---

## Files Created or Modified

### Stage 1

- Existing backend projects: `backend/Atlas.Api`, `backend/Atlas.Application`, `backend/Atlas.Domain`, `backend/Atlas.Infrastructure`, `backend/Atlas.DbTool`, `backend/Atlas.slnx`, `backend/Directory.Build.props`.
- Revalidation fixes: `.gitignore`, `backend/Atlas.Application/Atlas.Application.csproj`, `backend/Atlas.Api/Program.cs`, `backend/Atlas.Infrastructure/Data/SqlServerConnectionFactory.cs`.

### Stage 2

- Existing schema and reference scripts: `backend/database/Migrations/001_initial_schema.sql`, `002_reference_data.sql`.
- Existing migrator/seeder: `backend/Atlas.Infrastructure/Data/Migrations/DatabaseMigrator.cs`, `backend/Atlas.Infrastructure/Data/DatabaseSeeder.cs`.
- Added `backend/database/Verification/schema.sql` and `verify-schema` command in `backend/Atlas.DbTool/Program.cs`; embedded verifier through `backend/Atlas.Infrastructure/Atlas.Infrastructure.csproj`.
- Added `backend/README.md` with local configuration, migration, seed, startup, verification and preservation instructions.


### Stage 3

- `backend/Atlas.Domain/Entities/*`, `backend/Atlas.Application/Interfaces/Repositories/*`.
- `backend/Atlas.Infrastructure/Repositories/{Auth,Work,Org,Rbac,Ldap}Repositories.cs`, `SearchRepository.cs`.
- `backend/Atlas.Application/Interfaces/Repositories/ISearchRepository.cs`, infrastructure dependency injection, DbTool repository verification.

### Stage 4

- `backend/Atlas.Api/Authentication/AtlasSessionHandler.cs`, `Controllers/AuthController.cs`, `Middleware/ApiExceptionHandler.cs`, API startup.
- `backend/Atlas.Application/Services/AuthService.cs`, `Interfaces/IIdentityWriter.cs`, authentication DTOs/options/DI.
- `backend/Atlas.Infrastructure/Repositories/IdentityWriter.cs`, `Security/BetterAuthPasswordHasher.cs`.
- `backend/Atlas.Tests/AuthTests.cs` and test project/solution references.

### Stage 5

- `backend/Atlas.Application/Security/{AtlasPermissions,AccessGuard}.cs`, workspace service and writer interfaces.
- `backend/Atlas.Infrastructure/Repositories/{WorkspaceWriter,AdminWriter}.cs`.
- `backend/Atlas.Api/Controllers/{WorkspaceController,AdminController}.cs`.
- `backend/Atlas.Tests/{SecurityTests,AdminWorkflowTests,ProductionSecurityTests}.cs`.

### Stage 6

- `backend/Atlas.Application/Interfaces/ILdapServices.cs`, `Services/LdapService.cs`.
- `backend/Atlas.Infrastructure/Security/{LdapDirectory,LdapSecretProtector}.cs`, `Repositories/LdapUserWriter.cs`.
- `backend/Atlas.Api/Controllers/LdapController.cs`, LDAP repository interfaces/implementations and DI.
- `backend/Atlas.Tests/{LdapTests,LdapWorkflowTests}.cs`, `scripts/test-backend.ps1`, `tests/ldap-user.ldif`.

### Stage 7

- `src/lib/api/{client,server,contracts,generated,workspace,projects,tasks,members,org,rbac,ldap}.ts`.
- `src/lib/{auth,auth-client,auth-route-proxy,org,rbac}.ts`, `src/proxy.ts`, legacy auth-route forwarding adapters.
- `next.config.ts`, `src/data/flowboard-store.tsx`, dashboard layout/shell, project page/board and form imports.
- Settings pages (profile/security/notifications/connections/LDAP/labs/sections/roles/audit) and team administration components.
- `backend/database/Migrations/003_user_settings.sql`, settings repository/interface/controller.
- `tests/ui/migration.spec.ts`, existing smoke tests, `playwright.config.ts`.

### Stage 8

- `backend/database/Migrations/004_task_reference_width.sql`, final repository/concurrency/security fixes.
- `backend/Atlas.DbTool/SourceDataImporter.cs`; `tools/atlas-data-migrator/{migrate-data.mjs,package.json,pnpm-lock.yaml,README.md}`.
- Root `package.json`/`pnpm-lock.yaml`, `.env.example`, `.gitignore`, `eslint.config.mjs`, `README.md`, `progress.md`.
- Backend guide/configuration/launch profile, `Directory.Build.props` and all six project dependency lockfiles.
- Removed legacy `src/db`, `src/lib/ldap.ts`, `drizzle/`, `drizzle.config.ts`, PostgreSQL-only Compose and seed/deployment scripts.
- Local ignored recovery artifacts: PostgreSQL dump, original source configuration/archive, source-copy volume marker and diagnostics. Actual secrets are excluded from version control.

---

## Test Results

| Date | Command | Result |
| --- | --- | --- |
| 2026-11-21 | `dotnet --info` | PASS - SDK 11.0.100 |
| 2026-11-21 | `sqlcmd -S localhost\SQL2022DEV -E -Q "SELECT @@VERSION"` | PASS - SQL Server 2022 Developer |
| 2026-11-21 | NuGet connectivity check | PASS - HTTP 200 |
| 2026-11-21 | Restored backend solution build, single worker | PASS - 0 warnings, 0 errors |
| 2026-11-21 | `pnpm build` | PASS after retry outside process-restricted sandbox |
| 2026-11-21 | Rebuilt API `/health` and `/health/db` | PASS - both HTTP 200 |
| 2026-11-21 | `Atlas.DbTool migrate/status` on fresh `ATLAS_MigrationVerification` | PASS - both migrations applied and recorded |
| 2026-11-21 | `Atlas.DbTool seed` on fresh verification database | PASS - 8 users/accounts/members, 6 projects, 36 tasks |
| 2026-11-21 | `Atlas.DbTool verify-schema` | PASS - entity presence, trusted constraints, permission counts, selected orphan checks |
| 2026-11-21 | Repeat migration and seed on verification database | PASS - no pending migrations; existing users preserved |
| 2026-11-21 | Read-only source PostgreSQL connectivity probe | FAIL - `ECONNREFUSED`, localhost:5433 |
| 2026-11-21 | Docker container inventory | Source PostgreSQL container absent; source named volume exists |
| 2026-11-21 | `pnpm typecheck` | PASS |
| 2026-11-21 | `pnpm lint` after generated-output exclusion | FAIL - 6 baseline frontend errors, 29 warnings |
| 2026-11-21 | Seed without `ATLAS_DEMO_PASSWORD` | PASS - command rejected missing password with exit code 1 |
| 2026-11-21 | Seed verification database with generated password, schema check, repeated seed | PASS - fresh demo records, schema verification, repeated seed preserved data; no password logged |
| 2026-11-21 | Final backend build after seed changes | PASS - 0 warnings, 0 errors |
| 2026-11-21 | `git diff --check` | PASS |


| 2026-11-22 | Full .NET solution build | PASS — zero warnings/errors |
| 2026-11-22 | NuGet locked-mode restore | PASS — dependency locks for all six projects |
| 2026-11-22 | Backend unit/HTTP/SQL Server/live LDAP suite | PASS — 20 tests, 0 failures, 0 skipped |
| 2026-11-22 | Dapper repository verification | PASS — all entity groups, rollback, scope/search/pagination/injection, parallel task positions, reference 10000 |
| 2026-11-22 | Fresh full-precision source import and exact value verification | PASS — all 21 source tables, original IDs/relationships and PostgreSQL microseconds preserved |
| 2026-11-22 | Active ATLAS_V2 full-precision value verification | PASS — original values intact, prior sessions intentionally expired |
| 2026-11-22 | PostgreSQL custom-format dump restore | PASS — separate recovery database contains 8 users and 37 tasks |
| 2026-11-22 | Populated-destination import rejection | PASS — no overwrite |
| 2026-11-22 | Final production frontend build | PASS |
| 2026-11-22 | TypeScript check | PASS |
| 2026-11-22 | ESLint | PASS — 0 errors, 23 existing unused-code warnings |
| 2026-11-22 | Production frontend browser suite, PostgreSQL stopped | PASS — 7 tests including invalid-cookie redirect and actual Kanban drag persistence |
| 2026-11-22 | Active application legacy-reference scan | PASS — no Drizzle/PostgreSQL/Better Auth/ldapts imports/dependencies or Server Action directives |
| 2026-11-22 | Whitespace review | PASS — git diff --check |

---

## Blockers and Outstanding Issues

| # | Issue | Impact | Status |
| --- | --- | --- | --- |
| B-1 | Source PostgreSQL container originally unavailable | Resolved using an isolated read-only-volume copy; original volume preserved | Completed |
| B-2 | No original LDAP test directory available | Resolved with a real disposable LDAPS/StartTLS fixture plus identity/mapping tests | Completed |
| B-3 | Source width/collation/precision compatibility | Transactional import and raw source-value comparison passed; incompatible future imports roll back | Completed |
| B-4 | Existing frontend lint errors | Errors resolved; 23 unused-code warnings remain non-blocking | Completed |

---

## Migration Notes

### Revalidation session — 2026-11-21

- Current stage: **Stage 1**, re-establishing acceptance before proceeding. Earlier verification entries are retained as historical reports; they do not describe the current checkout's verified state.
- Found existing untracked backend implementation, including repository and authentication drafts. No later stage is considered validated by the presence of these files.
- Backend build initially failed on sandbox compiler named-pipe access. Elevated single-worker build exposed stale restore assets and then a missing configuration/options reference in Atlas.Application. Added Microsoft.Extensions.Options.ConfigurationExtensions to support its existing registration code.
- Frontend dependencies pointed through broken junctions into another checkout. Offline install failed (missing tarball); restored dependencies successfully with `pnpm install --frozen-lockfile`, preserving the lockfile.
- `dotnet restore backend/Atlas.slnx --disable-parallel -m:1` passed. Initial parallel restore exited unsuccessfully without diagnostics.
- Initial sandbox SQL Server probe failed with SSPI/client encryption errors; connection still requires revalidation.
- Added .NET bin/obj and local diagnostic exclusions to `.gitignore`.
- Files changed in this session: `.gitignore`, `backend/Atlas.Application/Atlas.Application.csproj`, `progress.md`.
- Frontend build, API startup, SQL connectivity, migrations and seeds must pass before Stage 3 work continues.

### Stage 1 acceptance re-established — 2026-11-21

- `dotnet build backend/Atlas.slnx --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal`: PASS, zero warnings/errors (elevated execution required for compiler IPC).
- `pnpm build`: PASS, all routes compiled and TypeScript passed. Sandbox attempt compiled then failed to spawn TypeScript (`EPERM`); elevated retry passed.
- Started the rebuilt API from `backend/Atlas.Api`: `/health` and `/health/db` both returned HTTP 200. SQL Server connection uses Windows integrated authentication, without database passwords.
- Initial direct DLL startup from the repository root did not load project configuration and returned 500 for database health; corrected working directory and verified success.
- Database health no longer returns SQL instance details or exception messages. OpenAPI/migration diagnostics are development-only; structured JSON logging enabled.
- Connection factory disposes connections when opening fails.
- Additional Stage 1 files modified: `backend/Atlas.Api/Program.cs`, `backend/Atlas.Infrastructure/Data/SqlServerConnectionFactory.cs`.
- Stage 1 complete; current stage is **Stage 2** schema/migration/seed verification. Historical Stage 2 completion remains unverified until the fresh-database checks pass.

### Stage 2 fresh-database validation — 2026-11-21

- Created isolated local SQL Server database `ATLAS_MigrationVerification`; existing ATLAS and original PostgreSQL data were not changed.
- Fresh migration, history, seed and idempotency checks all passed. Added a repeatable Dapper-driven SQL schema verifier.
- Read source Drizzle schema definitions and metadata. All 21 entity groups are represented, but unbounded source text maps to bounded target columns and default SQL Server collation may change uniqueness behavior. These differences prevent declaring constraint/data compatibility verified.
- Source `localhost:5433/flowboard` is unavailable (`ECONNREFUSED`). Docker inspection shows no PostgreSQL container, but `flowboard_flowboard-postgres-data` exists. The unrelated host PostgreSQL service is not the configured source.
- Requested guidance on using an isolated copy of the source volume, with the original mounted read-only, versus a different supplied source. No volume copy or source startup has been performed.
- Stage 2 remains **Needs Verification**. Stages 3–8 have not been advanced; existing drafts remain unverified.
- `pnpm typecheck`: PASS. `pnpm lint`: FAIL (6 existing frontend errors and 30 warnings including one generated .NET JavaScript warning). Added ESLint exclusions for .NET bin/obj outputs; baseline frontend findings remain to be addressed in the appropriate stage.
- Removed the seed CLI's built-in demo password and password logging; `ATLAS_DEMO_PASSWORD` now requires at least 12 characters. Corrected the unimplemented data-migrator command's misleading tool-path message.
- Additional modified files: `eslint.config.mjs`, `backend/Atlas.DbTool/Program.cs`, `backend/README.md`.
- Final lint rerun after excluding generated outputs: 6 pre-existing frontend errors, 29 warnings. Backend generated JavaScript is no longer linted. Full diagnostics are local in ignored `backend/lint-diagnostic.log`.
- Verified missing seed password is rejected (exit code 1), then reseeded **only** `ATLAS_MigrationVerification` using a random password held in a process environment variable. Password was not printed. Schema and repeated-seed checks passed; final backend build passed with zero warnings/errors.
- Work awaits source database selection/recovery for Stage 2 compatibility verification. The original source volume has not been mounted, copied, started, reseeded, or deleted. No production migration or legacy removal has occurred.

- Existing application remains untouched until Stage 7; PostgreSQL/Drizzle stay in place as the source of truth during backend construction.
- The source PostgreSQL database must not be modified or deleted until sign-off.
- The `drizzle/` migrations and `scripts/*.ts` remain until Stage 8.

---

### Continuation: Stage 2 verified — 2026-11-21

- Copied source volume read-only to isolated volume atlas-migration-source-ac6a0f2798; container atlas-migration-source serves only the copy on localhost:5433. Original volume remains unchanged.
- Implemented scripts/migrate-data.mjs and backend/Atlas.DbTool/SourceDataImporter.cs. Source transaction is REPEATABLE READ READ ONLY. Data streams directly to Dapper importer without export files. Destination must be empty apart from reference seed rows; import is one transaction and enforces all widths, types and existing constraints.
- Fresh ATLAS_V2 import and verify-schema passed. Counts: users/accounts/members=8 each; projects=6; tasks=37; memberships=18; labs=6; sections=7; sessions=31; org audits=26; RBAC audits=2; permissions=21; roles=3. All 21 source tables checked, including empty tables. All source IDs preserved; old sessions explicitly expired.
- Existing data fits bounded target columns and target uniqueness constraints, without truncation or record merging. Future imports fail and roll back if incompatible data is encountered.
- Backend build passed with zero warnings/errors. Stage 2 complete; current stage: Stage 3 repository verification. New files: importer, migration script, backend/source-copy-volume.txt (local recovery marker).




### Stage 3 completed — 2026-11-21

- All entity repository checks passed against ATLAS_MigrationVerification, including LDAP configuration, mappings, identities and verification tokens.
- Added ISearchRepository/SearchRepository with scope-aware pagination, literal search, status filtering, allowlisted sorting and parameterized values. SQL-injection search test passed.
- Added rollback tests proving failed project membership replacement restores deleted memberships and a failed reorder restores earlier task updates.
- Restricted repository verification to a dedicated database named Verification; cleanup now targets only the exact project created by the run.
- Build passed, zero warnings/errors. Files: I search repository, SearchRepository, Infrastructure dependency registration and DbTool verification runner. Current stage: Stage 4.



### Stage 4 completed — 2026-11-21

- Implemented database-validated opaque sessions, HttpOnly SameSite cookies (Secure outside Development), global API CSRF validation and per-IP login rate limits. Browser responses contain no session token.
- Session database stores SHA-256 token digests; imported Better Auth sessions were deliberately expired. Existing scrypt hashes verified using an independent Node-generated compatibility vector.
- Identity/account/member creation and password change plus other-session revocation are transactional. New identities use random UUIDs; existing IDs unchanged. Registration remains disabled by default, matching the source configuration.
- Six automated .NET tests passed: Node hash compatibility, CSRF/login/logout, password rotation and other-device revocation, expired/invalid sessions, hashed storage/disabled registration, transaction rollback. Build successful.
- Files: API Authentication/AtlasSessionHandler.cs, Controllers/AuthController.cs, Middleware/ApiExceptionHandler.cs, Program.cs; Application IIdentityWriter/AuthService; Infrastructure IdentityWriter/hasher/DI; Atlas.Tests project and solution registration. Current stage: Stage 5.



### Stage 5 completed — 2026-11-21

- Added centralized AccessGuard permission/scope policies and org validation. API handlers and services enforce permissions and scopes; unassigned users cannot access unscoped projects/tasks. Admin bypass preserves original role semantics.
- Workspace, task, project, role, organization and user administration endpoints implemented. Atomic project membership writes and serializable admin writes include audit records. Self-role changes, protected role deletion and last-admin demotion/removal are guarded.
- Ten total .NET tests passed, including both cross-Lab and cross-Section denials, hidden cross-org workspace records, administrator bypass, all 21 keys, legacy Users/Supervisor/Admin fallback, denied-operation audit records and protected-role/self-role checks.
- Files: AccessGuard, WorkspaceService, IWorkspaceWriter/WorkspaceWriter, IAdminWriter/AdminWriter, WorkspaceController/AdminController, security tests and DI registrations. Current stage: Stage 6 LDAP.



### Stage 6 completed — 2026-11-21

- Used System.DirectoryServices.Protocols for LDAPS/StartTLS, escaped filters, unique search, separate user bind, explicit timeouts and cancellation-aware asynchronous searches. TLS CA validation includes hostname verification.
- AES-256-GCM retains Node iv.tag.ciphertext format and SHA-256 key derivation; no built-in production key. New CA certificates are encrypted; old plaintext CA values remain readable for migration. Previous keys support rotation.
- Added LDAP configuration/test/mappings/audit/login endpoints, transactional identity linking, role mapping, provisioning/approval and required Lab/Section assignment before LDAP session creation. Secrets and CA content are redacted from API output.
- 15 .NET tests passed, including live LDAPS and StartTLS bind/search/authentication, wrong-password and untrusted-certificate rejection, independent Node encryption vector, tamper/key rotation, insecure mode/filter rejection, provisioning/group mapping/org requirement and audits.
- Disposable test directory atlas-ldap-verification uses localhost-only ports 1389/1636. Its bundled CA was expired; fixture script generates a current test-only certificate, with no machine trust-store changes. Initial TLS failures are resolved. Production directory-specific AD behavior still requires deployment validation.
- Files: ILdapServices, LdapDirectory/LdapSecretProtector/LdapUserWriter/LdapService, LdapController, test fixtures and scripts/test-backend.ps1. Current stage: Stage 7.


### Stage 7 integration checkpoint — 2026-11-21

- Replaced active database-dependent actions with browser API adapters; server layouts use a cookie-forwarding server API client and request-cached .NET session lookup. Next.js uses same-origin /api/atlas rewrites. All active authorization and SQL operations now run in .NET.
- Generated src/lib/api/generated.ts from live OpenAPI using openapi-typescript 7.13.0, with Zod runtime contracts checked against generated DTO/input types. Tool reports a TypeScript 5 peer range; generation and TypeScript 6 checks pass.
- Preserved optimistic state and added partial project update support. Current member now resolves by authenticated email instead of first workspace row. Profile, password, notification preferences and integration toggles call the backend. Added migration 003_user_settings.sql for per-user JSON settings with SQL JSON validation and FK cascade.
- Frontend/backend builds and TypeScript passed. Six Playwright tests passed, covering existing smoke tests, source-user sign-in, actual pointer-based Kanban drag with reload persistence, task edit/duplicate/delete, page loading, Lab creation/reload/deletion and notification saving.
- Initial browser validation found a test selector mismatch plus notification permission and dnd-kit SSR ID hydration differences; corrected selector, hydration-safe notification snapshot and deterministic project DndContext IDs. A final relevant rerun remains before closing Stage 7.
- Fixed baseline React effect lint errors without disabling lint rules. ESLint now exits successfully (0 errors, 24 pre-existing unused/dependency warnings).
- New/modified groups: src/lib/api, API compatibility adapters, auth/proxy routes, project page, settings forms, store/current-member selection, backend settings repository/controller, browser migration tests. Current stage remains Stage 7 pending final integration checks.



### Stage 7 completed; Stage 8 started — 2026-11-21

- Final Stage 7 production frontend build passed. Two migration browser workflows reran successfully after deterministic DnD IDs and hydration-safe notification state; prior full suite passed six browser tests. Backend suite now passes 16 tests, including profile/preferences persistence and user isolation.
- Backup: .backups/postgresql-source-20261121.dump, 61,085 bytes, SHA-256 D0663270FD53AB735EEF026B32A475B5E94D2E5511275D3D47545046770B7356. pg_restore list validated 141 lines. Source configuration archived in .backups/source.env. Folder ACL restricted to the current Windows account; excluded from git. Original Docker source volume remains intact.
- All stages 1–7 verified. Stage 8 may now remove inactive legacy source code and dependencies, retaining a standalone PostgreSQL source-transfer utility and all backups.


### Stage 8 legacy removal and integrity checkpoint — 2026-11-21

- Archived original tracked source into protected .backups/pre-migration-source.zip before removing legacy code. First sandbox archive attempt was blocked by the intentionally restricted backup-folder ACL; retry with the current Windows account succeeded.
- Removed src/db schemas/connection/actions, Drizzle migrations/config, PostgreSQL Docker Compose and obsolete seed/deployment scripts. Moved thin API adapters to src/lib/api and updated all imports. Removed Better Auth, Drizzle, ldapts and pg from application dependencies. PostgreSQL reader is isolated in tools/atlas-data-migrator with its own package/lockfile.
- Application source, root package/lockfile, Next config and frontend environment example have no remaining Drizzle/PostgreSQL/Better Auth dependencies or imports. Source-transfer and historical documentation retain deliberate migration references.
- Production backend connection must be supplied securely; Development and DbTool default to local Windows-authenticated ATLAS_V2. Added a launch profile and pnpm API/database/test/type-generation commands. Archived source env before removing inactive source connection values from frontend .env.local.
- Added migration 004 fixing task-reference truncation beyond 9999; dedicated repository boundary test passes with TSK-10000. Backend build passed, repository checks passed.
- Added read-only --verify transfer mode. Full original source-value comparison passed for every row of all 21 source tables, including UTF-16 string lengths/binary comparisons. New app sessions/audits are allowed in target; all original sessions remain expired. Original task count remains 37 and original IDs/values are intact.
- Current stage: Stage 8; final fresh-database/import/backup-restore/build/lint/backend/browser checks and final documentation are pending.


### Final validation — 2026-11-22

- Fresh ATLAS_FinalMigrationVerification passed all four migrations, transactional import, schema checks and every-source-value comparison. Re-import into the populated database was rejected without overwrite.
- Restored the protected PostgreSQL dump into a separate database in the isolated copy: 8 users and 37 tasks recovered. Stopped atlas-migration-source afterward. Application builds/tests continue with that PostgreSQL server stopped.
- Final cleaned frontend build and TypeScript checks passed. Full browser suite passed 6 tests with SQL Server as the only application database; a Next.js development stream-abort diagnostic appeared during rapid navigation despite all assertions passing. Added production-server test mode for final deployment-style browser validation.
- Backend suite passed 17 tests, and a final safeguard test is being added for password-confirmed self-account deletion. Account deletion now uses transactional ownership transfer, last-admin protection and session revocation rather than the source template's placeholder toast.
- Root/backend/transfer documentation rewritten for the target architecture. NuGet package locking enabled. ESLint passes with 23 baseline warnings and no errors.
- Final source scan: no Drizzle/PostgreSQL/Better Auth/ldapts imports, DATABASE_URL use or Server Action directives in application source/package/lockfile. Historical compatibility references remain only in backend migration notes/hasher and the isolated source utility.


### Final integrity corrections verified — 2026-11-22

- Found and corrected an important blind spot in the initial data verifier: node-postgres converted timestamps to JavaScript Date, normalizing away PostgreSQL microseconds before both import and comparison. The earlier value-comparison passes covered those normalized values, not original sub-millisecond precision.
- Source reader now preserves raw PostgreSQL date/timestamptz text. A transactionally guarded precision-repair mode updates only timestamp values that still equal the earlier truncated import; all other fields must verify exactly before commit. Repaired active ATLAS_V2 and the earlier rehearsal destination without changing IDs, passwords, relationships or later application changes.
- Fresh ATLAS_PrecisionVerification import and read-only comparison passed against raw source values, including microseconds. All original values in active ATLAS_V2 also passed the corrected comparison. Original sessions remain deliberately expired. PostgreSQL copy stopped again afterward.
- Task insertion now locks the parent project and computes top-of-column order in the insertion transaction. Parallel repository creation test passed with distinct positions; scope changes during insertion are rejected.
- Added regression for invalid/expired cookies: ASP.NET returns an empty session response, now normalized to null by the frontend so stale cookies redirect to sign-in instead of failing layout rendering.
- Production frontend suite passed 7 tests with PostgreSQL stopped. Backend suite includes 20 tests covering production Secure cookies/hidden diagnostics, all 21 permissions, last-admin protection, session revocation, self-account deletion, LDAP TLS and identity/profile update workflows. Authenticated API responses now explicitly disable caching.
- Full solution build passed with zero warnings/errors. A prior test build temporarily waited on the running API executable; stopping the owned API process resolved the Windows file lock. No unresolved build failures remain.

### Final local sign-off — 2026-11-22

- All eight stages completed in order. No unresolved migration blockers remain.
- Final backend: 20 tests passed, including live secure LDAP, all 21 permissions, organization isolation, last-admin safeguards, production cookie settings/no-store responses, password/session lifecycle, and identity updates.
- Final frontend: production build and 7 production-browser tests passed. TypeScript and ESLint passed; 23 baseline unused-code warnings remain.
- LDAP administration mutations and their audit entries now commit atomically. Concurrent task creation and multi-row ordering changes are transactional.
- Corrected timestamp normalization blind spot and verified original PostgreSQL microseconds, not just JavaScript-normalized values.
- Original PostgreSQL volume, restored-tested dump, source configuration and archived code remain recoverable. No original source data was modified or deleted.
- Runtime application uses only SQL Server through .NET/Dapper. The PostgreSQL driver exists solely in the separate source-transfer utility.
- Deployment-specific TLS/identity/LDAP configuration and organizational production rollout approval remain operational responsibilities; no production deployment was performed.
- Original external OAuth/synchronization and sample API-key widgets remain their documented baseline placeholders; no new external-provider integration was represented as implemented.

- Final repository review also verified fresh role-response timestamps and atomic LDAP audit commits; the full backend suite passed again (20/20).
- Validation API, LDAP fixture and PostgreSQL recovery copy are stopped. SQL Server remains available. Start the application with the documented pnpm API/frontend commands.

## Final Sign-Off

### Build and run — 2026-11-22

- Backend build passed with zero warnings/errors; production frontend build and TypeScript compilation passed.
- Started hidden background processes: API PID 48080 at http://127.0.0.1:5080; production frontend PID 50744 at http://localhost:3000.
- Verified API liveness and SQL Server health both report ok; sign-in page returns HTTP 200 and renders its expected content.
- Runtime output is in ignored `backend/api-run*-diagnostic.log` and `backend/frontend-run*-diagnostic.log` files.

- [x] All eight stages completed.
- [x] All stage acceptance criteria verified locally.
- [x] Frontend build successful.
- [x] Backend build successful.
- [x] Automated tests successful.
- [x] Security validation completed.
- [x] PostgreSQL and Drizzle removed from active application.
- [x] Original PostgreSQL database/backup retained for operational sign-off.
- [x] Documentation updated.

**Final Status:** Completed

