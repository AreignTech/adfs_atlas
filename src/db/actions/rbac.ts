"use server"

import "server-only"

import { and, asc, eq, sql } from "drizzle-orm"
import { revalidatePath } from "next/cache"
import { z } from "zod"

import { db } from "@/db"
import {
  appPermission,
  appRole,
  appRolePermission,
  appUserRole,
  member,
  rbacAudit,
  user,
} from "@/db/schema"
import {
  requireRbacPermission,
  recordRbacAudit,
} from "@/lib/rbac"

const roleInputSchema = z.object({
  name: z.string().trim().min(2).max(80),
  description: z.string().trim().max(1000),
  permissionKeys: z.array(z.string().min(1)).max(100),
})

export async function getRbacRoles() {
  const authorized = await requireRbacPermission("roles.view")
  if (!authorized.ok) throw new Error(authorized.message)

  const roles = await db.select().from(appRole).orderBy(asc(appRole.name))
  return Promise.all(
    roles.map(async (role) => {
      const [countRow] = await db
        .select({ count: sql<number>`count(*)::int` })
        .from(appUserRole)
        .where(eq(appUserRole.roleId, role.id))
      const permissions = await db
        .select({ permissionKey: appPermission.permissionKey })
        .from(appRolePermission)
        .innerJoin(appPermission, eq(appRolePermission.permissionId, appPermission.id))
        .where(eq(appRolePermission.roleId, role.id))
      const assignedUsers = await db
        .select({
          name: user.name,
          email: user.email,
        })
        .from(appUserRole)
        .innerJoin(member, eq(appUserRole.userId, member.userId))
        .innerJoin(user, eq(member.userId, user.id))
        .where(eq(appUserRole.roleId, role.id))
      return {
        ...role,
        assignedUsers: countRow?.count ?? 0,
        assignedUserNames: assignedUsers.map((entry) =>
          entry.name || entry.email
        ),
        permissionKeys: permissions.map((permission) => permission.permissionKey),
      }
    })
  )
}

export async function getRbacPermissions() {
  const authorized = await requireRbacPermission("roles.view")
  if (!authorized.ok) throw new Error(authorized.message)
  return db.select().from(appPermission).orderBy(asc(appPermission.permissionKey))
}

export async function getRbacAudit() {
  const authorized = await requireRbacPermission("admin.audit_logs")
  if (!authorized.ok) throw new Error(authorized.message)
  return db
    .select()
    .from(rbacAudit)
    .orderBy(sql`${rbacAudit.createdAt} desc`)
    .limit(200)
}

export async function createRbacRole(raw: z.infer<typeof roleInputSchema>) {
  const authorized = await requireRbacPermission("roles.create")
  if (!authorized.ok) throw new Error(authorized.message)
  const input = roleInputSchema.parse(raw)

  const [existing] = await db
    .select({ id: appRole.id })
    .from(appRole)
    .where(sql`lower(${appRole.name}) = lower(${input.name})`)
    .limit(1)
  if (existing) throw new Error("A role with this name already exists.")

  const [created] = await db
    .insert(appRole)
    .values({
      name: input.name,
      description: input.description,
      isSystemRole: false,
      isActive: true,
    })
    .returning()

  await syncRolePermissions(created.id, input.permissionKeys)
  await recordRbacAudit({
    action: "role.created",
    affectedRoleId: created.id,
    newValue: input.name,
    success: true,
  })
  revalidatePath("/", "layout")
  return created
}

export async function updateRbacRole(
  roleId: string,
  raw: z.infer<typeof roleInputSchema>
) {
  const authorized = await requireRbacPermission("roles.edit")
  if (!authorized.ok) throw new Error(authorized.message)
  const input = roleInputSchema.parse(raw)

  const [role] = await db.select().from(appRole).where(eq(appRole.id, roleId)).limit(1)
  if (!role) throw new Error("Role was not found.")
  if (role.isSystemRole && input.name !== role.name) {
    throw new Error("Default system role names cannot be changed.")
  }

  const [existing] = await db
    .select({ id: appRole.id })
    .from(appRole)
    .where(and(
      sql`lower(${appRole.name}) = lower(${input.name})`,
      sql`${appRole.id} <> ${roleId}`
    ))
    .limit(1)
  if (existing) throw new Error("A role with this name already exists.")

  await db
    .update(appRole)
    .set({
      name: input.name,
      description: input.description,
      updatedAt: new Date(),
    })
    .where(eq(appRole.id, roleId))
  await syncRolePermissions(roleId, input.permissionKeys)
  await recordRbacAudit({
    action: "role.updated",
    affectedRoleId: roleId,
    previousValue: role.name,
    newValue: input.name,
    success: true,
  })
  revalidatePath("/", "layout")
  return { success: true }
}

async function syncRolePermissions(roleId: string, permissionKeys: string[]) {
  await db.delete(appRolePermission).where(eq(appRolePermission.roleId, roleId))
  if (permissionKeys.length === 0) return
  const permissions = await db
    .select({ id: appPermission.id })
    .from(appPermission)
    .where(sql`${appPermission.permissionKey} = ANY(${permissionKeys})`)
  if (permissions.length > 0) {
    await db.insert(appRolePermission).values(
      permissions.map((permission) => ({
        roleId,
        permissionId: permission.id,
      }))
    )
  }
}

export async function deleteRbacRole(roleId: string) {
  const authorized = await requireRbacPermission("roles.delete")
  if (!authorized.ok) throw new Error(authorized.message)

  const [role] = await db.select().from(appRole).where(eq(appRole.id, roleId)).limit(1)
  if (!role) throw new Error("Role was not found.")
  if (role.isSystemRole) {
    await recordRbacAudit({
      action: "role.delete_denied",
      affectedRoleId: roleId,
      previousValue: role.name,
      success: false,
      diagnostic: "Protected system roles cannot be deleted.",
    })
    throw new Error("Default system roles are protected and cannot be deleted.")
  }

  const [assignment] = await db
    .select({ userId: appUserRole.userId })
    .from(appUserRole)
    .where(eq(appUserRole.roleId, roleId))
    .limit(1)
  if (assignment) {
    throw new Error("Reassign users from this role before deleting it.")
  }

  await db.delete(appRole).where(eq(appRole.id, roleId))
  await recordRbacAudit({
    action: "role.deleted",
    affectedRoleId: roleId,
    previousValue: role.name,
    success: true,
  })
  revalidatePath("/", "layout")
  return { success: true }
}

export async function assignRbacRole(memberId: string, roleId: string) {
  const authorized = await requireRbacPermission("users.assign_roles")
  if (!authorized.ok) throw new Error(authorized.message)

  const [targetMember] = await db
    .select()
    .from(member)
    .where(eq(member.id, memberId))
    .limit(1)
  if (!targetMember) throw new Error("User was not found.")

  const [targetRole] = await db
    .select()
    .from(appRole)
    .where(eq(appRole.id, roleId))
    .limit(1)
  if (!targetRole) throw new Error("Role was not found.")

  if (authorized.context.userId === targetMember.userId) {
    throw new Error("Administrators cannot change their own role.")
  }

  const [currentAssignment] = await db
    .select({ roleId: appUserRole.roleId })
    .from(appUserRole)
    .innerJoin(appRole, eq(appUserRole.roleId, appRole.id))
    .where(eq(appUserRole.userId, targetMember.userId))
    .limit(1)

  if (currentAssignment) {
    const [currentRole] = await db
      .select()
      .from(appRole)
      .where(eq(appRole.id, currentAssignment.roleId))
      .limit(1)
    if (
      currentRole?.name === "Administrators" &&
      targetRole.name !== "Administrators"
    ) {
      const [adminCount] = await db
        .select({ count: sql<number>`count(*)::int` })
        .from(appUserRole)
        .innerJoin(appRole, eq(appUserRole.roleId, appRole.id))
        .innerJoin(member, eq(appUserRole.userId, member.userId))
        .where(and(
          eq(appRole.name, "Administrators"),
          eq(member.status, "active")
        ))
      if ((adminCount?.count ?? 0) <= 1) {
        throw new Error("The last active administrator cannot be demoted.")
      }
    }
  }

  await db.delete(appUserRole).where(eq(appUserRole.userId, targetMember.userId))
  await db.insert(appUserRole).values({
    userId: targetMember.userId,
    roleId,
    assignedBy: authorized.context.email,
  })
  await db.update(member).set({ role: targetRole.name }).where(eq(member.id, memberId))

  await recordRbacAudit({
    action: "user.role_changed",
    actorEmail: authorized.context.email,
    affectedUserId: targetMember.userId,
    affectedRoleId: roleId,
    previousValue: currentAssignment ? String(currentAssignment.roleId) : targetMember.role,
    newValue: targetRole.name,
    success: true,
  })
  revalidatePath("/", "layout")
  return { success: true }
}
