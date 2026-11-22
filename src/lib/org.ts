import "server-only"

import { eq } from "drizzle-orm"
import { headers } from "next/headers"

import { auth } from "@/lib/auth"
import { db } from "@/db"
import { labCode, orgAudit, sectionCode, userOrgAssignment } from "@/db/schema"
import { getRbacContext } from "@/lib/rbac"

export interface OrgScope {
  labId: string | null
  sectionId: string | null
  isAdministrator: boolean
  isAssigned: boolean
}

export async function getOrgScope(userId?: string): Promise<OrgScope | null> {
  let resolvedUserId = userId
  if (!resolvedUserId) {
    const session = await auth.api.getSession({ headers: await headers() })
    if (!session) return null
    resolvedUserId = session.user.id
  }
  const rbac = await getRbacContext(resolvedUserId)
  const [assignment] = await db
    .select({
      labId: userOrgAssignment.labId,
      sectionId: userOrgAssignment.sectionId,
    })
    .from(userOrgAssignment)
    .where(eq(userOrgAssignment.userId, resolvedUserId))
    .limit(1)

  return {
    labId: assignment?.labId ?? null,
    sectionId: assignment?.sectionId ?? null,
    isAdministrator: rbac?.isAdministrator ?? false,
    isAssigned: Boolean(assignment?.labId && assignment?.sectionId),
  }
}

export async function requireOrgScope(labId: string | null, sectionId: string | null) {
  const scope = await getOrgScope()
  if (!scope) {
    return { ok: false as const, status: 401 as const, message: "Authentication required." }
  }
  if (scope.isAdministrator) {
    return { ok: true as const, scope }
  }
  if (
    !scope.isAssigned ||
    !labId ||
    !sectionId ||
    scope.labId !== labId ||
    scope.sectionId !== sectionId
  ) {
    await db.insert(orgAudit).values({
      action: "org.access.denied",
      success: false,
      diagnostic: "Requested organizational scope did not match the account assignment.",
    })
    return { ok: false as const, status: 403 as const, message: "You do not have access to this organizational area." }
  }
  return { ok: true as const, scope }
}

export async function validateOrgCombination(labId: string, sectionId: string) {
  const [lab] = await db
    .select()
    .from(labCode)
    .where(eq(labCode.id, labId))
    .limit(1)
  if (!lab?.isActive) return null
  const [section] = await db
    .select()
    .from(sectionCode)
    .where(eq(sectionCode.id, sectionId))
    .limit(1)
  if (!section?.isActive) return null
  return { lab, section }
}
