CREATE TABLE "lab_code" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"description" text DEFAULT '' NOT NULL,
	"is_active" boolean DEFAULT true NOT NULL,
	"created_by" text,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "org_audit" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"actor_email" text,
	"action" text NOT NULL,
	"affected_user_id" text,
	"affected_lab_id" uuid,
	"affected_section_id" uuid,
	"previous_value" text,
	"new_value" text,
	"success" boolean NOT NULL,
	"diagnostic" text DEFAULT '' NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "section_code" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"lab_id" uuid NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"description" text DEFAULT '' NOT NULL,
	"is_active" boolean DEFAULT true NOT NULL,
	"created_by" text,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "user_org_assignment" (
	"user_id" text NOT NULL,
	"lab_id" uuid,
	"section_id" uuid,
	"assigned_by" text,
	"assigned_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
ALTER TABLE "project" ADD COLUMN "lab_id" uuid;--> statement-breakpoint
ALTER TABLE "project" ADD COLUMN "section_id" uuid;--> statement-breakpoint
ALTER TABLE "task" ADD COLUMN "lab_id" uuid;--> statement-breakpoint
ALTER TABLE "task" ADD COLUMN "section_id" uuid;--> statement-breakpoint
ALTER TABLE "section_code" ADD CONSTRAINT "section_code_lab_id_lab_code_id_fk" FOREIGN KEY ("lab_id") REFERENCES "public"."lab_code"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "user_org_assignment" ADD CONSTRAINT "user_org_assignment_user_id_user_id_fk" FOREIGN KEY ("user_id") REFERENCES "public"."user"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "user_org_assignment" ADD CONSTRAINT "user_org_assignment_lab_id_lab_code_id_fk" FOREIGN KEY ("lab_id") REFERENCES "public"."lab_code"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "user_org_assignment" ADD CONSTRAINT "user_org_assignment_section_id_section_code_id_fk" FOREIGN KEY ("section_id") REFERENCES "public"."section_code"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
CREATE UNIQUE INDEX "labCode_code_uidx" ON "lab_code" USING btree ("code");--> statement-breakpoint
CREATE INDEX "orgAudit_createdAt_idx" ON "org_audit" USING btree ("created_at");--> statement-breakpoint
CREATE UNIQUE INDEX "sectionCode_labCode_uidx" ON "section_code" USING btree ("lab_id","code");--> statement-breakpoint
CREATE INDEX "sectionCode_labId_idx" ON "section_code" USING btree ("lab_id");--> statement-breakpoint
CREATE UNIQUE INDEX "userOrgAssignment_userId_uidx" ON "user_org_assignment" USING btree ("user_id");--> statement-breakpoint
ALTER TABLE "project" ADD CONSTRAINT "project_lab_id_lab_code_id_fk" FOREIGN KEY ("lab_id") REFERENCES "public"."lab_code"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "project" ADD CONSTRAINT "project_section_id_section_code_id_fk" FOREIGN KEY ("section_id") REFERENCES "public"."section_code"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "task" ADD CONSTRAINT "task_lab_id_lab_code_id_fk" FOREIGN KEY ("lab_id") REFERENCES "public"."lab_code"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "task" ADD CONSTRAINT "task_section_id_section_code_id_fk" FOREIGN KEY ("section_id") REFERENCES "public"."section_code"("id") ON DELETE restrict ON UPDATE no action;