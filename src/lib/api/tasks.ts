import { z } from "zod"
import { api } from "@/lib/api/client"
import { taskInputSchema, taskSchema } from "@/lib/api/contracts"
import type { Task } from "@/data/flowboard"
export type TaskInput = z.infer<typeof taskInputSchema>
export const createTaskAction = (input: TaskInput) => api("/tasks", "POST", taskInputSchema.parse(input), taskSchema)
export const updateTaskAction = (id: string, input: Partial<TaskInput>) => api("/tasks/" + id, "PATCH", taskInputSchema.partial().parse(input), taskSchema)
export const setTaskStatusAction = (id: string, status: Task["status"]) => updateTaskAction(id, { status })
export const reorderTasksAction = (input: { projectId: string; items: { taskId: string; status: Task["status"]; position: number }[] }) => api<void>("/tasks/reorder", "POST", input)
export const duplicateTaskAction = (id: string) => api("/tasks/" + id + "/duplicate", "POST", {}, taskSchema)
export const deleteTaskAction = (id: string) => api<string>("/tasks/" + id, "DELETE")
