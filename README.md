# ATLAS - ADFS Task Lifecycle and Assignment System

A modern project & task management dashboard built with Next.js, TypeScript, Tailwind CSS v4 and shadcn/ui.

<!-- prettier-ignore -->
> ATLAS is a centralized task lifecycle and assignment management platform for ADFS. It is built on the open-source [shadcn Dashboard + Landing Page Template](https://github.com/shadcnstore/shadcn-dashboard-landing-template) by [ShadcnStore](https://shadcnstore.com), extended with authentication, a Postgres data layer and Kanban project/task features.

## Features

- **Projects & Tasks** — create projects, break them into tasks, drag-and-drop task boards
- **Dashboard overview** — stats cards, activity charts, recent projects and tasks
- **Calendar** — deadlines and task scheduling
- **Team management** — invite members, role-based team table
- **Settings** — profile, account and notification preferences
- **Landing page** — marketing page built into the same app
- **Auth** — email/password sign in and sign up via [Better Auth](https://better-auth.com)

## Stack

| | |
| --- | --- |
| Framework | Next.js 16 (App Router), React 19 |
| Language | TypeScript |
| Styling | Tailwind CSS v4, shadcn/ui on Base UI |
| Database | PostgreSQL + Drizzle ORM |
| Auth | Better Auth |
| Tables | TanStack Table |
| Charts | Recharts |
| Forms | React Hook Form + Zod |
| Drag & drop | dnd-kit |

## Getting started

### 1. Install dependencies

```bash
pnpm install
```

### 2. Start the local database

Install and start Docker Desktop, then deploy PostgreSQL, apply the migrations, and
seed the demo workspace:

```bash
pnpm db:docker:up
```

This generates local database credentials, a Better Auth secret, and a demo
login in `.env.local`. The demo login is `nathan@aol.com` with password
`P@ssword1`; the database is exposed only on
localhost port `5433` to avoid conflicting with a PostgreSQL service on `5432`.

| Variable | Description |
| --- | --- |
| `DATABASE_URL` | Generated PostgreSQL connection string |
| `BETTER_AUTH_SECRET` | Generated local Better Auth secret |
| `BETTER_AUTH_URL` | Base URL the app runs on, e.g. `http://localhost:3000` |

## LDAP authentication

LDAP is an optional authentication provider. Configure it for a seeded
administrator or an account whose ATLAS role includes `Administrator`.

Set `LDAP_CONFIG_ENCRYPTION_KEY` to a long, stable secret before saving LDAP
settings. Set `LDAP_ADMIN_EMAIL` if you need an additional email-based
bootstrap administrator. Bind passwords and TLS CA certificates are never
returned to the browser.

1. Open **Settings > Authentication**.
2. Enter the LDAP host, port, protocol, base/search DN, bind DN, and bind password.
3. Save the configuration and run **Test LDAP Connection**.
4. Optionally enter a test username to verify directory lookup.
5. Configure group mappings and provisioning, then enable LDAP.

LDAPS or StartTLS is recommended. Plain LDAP is blocked when secure connections
are required. The existing email/password provider remains active when LDAP is
disabled or unavailable.

### 3. Run the app

```bash
pnpm dev
```

Open [http://localhost:3000](http://localhost:3000).

Stop the local database without deleting its data:
```bash
pnpm db:docker:down
```

The setup skips demo seeding when the database already contains users, so
re-running it does not erase existing data.

## Scripts

| Script | Description |
| --- | --- |
| `pnpm dev` | Start the development server |
| `pnpm build` | Production build |
| `pnpm start` | Start the production server |
| `pnpm lint` | Run ESLint |
| `pnpm typecheck` | Run `tsc --noEmit` |
| `pnpm test:ui` | Run Playwright UI smoke tests |
| `pnpm test:ui:headed` | Run Playwright UI tests in a visible browser |
| `pnpm db:generate` | Generate Drizzle migrations |
| `pnpm db:migrate` | Run migrations |
| `pnpm db:push` | Push schema directly |
| `pnpm db:studio` | Open Drizzle Studio |
| `pnpm db:seed` | Seed sample data |
| `pnpm db:seed:auth` | Seed auth users |
| `pnpm db:docker:up` | Start local PostgreSQL, apply schema, and seed a demo login |
| `pnpm db:docker:down` | Stop the local PostgreSQL container |

## UI testing

Install the Playwright Chromium browser once, then run the UI tests:

```bash
pnpm exec playwright install chromium
pnpm test:ui
```

Playwright starts a dedicated Next.js development server on port `3100`.
The smoke tests cover public navigation, sign-in form validation, and the
signed-out dashboard redirect; they do not require a seeded database.

## License

MIT
