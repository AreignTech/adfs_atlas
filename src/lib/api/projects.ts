import { z } from "zod"
import { api } from "@/lib/api/client"
import { projectInputSchema, projectSchema } from "@/lib/api/contracts"
import type { Project } from "@/data/flowboard"
export type ProjectInput = z.input<typeof projectInputSchema>
export const createProjectAction = (input: ProjectInput) => api("/projects", "POST", projectInputSchema.parse(input), projectSchema)
export const updateProjectAction = (id: string, input: Partial<ProjectInput>) => api("/projects/" + id, "PATCH", projectInputSchema.partial().parse(input), projectSchema)
export const setProjectStatusAction = (id: string, status: Project["status"]) => api("/projects/" + id + "/status", "POST", { status }, projectSchema)
export const deleteProjectAction = (id: string) => api<string>("/projects/" + id, "DELETE")
