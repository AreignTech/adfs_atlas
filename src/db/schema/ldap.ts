import {
  boolean,
  index,
  integer,
  pgTable,
  uniqueIndex,
  text,
  timestamp,
  uuid,
} from "drizzle-orm/pg-core"

import { user } from "./auth"

export const ldapConfig = pgTable("ldap_config", {
  id: integer("id").primaryKey().default(1),
  enabled: boolean("enabled").notNull().default(false),
  host: text("host").notNull().default(""),
  port: integer("port").notNull().default(389),
  protocol: text("protocol").notNull().default("ldap"),
  baseDn: text("base_dn").notNull().default(""),
  bindDn: text("bind_dn").notNull().default(""),
  bindPasswordEncrypted: text("bind_password_encrypted").notNull().default(""),
  searchBase: text("search_base").notNull().default(""),
  userSearchAttribute: text("user_search_attribute").notNull().default("sAMAccountName"),
  loginIdentifierAttribute: text("login_identifier_attribute").notNull().default("sAMAccountName"),
  emailAttribute: text("email_attribute").notNull().default("mail"),
  displayNameAttribute: text("display_name_attribute").notNull().default("displayName"),
  directoryIdentifierAttribute: text("directory_identifier_attribute").notNull().default("objectGUID"),
  requireSecure: boolean("require_secure").notNull().default(true),
  validateTls: boolean("validate_tls").notNull().default(true),
  tlsCaCertificate: text("tls_ca_certificate").notNull().default(""),
  connectionTimeout: integer("connection_timeout").notNull().default(10000),
  autoProvision: boolean("auto_provision").notNull().default(true),
  requireApproval: boolean("require_approval").notNull().default(false),
  status: text("status").notNull().default("not-tested"),
  lastTestAt: timestamp("last_test_at", { withTimezone: true }),
  lastSuccessfulTestAt: timestamp("last_successful_test_at", { withTimezone: true }),
  lastConfigUpdateAt: timestamp("last_config_update_at", { withTimezone: true }),
  updatedBy: text("updated_by"),
  createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
  updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull(),
})

export const ldapIdentity = pgTable(
  "ldap_identity",
  {
    id: uuid("id").primaryKey().defaultRandom(),
    userId: text("user_id").notNull().references(() => user.id, { onDelete: "cascade" }),
    directoryIdentifier: text("directory_identifier").notNull(),
    directoryDn: text("directory_dn").notNull().default(""),
    linkedAt: timestamp("linked_at", { withTimezone: true }).defaultNow().notNull(),
  },
  (table) => [
    index("ldapIdentity_directoryIdentifier_idx").on(table.directoryIdentifier),
    uniqueIndex("ldapIdentity_directoryIdentifier_uidx").on(table.directoryIdentifier),
    uniqueIndex("ldapIdentity_userId_uidx").on(table.userId),
  ]
)

export const ldapGroupMapping = pgTable("ldap_group_mapping", {
  id: uuid("id").primaryKey().defaultRandom(),
  ldapGroup: text("ldap_group").notNull(),
  atlasRole: text("atlas_role").notNull(),
  enabled: boolean("enabled").notNull().default(true),
  updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull(),
})

export const ldapAudit = pgTable(
  "ldap_audit",
  {
    id: uuid("id").primaryKey().defaultRandom(),
    eventType: text("event_type").notNull(),
    username: text("username"),
    administrator: text("administrator"),
    success: boolean("success").notNull(),
    diagnostic: text("diagnostic").notNull().default(""),
    createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
  },
  (table) => [
    index("ldapAudit_createdAt_idx").on(table.createdAt),
  ]
)
