"use client"

import { ApiReferenceReact } from "@scalar/api-reference-react"
import "@scalar/api-reference-react/style.css"
import { readResponse } from "@/lib/api/client"

async function authenticatedFetch(input: string | URL | Request, init?: RequestInit) {
  const request = new Request(input instanceof Request ? input : new URL(input, window.location.origin), init)
  const url = new URL(request.url)
  if (url.origin !== window.location.origin || !url.pathname.startsWith("/api/atlas/")) throw new Error("The API explorer only permits this application's API.")
  const headers = new Headers(request.headers)
  if (!["GET", "HEAD", "OPTIONS"].includes(request.method.toUpperCase())) {
    const csrf = await readResponse(await fetch("/api/atlas/auth/csrf", { credentials: "same-origin", cache: "no-store" })) as { token: string }
    headers.set("X-ATLAS-CSRF", csrf.token)
  }
  return fetch(new Request(request, { headers, credentials: "same-origin", redirect: "error" }))
}

const configuration = {
  url: "/api/atlas/admin/api-management/openapi/v1.json",
  customFetch: authenticatedFetch,
  persistAuth: false,
  telemetry: false,
  agent: { disabled: true },
  hideClientButton: true,
  showDeveloperTools: "never" as const,
  withDefaultFonts: false,
}

export default function ScalarReference() {
  return <div className="h-[80vh] overflow-auto rounded-lg border"><ApiReferenceReact configuration={configuration} /></div>
}
