import "server-only"

import crypto from "node:crypto"
import { Client } from "ldapts"

export interface LdapConfig {
  enabled: boolean
  host: string
  port: number
  protocol: "ldap" | "ldaps" | "starttls"
  baseDn: string
  bindDn: string
  bindPassword: string
  searchBase: string
  userSearchAttribute: string
  loginIdentifierAttribute: string
  emailAttribute: string
  displayNameAttribute: string
  directoryIdentifierAttribute: string
  requireSecure: boolean
  validateTls: boolean
  tlsCaCertificate: string
  connectionTimeout: number
  autoProvision: boolean
  requireApproval: boolean
}

export function encryptSecret(value: string) {
  if (!value) return ""
  const key = crypto
    .createHash("sha256")
    .update(
      process.env.LDAP_CONFIG_ENCRYPTION_KEY ??
        process.env.BETTER_AUTH_SECRET ??
        "atlas-local-development-key"
    )
    .digest()
  const iv = crypto.randomBytes(12)
  const cipher = crypto.createCipheriv("aes-256-gcm", key, iv)
  const encrypted = Buffer.concat([cipher.update(value, "utf8"), cipher.final()])
  const tag = cipher.getAuthTag()
  return `${iv.toString("base64")}.${tag.toString("base64")}.${encrypted.toString("base64")}`
}

export function decryptSecret(value: string) {
  if (!value) return ""
  const parts = value.split(".")
  if (parts.length !== 3) throw new Error("Stored LDAP credential is invalid.")
  const key = crypto
    .createHash("sha256")
    .update(
      process.env.LDAP_CONFIG_ENCRYPTION_KEY ??
        process.env.BETTER_AUTH_SECRET ??
        "atlas-local-development-key"
    )
    .digest()
  const iv = Buffer.from(parts[0], "base64")
  const tag = Buffer.from(parts[1], "base64")
  const encrypted = Buffer.from(parts[2], "base64")
  const decipher = crypto.createDecipheriv("aes-256-gcm", key, iv)
  decipher.setAuthTag(tag)
  return Buffer.concat([decipher.update(encrypted), decipher.final()]).toString("utf8")
}

export function escapeLdapFilter(value: string) {
  return value
    .replace(/\\/g, "\\5c")
    .replace(/\*/g, "\\2a")
    .replace(/\(/g, "\\28")
    .replace(/\)/g, "\\29")
    .replace(/\0/g, "\\00")
}

function safeDiagnostic(error: unknown) {
  if (!(error instanceof Error)) return "LDAP operation failed."
  const message = error.message
    .replace(/password[=:]\s*\S+/gi, "password=<redacted>")
    .replace(/binddn[=:]\s*\S+/gi, "bindDN=<redacted>")
  return message.slice(0, 500)
}

export function ldapDiagnostic(error: unknown) {
  return safeDiagnostic(error)
}

function ldapAttributeValue(
  attributes: Array<{ type: string; values: Buffer[] | string[] }>,
  name: string
) {
  const attribute = attributes.find(
    (entry) => entry.type.toLowerCase() === name.toLowerCase()
  )
  if (!attribute) return [] as string[]
  return attribute.values.map((value) =>
    Buffer.isBuffer(value) ? value.toString("utf8") : value
  )
}

export function createLdapClient(config: LdapConfig) {
  const scheme = config.protocol === "ldaps" ? "ldaps" : "ldap"
  return new Client({
    url: `${scheme}://${config.host}:${config.port}`,
    timeout: config.connectionTimeout,
    connectTimeout: config.connectionTimeout,
    tlsOptions: {
      rejectUnauthorized: config.validateTls,
      minVersion: "TLSv1.2",
      ...(config.tlsCaCertificate
        ? { ca: [config.tlsCaCertificate] }
        : {}),
    },
  })
}

export async function upgradeToTls(client: Client, config: LdapConfig) {
  if (config.protocol !== "starttls") return
  await client.startTLS({
    rejectUnauthorized: config.validateTls,
    minVersion: "TLSv1.2",
    ...(config.tlsCaCertificate
      ? { ca: [config.tlsCaCertificate] }
      : {}),
  })
}

export async function testLdapConnection(config: LdapConfig) {
  if (!config.host || !config.bindDn || !config.bindPassword) {
    throw new Error("Host, bind DN, and bind password are required.")
  }
  if (
    config.requireSecure &&
    config.protocol === "ldap"
  ) {
    throw new Error("Unencrypted LDAP is prohibited by the security configuration.")
  }

  const client = createLdapClient(config)
  try {
    await upgradeToTls(client, config)
    await client.bind(config.bindDn, config.bindPassword)
    await client.search(config.searchBase || config.baseDn, {
      scope: "base",
      filter: "(objectClass=*)",
      attributes: ["dn"],
    })
  } finally {
    await client.unbind().catch(() => undefined)
  }
}

