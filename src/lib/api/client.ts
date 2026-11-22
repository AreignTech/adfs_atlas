import type { z } from "zod"

export class ApiError extends Error {
  constructor(message: string, public readonly status: number) { super(message) }
}
export async function readResponse(response: Response): Promise<unknown> {
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string }
    throw new ApiError(problem.detail ?? problem.title ?? "Request failed.", response.status)
  }
  if (response.status === 204) return undefined
  const text = await response.text()
  return text ? JSON.parse(text) as unknown : null
}
export async function api<T = unknown>(path: string, method = "GET", body?: unknown, schema?: z.ZodType<T>): Promise<T> {
  const headers = new Headers()
  if (method !== "GET" && method !== "HEAD") {
    const result = await fetch("/api/atlas/auth/csrf", { credentials: "same-origin", cache: "no-store" })
    const csrf = await readResponse(result) as { token: string }
    headers.set("X-ATLAS-CSRF", csrf.token)
    headers.set("Content-Type", "application/json")
  }
  const value = await readResponse(await fetch(`/api/atlas${path}`, { method, headers, credentials: "same-origin", cache: "no-store", ...(body !== undefined ? { body: JSON.stringify(body) } : {}) }))
  return schema ? schema.parse(value) : value as T
}
