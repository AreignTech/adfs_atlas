import { z } from "zod"
import { api } from "@/lib/api/client"
import { inviteInputSchema, memberSchema } from "@/lib/api/contracts"
import type { Member } from "@/data/flowboard"
export type InviteInput = z.infer<typeof inviteInputSchema>
export const inviteMemberAction = (input: InviteInput) => api("/admin/members", "POST", inviteInputSchema.parse(input), memberSchema)
export const setMemberStatusAction = (id: string, status: Member["status"]) => api("/admin/members/" + id + "/status", "POST", { status }, memberSchema)
export const removeMemberAction = (id: string) => api<string>("/admin/members/" + id, "DELETE")
