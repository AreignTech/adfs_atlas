import "server-only"

import crypto from "node:crypto"
import { and, eq } from "drizzle-orm"
import { makeSignature } from "better-auth/crypto"
import { NextResponse, type NextRequest } from "next/server"
import { z } from "zod"

import { db } from "@/db"
import {
  account,
  appRole,
  appUserRole,
  ldapAudit,
  ldapConfig,
  ldapGroupMapping,
  ldapIdentity,
  member,
  session,
  user,
  userOrgAssignment,
} from "@/db/schema"
import {
  decryptSecret,
  ldapDiagnostic,
  lookupAndAuthenticate,
  type LdapConfig,
} from "@/lib/ldap"

const loginSchema = z.object({
  username: z.string().trim().min(1).max(256),
  password: z.string().min(1).max(4000),
})

const attempts = new Map<string, { count: number; resetAt: number }>()

function getRateLimitKey(request: NextRequest) {
  return (
    request.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ??
    request.headers.get("x-real-ip") ??
    "unknown"
  )
}

function checkRateLimit(key: string) {
  const now = Date.now()
  const current = attempts.get(key)
  if (!current || current.resetAt <= now) {
    attempts.set(key, { count: 1, resetAt: now + 10 * 60 * 1000 })
    return true
  }
  if (current.count >= 10) return false
  current.count += 1
  return true
}

async function audit(
  eventType: string,
  username: string,
  success: boolean,
  diagnostic: string
) {
  await db.insert(ldapAudit).values({
    eventType,
    username,
    success,
    diagnostic,
  })
}

