import "server-only"

import { eq, inArray } from "drizzle-orm"
import { headers } from "next/headers"

import { auth } from "@/lib/auth"
import { db } from "@/db"
import {
  appPermission,
  appRole,
  appRolePermission,
  appUserRole,
  member,
  rbacAudit,
} from "@/db/schema"

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
  let resolvedUserId = userId
  let resolvedEmail = ""

  if (!resolvedUserId) {
    const session = await auth.api.getSession({ headers: await headers() })
    if (!session) return null
    resolvedUserId = session.user.id
    resolvedEmail = session.user.email
  } else {
    const session = await auth.api.getSession({ headers: await headers() })
    resolvedEmail = session?.user.email ?? ""
  }

  const assignments = await db
    .select({
      roleId: appUserRole.roleId,
      roleName: appRole.name,
    })
    .from(appUserRole)
    .innerJoin(appRole, eq(appUserRole.roleId, appRole.id))
    .where(eq(appUserRole.userId, resolvedUserId))

  let roleNames = assignments.map((assignment) => assignment.roleName)
  let permissions: string[] = []

  if (assignments.length > 0) {
    const roleIds = assignments.map((assignment) => assignment.roleId)
    const rows = await db
      .select({ permissionKey: appPermission.permissionKey })
      .from(appRolePermission)
      .innerJoin(appPermission, eq(appRolePermission.permissionId, appPermission.id))
      .where(inArray(appRolePermission.roleId, roleIds))
    permissions = Array.from(new Set(rows.map((row) => row.permissionKey)))
  } else {
    const [membership] = await db
      .select({ role: member.role })
      .from(member)
      .where(eq(member.userId, resolvedUserId))
      .limit(1)
    const fallbackRole = membership?.role ?? "Users"
    roleNames = [fallbackRole]
    if (/admin/i.test(fallbackRole)) {
      permissions = [...RBAC_PERMISSION_KEYS]
    } else if (/supervisor/i.test(fallbackRole)) {
      permissions = [
        "users.view",
        "tasks.view",
        "tasks.create",
        "tasks.edit",
        "tasks.assign",
        "tasks.manage_all",
        "reports.view",
      ]
    } else {
      permissions = [
        "tasks.view",
        "tasks.create",
        "tasks.edit",
        "tasks.assign",
      ]
    }
  }

  return {
    userId: resolvedUserId,
    email: resolvedEmail,
    roleNames,
    permissions,
    isAdministrator:
      roleNames.some((role) => role.toLowerCase() === "administrators") ||
      roleNames.some((role) => /admin/i.test(role)),
  }
}

export async function requireRbacPermission(permission: RbacPermission) {
  const context = await getRbacContext()
  if (!context) {
    return { ok: false as const, status: 401 as const, message: "Authentication required." }
  }
  if (!context.permissions.includes(permission)) {
    await db.insert(rbacAudit).values({
      actorUserId: context.userId,
      actorEmail: context.email,
      action: "authorization.denied",
      success: false,
      diagnostic: `Missing permission: ${permission}`,
    })
    return { ok: false as const, status: 403 as const, message: "You do not have permission to perform this action." }
  }
  return { ok: true as const, context }
}

export async function requireAdministrator() {
  return requireRbacPermission("admin.access")
}

export async function recordRbacAudit(input: {
  action: string
  actorEmail?: string
  affectedUserId?: string
  affectedRoleId?: string
  previousValue?: string
  newValue?: string
  success: boolean
  diagnostic?: string
}) {
  const context = await getRbacContext()
  await db.insert(rbacAudit).values({
    actorUserId: context?.userId ?? null,
    actorEmail: input.actorEmail ?? context?.email ?? null,
    action: input.action,
    affectedUserId: input.affectedUserId ?? null,
    affectedRoleId: input.affectedRoleId ?? null,
    previousValue: input.previousValue ?? null,
    newValue: input.newValue ?? null,
    success: input.success,
    diagnostic: input.diagnostic ?? "",
  })
}
