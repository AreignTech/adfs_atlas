import "server-only"
import { getBackendSession } from "@/lib/api/server"
import type { SessionResponse } from "@/lib/api/contracts"
export const auth = { api: { getSession: async (_options?: { headers: Headers }) => { void _options; return getBackendSession() } } }
export type Session = NonNullable<SessionResponse>
export type SessionUser = Session["user"]
