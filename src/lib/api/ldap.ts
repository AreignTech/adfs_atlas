import { z } from "zod"
import { api } from "@/lib/api/client"
import type { components } from "@/lib/api/generated"
const configSchema = z.object({
  enabled: z.boolean(), host: z.string(), port: z.number(), protocol: z.enum(["ldap", "ldaps", "starttls"]), baseDn: z.string(), bindDn: z.string(), bindPasswordSet: z.boolean(), bindPassword: z.string(), searchBase: z.string(), userSearchAttribute: z.string(), loginIdentifierAttribute: z.string(), emailAttribute: z.string(), displayNameAttribute: z.string(), directoryIdentifierAttribute: z.string(), requireSecure: z.boolean(), validateTls: z.boolean(), tlsCaCertificate: z.string(), connectionTimeout: z.number(), autoProvision: z.boolean(), requireApproval: z.boolean(), status: z.string(), lastTestAt: z.string().nullable(), lastSuccessfulTestAt: z.string().nullable(), lastConfigUpdateAt: z.string().nullable()
}) satisfies z.ZodType<components["schemas"]["LdapAdminConfigDto"]>
const mappingSchema = z.object({ id: z.string(), ldapGroup: z.string(), atlasRole: z.string(), enabled: z.boolean(), updatedAt: z.string() })
const auditSchema = z.object({ id: z.string(), eventType: z.string(), username: z.string().nullable(), administrator: z.string().nullable(), success: z.boolean(), diagnostic: z.string(), createdAt: z.string() })
type ConfigInput = Omit<z.infer<typeof configSchema>, "bindPasswordSet" | "status" | "lastTestAt" | "lastSuccessfulTestAt" | "lastConfigUpdateAt">
export const getLdapAdminConfig = () => api("/ldap/config", "GET", undefined, configSchema)
export const saveLdapAdminConfig = (input: ConfigInput) => api<{ success: boolean }>("/ldap/config", "POST", input)
export const testLdapAdminConnection = (username?: string) => api<{ success: boolean; message: string }>("/ldap/test", "POST", { username })
export const getLdapGroupMappings = () => api("/ldap/mappings", "GET", undefined, z.array(mappingSchema))
export const saveLdapGroupMappings = (mappings: { ldapGroup: string; atlasRole: string; enabled: boolean }[]) => api<{ success: boolean }>("/ldap/mappings", "POST", mappings)
export const getLdapAudit = () => api("/ldap/audit", "GET", undefined, z.array(auditSchema))
