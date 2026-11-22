import { headers } from "next/headers"
import { redirect } from "next/navigation"

import { auth } from "@/lib/auth"
import { getRbacContext } from "@/lib/rbac"

export default async function RoleManagementLayout({
  children,
}: {
  children: React.ReactNode
}) {
  const session = await auth.api.getSession({ headers: await headers() })
  if (!session) redirect("/sign-in")
  const rbac = await getRbacContext(session.user.id)
  if (!rbac?.isAdministrator) redirect("/dashboard")
  return children
}
