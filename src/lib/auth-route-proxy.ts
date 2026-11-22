import "server-only"
export async function forwardAuth(request: Request) {
  const incoming = new URL(request.url)
  const headers = new Headers()
  for (const name of ["cookie", "content-type", "x-atlas-csrf"]) {
    const value = request.headers.get(name)
    if (value) headers.set(name, value)
  }
  return fetch((process.env.ATLAS_API_URL ?? "http://127.0.0.1:5080") + incoming.pathname + incoming.search, { method: request.method, headers, cache: "no-store", ...(request.method === "POST" ? { body: await request.text() } : {}) })
}
