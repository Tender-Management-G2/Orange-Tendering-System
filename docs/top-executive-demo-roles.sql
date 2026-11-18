-- Demo executive roles for the four explicitly selected test accounts.
-- Retains existing Top Committee access; does not touch votes, signatures or reports.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @Map TABLE (Login nvarchar(256) PRIMARY KEY, RoleName nvarchar(256) UNIQUE);
 INSERT @Map VALUES (N'topdemo1@orange.test',N'CEO'),(N'topdemo2@orange.test',N'CFO'),
                    (N'topdemo3@orange.test',N'CTO'),(N'topdemo4@orange.test',N'COO');
 IF (SELECT COUNT(*) FROM @Map m JOIN dbo.AspNetUsers u ON u.UserName=m.Login WHERE u.IsActive=1)<>4
  THROW 51000,'All four named demo accounts must exist and be active.',1;
 IF EXISTS (SELECT 1 FROM @Map m JOIN dbo.AspNetUsers u ON u.UserName=m.Login
  JOIN dbo.AspNetUserRoles ur ON ur.UserId=u.Id JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId
  WHERE r.Name IN (N'CEO',N'CFO',N'CTO',N'COO',N'Chief Executive Officer (CEO)',N'Chief Financial Officer (CFO)',N'Chief Technology Officer (CTO)',N'Chief Operations Officer (COO)',N'Chief Executive Officer',N'Chief Financial Officer',N'Chief Technology Officer',N'Chief Operations Officer') AND r.Name<>m.RoleName)
  THROW 51000,'A selected demo account already has a different executive role; no changes made.',1;
 INSERT dbo.AspNetRoles (Id,Name,NormalizedName,ConcurrencyStamp)
 SELECT CONVERT(nvarchar(450),NEWID()),m.RoleName,UPPER(m.RoleName),CONVERT(nvarchar(36),NEWID()) FROM @Map m
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles r WHERE r.NormalizedName=UPPER(m.RoleName));
 DECLARE @Changed TABLE (UserId nvarchar(450));
 INSERT dbo.AspNetUserRoles (UserId,RoleId)
 OUTPUT inserted.UserId INTO @Changed
 SELECT u.Id,r.Id FROM @Map m JOIN dbo.AspNetUsers u ON u.UserName=m.Login
 JOIN dbo.AspNetRoles r ON r.NormalizedName=UPPER(m.RoleName)
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId=u.Id AND ur.RoleId=r.Id);
 UPDATE u SET SecurityStamp=CONVERT(nvarchar(36),NEWID()),ConcurrencyStamp=CONVERT(nvarchar(36),NEWID())
 FROM dbo.AspNetUsers u WHERE EXISTS (SELECT 1 FROM @Changed c WHERE c.UserId=u.Id);
 COMMIT;
 SELECT CONVERT(nvarchar(80),u.UserName) AS Login,CONVERT(nvarchar(80),u.FullName) AS FullName,CONVERT(nvarchar(100),r.Name) AS Role
 FROM @Map m JOIN dbo.AspNetUsers u ON u.UserName=m.Login
 JOIN dbo.AspNetUserRoles ur ON ur.UserId=u.Id JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId ORDER BY u.UserName,r.Name;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
