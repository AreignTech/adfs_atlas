import { z } from "zod"
import { api } from "@/lib/api/client"
import { adminPasswordResetSchema, inviteInputSchema, memberProfileUpdateSchema, memberSchema } from "@/lib/api/contracts"
import type { Member } from "@/data/flowboard"
export type InviteInput = z.infer<typeof inviteInputSchema>
export type MemberProfileUpdate = z.infer<typeof memberProfileUpdateSchema>
export const inviteMemberAction = (input: InviteInput) => api("/admin/members", "POST", inviteInputSchema.parse(input), memberSchema)
export const setMemberStatusAction = (id: string, status: Member["status"]) => api("/admin/members/" + id + "/status", "POST", { status }, memberSchema)
export const updateMemberAction = (id: string, input: MemberProfileUpdate) => api("/admin/members/" + id, "PUT", memberProfileUpdateSchema.parse(input), memberSchema)
export const resetMemberPasswordAction = (id: string, newPassword: string) => api("/admin/members/" + id + "/password", "POST", adminPasswordResetSchema.parse({ newPassword }))
export const removeMemberAction = (id: string) => api<string>("/admin/members/" + id, "DELETE")
export async function createInvitationLink(id: string) {
  const { token } = await api("/admin/members/" + id + "/invitation", "POST", {}, z.object({ token: z.string() }))
  return `${window.location.origin}/sign-up#invitation=${encodeURIComponent(token)}`
}
