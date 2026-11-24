/*
 * ATLAS v2.0 - initial SQL Server schema.
 *
 * This migration is applied once by the embedded migration runner in
 * Atlas.Infrastructure (tracked in dbo.__AtlasMigrations). It is written with
 * IF OBJECT_ID guards so a partially-created development database can be
 * repaired by re-running it.
 *
 * Conventions
 *  - Identifiers keep the existing snake_case column names so the one-off
 *    PostgreSQL data migration is a straight column copy.
 *  - PostgreSQL enums become NVARCHAR + CHECK constraints, preserving the
 *    exact string values used by the frontend.
 *  - UUID becomes UNIQUEIDENTIFIER, TEXT becomes NVARCHAR(MAX), BOOLEAN
 *    becomes BIT, DATE stays DATE and TIMESTAMPTZ becomes DATETIMEOFFSET(7).
 *  - Timestamps are stored in UTC; defaults use SYSUTCDATETIME().
 */

SET NOCOUNT ON;

/* ------------------------------------------------------------------------- */
/* Sequences                                                                  */
/* ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.task_reference_seq', 'SO') IS NULL
    CREATE SEQUENCE dbo.task_reference_seq AS INT START WITH 1 INCREMENT BY 1;

/* ------------------------------------------------------------------------- */
/* Authentication (Better Auth compatible table shape)                        */
/* ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.[user]', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[user] (
        id              NVARCHAR(255)  NOT NULL CONSTRAINT PK_user PRIMARY KEY,
        name            NVARCHAR(255)  NOT NULL,
        email           NVARCHAR(255)  NOT NULL,
        email_verified  BIT            NOT NULL CONSTRAINT DF_user_email_verified DEFAULT (0),
        image           NVARCHAR(MAX)  NULL,
        created_at      DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_user_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at      DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_user_updated_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX user_email_uidx ON dbo.[user] (email);
END;

IF OBJECT_ID('dbo.session', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.session (
        id          NVARCHAR(255) NOT NULL CONSTRAINT PK_session PRIMARY KEY,
        expires_at  DATETIMEOFFSET(7) NOT NULL,
        token       NVARCHAR(255) NOT NULL,
        created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_session_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_session_updated_at DEFAULT (SYSUTCDATETIME()),
        ip_address  NVARCHAR(255) NULL,
        user_agent  NVARCHAR(MAX) NULL,
        user_id     NVARCHAR(255) NOT NULL
            CONSTRAINT FK_session_user REFERENCES dbo.[user] (id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX session_token_uidx ON dbo.session (token);
    CREATE INDEX session_userId_idx ON dbo.session (user_id);
END;

IF OBJECT_ID('dbo.account', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.account (
        id                        NVARCHAR(255) NOT NULL CONSTRAINT PK_account PRIMARY KEY,
        account_id                NVARCHAR(255) NOT NULL,
        provider_id               NVARCHAR(255) NOT NULL,
        user_id                   NVARCHAR(255) NOT NULL
            CONSTRAINT FK_account_user REFERENCES dbo.[user] (id) ON DELETE CASCADE,
        access_token              NVARCHAR(MAX) NULL,
        refresh_token             NVARCHAR(MAX) NULL,
        id_token                  NVARCHAR(MAX) NULL,
        access_token_expires_at   DATETIMEOFFSET(7) NULL,
        refresh_token_expires_at  DATETIMEOFFSET(7) NULL,
        scope                     NVARCHAR(MAX) NULL,
        password                  NVARCHAR(MAX) NULL,
        created_at                DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_account_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at                DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_account_updated_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX account_userId_idx ON dbo.account (user_id);
END;

IF OBJECT_ID('dbo.verification', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.verification (
        id          NVARCHAR(255) NOT NULL CONSTRAINT PK_verification PRIMARY KEY,
        identifier  NVARCHAR(255) NOT NULL,
        value       NVARCHAR(MAX) NOT NULL,
        expires_at  DATETIMEOFFSET(7) NOT NULL,
        created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_verification_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_verification_updated_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX verification_identifier_idx ON dbo.verification (identifier);
END;

/* ------------------------------------------------------------------------- */
/* Organization structure                                                     */
/* ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.lab_code', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.lab_code (
        id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_lab_code PRIMARY KEY
            CONSTRAINT DF_lab_code_id DEFAULT (NEWID()),
        code        NVARCHAR(40)  NOT NULL,
        name        NVARCHAR(120) NOT NULL,
        description NVARCHAR(MAX) NOT NULL CONSTRAINT DF_lab_code_description DEFAULT (N''),
        is_active   BIT NOT NULL CONSTRAINT DF_lab_code_is_active DEFAULT (1),
        created_by  NVARCHAR(255) NULL,
        created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_lab_code_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_lab_code_updated_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX labCode_code_uidx ON dbo.lab_code (code);
END;

IF OBJECT_ID('dbo.section_code', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.section_code (
        id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_section_code PRIMARY KEY
            CONSTRAINT DF_section_code_id DEFAULT (NEWID()),
        code        NVARCHAR(40)  NOT NULL,
        name        NVARCHAR(120) NOT NULL,
        description NVARCHAR(MAX) NOT NULL CONSTRAINT DF_section_code_description DEFAULT (N''),
        is_active   BIT NOT NULL CONSTRAINT DF_section_code_is_active DEFAULT (1),
        created_by  NVARCHAR(255) NULL,
        created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_section_code_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_section_code_updated_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX sectionCode_code_uidx ON dbo.section_code (code);
END;

IF OBJECT_ID('dbo.user_org_assignment', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.user_org_assignment (
        user_id     NVARCHAR(255) NOT NULL
            CONSTRAINT PK_user_org_assignment PRIMARY KEY
            CONSTRAINT FK_user_org_assignment_user REFERENCES dbo.[user] (id) ON DELETE CASCADE,
        lab_id      UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_user_org_assignment_lab REFERENCES dbo.lab_code (id),
        section_id  UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_user_org_assignment_section REFERENCES dbo.section_code (id),
        assigned_by NVARCHAR(255) NULL,
        assigned_at DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_user_org_assignment_assigned_at DEFAULT (SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('dbo.org_audit', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.org_audit (
        id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_org_audit PRIMARY KEY
            CONSTRAINT DF_org_audit_id DEFAULT (NEWID()),
        actor_email         NVARCHAR(255) NULL,
        action              NVARCHAR(255) NOT NULL,
        affected_user_id    NVARCHAR(255) NULL,
        affected_lab_id     UNIQUEIDENTIFIER NULL,
        affected_section_id UNIQUEIDENTIFIER NULL,
        previous_value      NVARCHAR(MAX) NULL,
        new_value           NVARCHAR(MAX) NULL,
        success             BIT NOT NULL,
        diagnostic          NVARCHAR(MAX) NOT NULL CONSTRAINT DF_org_audit_diagnostic DEFAULT (N''),
        created_at          DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_org_audit_created_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX orgAudit_createdAt_idx ON dbo.org_audit (created_at);
END;

/* ------------------------------------------------------------------------- */
/* Work management                                                            */
/* ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.member', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.member (
        id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_member PRIMARY KEY
            CONSTRAINT DF_member_id DEFAULT (NEWID()),
        user_id        NVARCHAR(255) NOT NULL
            CONSTRAINT FK_member_user REFERENCES dbo.[user] (id) ON DELETE CASCADE,
        role           NVARCHAR(255) NOT NULL CONSTRAINT DF_member_role DEFAULT (N'Member'),
        status         NVARCHAR(20)  NOT NULL CONSTRAINT DF_member_status DEFAULT (N'invited'),
        joined_at      DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_member_joined_at DEFAULT (SYSUTCDATETIME()),
        last_active_at DATETIMEOFFSET(7) NULL,
        CONSTRAINT CK_member_status CHECK (status IN (N'active', N'invited', N'away'))
    );
    CREATE UNIQUE INDEX member_userId_uidx ON dbo.member (user_id);
    CREATE INDEX member_status_idx ON dbo.member (status);
END;

IF OBJECT_ID('dbo.project', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.project (
        id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_project PRIMARY KEY
            CONSTRAINT DF_project_id DEFAULT (NEWID()),
        name        NVARCHAR(255) NOT NULL,
        [key]       NVARCHAR(10)  NOT NULL,
        description NVARCHAR(MAX) NOT NULL CONSTRAINT DF_project_description DEFAULT (N''),
        status      NVARCHAR(20)  NOT NULL CONSTRAINT DF_project_status DEFAULT (N'planning'),
        color       NVARCHAR(64)  NOT NULL CONSTRAINT DF_project_color DEFAULT (N'var(--chart-1)'),
        owner_id    UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_project_owner REFERENCES dbo.member (id),
        start_date  DATE NOT NULL,
        due_date    DATE NOT NULL,
        lab_id      UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_project_lab REFERENCES dbo.lab_code (id),
        section_id  UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_project_section REFERENCES dbo.section_code (id),
        created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_project_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_project_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_project_status CHECK (status IN (N'planning', N'active', N'on-hold', N'completed'))
    );
    CREATE UNIQUE INDEX project_key_uidx ON dbo.project ([key]);
    CREATE INDEX project_status_idx ON dbo.project (status);
    CREATE INDEX project_ownerId_idx ON dbo.project (owner_id);
    CREATE INDEX project_dueDate_idx ON dbo.project (due_date);
END;

IF OBJECT_ID('dbo.project_member', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_member (
        project_id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_project_member_project REFERENCES dbo.project (id) ON DELETE CASCADE,
        member_id  UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_project_member_member REFERENCES dbo.member (id) ON DELETE CASCADE,
        CONSTRAINT PK_project_member PRIMARY KEY (project_id, member_id)
    );
    CREATE INDEX project_member_memberId_idx ON dbo.project_member (member_id);
END;

IF OBJECT_ID('dbo.task', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.task (
        id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_task PRIMARY KEY
            CONSTRAINT DF_task_id DEFAULT (NEWID()),
        reference    NVARCHAR(20) NOT NULL
            CONSTRAINT DF_task_reference DEFAULT
                ('TSK-' + RIGHT('0000' + CAST(NEXT VALUE FOR dbo.task_reference_seq AS VARCHAR(10)), 4)),
        project_id   UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_task_project REFERENCES dbo.project (id) ON DELETE CASCADE,
        title        NVARCHAR(300) NOT NULL,
        description  NVARCHAR(MAX) NOT NULL CONSTRAINT DF_task_description DEFAULT (N''),
        status       NVARCHAR(20) NOT NULL CONSTRAINT DF_task_status DEFAULT (N'todo'),
        priority     NVARCHAR(20) NOT NULL CONSTRAINT DF_task_priority DEFAULT (N'medium'),
        assignee_id  UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_task_assignee REFERENCES dbo.member (id) ON DELETE SET NULL,
        due_date     DATE NOT NULL,
        lab_id       UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_task_lab REFERENCES dbo.lab_code (id),
        section_id   UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_task_section REFERENCES dbo.section_code (id),
        position     INT NOT NULL CONSTRAINT DF_task_position DEFAULT (0),
        completed_at DATETIMEOFFSET(7) NULL,
        created_at   DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_task_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at   DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_task_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_task_status CHECK (status IN (N'todo', N'in-progress', N'in-review', N'completed')),
        CONSTRAINT CK_task_priority CHECK (priority IN (N'urgent', N'high', N'medium', N'low'))
    );
    CREATE UNIQUE INDEX task_reference_uidx ON dbo.task (reference);
    CREATE INDEX task_projectId_idx ON dbo.task (project_id);
    CREATE INDEX task_status_idx ON dbo.task (status);
    CREATE INDEX task_dueDate_idx ON dbo.task (due_date);
    CREATE INDEX task_assigneeId_idx ON dbo.task (assignee_id);
    CREATE INDEX task_projectId_status_idx ON dbo.task (project_id, status);
END;

/* ------------------------------------------------------------------------- */
/* RBAC                                                                       */
/* ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.app_role', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.app_role (
        id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_app_role PRIMARY KEY
            CONSTRAINT DF_app_role_id DEFAULT (NEWID()),
        name           NVARCHAR(255) NOT NULL,
        description    NVARCHAR(MAX) NOT NULL CONSTRAINT DF_app_role_description DEFAULT (N''),
        is_system_role BIT NOT NULL CONSTRAINT DF_app_role_is_system_role DEFAULT (0),
        is_active      BIT NOT NULL CONSTRAINT DF_app_role_is_active DEFAULT (1),
        created_at     DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_app_role_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at     DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_app_role_updated_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX appRole_name_uidx ON dbo.app_role (name);
END;

IF OBJECT_ID('dbo.app_permission', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.app_permission (
        id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_app_permission PRIMARY KEY
            CONSTRAINT DF_app_permission_id DEFAULT (NEWID()),
        permission_key NVARCHAR(100) NOT NULL,
        description    NVARCHAR(MAX) NOT NULL CONSTRAINT DF_app_permission_description DEFAULT (N''),
        created_at     DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_app_permission_created_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX appPermission_permissionKey_uidx ON dbo.app_permission (permission_key);
END;

IF OBJECT_ID('dbo.app_role_permission', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.app_role_permission (
        role_id       UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_app_role_permission_role REFERENCES dbo.app_role (id) ON DELETE CASCADE,
        permission_id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_app_role_permission_permission REFERENCES dbo.app_permission (id) ON DELETE CASCADE,
        CONSTRAINT PK_app_role_permission PRIMARY KEY (role_id, permission_id)
    );
END;

IF OBJECT_ID('dbo.app_user_role', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.app_user_role (
        user_id     NVARCHAR(255) NOT NULL
            CONSTRAINT FK_app_user_role_user REFERENCES dbo.[user] (id) ON DELETE CASCADE,
        role_id     UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_app_user_role_role REFERENCES dbo.app_role (id),
        assigned_by NVARCHAR(255) NULL,
        assigned_at DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_app_user_role_assigned_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_app_user_role PRIMARY KEY (user_id, role_id)
    );
    CREATE INDEX appUserRole_roleId_idx ON dbo.app_user_role (role_id);
END;

IF OBJECT_ID('dbo.rbac_audit', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.rbac_audit (
        id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rbac_audit PRIMARY KEY
            CONSTRAINT DF_rbac_audit_id DEFAULT (NEWID()),
        actor_user_id    NVARCHAR(255) NULL,
        actor_email      NVARCHAR(255) NULL,
        action           NVARCHAR(255) NOT NULL,
        affected_user_id NVARCHAR(255) NULL,
        affected_role_id UNIQUEIDENTIFIER NULL,
        previous_value   NVARCHAR(MAX) NULL,
        new_value        NVARCHAR(MAX) NULL,
        success          BIT NOT NULL,
        diagnostic       NVARCHAR(MAX) NOT NULL CONSTRAINT DF_rbac_audit_diagnostic DEFAULT (N''),
        created_at       DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_rbac_audit_created_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX rbacAudit_createdAt_idx ON dbo.rbac_audit (created_at);
END;

/* ------------------------------------------------------------------------- */
/* LDAP                                                                       */
/* ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.ldap_config', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ldap_config (
        id                            INT NOT NULL CONSTRAINT PK_ldap_config PRIMARY KEY
            CONSTRAINT DF_ldap_config_id DEFAULT (1),
        enabled                       BIT NOT NULL CONSTRAINT DF_ldap_config_enabled DEFAULT (0),
        host                          NVARCHAR(255) NOT NULL CONSTRAINT DF_ldap_config_host DEFAULT (N''),
        port                          INT NOT NULL CONSTRAINT DF_ldap_config_port DEFAULT (389),
        protocol                      NVARCHAR(20) NOT NULL CONSTRAINT DF_ldap_config_protocol DEFAULT (N'ldap'),
        base_dn                       NVARCHAR(1000) NOT NULL CONSTRAINT DF_ldap_config_base_dn DEFAULT (N''),
        bind_dn                       NVARCHAR(1000) NOT NULL CONSTRAINT DF_ldap_config_bind_dn DEFAULT (N''),
        bind_password_encrypted       NVARCHAR(MAX) NOT NULL CONSTRAINT DF_ldap_config_bind_password DEFAULT (N''),
        search_base                   NVARCHAR(1000) NOT NULL CONSTRAINT DF_ldap_config_search_base DEFAULT (N''),
        user_search_attribute         NVARCHAR(100) NOT NULL CONSTRAINT DF_ldap_config_user_search DEFAULT (N'sAMAccountName'),
        login_identifier_attribute    NVARCHAR(100) NOT NULL CONSTRAINT DF_ldap_config_login_identifier DEFAULT (N'sAMAccountName'),
        email_attribute               NVARCHAR(100) NOT NULL CONSTRAINT DF_ldap_config_email DEFAULT (N'mail'),
        display_name_attribute        NVARCHAR(100) NOT NULL CONSTRAINT DF_ldap_config_display_name DEFAULT (N'displayName'),
        directory_identifier_attribute NVARCHAR(100) NOT NULL CONSTRAINT DF_ldap_config_directory_identifier DEFAULT (N'objectGUID'),
        require_secure                BIT NOT NULL CONSTRAINT DF_ldap_config_require_secure DEFAULT (1),
        validate_tls                  BIT NOT NULL CONSTRAINT DF_ldap_config_validate_tls DEFAULT (1),
        tls_ca_certificate            NVARCHAR(MAX) NOT NULL CONSTRAINT DF_ldap_config_tls_ca DEFAULT (N''),
        connection_timeout            INT NOT NULL CONSTRAINT DF_ldap_config_timeout DEFAULT (10000),
        auto_provision                BIT NOT NULL CONSTRAINT DF_ldap_config_auto_provision DEFAULT (1),
        require_approval              BIT NOT NULL CONSTRAINT DF_ldap_config_require_approval DEFAULT (0),
        status                        NVARCHAR(40) NOT NULL CONSTRAINT DF_ldap_config_status DEFAULT (N'not-tested'),
        last_test_at                  DATETIMEOFFSET(7) NULL,
        last_successful_test_at       DATETIMEOFFSET(7) NULL,
        last_config_update_at         DATETIMEOFFSET(7) NULL,
        updated_by                    NVARCHAR(255) NULL,
        created_at                    DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ldap_config_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at                    DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ldap_config_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_ldap_config_singleton CHECK (id = 1),
        CONSTRAINT CK_ldap_config_protocol CHECK (protocol IN (N'ldap', N'ldaps', N'starttls'))
    );
END;

IF OBJECT_ID('dbo.ldap_identity', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ldap_identity (
        id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ldap_identity PRIMARY KEY
            CONSTRAINT DF_ldap_identity_id DEFAULT (NEWID()),
        user_id              NVARCHAR(255) NOT NULL
            CONSTRAINT FK_ldap_identity_user REFERENCES dbo.[user] (id) ON DELETE CASCADE,
        directory_identifier NVARCHAR(1000) NOT NULL,
        directory_dn         NVARCHAR(1000) NOT NULL CONSTRAINT DF_ldap_identity_dn DEFAULT (N''),
        linked_at            DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ldap_identity_linked_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX ldapIdentity_directoryIdentifier_idx ON dbo.ldap_identity (directory_identifier);
    CREATE UNIQUE INDEX ldapIdentity_directoryIdentifier_uidx ON dbo.ldap_identity (directory_identifier);
    CREATE UNIQUE INDEX ldapIdentity_userId_uidx ON dbo.ldap_identity (user_id);
END;

IF OBJECT_ID('dbo.ldap_group_mapping', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ldap_group_mapping (
        id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ldap_group_mapping PRIMARY KEY
            CONSTRAINT DF_ldap_group_mapping_id DEFAULT (NEWID()),
        ldap_group NVARCHAR(1000) NOT NULL,
        atlas_role NVARCHAR(100)  NOT NULL,
        enabled    BIT NOT NULL CONSTRAINT DF_ldap_group_mapping_enabled DEFAULT (1),
        updated_at DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ldap_group_mapping_updated_at DEFAULT (SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('dbo.ldap_audit', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ldap_audit (
        id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ldap_audit PRIMARY KEY
            CONSTRAINT DF_ldap_audit_id DEFAULT (NEWID()),
        event_type    NVARCHAR(255) NOT NULL,
        username      NVARCHAR(255) NULL,
        administrator NVARCHAR(255) NULL,
        success       BIT NOT NULL,
        diagnostic    NVARCHAR(MAX) NOT NULL CONSTRAINT DF_ldap_audit_diagnostic DEFAULT (N''),
        created_at    DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ldap_audit_created_at DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX ldapAudit_createdAt_idx ON dbo.ldap_audit (created_at);
END;

/* ------------------------------------------------------------------------- */
/* Better Auth compatibility objects retained for migration only             */
/* ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.__AtlasMigrations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.__AtlasMigrations (
        script_name NVARCHAR(255) NOT NULL CONSTRAINT PK___AtlasMigrations PRIMARY KEY,
        applied_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF___AtlasMigrations_applied_at DEFAULT (SYSUTCDATETIME())
    );
END;
