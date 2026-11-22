CREATE TABLE dbo.user_setting (
    user_id NVARCHAR(255) NOT NULL CONSTRAINT FK_user_setting_user REFERENCES dbo.[user](id) ON DELETE CASCADE,
    setting_key NVARCHAR(80) NOT NULL,
    setting_value NVARCHAR(MAX) NOT NULL,
    updated_at DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_user_setting_updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_user_setting PRIMARY KEY(user_id,setting_key),
    CONSTRAINT CK_user_setting_json CHECK(ISJSON(setting_value)=1)
);
