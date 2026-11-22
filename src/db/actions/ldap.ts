"use server"

import "server-only"

import { asc, eq } from "drizzle-orm"
import { z } from "zod"

import { db } from "@/db"
import {
  ldapAudit,
  ldapConfig,
  ldapGroupMapping,
} from "@/db/schema"
import {
  decryptSecret,
  encryptSecret,
  ldapDiagnostic,
  testLdapConnection,
  type LdapConfig,
} from "@/lib/ldap"
import { requireRbacPermission } from "@/lib/rbac"

const ldapInputSchema = z.object({
  enabled: z.boolean(),
  host: z.string().trim().max(255),
  port: z.number().int().min(1).max(65535),
  protocol: z.enum(["ldap", "ldaps", "starttls"]),
  baseDn: z.string().trim().max(1000),
  bindDn: z.string().trim().max(1000),
  bindPassword: z.string().max(4000),
  searchBase: z.string().trim().max(1000),
  userSearchAttribute: z.string().trim().min(1).max(100),
  loginIdentifierAttribute: z.string().trim().min(1).max(100),
  emailAttribute: z.string().trim().min(1).max(100),
  displayNameAttribute: z.string().trim().min(1).max(100),
  directoryIdentifierAttribute: z.string().trim().min(1).max(100),
  requireSecure: z.boolean(),
  validateTls: z.boolean(),
  tlsCaCertificate: z.string().max(100000),
  connectionTimeout: z.number().int().min(1000).max(120000),
  autoProvision: z.boolean(),
  requireApproval: z.boolean(),
})

async function requireAdministrator() {
  const authorized = await requireRbacPermission("admin.authentication")
  if (!authorized.ok) throw new Error(authorized.message)
  return { email: authorized.context.email }
}

async function getStoredConfig() {
  const [row] = await db.select().from(ldapConfig).where(eq(ldapConfig.id, 1)).limit(1)
  return row
}

async function toRuntimeConfig(row: typeof ldapConfig.$inferSelect): Promise<LdapConfig> {
  return {
    enabled: row.enabled,
    host: row.host,
    port: row.port,
    protocol: row.protocol as LdapConfig["protocol"],
    baseDn: row.baseDn,
    bindDn: row.bindDn,
    bindPassword: decryptSecret(row.bindPasswordEncrypted),
    searchBase: row.searchBase,
    userSearchAttribute: row.userSearchAttribute,
    loginIdentifierAttribute: row.loginIdentifierAttribute,
    emailAttribute: row.emailAttribute,
    displayNameAttribute: row.displayNameAttribute,
    directoryIdentifierAttribute: row.directoryIdentifierAttribute,
    requireSecure: row.requireSecure,
    validateTls: row.validateTls,
    tlsCaCertificate: row.tlsCaCertificate,
    connectionTimeout: row.connectionTimeout,
    autoProvision: row.autoProvision,
    requireApproval: row.requireApproval,
  }
}

export async function getLdapAdminConfig() {
  await requireAdministrator()
  const row = await getStoredConfig()
  if (!row) {
    return {
      enabled: false,
      host: "",
      port: 389,
      protocol: "ldap",
      baseDn: "",
      bindDn: "",
      bindPasswordSet: false,
      bindPassword: "",
      searchBase: "",
      userSearchAttribute: "sAMAccountName",
      loginIdentifierAttribute: "sAMAccountName",
      emailAttribute: "mail",
      displayNameAttribute: "displayName",
      directoryIdentifierAttribute: "objectGUID",
      requireSecure: true,
      validateTls: true,
      tlsCaCertificate: "",
      connectionTimeout: 10000,
      autoProvision: true,
      requireApproval: false,
      status: "not-tested",
      lastTestAt: null,
      lastSuccessfulTestAt: null,
      lastConfigUpdateAt: null,
    }
  }
  return {
    enabled: row.enabled,
    host: row.host,
    port: row.port,
    protocol: row.protocol,
    baseDn: row.baseDn,
    bindDn: row.bindDn,
    bindPasswordSet: Boolean(row.bindPasswordEncrypted),
    bindPassword: "",
    searchBase: row.searchBase,
    userSearchAttribute: row.userSearchAttribute,
    loginIdentifierAttribute: row.loginIdentifierAttribute,
    emailAttribute: row.emailAttribute,
    displayNameAttribute: row.displayNameAttribute,
    directoryIdentifierAttribute: row.directoryIdentifierAttribute,
    requireSecure: row.requireSecure,
    validateTls: row.validateTls,
    tlsCaCertificate: row.tlsCaCertificate ? "<configured>" : "",
    connectionTimeout: row.connectionTimeout,
    autoProvision: row.autoProvision,
    requireApproval: row.requireApproval,
    status: row.status,
    lastTestAt: row.lastTestAt?.toISOString() ?? null,
    lastSuccessfulTestAt: row.lastSuccessfulTestAt?.toISOString() ?? null,
    lastConfigUpdateAt: row.lastConfigUpdateAt?.toISOString() ?? null,
  }
}

