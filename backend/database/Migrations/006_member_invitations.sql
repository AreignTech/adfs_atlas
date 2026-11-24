IF OBJECT_ID('dbo.member_invitation', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.member_invitation (
        user_id NVARCHAR(255) NOT NULL PRIMARY KEY
            REFERENCES dbo.[user](id) ON DELETE CASCADE,
        token_hash CHAR(64) NOT NULL UNIQUE,
        expires_at DATETIMEOFFSET(7) NOT NULL
    );
END;

-- Credential accounts have already completed onboarding.
UPDATE m SET status = N'active'
FROM dbo.member m
WHERE m.status = N'invited'
AND EXISTS (SELECT 1 FROM dbo.account a WHERE a.user_id=m.user_id
            AND a.provider_id=N'credential' AND a.password IS NOT NULL);
