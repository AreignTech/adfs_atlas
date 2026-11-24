-- Administrators can deactivate an account (status = 'inactive'), which blocks
-- sign-in and revokes existing sessions. 'away' remains a presence value only.
IF EXISTS (SELECT 1 FROM sys.check_constraints cc
           WHERE cc.name = N'CK_member_status'
             AND cc.parent_object_id = OBJECT_ID(N'dbo.member')
             AND cc.definition NOT LIKE N'%inactive%')
BEGIN
    ALTER TABLE dbo.member DROP CONSTRAINT CK_member_status;
    ALTER TABLE dbo.member ADD CONSTRAINT CK_member_status CHECK (status IN (N'active', N'invited', N'away', N'inactive'));
END;
