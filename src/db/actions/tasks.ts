"use server"

import "server-only"

import { eq, sql } from "drizzle-orm"
import { revalidatePath } from "next/cache"
import { z } from "zod"

import { db } from "@/db"
import { project, task } from "@/db/schema"
import type { Task } from "@/data/flowboard"
import { requireRbacPermission } from "@/lib/rbac"
import { requireOrgScope } from "@/lib/org"

const taskInputSchema = z.object({
  projectId: z.string().uuid(),
  title: z.string().min(1).max(300),
  description: z.string().max(5000).default(""),
  status: z.enum(["todo", "in-progress", "in-review", "completed"]),
  priority: z.enum(["urgent", "high", "medium", "low"]),
  assigneeId: z.string().uuid().nullable().optional(),
  dueDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, "Expected YYYY-MM-DD."),
})

export type TaskInput = z.infer<typeof taskInputSchema>

const reorderInputSchema = z.object({
  projectId: z.string().uuid(),
  items: z.array(
    z.object({
      taskId: z.string().uuid(),
      status: z.enum(["todo", "in-progress", "in-review", "completed"]),
      position: z.number().int().nonnegative(),
    })
  ).max(500),
})

function toTask(row: typeof task.$inferSelect): Task {
  return {
    id: row.id,
    reference: row.reference,
    title: row.title,
    projectId: row.projectId,
    status: row.status,
    priority: row.priority,
    assigneeId: row.assigneeId ?? "",
    dueDate: toISO(row.dueDate),
    position: row.position,
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

/** New tasks land at the top of their column. */
async function nextPosition(projectId: string, status: Task["status"]) {
  const [row] = await db
    .select({ min: sql<number>`coalesce(min(${task.position}), -1)::int` })
    .from(task)
    .where(eq(task.projectId, projectId))

  const base = row?.min ?? -1

  const [{ max }] = await db
    .select({ max: sql<number>`coalesce(max(${task.position}), 0)::int` })
    .from(task)
    .where(sql`${task.projectId} = ${projectId} and ${task.status} = ${status}`)

  return Math.min(base, max ?? 0) - 1
}

export async function createTaskAction(raw: TaskInput): Promise<Task> {
  const authorized = await requireRbacPermission("tasks.create")
  if (!authorized.ok) throw new Error(authorized.message)
  const input = taskInputSchema.parse(raw)
  const [parentProject] = await db
    .select({ labId: project.labId, sectionId: project.sectionId })
    .from(project)
    .where(eq(project.id, input.projectId))
    .limit(1)
  if (!parentProject) throw new Error("Project was not found.")
  const orgAuthorized = await requireOrgScope(
    parentProject.labId,
    parentProject.sectionId
  )
  if (!orgAuthorized.ok) throw new Error(orgAuthorized.message)
  const position = await nextPosition(input.projectId, input.status)

  const [row] = await db
    .insert(task)
    .values({
      projectId: input.projectId,
      title: input.title,
      description: input.description,
      status: input.status,
      priority: input.priority,
      assigneeId: input.assigneeId || null,
      dueDate: input.dueDate,
      position,
      completedAt: input.status === "completed" ? new Date() : null,
      labId: parentProject.labId,
      sectionId: parentProject.sectionId,
    })
    .returning()

  if (!row) throw new Error("Task insert returned no row.")

  revalidatePath("/", "layout")
  return toTask(row)
}

export async function updateTaskAction(
  taskId: string,
  raw: Partial<TaskInput>
): Promise<Task> {
  const authorized = await requireRbacPermission("tasks.edit")
  if (!authorized.ok) throw new Error(authorized.message)
  const input = taskInputSchema.partial().parse(raw)

  const [current] = await db
    .select({
      id: task.id,
      labId: task.labId,
      sectionId: task.sectionId,
    })
    .from(task)
    .where(eq(task.id, taskId))
    .limit(1)

  if (!current) throw new Error(`Task ${taskId} was not found.`)
  const orgAuthorized = await requireOrgScope(
    current.labId,
    current.sectionId
  )
  if (!orgAuthorized.ok) throw new Error(orgAuthorized.message)

  const [row] = await db
    .update(task)
    .set({
      ...(input.title !== undefined && { title: input.title }),
      ...(input.description !== undefined && { description: input.description }),
      ...(input.status !== undefined && { status: input.status }),
      ...(input.priority !== undefined && { priority: input.priority }),
      ...(input.dueDate !== undefined && { dueDate: input.dueDate }),
      ...(input.assigneeId !== undefined && {
        assigneeId: input.assigneeId || null,
      }),
      // Keep completedAt in step with the status.
      ...(input.status !== undefined && {
        completedAt:
          input.status === "completed"
            ? new Date()
            : input.status === "todo"
              ? null
              : sql`null`,
      }),
    })
    .where(eq(task.id, taskId))
    .returning()

  if (!row) throw new Error(`Task ${taskId} was not found.`)

  revalidatePath("/", "layout")
  return toTask(row)
}

export async function setTaskStatusAction(
  taskId: string,
  status: Task["status"]
): Promise<Task> {
  return updateTaskAction(taskId, { status })
}

export async function reorderTasksAction(
  raw: z.infer<typeof reorderInputSchema>
): Promise<void> {
  const authorized = await requireRbacPermission("tasks.edit")
  if (!authorized.ok) throw new Error(authorized.message)
  const input = reorderInputSchema.parse(raw)
  const [parentProject] = await db
    .select({ labId: project.labId, sectionId: project.sectionId })
    .from(project)
    .where(eq(project.id, input.projectId))
    .limit(1)
  if (!parentProject) throw new Error("Project was not found.")
  const orgAuthorized = await requireOrgScope(
    parentProject.labId,
    parentProject.sectionId
  )
  if (!orgAuthorized.ok) throw new Error(orgAuthorized.message)

  await db.transaction(async (tx) => {
    for (const item of input.items) {
      await tx
        .update(task)
        .set({
          status: item.status,
          position: item.position,
          completedAt:
            item.status === "completed"
              ? sql`coalesce(${task.completedAt}, now())`
              : item.status === "todo"
                ? null
                : sql`null`,
        })
        .where(
          sql`${task.id} = ${item.taskId} and ${task.projectId} = ${input.projectId}`
        )
    }
  })

  revalidatePath("/", "layout")
}

export async function duplicateTaskAction(taskId: string): Promise<Task> {
  const authorized = await requireRbacPermission("tasks.create")
  if (!authorized.ok) throw new Error(authorized.message)
  const [source] = await db.select().from(task).where(eq(task.id, taskId)).limit(1)

  if (!source) throw new Error(`Task ${taskId} was not found.`)
  const orgAuthorized = await requireOrgScope(source.labId, source.sectionId)
  if (!orgAuthorized.ok) throw new Error(orgAuthorized.message)

  const [row] = await db
    .insert(task)
    .values({
      projectId: source.projectId,
      title: `${source.title} (copy)`,
      description: source.description,
      status: "todo",
      priority: source.priority,
      assigneeId: source.assigneeId,
      dueDate: source.dueDate,
      position: source.position - 1,
      labId: source.labId,
      sectionId: source.sectionId,
    })
    .returning()

  if (!row) throw new Error("Task insert returned no row.")

  revalidatePath("/", "layout")
  return toTask(row)
}

export async function deleteTaskAction(taskId: string) {
  const authorized = await requireRbacPermission("tasks.delete")
  if (!authorized.ok) throw new Error(authorized.message)
  const [source] = await db.select({ labId: task.labId, sectionId: task.sectionId }).from(task).where(eq(task.id, taskId)).limit(1)
  if (!source) throw new Error(`Task ${taskId} was not found.`)
  const orgAuthorized = await requireOrgScope(source.labId, source.sectionId)
  if (!orgAuthorized.ok) throw new Error(orgAuthorized.message)
  const deleted = await db
    .delete(task)
    .where(eq(task.id, taskId))
    .returning({ id: task.id })

  if (deleted.length === 0) {
    throw new Error(`Task ${taskId} was not found.`)
  }

  revalidatePath("/", "layout")
  return deleted[0].id
}