export interface LdapUser {
  dn: string
  directoryIdentifier: string
  email: string
  displayName: string
  groups: string[]
  loginIdentifier: string
}

export async function searchLdapUser(
  config: LdapConfig,
  username: string
): Promise<LdapUser> {
  if (!username) throw new Error("Username is required.")
  const client = createLdapClient(config)
  try {
    await upgradeToTls(client, config)
    await client.bind(config.bindDn, config.bindPassword)
    const result = await client.search(
      config.searchBase || config.baseDn,
      {
        scope: "sub",
        filter: `(${config.userSearchAttribute}=${escapeLdapFilter(username)})`,
        attributes: [
          "dn",
          config.directoryIdentifierAttribute,
          config.emailAttribute,
          config.displayNameAttribute,
          config.loginIdentifierAttribute,
          "memberOf",
        ],
      }
    )
    if (result.searchEntries.length !== 1) {
      throw new Error("Directory user lookup returned no unique match.")
    }
    const entry = result.searchEntries[0] as unknown as {
      name: string
      attributes: Array<{ type: string; values: Buffer[] | string[] }>
    }
    const first = (value: string | string[]) =>
      Array.isArray(value) ? String(value[0] ?? "") : String(value ?? "")
    const dn = entry.name
    const directoryIdentifierValues = ldapAttributeValue(
      entry.attributes,
      config.directoryIdentifierAttribute
    )
    const emailValues = ldapAttributeValue(entry.attributes, config.emailAttribute)
    const displayNameValues = ldapAttributeValue(
      entry.attributes,
      config.displayNameAttribute
    )
    const loginIdentifierValues = ldapAttributeValue(
      entry.attributes,
      config.loginIdentifierAttribute
    )
    const groups = ldapAttributeValue(entry.attributes, "memberOf")
      .map(String)
    return {
      dn,
      directoryIdentifier:
        first(directoryIdentifierValues) || dn,
      email: first(emailValues),
      displayName:
        first(displayNameValues) || username,
      groups,
      loginIdentifier:
        first(loginIdentifierValues) || username,
    }
  } finally {
    await client.unbind().catch(() => undefined)
  }
}

export async function lookupAndAuthenticate(
  config: LdapConfig,
  username: string,
  password: string
): Promise<LdapUser> {
  if (!username || !password) throw new Error("Username and password are required.")
  if (
    config.requireSecure &&
    config.protocol === "ldap"
  ) {
    throw new Error("LDAP authentication is disabled for insecure connections.")
  }

  const searchClient = createLdapClient(config)
  try {
    await upgradeToTls(searchClient, config)
    await searchClient.bind(config.bindDn, config.bindPassword)
    const result = await searchClient.search(
      config.searchBase || config.baseDn,
      {
        scope: "sub",
        filter: `(${config.userSearchAttribute}=${escapeLdapFilter(username)})`,
        attributes: [
          "dn",
          config.directoryIdentifierAttribute,
          config.emailAttribute,
          config.displayNameAttribute,
          config.loginIdentifierAttribute,
          "memberOf",
        ],
      }
    )
    if (result.searchEntries.length !== 1) {
      throw new Error("Directory user lookup failed.")
    }

    const entry = result.searchEntries[0] as unknown as {
      name: string
      attributes: Array<{ type: string; values: Buffer[] | string[] }>
    }
    const dn = entry.name
    const first = (value: string | string[]) =>
      Array.isArray(value) ? String(value[0] ?? "") : String(value ?? "")
    const directoryIdentifierValues = ldapAttributeValue(
      entry.attributes,
      config.directoryIdentifierAttribute
    )
    const emailValues = ldapAttributeValue(entry.attributes, config.emailAttribute)
    const displayNameValues = ldapAttributeValue(
      entry.attributes,
      config.displayNameAttribute
    )
    const loginIdentifierValues = ldapAttributeValue(
      entry.attributes,
      config.loginIdentifierAttribute
    )
    const groups = ldapAttributeValue(entry.attributes, "memberOf").map(String)
    const directoryIdentifier =
      first(directoryIdentifierValues) || dn

    const userClient = createLdapClient(config)
    try {
      await upgradeToTls(userClient, config)
      await userClient.bind(dn, password)
    } finally {
      await userClient.unbind().catch(() => undefined)
    }

    return {
      dn,
      directoryIdentifier,
      email: first(emailValues),
      displayName: first(displayNameValues) || username,
      groups,
      loginIdentifier: first(loginIdentifierValues) || username,
    }
  } finally {
    await searchClient.unbind().catch(() => undefined)
  }
}