export async function POST(request: NextRequest) {
  const key = getRateLimitKey(request)
  if (!checkRateLimit(key)) {
    return NextResponse.json(
      { error: "Too many LDAP sign-in attempts. Try again later." },
      { status: 429 }
    )
  }

  let input: z.infer<typeof loginSchema>
  try {
    input = loginSchema.parse(await request.json())
  } catch {
    return NextResponse.json(
      { error: "Enter a directory username and password." },
      { status: 400 }
    )
  }

  const [configRow] = await db
    .select()
    .from(ldapConfig)
    .where(eq(ldapConfig.id, 1))
    .limit(1)

  if (!configRow?.enabled) {
    await audit("ldap.authentication.failed", input.username, false, "LDAP authentication is disabled.")
    return NextResponse.json(
      { error: "LDAP authentication is not enabled." },
      { status: 403 }
    )
  }

  let runtime: LdapConfig
  try {
    runtime = {
      enabled: configRow.enabled,
      host: configRow.host,
      port: configRow.port,
      protocol: configRow.protocol as LdapConfig["protocol"],
      baseDn: configRow.baseDn,
      bindDn: configRow.bindDn,
      bindPassword: decryptSecret(configRow.bindPasswordEncrypted),
      searchBase: configRow.searchBase,
      userSearchAttribute: configRow.userSearchAttribute,
      loginIdentifierAttribute: configRow.loginIdentifierAttribute,
      emailAttribute: configRow.emailAttribute,
      displayNameAttribute: configRow.displayNameAttribute,
      directoryIdentifierAttribute: configRow.directoryIdentifierAttribute,
      requireSecure: configRow.requireSecure,
      validateTls: configRow.validateTls,
      tlsCaCertificate: configRow.tlsCaCertificate,
      connectionTimeout: configRow.connectionTimeout,
      autoProvision: configRow.autoProvision,
      requireApproval: configRow.requireApproval,
    }
  } catch (error) {
    const diagnostic = ldapDiagnostic(error)
    await audit("ldap.authentication.failed", input.username, false, "LDAP configuration credential could not be read.")
    return NextResponse.json(
      { error: "LDAP authentication is temporarily unavailable." },
      { status: 503 }
    )
  }

  let directoryUser
  try {
    directoryUser = await lookupAndAuthenticate(
      runtime,
      input.username,
      input.password
    )
  } catch (error) {
    const diagnostic = ldapDiagnostic(error)
    await audit("ldap.authentication.failed", input.username, false, diagnostic)
    return NextResponse.json(
      { error: "Directory authentication failed." },
      { status: 401 }
    )
  }

  try {
    let localUser: typeof user.$inferSelect | undefined

    const [identity] = await db
      .select()
      .from(ldapIdentity)
      .where(eq(ldapIdentity.directoryIdentifier, directoryUser.directoryIdentifier))
      .limit(1)

    if (identity) {
      const [found] = await db
        .select()
        .from(user)
        .where(eq(user.id, identity.userId))
        .limit(1)
      localUser = found
    } else if (directoryUser.email) {
      const [matched] = await db
        .select()
        .from(user)
        .where(eq(user.email, directoryUser.email.toLowerCase()))
        .limit(1)
      localUser = matched
      if (localUser) {
        await db.insert(ldapIdentity).values({
          userId: localUser.id,
          directoryIdentifier: directoryUser.directoryIdentifier,
          directoryDn: directoryUser.dn,
        })
      }
    }

    if (!localUser) {
      if (!runtime.autoProvision || runtime.requireApproval) {
        await audit(
          "ldap.authentication.failed",
          input.username,
          false,
          "LDAP user provisioning requires administrator approval."
        )
        return NextResponse.json(
          { error: "This directory account has not been approved for ATLAS." },
          { status: 403 }
        )
      }

      if (!directoryUser.email) {
        throw new Error("Directory user has no email address.")
      }

      const userId = crypto.randomUUID()
      let atlasRole = "Member"
      if (directoryUser.groups.length) {
        const mappings = await db
          .select()
          .from(ldapGroupMapping)
          .where(eq(ldapGroupMapping.enabled, true))
        const mapped = mappings.find((mapping) =>
          directoryUser.groups.some(
            (group) =>
              group.toLowerCase() === mapping.ldapGroup.toLowerCase()
          )
        )
        if (mapped) atlasRole = mapped.atlasRole
      }

      const [created] = await db
        .insert(user)
        .values({
          id: userId,
          name: directoryUser.displayName,
          email: directoryUser.email.toLowerCase(),
          emailVerified: true,
        })
        .returning()
      localUser = created

      await db.insert(member).values({
        userId,
        role: atlasRole,
        status: "active",
      })
      await db.insert(account).values({
        id: crypto.randomUUID(),
        accountId: directoryUser.directoryIdentifier,
        providerId: "ldap",
        userId,
      })
      await db.insert(ldapIdentity).values({
        userId,
        directoryIdentifier: directoryUser.directoryIdentifier,
        directoryDn: directoryUser.dn,
      })
      await audit(
        "ldap.user.provisioned",
        input.username,
        true,
        "A local ATLAS account was created for the directory identity."
      )
    } else {
      await db
        .update(user)
        .set({
          name: directoryUser.displayName || localUser.name,
          ...(directoryUser.email
            ? { email: directoryUser.email.toLowerCase() }
            : {}),
          emailVerified: true,
        })
        .where(eq(user.id, localUser.id))

      const mappings = await db
        .select()
        .from(ldapGroupMapping)
        .where(eq(ldapGroupMapping.enabled, true))
      const mapped = mappings.find((mapping) =>
        directoryUser.groups.some(
          (group) => group.toLowerCase() === mapping.ldapGroup.toLowerCase()
        )
      )
      if (mapped) {
        await db
          .update(member)
          .set({ role: mapped.atlasRole, status: "active" })
          .where(eq(member.userId, localUser.id))
      }
    }

    const [ldapMember] = await db
      .select({ role: member.role })
      .from(member)
      .where(eq(member.userId, localUser.id))
      .limit(1)
    const ldapRoleName =
      ldapMember?.role === "Member" ? "Users" : ldapMember?.role ?? "Users"
    const [ldapRole] = await db
      .select({ id: appRole.id })
      .from(appRole)
      .where(eq(appRole.name, ldapRoleName))
      .limit(1)
    if (ldapRole) {
      await db.delete(appUserRole).where(eq(appUserRole.userId, localUser.id))
      await db.insert(appUserRole).values({
        userId: localUser.id,
        roleId: ldapRole.id,
        assignedBy: "LDAP authentication",
      })
    }

    const [orgAssignment] = await db
      .select({ labId: userOrgAssignment.labId, sectionId: userOrgAssignment.sectionId })
      .from(userOrgAssignment)
      .where(eq(userOrgAssignment.userId, localUser.id))
      .limit(1)
    if (!orgAssignment?.labId || !orgAssignment.sectionId) {
      await audit(
        "ldap.authentication.failed",
        input.username,
        false,
        "LDAP user has no required Lab and Section assignment."
      )
      return NextResponse.json(
        { error: "An administrator must assign your Lab and Section before sign-in." },
        { status: 403 }
      )
    }

    const token = crypto.randomBytes(32).toString("base64url")
    const expiresAt = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000)
    await db.insert(session).values({
      id: crypto.randomUUID(),
      token,
      expiresAt,
      userId: localUser.id,
      ipAddress:
        request.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ?? null,
      userAgent: request.headers.get("user-agent"),
    })

    const secret =
      process.env.BETTER_AUTH_SECRET ?? "better-auth-development-secret"
    const signedToken = `${token}.${await makeSignature(token, secret)}`
    const secure = request.nextUrl.protocol === "https:"
    const response = NextResponse.json({
      success: true,
      redirectTo: "/dashboard",
    })
    response.cookies.set("better-auth.session_token", signedToken, {
      httpOnly: true,
      sameSite: "lax",
      secure,
      path: "/",
      expires: expiresAt,
    })

    await audit(
      "ldap.authentication.success",
      input.username,
      true,
      "LDAP credentials validated and an ATLAS session was created."
    )
    return response
  } catch (error) {
    const diagnostic = ldapDiagnostic(error)
    await audit("ldap.authentication.failed", input.username, false, diagnostic)
    return NextResponse.json(
      { error: "The directory account could not be linked or provisioned." },
      { status: 403 }
    )
  }
}
