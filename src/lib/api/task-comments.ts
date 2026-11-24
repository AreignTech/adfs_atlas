import { z } from "zod"
import { api } from "./client"
import type { components } from "./generated"

export const taskCommentSchema = z.object({
  id: z.guid(), taskId: z.guid(), userId: z.string(),
  authorName: z.string(), authorImage: z.string().nullable(), content: z.string(),
  createdAt: z.string(), updatedAt: z.string().nullable(), canEdit: z.boolean(), canDelete: z.boolean(),
}) satisfies z.ZodType<components["schemas"]["TaskCommentDto"]>
const pageSchema = z.object({
  items: z.array(taskCommentSchema), total: z.number().int(), offset: z.number().int(),
  limit: z.number().int(), canComment: z.boolean(),
}) satisfies z.ZodType<components["schemas"]["TaskCommentPage"]>
export type TaskComment = z.infer<typeof taskCommentSchema>
const path = (taskId: string) => `/tasks/${taskId}/comments`
export const getTaskComments = (taskId: string, offset = 0, signal?: AbortSignal) =>
  api(`${path(taskId)}?offset=${offset}&limit=20`, "GET", undefined, pageSchema, signal)
export const addTaskComment = (taskId: string, content: string) =>
  api(path(taskId), "POST", { content }, taskCommentSchema)
export const updateTaskComment = (taskId: string, id: string, content: string) =>
  api(`${path(taskId)}/${id}`, "PUT", { content }, taskCommentSchema)
export const deleteTaskComment = (taskId: string, id: string) =>
  api<void>(`${path(taskId)}/${id}`, "DELETE")