export async function saveLdapAdminConfig(raw: z.infer<typeof ldapInputSchema>) {
  const administrator = await requireAdministrator()
  const input = ldapInputSchema.parse(raw)

  if (
    input.requireSecure &&
    input.protocol === "ldap"
  ) {
    throw new Error("Require secure LDAP connections is incompatible with plain LDAP.")
  }

  const existing = await getStoredConfig()
  const bindPasswordEncrypted =
    input.bindPassword
      ? encryptSecret(input.bindPassword)
      : existing?.bindPasswordEncrypted ?? ""

  const {
    bindPassword: _bindPassword,
    ...storedInput
  } = input

  await db
    .insert(ldapConfig)
    .values({
      id: 1,
      ...storedInput,
      bindPasswordEncrypted,
      status: "not-tested",
      lastConfigUpdateAt: new Date(),
      updatedBy: administrator.email,
    })
    .onConflictDoUpdate({
      target: ldapConfig.id,
      set: {
        enabled: input.enabled,
        host: input.host,
        port: input.port,
        protocol: input.protocol,
        baseDn: input.baseDn,
        bindDn: input.bindDn,
        bindPasswordEncrypted,
        searchBase: input.searchBase,
        userSearchAttribute: input.userSearchAttribute,
        loginIdentifierAttribute: input.loginIdentifierAttribute,
        emailAttribute: input.emailAttribute,
        displayNameAttribute: input.displayNameAttribute,
        directoryIdentifierAttribute: input.directoryIdentifierAttribute,
        requireSecure: input.requireSecure,
        validateTls: input.validateTls,
        tlsCaCertificate: input.tlsCaCertificate,
        connectionTimeout: input.connectionTimeout,
        autoProvision: input.autoProvision,
        requireApproval: input.requireApproval,
        status: "not-tested",
        lastConfigUpdateAt: new Date(),
        updatedBy: administrator.email,
      },
    })

  await db.insert(ldapAudit).values({
    eventType: "ldap.configuration.changed",
    administrator: administrator.email,
    success: true,
    diagnostic: "LDAP configuration updated.",
  })

  return { success: true }
}

export async function testLdapAdminConnection(username?: string) {
  const administrator = await requireAdministrator()
  const row = await getStoredConfig()
  if (!row) throw new Error("LDAP has not been configured.")
  const runtime = await toRuntimeConfig(row)
  const now = new Date()

  try {
    await testLdapConnection(runtime)
    if (username?.trim()) {
      const { searchLdapUser } = await import("@/lib/ldap")
      await searchLdapUser(runtime, username.trim())
    }
    await db
      .update(ldapConfig)
      .set({ status: "successful", lastTestAt: now, lastSuccessfulTestAt: now })
      .where(eq(ldapConfig.id, 1))
    await db.insert(ldapAudit).values({
      eventType: username ? "ldap.user_lookup.test" : "ldap.connection.test",
      administrator: administrator.email,
      success: true,
      diagnostic: "LDAP directory operation succeeded.",
    })
    return { success: true, message: username ? "User lookup succeeded." : "LDAP connection succeeded." }
  } catch (error) {
    const diagnostic = ldapDiagnostic(error)
    await db
      .update(ldapConfig)
      .set({ status: "failed", lastTestAt: now })
      .where(eq(ldapConfig.id, 1))
    await db.insert(ldapAudit).values({
      eventType: username ? "ldap.user_lookup.test" : "ldap.connection.test",
      administrator: administrator.email,
      success: false,
      diagnostic,
    })
    throw new Error(diagnostic)
  }
}

export async function getLdapGroupMappings() {
  await requireAdministrator()
  return db.select().from(ldapGroupMapping).orderBy(asc(ldapGroupMapping.ldapGroup))
}

export async function saveLdapGroupMappings(
  mappings: { ldapGroup: string; atlasRole: string; enabled: boolean }[]
) {
  const administrator = await requireAdministrator()
  const schema = z.array(
    z.object({
      ldapGroup: z.string().trim().min(1).max(1000),
      atlasRole: z.enum(["Administrator", "Project Manager", "Member"]),
      enabled: z.boolean(),
    })
  ).max(100)
  const input = schema.parse(mappings)

  await db.delete(ldapGroupMapping)
  if (input.length) {
    await db.insert(ldapGroupMapping).values(input)
  }
  await db.insert(ldapAudit).values({
    eventType: "ldap.group_mapping.changed",
    administrator: administrator.email,
    success: true,
    diagnostic: `LDAP group mappings updated (${input.length} rules).`,
  })
  return { success: true }
}

export async function getLdapAudit() {
  await requireAdministrator()
  return db
    .select()
    .from(ldapAudit)
    .orderBy(asc(ldapAudit.createdAt))
    .limit(100)
}
