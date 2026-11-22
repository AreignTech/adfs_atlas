CREATE TABLE "ldap_audit" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"event_type" text NOT NULL,
	"username" text,
	"administrator" text,
	"success" boolean NOT NULL,
	"diagnostic" text DEFAULT '' NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "ldap_config" (
	"id" integer PRIMARY KEY DEFAULT 1 NOT NULL,
	"enabled" boolean DEFAULT false NOT NULL,
	"host" text DEFAULT '' NOT NULL,
	"port" integer DEFAULT 389 NOT NULL,
	"protocol" text DEFAULT 'ldap' NOT NULL,
	"base_dn" text DEFAULT '' NOT NULL,
	"bind_dn" text DEFAULT '' NOT NULL,
	"bind_password_encrypted" text DEFAULT '' NOT NULL,
	"search_base" text DEFAULT '' NOT NULL,
	"user_search_attribute" text DEFAULT 'sAMAccountName' NOT NULL,
	"login_identifier_attribute" text DEFAULT 'sAMAccountName' NOT NULL,
	"email_attribute" text DEFAULT 'mail' NOT NULL,
	"display_name_attribute" text DEFAULT 'displayName' NOT NULL,
	"directory_identifier_attribute" text DEFAULT 'objectGUID' NOT NULL,
	"require_secure" boolean DEFAULT true NOT NULL,
	"validate_tls" boolean DEFAULT true NOT NULL,
	"tls_ca_certificate" text DEFAULT '' NOT NULL,
	"connection_timeout" integer DEFAULT 10000 NOT NULL,
	"auto_provision" boolean DEFAULT true NOT NULL,
	"require_approval" boolean DEFAULT false NOT NULL,
	"status" text DEFAULT 'not-tested' NOT NULL,
	"last_test_at" timestamp with time zone,
	"last_successful_test_at" timestamp with time zone,
	"last_config_update_at" timestamp with time zone,
	"updated_by" text,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "ldap_group_mapping" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"ldap_group" text NOT NULL,
	"atlas_role" text NOT NULL,
	"enabled" boolean DEFAULT true NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "ldap_identity" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"user_id" text NOT NULL,
	"directory_identifier" text NOT NULL,
	"directory_dn" text DEFAULT '' NOT NULL,
	"linked_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
ALTER TABLE "ldap_identity" ADD CONSTRAINT "ldap_identity_user_id_user_id_fk" FOREIGN KEY ("user_id") REFERENCES "public"."user"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
CREATE INDEX "ldapAudit_createdAt_idx" ON "ldap_audit" USING btree ("created_at");--> statement-breakpoint
CREATE INDEX "ldapIdentity_directoryIdentifier_idx" ON "ldap_identity" USING btree ("directory_identifier");--> statement-breakpoint
CREATE UNIQUE INDEX "ldapIdentity_directoryIdentifier_uidx" ON "ldap_identity" USING btree ("directory_identifier");--> statement-breakpoint
CREATE UNIQUE INDEX "ldapIdentity_userId_uidx" ON "ldap_identity" USING btree ("user_id");