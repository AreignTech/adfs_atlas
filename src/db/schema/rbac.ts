import {
  boolean,
  index,
  pgTable,
  primaryKey,
  text,
  timestamp,
  uuid,
} from "drizzle-orm/pg-core"

import { user } from "./auth"

export const appRole = pgTable("app_role", {
  id: uuid("id").primaryKey().defaultRandom(),
  name: text("name").notNull().unique(),
  description: text("description").notNull().default(""),
  isSystemRole: boolean("is_system_role").notNull().default(false),
  isActive: boolean("is_active").notNull().default(true),
  createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
  updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull(),
})

export const appPermission = pgTable("app_permission", {
  id: uuid("id").primaryKey().defaultRandom(),
  permissionKey: text("permission_key").notNull().unique(),
  description: text("description").notNull().default(""),
  createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
})

export const appRolePermission = pgTable(
  "app_role_permission",
  {
    roleId: uuid("role_id")
      .notNull()
      .references(() => appRole.id, { onDelete: "cascade" }),
    permissionId: uuid("permission_id")
      .notNull()
      .references(() => appPermission.id, { onDelete: "cascade" }),
  },
  (table) => [
    primaryKey({ columns: [table.roleId, table.permissionId] }),
  ]
)

export const appUserRole = pgTable(
  "app_user_role",
  {
    userId: text("user_id")
      .notNull()
      .references(() => user.id, { onDelete: "cascade" }),
    roleId: uuid("role_id")
      .notNull()
      .references(() => appRole.id, { onDelete: "restrict" }),
    assignedBy: text("assigned_by"),
    assignedAt: timestamp("assigned_at", { withTimezone: true }).defaultNow().notNull(),
  },
  (table) => [
    primaryKey({ columns: [table.userId, table.roleId] }),
    index("appUserRole_roleId_idx").on(table.roleId),
  ]
)

export const rbacAudit = pgTable(
  "rbac_audit",
  {
    id: uuid("id").primaryKey().defaultRandom(),
    actorUserId: text("actor_user_id"),
    actorEmail: text("actor_email"),
    action: text("action").notNull(),
    affectedUserId: text("affected_user_id"),
    affectedRoleId: uuid("affected_role_id"),
    previousValue: text("previous_value"),
    newValue: text("new_value"),
    success: boolean("success").notNull(),
    diagnostic: text("diagnostic").notNull().default(""),
    createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
  },
  (table) => [
    index("rbacAudit_createdAt_idx").on(table.createdAt),
  ]
)
