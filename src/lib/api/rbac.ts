import { z } from "zod"
import { api } from "@/lib/api/client"
import { roleSchema, permissionSchema, roleInputSchema } from "@/lib/api/contracts"
type RoleInput = z.infer<typeof roleInputSchema>
const auditSchema = z.object({ id: z.string(), actorUserId: z.string().nullable(), actorEmail: z.string().nullable(), action: z.string(), affectedUserId: z.string().nullable(), affectedRoleId: z.string().nullable(), previousValue: z.string().nullable(), newValue: z.string().nullable(), success: z.boolean(), diagnostic: z.string(), createdAt: z.string() })
export const getRbacRoles = () => api("/admin/roles", "GET", undefined, z.array(roleSchema))
export const getRbacPermissions = () => api("/admin/permissions", "GET", undefined, z.array(permissionSchema))
export const getRbacAudit = () => api("/admin/rbac-audit", "GET", undefined, z.array(auditSchema))
export const createRbacRole = (input: RoleInput) => api("/admin/roles", "POST", roleInputSchema.parse(input))
export const updateRbacRole = (id: string, input: RoleInput) => api("/admin/roles/" + id, "PUT", roleInputSchema.parse(input))
export const deleteRbacRole = (id: string) => api("/admin/roles/" + id, "DELETE")
export const assignRbacRole = (id: string, roleId: string) => api("/admin/members/" + id + "/role", "POST", { roleId })
