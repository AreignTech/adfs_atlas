"use client"
import { api } from "@/lib/api/client"
import { sessionSchema } from "@/lib/api/contracts"
async function result(path: string, body?: unknown) {
  try { return { data: await api(path, "POST", body ?? {}), error: null } }
  catch (error) { return { data: null, error: { message: error instanceof Error ? error.message : "Authentication failed." } } }
}
export const authClient = {
  signIn: { email: (body: { email: string; password: string }) => result("/auth/login", body) },
  signUp: { email: (body: { name: string; email: string; password: string }) => result("/auth/register", body) },
  signOut: () => result("/auth/logout"),
  getSession: async () => sessionSchema.parse((await api("/auth/session")) ?? null),
}
export const { signIn, signUp, signOut } = authClient
