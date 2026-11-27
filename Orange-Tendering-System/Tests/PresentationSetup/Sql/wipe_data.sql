-- Clears all application data in OrangeTenderingTeamDev while keeping the schema.
-- Kept: __EFMigrationsHistory, AspNetRoles, ProcurementCategories (inserted by migrations), real Departments.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'OrangeTenderingTeamDev' THROW 50000, 'Wrong database', 1;

BEGIN TRANSACTION;

EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';

DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'DELETE FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';' + CHAR(10)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name NOT IN (N'__EFMigrationsHistory', N'AspNetRoles', N'ProcurementCategories', N'Departments');
EXEC sp_executesql @sql;

DELETE FROM dbo.Departments WHERE Name IN (N'HR2', N'well');

EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';

-- Restart identity numbering for the emptied tables so the first tender is T-0001.
DECLARE @reseed nvarchar(max) = N'';
SELECT @reseed += N'DBCC CHECKIDENT (''' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N''', RESEED, 0) WITH NO_INFOMSGS;' + CHAR(10)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE OBJECTPROPERTY(t.object_id, 'TableHasIdentity') = 1
  AND t.name NOT IN (N'__EFMigrationsHistory', N'AspNetRoles', N'ProcurementCategories', N'Departments');
EXEC sp_executesql @reseed;

COMMIT TRANSACTION;

SELECT t.name, SUM(p.rows) AS rows
FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
GROUP BY t.name HAVING SUM(p.rows) > 0 ORDER BY t.name;
