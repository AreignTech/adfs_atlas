"use server"

import "server-only"

import { eq, sql } from "drizzle-orm"
import { revalidatePath } from "next/cache"
import { z } from "zod"

import { db } from "@/db"
import {
  appRole,
  appUserRole,
  labCode,
  member,
  project,
  sectionCode,
  user,
  userOrgAssignment,
} from "@/db/schema"
import type { Member } from "@/data/flowboard"
import { requireRbacPermission } from "@/lib/rbac"

const inviteSchema = z.object({
  name: z.string().min(2).max(120),
  email: z.string().email(),
  role: z.string().min(1).max(120).optional(),
  roleId: z.string().uuid(),
  labId: z.string().uuid(),
  sectionId: z.string().uuid(),
})

export type InviteInput = z.infer<typeof inviteSchema>

function initialsFromName(name: string) {
  return name
    .split(" ")
    .map((part) => part[0])
    .filter(Boolean)
    .slice(0, 2)
    .join("")
    .toUpperCase()
}

function toISO(value: Date | string) {
  const date = value instanceof Date ? value : new Date(value)
  const month = String(date.getUTCMonth() + 1).padStart(2, "0")
  const day = String(date.getUTCDate()).padStart(2, "0")
  return `${date.getUTCFullYear()}-${month}-${day}`
}

/**
 * Creates a `user` row plus a `member` row.
 *
 * The user row is what Better Auth will adopt on the next step: it owns
 * identity and credentials, so this never duplicates auth data — it only
 * pre-creates the account that the invitee will claim by signing in.
 */
export async function inviteMemberAction(raw: InviteInput): Promise<Member> {
  const authorized = await requireRbacPermission("users.create")
  if (!authorized.ok) throw new Error(authorized.message)
  const input = inviteSchema.parse(raw)
  const email = input.email.toLowerCase()

  const [existing] = await db
    .select({ id: user.id })
    .from(user)
    .where(eq(user.email, email))
    .limit(1)

  if (existing) {
    throw new Error("Someone with that email is already in the workspace.")
  }

  const [lab] = await db
    .select({ id: labCode.id })
    .from(labCode)
    .where(eq(labCode.id, input.labId))
    .limit(1)
  if (!lab) throw new Error("Select a valid active Lab Code.")

  const [labStatus] = await db
    .select({ isActive: labCode.isActive })
    .from(labCode)
    .where(eq(labCode.id, input.labId))
    .limit(1)
  if (!labStatus?.isActive) throw new Error("Select a valid active Lab Code.")

  const [section] = await db
    .select({ id: sectionCode.id, isActive: sectionCode.isActive })
    .from(sectionCode)
    .where(eq(sectionCode.id, input.sectionId))
    .limit(1)
  if (!section?.isActive) throw new Error("Select a valid active Section Code.")

  const [role] = await db
    .select({ id: appRole.id, name: appRole.name })
    .from(appRole)
    .where(eq(appRole.id, input.roleId))
    .limit(1)
  if (!role) throw new Error("Select a valid application role.")

  // Deterministic id so re-seeding stays idempotent; Better Auth can adopt it.
  const userId = `usr_${Buffer.from(email).toString("base64url").slice(0, 22)}`
  const { createdUser, createdMember } = await db.transaction(async (tx) => {
    const [createdUser] = await tx
      .insert(user)
      .values({
        id: userId,
        name: input.name,
        email,
        emailVerified: false,
      })
      .returning()
    if (!createdUser) throw new Error("User insert returned no row.")

    const [createdMember] = await tx
      .insert(member)
      .values({
        userId: createdUser.id,
        role: role.name,
        status: "invited",
      })
      .returning()
    if (!createdMember) throw new Error("Member insert returned no row.")

    await tx.insert(userOrgAssignment).values({
      userId: createdUser.id,
      labId: input.labId,
      sectionId: input.sectionId,
      assignedBy: authorized.context.email,
    })
    await tx.insert(appUserRole).values({
      userId: createdUser.id,
      roleId: role.id,
      assignedBy: authorized.context.email,
    })
    return { createdUser, createdMember }
  })

  revalidatePath("/", "layout")

  return {
    id: createdMember.id,
    name: createdUser.name,
    email: createdUser.email,
    initials: initialsFromName(createdUser.name),
    role: role.name,
    status: "Invited",
    joinedDate: toISO(createdMember.joinedAt),
    lastActive: "Invite sent just now",
    labId: input.labId,
    sectionId: input.sectionId,
  }
}

export async function setMemberStatusAction(
  memberId: string,
  status: Member["status"]
): Promise<Member> {
  const authorized = await requireRbacPermission("users.edit")
  if (!authorized.ok) throw new Error(authorized.message)
  const dbStatus =
    status === "Active" ? "active" : status === "Invited" ? "invited" : "away"

  const [row] = await db
    .update(member)
    .set({ status: dbStatus })
    .where(eq(member.id, memberId))
    .returning()

  if (!row) throw new Error(`Member ${memberId} was not found.`)

  revalidatePath("/", "layout")

  const [owner] = await db
    .select({ name: user.name, email: user.email })
    .from(user)
    .where(eq(user.id, row.userId))
    .limit(1)

  return {
    id: row.id,
    name: owner?.name ?? "Unknown",
    email: owner?.email ?? "",
    initials: initialsFromName(owner?.name ?? ""),
    role: row.role,
    status,
    joinedDate: toISO(row.joinedAt),
    lastActive: "Updated just now",
  }
}

/**
 * Removing a teammate deletes the auth user row, which cascades to the
 * membership and the join table. Projects they own are transferred first so the
 * `ON DELETE RESTRICT` on project.owner_id cannot block the delete.
 */
export async function removeMemberAction(memberId: string) {
  const authorized = await requireRbacPermission("users.delete")
  if (!authorized.ok) throw new Error(authorized.message)
  const [target] = await db
    .select()
    .from(member)
    .where(eq(member.id, memberId))
    .limit(1)

  if (!target) throw new Error(`Member ${memberId} was not found.`)

  const owned = await db
    .select({ id: project.id })
    .from(project)
    .where(eq(project.ownerId, memberId))

  if (owned.length > 0) {
    const [successor] = await db
      .select({ id: member.id })
      .from(member)
      .where(sql`${member.id} <> ${memberId}`)
      .limit(1)

    if (!successor) {
      throw new Error(
        "This member owns projects and there is nobody left to transfer them to."
      )
    }

    await db
      .update(project)
      .set({ ownerId: successor.id })
      .where(eq(project.ownerId, memberId))
  }

  // Tasks keep their place with a null assignee (ON DELETE SET NULL).
  await db.delete(user).where(eq(user.id, target.userId))

  revalidatePath("/", "layout")
  return memberId
}