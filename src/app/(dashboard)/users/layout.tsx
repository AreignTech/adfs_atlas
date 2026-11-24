import { headers } from "next/headers"
import { redirect } from "next/navigation"

import { auth } from "@/lib/auth"
import { getRbacContext } from "@/lib/rbac"

/**
 * Server-side guard for the member administration page. Hiding the sidebar
 * links is cosmetic; this is the actual enforcement — a non-administrator who
 * navigates straight to /users is redirected before any of the page renders.
 */
export const dynamic = "force-dynamic"

export default async function UsersLayout({
  children,
}: {
  children: React.ReactNode
}) {
  const session = await auth.api.getSession({ headers: await headers() })

  if (!session) {
    redirect("/sign-in")
  }

  const rbac = await getRbacContext(session.user.id)

  if (!(rbac?.isAdministrator ?? false)) {
    redirect("/dashboard")
  }

  return <>{children}</>
}
