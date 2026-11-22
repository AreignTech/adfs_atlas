-- Four digits are a minimum width, not a truncation boundary.
ALTER TABLE dbo.task DROP CONSTRAINT DF_task_reference;
ALTER TABLE dbo.task ADD CONSTRAINT DF_task_reference DEFAULT
    (N'TSK-' + FORMAT(NEXT VALUE FOR dbo.task_reference_seq, N'0000', N'en-US')) FOR reference;
