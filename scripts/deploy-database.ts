import { randomBytes } from "node:crypto"
import { existsSync, readFileSync, writeFileSync } from "node:fs"
import { spawn } from "node:child_process"
import { resolve } from "node:path"
import { Pool } from "pg"

const root = process.cwd()
const envPath = resolve(root, ".env.local")
const examplePath = resolve(root, ".env.example")

if (!existsSync(envPath)) {
  writeFileSync(envPath, readFileSync(examplePath))
}

const envLines = readFileSync(envPath, "utf8").split(/\r?\n/)

function getEnvValue(name: string) {
  const line = envLines.find((entry) =>
    new RegExp(`^\\s*${name}\\s*=`).test(entry)
  )

  if (!line) return undefined

  const value = line.slice(line.indexOf("=") + 1).trim()
  return value.replace(/^(['"])(.*)\1$/, "$2")
}

function isPlaceholder(value: string | undefined) {
  return !value || /YOUR_|generate-on-first-run|generate-with/i.test(value)
}

function setEnvValue(name: string, value: string) {
  const line = `${name}="${value.replaceAll('"', '\\"')}"`
  const index = envLines.findIndex((entry) =>
    new RegExp(`^\\s*${name}\\s*=`).test(entry)
  )

  if (index >= 0) {
    envLines[index] = line
  } else {
    if (envLines.at(-1) !== "") envLines.push("")
    envLines.push(line)
  }
}

const postgresUser = isPlaceholder(getEnvValue("POSTGRES_USER"))
  ? "flowboard"
  : getEnvValue("POSTGRES_USER")!
const generatePostgresPassword = isPlaceholder(
  getEnvValue("POSTGRES_PASSWORD")
)
const postgresPassword = generatePostgresPassword
  ? randomBytes(32).toString("base64url")
  : getEnvValue("POSTGRES_PASSWORD")!
const postgresDatabase = isPlaceholder(getEnvValue("POSTGRES_DB"))
  ? "flowboard"
  : getEnvValue("POSTGRES_DB")!
const postgresPort = isPlaceholder(getEnvValue("POSTGRES_PORT"))
  ? "5433"
  : getEnvValue("POSTGRES_PORT")!
const demoPassword = isPlaceholder(getEnvValue("DEMO_USER_PASSWORD"))
  ? randomBytes(18).toString("base64url")
  : getEnvValue("DEMO_USER_PASSWORD")!
const authSecret = isPlaceholder(getEnvValue("BETTER_AUTH_SECRET"))
  ? randomBytes(32).toString("base64url")
  : getEnvValue("BETTER_AUTH_SECRET")!

if (!/^\d+$/.test(postgresPort) || Number(postgresPort) < 1 || Number(postgresPort) > 65535) {
  throw new Error("POSTGRES_PORT must be a valid TCP port between 1 and 65535.")
}

const databaseUrl =
  `postgresql://${encodeURIComponent(postgresUser)}:` +
  `${encodeURIComponent(postgresPassword)}@localhost:${postgresPort}/` +
  encodeURIComponent(postgresDatabase)

setEnvValue("POSTGRES_USER", postgresUser)
setEnvValue("POSTGRES_PASSWORD", postgresPassword)
setEnvValue("POSTGRES_DB", postgresDatabase)
setEnvValue("POSTGRES_PORT", postgresPort)
setEnvValue("DATABASE_URL", databaseUrl)
setEnvValue("DEMO_USER_PASSWORD", demoPassword)
setEnvValue("BETTER_AUTH_SECRET", authSecret)
if (!getEnvValue("BETTER_AUTH_URL")) {
  setEnvValue("BETTER_AUTH_URL", "http://localhost:3000")
}

writeFileSync(envPath, `${envLines.join("\n").replace(/\n+$/, "")}\n`, {
  mode: 0o600,
})

function run(command: string, args: string[]) {
  return new Promise<void>((resolvePromise, rejectPromise) => {
    const child = spawn(command, args, {
      cwd: root,
      stdio: "inherit",
    })

    child.once("error", rejectPromise)
    child.once("exit", (code, signal) => {
      if (code === 0) {
        resolvePromise()
      } else {
        rejectPromise(
          new Error(
            `${command} ${args.join(" ")} failed${signal ? ` (${signal})` : ` with exit code ${code}`}.`
          )
        )
      }
    })
  })
}

async function main() {
  console.log("Starting local PostgreSQL with Docker Compose...")
  await run("docker", [
    "compose",
    "--env-file",
    ".env.local",
    "up",
    "-d",
    "--wait",
    "--wait-timeout",
    "90",
  ])

  console.log("Applying database migrations...")
  await run(process.execPath, [
    "--env-file=.env.local",
    "node_modules/drizzle-kit/bin.cjs",
    "migrate",
  ])

  const pool = new Pool({ connectionString: databaseUrl })
  let hasUsers: boolean

  try {
    const schemaResult = await pool.query<{
      hasUserTable: boolean
      hasTaskTable: boolean
      hasTaskSequence: boolean
    }>(
      `SELECT
         to_regclass('public."user"') IS NOT NULL AS "hasUserTable",
         to_regclass('public.task') IS NOT NULL AS "hasTaskTable",
         to_regclass('public.task_reference_seq') IS NOT NULL AS "hasTaskSequence"`
    )
    const schemaState = schemaResult.rows[0]

    if (
      !schemaState?.hasUserTable ||
      !schemaState.hasTaskTable ||
      !schemaState.hasTaskSequence
    ) {
      throw new Error(
        "Database migrations did not create the expected Atlas schema."
      )
    }

    const result = await pool.query<{ hasUsers: boolean }>(
      'SELECT EXISTS (SELECT 1 FROM "user" LIMIT 1) AS "hasUsers"'
    )
    hasUsers = result.rows[0]?.hasUsers ?? false
  } finally {
    await pool.end()
  }

  if (hasUsers) {
    console.log("Database already contains users; skipped demo seeding to preserve existing data.")
  } else {
    console.log("Creating the default demo workspace and login...")
    await run(process.execPath, [
      "--env-file=.env.local",
      "node_modules/tsx/dist/cli.mjs",
      "scripts/seed.ts",
    ])
    await run(process.execPath, [
      "--env-file=.env.local",
      "node_modules/tsx/dist/cli.mjs",
      "scripts/seed-auth.ts",
    ])
  }

  console.log("\nLocal PostgreSQL credentials (stored in .env.local):")
  console.log(`  Database: ${postgresDatabase}`)
  console.log(`  User:     ${postgresUser}`)
  console.log(
    generatePostgresPassword
      ? `  Password: ${postgresPassword}`
      : "  Password: retained in .env.local"
  )
  if (!hasUsers) {
    console.log("\nDefault demo login (also stored in .env.local):")
    console.log("  Email:    nathan@aol.com")
    console.log(`  Password: ${demoPassword}`)
  }
  console.log("\nStart the app with: pnpm dev")
}

main().catch((error: unknown) => {
  console.error(error)
  process.exitCode = 1
})
