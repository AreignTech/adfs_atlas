import { z } from "zod"
import { api } from "@/lib/api/client"
import { orgCodeSchema, orgInputSchema } from "@/lib/api/contracts"
type Input = z.input<typeof orgInputSchema>
export const getLabCodes = (includeInactive = false) => api("/admin/labs?includeInactive=" + includeInactive, "GET", undefined, z.array(orgCodeSchema))
export const getSectionCodes = (_labId?: string, includeInactive = false) => api("/admin/sections?includeInactive=" + includeInactive, "GET", undefined, z.array(orgCodeSchema))
export const createLabCode = (input: Input) => api("/admin/labs", "POST", orgInputSchema.parse(input), orgCodeSchema)
export const updateLabCode = (id: string, input: Input) => api("/admin/labs/" + id, "PUT", orgInputSchema.parse(input), orgCodeSchema)
export const deleteLabCode = (id: string) => api<{ success: boolean }>("/admin/labs/" + id, "DELETE")
export const createSectionCode = (input: Input) => api("/admin/sections", "POST", orgInputSchema.parse(input), orgCodeSchema)
export const updateSectionCode = (id: string, input: Input) => api("/admin/sections/" + id, "PUT", orgInputSchema.parse(input), orgCodeSchema)
export const deleteSectionCode = (id: string) => api<{ success: boolean }>("/admin/sections/" + id, "DELETE")
export const assignUserOrg = (id: string, labId: string, sectionId: string) => api<{ success: boolean }>("/admin/users/" + id + "/organization", "POST", { labId, sectionId })
export const getOrgAudit = (limit = 200) => api<unknown[]>("/admin/org-audit?limit=" + limit)
