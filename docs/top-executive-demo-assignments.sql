-- Confirmed demo assignments. No votes, signatures or report bytes are changed.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Positions TABLE (Login nvarchar(256) PRIMARY KEY, Position nvarchar(30) UNIQUE);
INSERT @Positions VALUES
(N'topdemo1@orange.test',N'CEO'),(N'topdemo2@orange.test',N'CFO'),
(N'topdemo3@orange.test',N'CTO'),(N'topdemo4@orange.test',N'COO');
IF EXISTS (SELECT 1 FROM CommitteeMembers m JOIN AspNetUsers u ON u.Id=m.UserId JOIN @Positions p ON p.Login=u.UserName
 WHERE m.CommitteeType=N'Top' AND m.ExecutivePosition IS NOT NULL AND m.ExecutivePosition<>p.Position)
 THROW 51000,'Existing executive assignment conflicts with requested mapping.',1;
UPDATE m SET ExecutivePosition=p.Position FROM CommitteeMembers m
 JOIN AspNetUsers u ON u.Id=m.UserId JOIN @Positions p ON p.Login=u.UserName
 WHERE m.CommitteeType=N'Top' AND m.ExecutivePosition IS NULL;
COMMIT;
SELECT m.TenderId,CONVERT(nvarchar(80),u.UserName) Login,m.ExecutivePosition FROM CommitteeMembers m
 JOIN AspNetUsers u ON u.Id=m.UserId JOIN @Positions p ON p.Login=u.UserName WHERE m.CommitteeType=N'Top' ORDER BY m.TenderId,m.ExecutivePosition;
