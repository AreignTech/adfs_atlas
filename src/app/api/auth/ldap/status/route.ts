import "server-only"

import { eq } from "drizzle-orm"
import { NextResponse } from "next/server"

import { db } from "@/db"
import { ldapConfig } from "@/db/schema"

export async function GET() {
  const [row] = await db
    .select({
      enabled: ldapConfig.enabled,
      status: ldapConfig.status,
      lastSuccessfulTestAt: ldapConfig.lastSuccessfulTestAt,
    })
    .from(ldapConfig)
    .where(eq(ldapConfig.id, 1))
    .limit(1)

  return NextResponse.json({
    enabled: row?.enabled ?? false,
  })
}
