import { redirect } from "next/navigation"
import { getRbacContext } from "@/lib/rbac"

export default async function ApiManagementLayout({ children }: { children: React.ReactNode }) {
  const actor = await getRbacContext()
  if (!actor) redirect("/sign-in")
  if (!actor.isAdministrator || !actor.permissions.includes("admin.access")) redirect("/errors/forbidden")
  return children
}
