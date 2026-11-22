"use server"

import "server-only"

import { and, asc, eq, sql } from "drizzle-orm"
import { revalidatePath } from "next/cache"
import { z } from "zod"

import { db } from "@/db"
import {
  labCode,
  member,
  orgAudit,
  project,
  sectionCode,
  task,
  userOrgAssignment,
} from "@/db/schema"
import { requireRbacPermission } from "@/lib/rbac"

const labInputSchema = z.object({
  code: z.string().trim().min(1).max(40).regex(/^[A-Za-z0-9_-]+$/, "Use letters, numbers, hyphen or underscore."),
  name: z.string().trim().min(1).max(120),
  description: z.string().trim().max(2000).default(""),
  isActive: z.boolean(),
})

const sectionInputSchema = z.object({
  code: z.string().trim().min(1).max(40).regex(/^[A-Za-z0-9_-]+$/, "Use letters, numbers, hyphen or underscore."),
  name: z.string().trim().min(1).max(120),
  description: z.string().trim().max(2000).default(""),
  isActive: z.boolean(),
})

async function requireOrgAdmin(permission: "admin.settings" | "admin.access" = "admin.settings") {
  const authorized = await requireRbacPermission(permission)
  if (!authorized.ok) throw new Error(authorized.message)
  return authorized.context
}

export async function getLabCodes(includeInactive = false) {
  return db
    .select()
    .from(labCode)
    .where(includeInactive ? undefined : eq(labCode.isActive, true))
    .orderBy(asc(labCode.code))
}

export async function getSectionCodes(_labId?: string, includeInactive = false) {
  let query = db.select().from(sectionCode)
  const conditions = []
  if (!includeInactive) conditions.push(eq(sectionCode.isActive, true))
  if (conditions.length) {
    return db
      .select()
      .from(sectionCode)
      .where(and(...conditions))
      .orderBy(asc(sectionCode.code))
  }
  return query.orderBy(asc(sectionCode.code))
}

export async function createLabCode(raw: z.infer<typeof labInputSchema>) {
  const actor = await requireOrgAdmin()
  const input = labInputSchema.parse(raw)
  const code = input.code.toUpperCase()
  const [existing] = await db.select({ id: labCode.id }).from(labCode).where(sql`lower(${labCode.code}) = lower(${code})`).limit(1)
  if (existing) throw new Error("A Lab Code with this identifier already exists.")
  const [created] = await db.insert(labCode).values({
    code,
    name: input.name,
    description: input.description,
    isActive: input.isActive,
    createdBy: actor.email,
  }).returning()
  await db.insert(orgAudit).values({
    actorEmail: actor.email,
    action: "lab.created",
    affectedLabId: created.id,
    newValue: code,
    success: true,
  })
  revalidatePath("/", "layout")
  return created
}

export async function updateLabCode(labId: string, raw: z.infer<typeof labInputSchema>) {
  const actor = await requireOrgAdmin()
  const input = labInputSchema.parse(raw)
  const code = input.code.toUpperCase()
  const [lab] = await db.select().from(labCode).where(eq(labCode.id, labId)).limit(1)
  if (!lab) throw new Error("Lab Code was not found.")
  const [existing] = await db.select({ id: labCode.id }).from(labCode).where(and(sql`lower(${labCode.code}) = lower(${code})`, sql`${labCode.id} <> ${labId}`)).limit(1)
  if (existing) throw new Error("A Lab Code with this identifier already exists.")
  await db.update(labCode).set({
    code,
    name: input.name,
    description: input.description,
    isActive: input.isActive,
    updatedAt: new Date(),
  }).where(eq(labCode.id, labId))
  await db.insert(orgAudit).values({
    actorEmail: actor.email,
    action: "lab.updated",
    affectedLabId: labId,
    previousValue: lab.code,
    newValue: code,
    success: true,
  })
  revalidatePath("/", "layout")
  return { success: true }
}

export async function deleteLabCode(labId: string) {
  const actor = await requireOrgAdmin()
  const [lab] = await db.select().from(labCode).where(eq(labCode.id, labId)).limit(1)
  if (!lab) throw new Error("Lab Code was not found.")
  const [users] = await db.select({ count: sql<number>`count(*)::int` }).from(userOrgAssignment).where(eq(userOrgAssignment.labId, labId))
  const [projects] = await db.select({ count: sql<number>`count(*)::int` }).from(project).where(eq(project.labId, labId))
  const [tasks] = await db.select({ count: sql<number>`count(*)::int` }).from(task).where(eq(task.labId, labId))
  if ((users?.count ?? 0) + (projects?.count ?? 0) + (tasks?.count ?? 0) > 0) {
    await db.insert(orgAudit).values({
      actorEmail: actor.email,
      action: "lab.delete_denied",
      affectedLabId: labId,
      success: false,
      diagnostic: "Lab Code has dependent records.",
    })
    throw new Error("This Lab Code has users, projects, or tasks assigned. Deactivate it instead of deleting it.")
  }
  await db.transaction(async (tx) => {
    const deleted = await tx
      .delete(labCode)
      .where(eq(labCode.id, labId))
      .returning({ id: labCode.id })
    if (deleted.length === 0) {
      throw new Error("Lab Code was not found or could not be deleted.")
    }
    await tx.insert(orgAudit).values({
      actorEmail: actor.email,
      action: "lab.deleted",
      affectedLabId: labId,
      previousValue: lab.code,
      success: true,
    })
  })
  revalidatePath("/", "layout")
  return { success: true }
}

