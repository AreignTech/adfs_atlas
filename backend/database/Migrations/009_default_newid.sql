-- NEWSEQUENTIALID() can emit GUIDs whose version nibble falls outside the
-- RFC-4122 range 1-8, which the frontend's zod uuid() validation rejects and
-- breaks any page that renders the affected row. The application, seeder and
-- importer all supply explicit ids, so these defaults only apply to rows
-- created directly in SQL; NEWID() keeps those valid version-4 GUIDs.
-- Replaces every remaining newsequentialid default in place.
SET NOCOUNT ON;

DECLARE @table NVARCHAR(520), @constraint NVARCHAR(260), @column NVARCHAR(260), @sql NVARCHAR(MAX);

WHILE 1 = 1
BEGIN
    SELECT TOP 1
        @table      = QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name),
        @constraint = QUOTENAME(dc.name),
        @column     = QUOTENAME(c.name)
    FROM sys.default_constraints dc
    JOIN sys.tables t  ON t.object_id  = dc.parent_object_id
    JOIN sys.columns c ON c.object_id  = dc.parent_object_id
                      AND c.column_id  = dc.parent_column_id
    WHERE dc.definition LIKE N'%newsequentialid%'
    ORDER BY t.name, dc.name;

    IF @@ROWCOUNT = 0 BREAK;

    SET @sql = N'ALTER TABLE ' + @table + N' DROP CONSTRAINT ' + @constraint + N';'
             + N'ALTER TABLE ' + @table + N' ADD CONSTRAINT ' + @constraint
             + N' DEFAULT (NEWID()) FOR ' + @column + N';';
    EXEC sys.sp_executesql @sql;
END;
