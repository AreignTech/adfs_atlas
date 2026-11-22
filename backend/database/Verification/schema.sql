SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @expected TABLE (name SYSNAME PRIMARY KEY);
INSERT INTO @expected VALUES
    ('user'), ('session'), ('account'), ('verification'),
    ('member'), ('project'), ('task'), ('project_member'),
    ('lab_code'), ('section_code'), ('user_org_assignment'), ('org_audit'),
    ('app_role'), ('app_permission'), ('app_role_permission'), ('app_user_role'), ('rbac_audit'),
    ('ldap_config'), ('ldap_identity'), ('ldap_group_mapping'), ('ldap_audit');

IF EXISTS (SELECT 1 FROM @expected e WHERE OBJECT_ID('dbo.' + QUOTENAME(e.name), 'U') IS NULL)
    THROW 51000, 'An application entity is missing.', 1;

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE schema_id = SCHEMA_ID('dbo') AND (is_disabled = 1 OR is_not_trusted = 1)
)
    THROW 51000, 'A foreign key is disabled or untrusted.', 1;

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE schema_id = SCHEMA_ID('dbo') AND (is_disabled = 1 OR is_not_trusted = 1)
)
    THROW 51000, 'A check constraint is disabled or untrusted.', 1;

IF (SELECT COUNT(*) FROM dbo.app_permission) <> 21
    THROW 51000, 'Expected exactly 21 seeded permissions.', 1;

IF (SELECT COUNT(*) FROM dbo.app_role WHERE is_system_role = 1 AND name IN ('Users', 'Supervisors', 'Administrators')) <> 3
    THROW 51000, 'A reference role is missing.', 1;

IF (SELECT COUNT(*) FROM dbo.app_role_permission rp JOIN dbo.app_role r ON r.id = rp.role_id WHERE r.name = 'Users') <> 4
    THROW 51000, 'Users permission set differs from the source.', 1;
IF (SELECT COUNT(*) FROM dbo.app_role_permission rp JOIN dbo.app_role r ON r.id = rp.role_id WHERE r.name = 'Supervisors') <> 7
    THROW 51000, 'Supervisors permission set differs from the source.', 1;
IF (SELECT COUNT(*) FROM dbo.app_role_permission rp JOIN dbo.app_role r ON r.id = rp.role_id WHERE r.name = 'Administrators') <> 21
    THROW 51000, 'Administrators permission set differs from the source.', 1;

IF EXISTS (SELECT 1 FROM dbo.task t LEFT JOIN dbo.project p ON p.id = t.project_id WHERE p.id IS NULL)
    THROW 51000, 'Orphan task detected.', 1;
IF EXISTS (SELECT 1 FROM dbo.account a LEFT JOIN dbo.[user] u ON u.id = a.user_id WHERE u.id IS NULL)
    THROW 51000, 'Orphan account detected.', 1;

SELECT 'PASS: 21 entities, trusted constraints, reference permission sets and relationship integrity.';
