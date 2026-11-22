import "server-only"
import { getBackendSession } from "@/lib/api/server"
export interface OrgScope { labId: string | null; sectionId: string | null; isAdministrator: boolean; isAssigned: boolean }
export async function getOrgScope(userId?: string): Promise<OrgScope | null> {
  const session = await getBackendSession()
  if (!session || (userId && session.user.id !== userId)) return null
  return session.context.scope
}
