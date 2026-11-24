SET NOCOUNT ON;

CREATE TABLE dbo.task_comment (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_task_comment PRIMARY KEY
        CONSTRAINT DF_task_comment_id DEFAULT (NEWSEQUENTIALID()),
    task_id UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT FK_task_comment_task REFERENCES dbo.task(id) ON DELETE CASCADE,
    user_id NVARCHAR(255) NOT NULL
        CONSTRAINT FK_task_comment_user REFERENCES dbo.[user](id) ON DELETE CASCADE,
    content NVARCHAR(MAX) NOT NULL,
    created_at DATETIMEOFFSET(7) NOT NULL
        CONSTRAINT DF_task_comment_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at DATETIMEOFFSET(7) NULL,
    CONSTRAINT CK_task_comment_content CHECK (LEN(LTRIM(RTRIM(content))) > 0 AND DATALENGTH(content) <= 10000)
);
CREATE INDEX taskComment_task_created_idx ON dbo.task_comment(task_id, created_at DESC, id DESC);
CREATE INDEX taskComment_user_idx ON dbo.task_comment(user_id);
