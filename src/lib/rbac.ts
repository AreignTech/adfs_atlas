import "server-only"
import { getBackendSession } from "@/lib/api/server"
export const RBAC_PERMISSION_KEYS = [
  "users.view",
  "users.create",
  "users.edit",
  "users.delete",
  "users.assign_roles",
  "roles.view",
  "roles.create",
  "roles.edit",
  "roles.delete",
  "admin.access",
  "admin.settings",
  "admin.authentication",
  "admin.audit_logs",
  "tasks.view",
  "tasks.create",
  "tasks.edit",
  "tasks.delete",
  "tasks.assign",
  "tasks.manage_all",
  "reports.view",
  "reports.manage",
] as const

export type RbacPermission = (typeof RBAC_PERMISSION_KEYS)[number]

export interface RbacContext {
  userId: string
  email: string
  roleNames: string[]
  permissions: string[]
  isAdministrator: boolean
}


export async function getRbacContext(userId?: string): Promise<RbacContext | null> {
  const session = await getBackendSession()
  if (!session || (userId && session.user.id !== userId)) return null
  return { userId: session.user.id, email: session.user.email, roleNames: session.context.roleNames, permissions: session.context.permissions, isAdministrator: session.context.isAdministrator }
}
export async function requireRbacPermission(permission: RbacPermission) {
  const context = await getRbacContext()
  if (!context) return { ok: false as const, status: 401 as const, message: "Authentication required." }
  if (!context.permissions.includes(permission)) return { ok: false as const, status: 403 as const, message: "You do not have permission to perform this action." }
  return { ok: true as const, context }
}
export const requireAdministrator = () => requireRbacPermission("admin.access")