export async function createSectionCode(raw: z.infer<typeof sectionInputSchema>) {
  const actor = await requireOrgAdmin()
  const input = sectionInputSchema.parse(raw)
  const code = input.code.toUpperCase()
  const [existing] = await db.select({ id: sectionCode.id }).from(sectionCode).where(sql`lower(${sectionCode.code}) = lower(${code})`).limit(1)
  if (existing) throw new Error("A Section Code with this identifier already exists.")
  const [created] = await db.insert(sectionCode).values({
    code,
    name: input.name,
    description: input.description,
    isActive: input.isActive,
    createdBy: actor.email,
  }).returning()
  await db.insert(orgAudit).values({
    actorEmail: actor.email,
    action: "section.created",
    affectedSectionId: created.id,
    newValue: code,
    success: true,
  })
  revalidatePath("/", "layout")
  return created
}

export async function updateSectionCode(sectionId: string, raw: z.infer<typeof sectionInputSchema>) {
  const actor = await requireOrgAdmin()
  const input = sectionInputSchema.parse(raw)
  const code = input.code.toUpperCase()
  const [section] = await db.select().from(sectionCode).where(eq(sectionCode.id, sectionId)).limit(1)
  if (!section) throw new Error("Section Code was not found.")
  const [existing] = await db.select({ id: sectionCode.id }).from(sectionCode).where(and(sql`lower(${sectionCode.code}) = lower(${code})`, sql`${sectionCode.id} <> ${sectionId}`)).limit(1)
  if (existing) throw new Error("A Section Code with this identifier already exists.")
  await db.update(sectionCode).set({
    code,
    name: input.name,
    description: input.description,
    isActive: input.isActive,
    updatedAt: new Date(),
  }).where(eq(sectionCode.id, sectionId))
  await db.insert(orgAudit).values({
    actorEmail: actor.email,
    action: "section.updated",
    affectedSectionId: sectionId,
    previousValue: section.code,
    newValue: code,
    success: true,
  })
  revalidatePath("/", "layout")
  return { success: true }
}

export async function deleteSectionCode(sectionId: string) {
  const actor = await requireOrgAdmin()
  const [section] = await db.select().from(sectionCode).where(eq(sectionCode.id, sectionId)).limit(1)
  if (!section) throw new Error("Section Code was not found.")
  const [users] = await db.select({ count: sql<number>`count(*)::int` }).from(userOrgAssignment).where(eq(userOrgAssignment.sectionId, sectionId))
  const [projects] = await db.select({ count: sql<number>`count(*)::int` }).from(project).where(eq(project.sectionId, sectionId))
  const [tasks] = await db.select({ count: sql<number>`count(*)::int` }).from(task).where(eq(task.sectionId, sectionId))
  if ((users?.count ?? 0) + (projects?.count ?? 0) + (tasks?.count ?? 0) > 0) {
    await db.insert(orgAudit).values({
      actorEmail: actor.email,
      action: "section.delete_denied",
      affectedSectionId: sectionId,
      success: false,
      diagnostic: "Section Code has dependent records.",
    })
    throw new Error("This Section Code has users, projects, or tasks assigned. Deactivate it instead of deleting it.")
  }
  await db.transaction(async (tx) => {
    const deleted = await tx
      .delete(sectionCode)
      .where(eq(sectionCode.id, sectionId))
      .returning({ id: sectionCode.id })
    if (deleted.length === 0) {
      throw new Error("Section Code was not found or could not be deleted.")
    }
    await tx.insert(orgAudit).values({
      actorEmail: actor.email,
      action: "section.deleted",
      affectedSectionId: sectionId,
      previousValue: section.code,
      success: true,
    })
  })
  revalidatePath("/", "layout")
  return { success: true }
}

export async function assignUserOrg(userId: string, labId: string, sectionId: string) {
  const actor = await requireOrgAdmin()
  const [lab] = await db.select().from(labCode).where(eq(labCode.id, labId)).limit(1)
  const [section] = await db.select().from(sectionCode).where(eq(sectionCode.id, sectionId)).limit(1)
  if (!lab?.isActive || !section?.isActive) {
    throw new Error("Select valid active Lab and Section Codes.")
  }
  const [previous] = await db.select().from(userOrgAssignment).where(eq(userOrgAssignment.userId, userId)).limit(1)
  await db.insert(userOrgAssignment).values({
    userId,
    labId,
    sectionId,
    assignedBy: actor.email,
  }).onConflictDoUpdate({
    target: userOrgAssignment.userId,
    set: { labId, sectionId, assignedBy: actor.email, assignedAt: new Date() },
  })
  await db.insert(orgAudit).values({
    actorEmail: actor.email,
    action: "user.organization.changed",
    affectedUserId: userId,
    affectedLabId: labId,
    affectedSectionId: sectionId,
    previousValue: previous ? `${previous.labId ?? "unassigned"}:${previous.sectionId ?? "unassigned"}` : "unassigned",
    newValue: `${lab.code}:${section.code}`,
    success: true,
  })
  revalidatePath("/", "layout")
  return { success: true }
}

export async function getOrgAudit(limit = 200) {
  const actor = await requireOrgAdmin("admin.access")
  return db
    .select()
    .from(orgAudit)
    .orderBy(sql`${orgAudit.createdAt} desc`)
    .limit(limit)
}
