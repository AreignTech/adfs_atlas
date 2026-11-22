import "server-only"

import { drizzle } from "drizzle-orm/node-postgres"
import { Pool } from "pg"

import * as schema from "./schema"

/**
 * Server-only database handle.
 *
 * Uses the standard PostgreSQL protocol and can connect to a local or
 * self-hosted PostgreSQL server. Nothing in this file may be imported from a
 * client component.
 */
function connectionString() {
  const url = process.env.DATABASE_URL

  if (!url) {
    throw new Error(
      "DATABASE_URL is not set. Copy .env.example to .env.local and fill it in."
    )
  }

  return url
}

const globalForDb = globalThis as unknown as {
  flowboardDb?: ReturnType<typeof drizzle<typeof schema>>
  flowboardPool?: Pool
}

/** Reuse one client across hot reloads in development. */
const pool =
  globalForDb.flowboardPool ??
  new Pool({ connectionString: connectionString() })

export const db =
  globalForDb.flowboardDb ??
  drizzle(pool, { schema, casing: "snake_case" })

if (process.env.NODE_ENV !== "production") {
  globalForDb.flowboardPool = pool
  globalForDb.flowboardDb = db
}

export { schema }
export type Database = typeof db