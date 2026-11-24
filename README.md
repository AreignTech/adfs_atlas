# ATLAS v2.0 — ADFS Task Lifecycle and Assignment System

ATLAS keeps its Next.js 16 / React 19 interface and uses an ASP.NET Core API,
Dapper, and Microsoft SQL Server for authentication, authorization, and data.

For automated Windows IIS deployment, use the [PowerShell deployment package](deploy_scripts/README.md).

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

Team invitations use a private link rather than automatic email delivery. After
creating a teammate, choose **Create new invitation link** in their Team menu
and share it directly. Links expire after seven days; generating another link
invalidates the previous one. The recipient registers with the invited email
address to activate the existing account, preserving its role and organization.
Invitation acceptance is available even when public self-registration is disabled.
Apply database migrations before updating the API; migration 006 also corrects
registered credential accounts that were incorrectly marked as invited.

Development defaults use Windows authentication against
`localhost\SQL2022DEV/ATLAS_V2`. Override the connection for another instance.
Production has no default connection string: supply secure configuration.

## Docker Deployment

Requires Docker Engine with Linux containers and the Docker Compose plugin
(Docker Desktop on Windows). Docker builds install pnpm 10.33.0 and use the
committed lockfile; Node.js and pnpm are not required on the host.

The image runs the Next.js frontend only. Deploy the ASP.NET Core API and SQL
Server separately and apply the database migrations described in the
[backend guide](backend/README.md). Configure SQL credentials and LDAP keys
on the API, never in the frontend image or Docker build arguments.

From the repository root in PowerShell:

```powershell
Copy-Item .env.docker.example .env
# Edit .env: ATLAS_API_URL must be reachable from inside the container.
docker compose up -d --build
docker compose ps
docker compose logs -f frontend
```

Open `http://localhost:3000/sign-in`. The default API origin is
`http://host.docker.internal:5080` for local development. A host API bound only
to `127.0.0.1` cannot accept container connections; configure a reachable
listener and firewall rule. Compose supplies the Linux host-gateway mapping.
For production, use your trusted HTTPS API origin and put the frontend behind
an HTTPS reverse proxy or load balancer. The frontend must trust the API certificate;
if your organization uses a private CA, configure Node's trust store in your
deployment. Production authentication also requires the API to see HTTPS;
configure trusted forwarded headers when TLS terminates upstream.

Compose reads the ignored `.env` file or your deployment shell environment
and passes only the declared variables. Do not commit populated environment
files. Keep `.env.local` for the existing local Next.js workflow.

| Variable | When used | Purpose |
| --- | --- | --- |
| `ATLAS_API_URL` | Build and runtime; use the same value | Non-secret API origin. Rewrites are compiled into the build; server-side fetches also read it at runtime. Changing this origin requires a rebuild and container recreation. |
| `FRONTEND_PORT` | Compose startup | Host port, defaults to `3000`; container still listens on `3000`. |
| `ATLAS_IMAGE_TAG` | Compose build/startup | Local image tag, defaults to `local`. |
| `ATLAS_STANDALONE` | Docker build only | Enables traced output; ordinary `pnpm dev`, `pnpm build`, and IIS deployment retain their existing behavior. |

There are currently no required `NEXT_PUBLIC_` variables. Any introduced later
are public, embedded in browser bundles at build time, and cannot be changed
by setting runtime values. Add explicit non-secret build arguments if needed.
Server-only variables can be supplied at runtime unless Next.js consumes them
while building, as it does with this application's API rewrites. `.env*` files
are excluded from the build context and are not copied into the image.

For a direct Docker build/run using the local development API default:

```powershell
docker build -t my-nextjs-app .
docker run -d -p 3000:3000 --name my-nextjs-app my-nextjs-app
docker logs -f my-nextjs-app
docker stop --timeout 30 my-nextjs-app
docker rm my-nextjs-app
```

Those minimal commands start the frontend; to connect it to a different API,
use the same origin at build and runtime. For example, after setting
`$env:ATLAS_API_URL` to your HTTPS API origin:

```powershell
docker build --build-arg "ATLAS_API_URL=$env:ATLAS_API_URL" -t my-nextjs-app .
docker run -d -p 3000:3000 --name my-nextjs-app --restart unless-stopped --init --stop-timeout 30 --add-host host.docker.internal:host-gateway -e "ATLAS_API_URL=$env:ATLAS_API_URL" my-nextjs-app
```

To change the published port, set `FRONTEND_PORT=8080` in `.env` and run
`docker compose up -d` (or use `-p 8080:3000` with `docker run`). To rebuild
after source or API-origin changes and replace the running Compose container:

```powershell
docker compose up -d --build
# Periodically refresh the Node base image when building updates:
docker compose build --pull
docker compose up -d
# Stop and remove this Compose deployment:
docker compose down
```

The runtime runs as UID 1001, starts `node server.js` directly, and includes
only traced dependencies, compiled output and public assets. Compose adds an
init process, a 30-second shutdown grace period, dropped capabilities and
bounded logs. The inherited health check requests `/sign-in` using Node;
healthy means the frontend responds, not that the API/database is available.
Retain writable container storage for Next.js image/data caches. Publish
images to your infrastructure's registry and inject configuration through
its deployment settings; the same image can run without Compose. Rebuild
regularly for base-image updates; pin a reviewed digest in release pipelines
when immutable base-image selection is required.

Troubleshooting:

- **Port already allocated:** change `FRONTEND_PORT` or stop the existing
  listener. Existing local development and IIS services may use port 3000.
- **Unhealthy container:** inspect `docker compose logs --tail 100 frontend`
  and `docker inspect atlas-frontend --format '{{json .State.Health}}'`.
- **Sign-in/data fails but the page loads:** verify API reachability, HTTPS
  certificate trust, database migrations, API logs and cookie settings.
  `localhost` inside the container refers to the frontend container itself.
- **Wrong API destination:** ensure build/runtime `ATLAS_API_URL` match and
  rebuild; changing just the runtime variable does not update rewrites.
- **Missing styles/images:** use the supplied final image, which includes
  `.next/static` and `public`; avoid mounting over `/app` in production.

Validated on Docker Desktop Linux/amd64: frozen-lockfile installation, the
existing `pnpm build` script inside Docker (including TypeScript checks),
image build, Compose startup on host port 3210, healthy status, sign-in HTTP
200, all 16 referenced JS/CSS assets, public SVG and image optimization.
Verified UID 1001, writable cache, matching compiled/runtime API origin,
no environment files or backend in the runtime image, API health and both
API rewrite and auth-route proxy responses. Logs contained no runtime errors.
SIGTERM stopped the container promptly (exit 143); the temporary container
and network were then removed. Full authenticated browser workflows,
production TLS, other CPU architectures and cloud deployments were not tested.

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
