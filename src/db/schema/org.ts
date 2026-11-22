import {
  boolean,
  index,
  pgTable,
  text,
  timestamp,
  uniqueIndex,
  uuid,
} from "drizzle-orm/pg-core"

import { user } from "./auth"

export const labCode = pgTable(
  "lab_code",
  {
    id: uuid("id").primaryKey().defaultRandom(),
    code: text("code").notNull(),
    name: text("name").notNull(),
    description: text("description").notNull().default(""),
    isActive: boolean("is_active").notNull().default(true),
    createdBy: text("created_by"),
    createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
    updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull(),
  },
  (table) => [uniqueIndex("labCode_code_uidx").on(table.code)]
)

export const sectionCode = pgTable(
  "section_code",
  {
    id: uuid("id").primaryKey().defaultRandom(),
    code: text("code").notNull(),
    name: text("name").notNull(),
    description: text("description").notNull().default(""),
    isActive: boolean("is_active").notNull().default(true),
    createdBy: text("created_by"),
    createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
    updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull(),
  },
  (table) => [
    uniqueIndex("sectionCode_code_uidx").on(table.code),
  ]
)

export const userOrgAssignment = pgTable(
  "user_org_assignment",
  {
    userId: text("user_id")
      .notNull()
      .references(() => user.id, { onDelete: "cascade" }),
    labId: uuid("lab_id").references(() => labCode.id, { onDelete: "restrict" }),
    sectionId: uuid("section_id").references(() => sectionCode.id, { onDelete: "restrict" }),
    assignedBy: text("assigned_by"),
    assignedAt: timestamp("assigned_at", { withTimezone: true }).defaultNow().notNull(),
  },
  (table) => [
    uniqueIndex("userOrgAssignment_userId_uidx").on(table.userId),
  ]
)

export const orgAudit = pgTable(
  "org_audit",
  {
    id: uuid("id").primaryKey().defaultRandom(),
    actorEmail: text("actor_email"),
    action: text("action").notNull(),
    affectedUserId: text("affected_user_id"),
    affectedLabId: uuid("affected_lab_id"),
    affectedSectionId: uuid("affected_section_id"),
    previousValue: text("previous_value"),
    newValue: text("new_value"),
    success: boolean("success").notNull(),
    diagnostic: text("diagnostic").notNull().default(""),
    createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
  },
  (table) => [
    index("orgAudit_createdAt_idx").on(table.createdAt),
  ]
)
