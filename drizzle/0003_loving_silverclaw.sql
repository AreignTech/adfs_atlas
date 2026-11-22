CREATE TABLE "app_permission" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"permission_key" text NOT NULL,
	"description" text DEFAULT '' NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "app_permission_permission_key_unique" UNIQUE("permission_key")
);
--> statement-breakpoint
CREATE TABLE "app_role" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"name" text NOT NULL,
	"description" text DEFAULT '' NOT NULL,
	"is_system_role" boolean DEFAULT false NOT NULL,
	"is_active" boolean DEFAULT true NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "app_role_name_unique" UNIQUE("name")
);
--> statement-breakpoint
CREATE TABLE "app_role_permission" (
	"role_id" uuid NOT NULL,
	"permission_id" uuid NOT NULL,
	CONSTRAINT "app_role_permission_role_id_permission_id_pk" PRIMARY KEY("role_id","permission_id")
);
--> statement-breakpoint
CREATE TABLE "app_user_role" (
	"user_id" text NOT NULL,
	"role_id" uuid NOT NULL,
	"assigned_by" text,
	"assigned_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "app_user_role_user_id_role_id_pk" PRIMARY KEY("user_id","role_id")
);
--> statement-breakpoint
CREATE TABLE "rbac_audit" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"actor_user_id" text,
	"actor_email" text,
	"action" text NOT NULL,
	"affected_user_id" text,
	"affected_role_id" uuid,
	"previous_value" text,
	"new_value" text,
	"success" boolean NOT NULL,
	"diagnostic" text DEFAULT '' NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
ALTER TABLE "app_role_permission" ADD CONSTRAINT "app_role_permission_role_id_app_role_id_fk" FOREIGN KEY ("role_id") REFERENCES "public"."app_role"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "app_role_permission" ADD CONSTRAINT "app_role_permission_permission_id_app_permission_id_fk" FOREIGN KEY ("permission_id") REFERENCES "public"."app_permission"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "app_user_role" ADD CONSTRAINT "app_user_role_user_id_user_id_fk" FOREIGN KEY ("user_id") REFERENCES "public"."user"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "app_user_role" ADD CONSTRAINT "app_user_role_role_id_app_role_id_fk" FOREIGN KEY ("role_id") REFERENCES "public"."app_role"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
CREATE INDEX "appUserRole_roleId_idx" ON "app_user_role" USING btree ("role_id");--> statement-breakpoint
CREATE INDEX "rbacAudit_createdAt_idx" ON "rbac_audit" USING btree ("created_at");

--> statement-breakpoint
INSERT INTO "app_permission" ("permission_key", "description") VALUES
  ('users.view', 'View application users'),
  ('users.create', 'Create application users'),
  ('users.edit', 'Edit application users'),
  ('users.delete', 'Delete application users'),
  ('users.assign_roles', 'Assign roles to users'),
  ('roles.view', 'View application roles'),
  ('roles.create', 'Create application roles'),
  ('roles.edit', 'Edit application roles'),
  ('roles.delete', 'Delete eligible application roles'),
  ('admin.access', 'Access administrative navigation'),
  ('admin.settings', 'Manage application settings'),
  ('admin.authentication', 'Manage authentication settings'),
  ('admin.audit_logs', 'View audit logs'),
  ('tasks.view', 'View tasks'),
  ('tasks.create', 'Create tasks'),
  ('tasks.edit', 'Edit tasks'),
  ('tasks.delete', 'Delete tasks'),
  ('tasks.assign', 'Assign tasks'),
  ('tasks.manage_all', 'Manage all tasks'),
  ('reports.view', 'View reports'),
  ('reports.manage', 'Manage reports')
ON CONFLICT ("permission_key") DO NOTHING;

--> statement-breakpoint
INSERT INTO "app_role" ("name", "description", "is_system_role") VALUES
  ('Users', 'Standard application users.', true),
  ('Supervisors', 'Operational task supervisors.', true),
  ('Administrators', 'Full application administrators.', true)
ON CONFLICT ("name") DO NOTHING;

--> statement-breakpoint
INSERT INTO "app_role_permission" ("role_id", "permission_id")
SELECT r.id, p.id
FROM "app_role" r
CROSS JOIN "app_permission" p
WHERE r.name = 'Users'
  AND p.permission_key IN ('tasks.view', 'tasks.create', 'tasks.edit', 'tasks.assign')
ON CONFLICT DO NOTHING;

--> statement-breakpoint
INSERT INTO "app_role_permission" ("role_id", "permission_id")
SELECT r.id, p.id
FROM "app_role" r
CROSS JOIN "app_permission" p
WHERE r.name = 'Supervisors'
  AND p.permission_key IN (
    'users.view', 'tasks.view', 'tasks.create', 'tasks.edit',
    'tasks.assign', 'tasks.manage_all', 'reports.view'
  )
ON CONFLICT DO NOTHING;

--> statement-breakpoint
INSERT INTO "app_role_permission" ("role_id", "permission_id")
SELECT r.id, p.id
FROM "app_role" r
CROSS JOIN "app_permission" p
WHERE r.name = 'Administrators'
ON CONFLICT DO NOTHING;

--> statement-breakpoint
UPDATE "member" m
SET role = 'Administrators'
FROM "user" u
WHERE m.user_id = u.id
  AND lower(u.email) = 'nathan@aol.com';

--> statement-breakpoint
UPDATE "member"
SET role = 'Administrators'
WHERE lower(role) LIKE '%admin%';

--> statement-breakpoint
UPDATE "member"
SET role = 'Supervisors'
WHERE lower(role) LIKE '%supervisor%'
   OR lower(role) IN ('engineering lead', 'product lead');

--> statement-breakpoint
UPDATE "member"
SET role = 'Users'
WHERE role NOT IN ('Administrators', 'Supervisors');

--> statement-breakpoint
INSERT INTO "app_user_role" ("user_id", "role_id", "assigned_by")
SELECT m.user_id, r.id, 'migration'
FROM "member" m
JOIN "app_role" r ON r.name = m.role
ON CONFLICT DO NOTHING;