/*
 * ATLAS v2.0 - reference security data.
 *
 * Mirrors drizzle/0003_loving_silverclaw.sql exactly: the same 21 permission
 * keys, the same three system roles, and the same role -> permission sets.
 * Idempotent: safe to run against a database that already contains them.
 */

SET NOCOUNT ON;

DECLARE @permissions TABLE (permission_key NVARCHAR(100) NOT NULL, description NVARCHAR(255) NOT NULL);
INSERT INTO @permissions (permission_key, description) VALUES
    (N'users.view', N'View application users'),
    (N'users.create', N'Create application users'),
    (N'users.edit', N'Edit application users'),
    (N'users.delete', N'Delete application users'),
    (N'users.assign_roles', N'Assign roles to users'),
    (N'roles.view', N'View application roles'),
    (N'roles.create', N'Create application roles'),
    (N'roles.edit', N'Edit application roles'),
    (N'roles.delete', N'Delete eligible application roles'),
    (N'admin.access', N'Access administrative navigation'),
    (N'admin.settings', N'Manage application settings'),
    (N'admin.authentication', N'Manage authentication settings'),
    (N'admin.audit_logs', N'View audit logs'),
    (N'tasks.view', N'View tasks'),
    (N'tasks.create', N'Create tasks'),
    (N'tasks.edit', N'Edit tasks'),
    (N'tasks.delete', N'Delete tasks'),
    (N'tasks.assign', N'Assign tasks'),
    (N'tasks.manage_all', N'Manage all tasks'),
    (N'reports.view', N'View reports'),
    (N'reports.manage', N'Manage reports');

INSERT INTO dbo.app_permission (permission_key, description)
SELECT p.permission_key, p.description
FROM @permissions p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.app_permission existing WHERE existing.permission_key = p.permission_key
);

DECLARE @roles TABLE (name NVARCHAR(255) NOT NULL, description NVARCHAR(255) NOT NULL);
INSERT INTO @roles (name, description) VALUES
    (N'Users', N'Standard application users.'),
    (N'Supervisors', N'Operational task supervisors.'),
    (N'Administrators', N'Full application administrators.');

INSERT INTO dbo.app_role (name, description, is_system_role, is_active)
SELECT r.name, r.description, 1, 1
FROM @roles r
WHERE NOT EXISTS (SELECT 1 FROM dbo.app_role existing WHERE existing.name = r.name);

/* Users: base task permissions. */
INSERT INTO dbo.app_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM dbo.app_role r
CROSS JOIN dbo.app_permission p
WHERE r.name = N'Users'
  AND p.permission_key IN (N'tasks.view', N'tasks.create', N'tasks.edit', N'tasks.assign')
  AND NOT EXISTS (
      SELECT 1 FROM dbo.app_role_permission rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

/* Supervisors: base plus oversight. */
INSERT INTO dbo.app_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM dbo.app_role r
CROSS JOIN dbo.app_permission p
WHERE r.name = N'Supervisors'
  AND p.permission_key IN (
      N'users.view', N'tasks.view', N'tasks.create', N'tasks.edit',
      N'tasks.assign', N'tasks.manage_all', N'reports.view'
  )
  AND NOT EXISTS (
      SELECT 1 FROM dbo.app_role_permission rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

/* Administrators: every permission. */
INSERT INTO dbo.app_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM dbo.app_role r
CROSS JOIN dbo.app_permission p
WHERE r.name = N'Administrators'
  AND NOT EXISTS (
      SELECT 1 FROM dbo.app_role_permission rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );
