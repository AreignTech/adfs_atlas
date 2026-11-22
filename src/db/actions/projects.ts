"use server"

import "server-only"

import { and, eq, sql } from "drizzle-orm"
import { revalidatePath } from "next/cache"
import { z } from "zod"

import { db } from "@/db"
import { member, project, projectMember, task } from "@/db/schema"
import { getMemberId } from "@/db/queries/flowboard"
import type { Project } from "@/data/flowboard"
import { requireOrgScope, validateOrgCombination } from "@/lib/org"

const projectInputSchema = z.object({
  name: z.string().min(2).max(120),
  key: z
    .string()
    .min(2)
    .max(4)
    .regex(/^[A-Za-z]+$/, "Letters only, no spaces."),
  description: z.string().max(2000).default(""),
  status: z.enum(["planning", "active", "on-hold", "completed"]),
  dueDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, "Expected YYYY-MM-DD."),
  startDate: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/, "Expected YYYY-MM-DD.")
    .optional(),
  color: z.string().min(1).max(64),
  memberIds: z.array(z.string()).default([]),
  labId: z.string().uuid(),
  sectionId: z.string().uuid(),
})

export type ProjectInput = z.infer<typeof projectInputSchema>

function toProject(
  row: typeof project.$inferSelect,
  memberIds: string[]
): Project {
  return {
    id: row.id,
    name: row.name,
    key: row.key,
    description: row.description,
    status: row.status,
    color: row.color,
    startDate: toISO(row.startDate),
    dueDate: toISO(row.dueDate),
    memberIds,
    labId: row.labId,
    sectionId: row.sectionId,
  }
}

function toISO(value: Date | string) {
  const date = value instanceof Date ? value : new Date(value)
  const month = String(date.getUTCMonth() + 1).padStart(2, "0")
  const day = String(date.getUTCDate()).padStart(2, "0")
  return `${date.getUTCFullYear()}-${month}-${day}`
}

async function syncMemberships(projectId: string, memberIds: string[]) {
  await db.delete(projectMember).where(eq(projectMember.projectId, projectId))

  if (memberIds.length === 0) return

  await db
    .insert(projectMember)
    .values(memberIds.map((memberId) => ({ projectId, memberId })))
}

export async function createProjectAction(
  raw: ProjectInput
): Promise<Project> {
  const input = projectInputSchema.parse(raw)
  const validOrg = await validateOrgCombination(input.labId, input.sectionId)
  if (!validOrg) throw new Error("Select a valid active Lab and Section combination.")
  const authorized = await requireOrgScope(input.labId, input.sectionId)
  if (!authorized.ok) throw new Error(authorized.message)

  const ownerId = input.memberIds[0] ?? (await getMemberId())

  if (!ownerId) {
    throw new Error(
      "No workspace member exists yet. Run `pnpm db:seed` before creating projects."
    )
  }

  const [row] = await db
    .insert(project)
    .values({
      name: input.name,
      key: input.key.toUpperCase(),
      description: input.description,
      status: input.status,
      color: input.color,
      ownerId,
      startDate: input.startDate ?? new Date().toISOString().slice(0, 10),
      dueDate: input.dueDate,
      labId: input.labId,
      sectionId: input.sectionId,
    })
    .returning()

  if (!row) throw new Error("Project insert returned no row.")

  await syncMemberships(row.id, input.memberIds)

  revalidatePath("/", "layout")
  return toProject(row, input.memberIds)
}

export async function updateProjectAction(
  projectId: string,
  raw: Partial<ProjectInput>
): Promise<Project> {
  const input = projectInputSchema.partial().parse(raw)
  const [existing] = await db.select().from(project).where(eq(project.id, projectId)).limit(1)
  if (!existing) throw new Error(`Project ${projectId} was not found.`)
  const labId = input.labId ?? existing.labId
  const sectionId = input.sectionId ?? existing.sectionId
  if (labId && sectionId) {
    const validOrg = await validateOrgCombination(labId, sectionId)
    if (!validOrg) throw new Error("Select a valid active Lab and Section combination.")
    const authorized = await requireOrgScope(labId, sectionId)
    if (!authorized.ok) throw new Error(authorized.message)
  }

  const [row] = await db.transaction(async (tx) => {
    const [updated] = await tx
      .update(project)
      .set({
        ...(input.name !== undefined && { name: input.name }),
        ...(input.key !== undefined && { key: input.key.toUpperCase() }),
        ...(input.description !== undefined && {
          description: input.description,
        }),
        ...(input.status !== undefined && { status: input.status }),
        ...(input.color !== undefined && { color: input.color }),
        ...(input.dueDate !== undefined && { dueDate: input.dueDate }),
        ...(input.labId !== undefined && { labId: input.labId }),
        ...(input.sectionId !== undefined && { sectionId: input.sectionId }),
      })
      .where(eq(project.id, projectId))
      .returning()

    if (
      updated &&
      (input.labId !== undefined || input.sectionId !== undefined)
    ) {
      await tx
        .update(task)
        .set({
          labId: updated.labId,
          sectionId: updated.sectionId,
        })
        .where(eq(task.projectId, projectId))
    }

    return updated ? [updated] : []
  })

  if (!row) throw new Error(`Project ${projectId} was not found.`)

  if (input.memberIds !== undefined) {
    await syncMemberships(projectId, input.memberIds)
  }

  revalidatePath("/", "layout")

  const memberships = input.memberIds ??
    (
      await db
        .select({ memberId: projectMember.memberId })
        .from(projectMember)
        .where(eq(projectMember.projectId, projectId))
    ).map((row) => row.memberId)

  return toProject(row, memberships)
}

export async function setProjectStatusAction(
  projectId: string,
  status: Project["status"]
) {
  const [existing] = await db.select().from(project).where(eq(project.id, projectId)).limit(1)
  if (!existing) throw new Error(`Project ${projectId} was not found.`)
  const authorized = await requireOrgScope(existing.labId, existing.sectionId)
  if (!authorized.ok) throw new Error(authorized.message)
  const [row] = await db
    .update(project)
    .set({ status })
    .where(eq(project.id, projectId))
    .returning()

  if (!row) throw new Error(`Project ${projectId} was not found.`)

  revalidatePath("/", "layout")
  return row
}

export async function deleteProjectAction(projectId: string) {
  const [existing] = await db.select().from(project).where(eq(project.id, projectId)).limit(1)
  if (!existing) throw new Error(`Project ${projectId} was not found.`)
  const authorized = await requireOrgScope(existing.labId, existing.sectionId)
  if (!authorized.ok) throw new Error(authorized.message)
  // Tasks and memberships cascade in the database.
  const deleted = await db
    .delete(project)
    .where(eq(project.id, projectId))
    .returning({ id: project.id })

  if (deleted.length === 0) {
    throw new Error(`Project ${projectId} was not found.`)
  }

  revalidatePath("/", "layout")
  return deleted[0].id
}

/** Frees an owner id if the member still owns nothing (used by the team page). */
export async function countOwnedProjects(memberId: string) {
  const [row] = await db
    .select({ count: sql<number>`count(*)::int` })
    .from(project)
    .where(eq(project.ownerId, memberId))

  return row?.count ?? 0
}

export async function assertMemberExists(memberId: string) {
  const [row] = await db
    .select({ id: member.id })
    .from(member)
    .where(and(eq(member.id, memberId)))
    .limit(1)

  return Boolean(row)
}