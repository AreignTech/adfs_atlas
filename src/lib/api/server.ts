import "server-only"
import { headers } from "next/headers"
import { cache } from "react"
import { readResponse } from "./client"
import { sessionSchema } from "./contracts"

export async function serverApi(path: string): Promise<unknown> {
  const incoming = await headers()
  return readResponse(await fetch(`${process.env.ATLAS_API_URL ?? "http://127.0.0.1:5080"}/api${path}`, { headers: { cookie: incoming.get("cookie") ?? "" }, cache: "no-store" }))
}
export const getBackendSession = cache(async () => sessionSchema.parse((await serverApi("/auth/session")) ?? null))
