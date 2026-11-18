-- Run in SSMS against OrangeTenderingSystem after applying current migrations.
-- Synthetic data only. Re-running preserves existing scenarios and decisions.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ManagerLogin nvarchar(256) = N'manager2@orange.com'; -- Exact username, email or user Id.
DECLARE @Manager nvarchar(450), @Officer nvarchar(450), @Now datetime2 = SYSUTCDATETIME();
DECLARE @Seats TABLE (Seat int PRIMARY KEY, UserId nvarchar(450) UNIQUE, CommitteeType nvarchar(30), Responsibility nvarchar(30));
DECLARE @Results TABLE (TenderId int, RoundId int, Scenario nvarchar(200));
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @LockResult int;
 EXEC @LockResult = sys.sp_getapplock @Resource=N'Orange:Seed:TopCycleManager2', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
 IF @LockResult < 0 THROW 51000, 'Seed is busy; retry.', 1;
 IF (SELECT COUNT(*) FROM dbo.AspNetUsers u WHERE u.IsActive=1 AND (u.UserName=@ManagerLogin OR u.Email=@ManagerLogin OR u.Id=@ManagerLogin)
     AND EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Name=N'Sourcing Manager')) <> 1
 BEGIN
  SELECT u.Id,u.UserName,u.Email,u.FullName FROM dbo.AspNetUsers u WHERE EXISTS
   (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Name=N'Sourcing Manager');
  THROW 51000, 'Set @ManagerLogin to the exact username/email/Id of Manager 2 from the result above.', 1;
 END;
 SELECT @Manager=u.Id FROM dbo.AspNetUsers u WHERE u.IsActive=1 AND (u.UserName=@ManagerLogin OR u.Email=@ManagerLogin OR u.Id=@ManagerLogin)
 AND EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Name=N'Sourcing Manager');
 INSERT @Seats VALUES (1,@Manager,N'Evaluation',N'SourcingManager');
 INSERT @Seats
 SELECT TOP (4) 4+ROW_NUMBER() OVER (ORDER BY u.UserName,u.Id),u.Id,N'Top',NULL
 FROM dbo.AspNetUsers u WHERE u.IsActive=1 AND u.Id<>@Manager
 AND u.UserName IN (N'topdemo1@orange.test',N'topdemo2@orange.test',N'topdemo3@orange.test',N'topdemo4@orange.test') AND EXISTS
 (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Name=N'Top Committee') ORDER BY u.UserName,u.Id;
 INSERT @Seats
 SELECT TOP (3) 1+ROW_NUMBER() OVER (ORDER BY u.UserName,u.Id),u.Id,N'Evaluation',
 CASE ROW_NUMBER() OVER (ORDER BY u.UserName,u.Id) WHEN 1 THEN N'DepartmentRepresentative' WHEN 2 THEN N'FinancialRepresentative' ELSE N'TechnicalRepresentative' END
 FROM dbo.AspNetUsers u WHERE u.IsActive=1 AND NOT EXISTS (SELECT 1 FROM @Seats s WHERE s.UserId=u.Id) AND EXISTS
 (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Name=N'Evaluation Committee') ORDER BY u.UserName,u.Id;
 IF (SELECT COUNT(*) FROM @Seats)<>8 THROW 51000, 'Requires four active Top Committee accounts and three separate Evaluation Committee accounts, plus Manager 2.', 1;
 SELECT TOP (1) @Officer=u.Id FROM dbo.AspNetUsers u WHERE u.IsActive=1 AND EXISTS
 (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Name=N'Sourcing Officer') ORDER BY u.UserName,u.Id;
 IF @Officer IS NULL THROW 51000, 'An active Sourcing Officer is required.', 1;
 DECLARE @Department int;
 SELECT @Department=Id FROM dbo.Departments WHERE Name=N'Top Cycle SQL Training';
 IF @Department IS NULL
 BEGIN
  INSERT dbo.Departments (Name,Description,IsActive) VALUES (N'Top Cycle SQL Training',N'Synthetic Top Committee cycle scenarios',1);
  SET @Department=SCOPE_IDENTITY();
 END;
 DECLARE @Case int=1;
 WHILE @Case<=3
 BEGIN
  DECLARE @Title nvarchar(200)=N'TOP-SQL-MANAGER2-'+CONVERT(nvarchar(10),@Case)+
   CASE @Case WHEN 1 THEN N' Approval' WHEN 2 THEN N' Rejection' ELSE N' Clarification' END;
  DECLARE @Tender int=NULL,@Request int,@Round int,@Item int,@Recommended int=NULL,@Full int,@Summary int,@Evaluation int,@Top int;
  SELECT @Tender=Id FROM dbo.Tenders WHERE Title=@Title;
  IF @Tender IS NOT NULL
  BEGIN
   IF NOT EXISTS (SELECT 1 FROM dbo.CommitteeMembers WHERE TenderId=@Tender AND UserId=@Manager AND Responsibility=N'SourcingManager')
    THROW 51000, 'An existing seed scenario belongs to another Manager; no data changed.', 1;
   INSERT @Results SELECT @Tender,c.Id,@Title FROM dbo.CommitteeRounds c JOIN dbo.TenderRounds r ON r.Id=c.TenderRoundId WHERE r.TenderId=@Tender AND c.CommitteeType=N'Top';
   SET @Case+=1;
   CONTINUE;
  END;
  INSERT dbo.PurchaseRequests (DepartmentId,CreatedByUserId,Title,Justification,Priority,NeededBy,Status,CreatedAt,SubmittedAt)
  VALUES (@Department,@Officer,@Title,N'Synthetic laptop procurement for manual workflow testing.',N'Normal',DATEADD(day,30,@Now),N'Accepted',DATEADD(day,-14,@Now),DATEADD(day,-13,@Now));
  SET @Request=SCOPE_IDENTITY();
  INSERT dbo.PurchaseRequestItems (PurchaseRequestId,Name,Specifications,Quantity,Unit) VALUES (@Request,N'Business laptop',N'16GB RAM; 512GB SSD; 3-year warranty',10,N'Each');
  INSERT dbo.Tenders (PurchaseRequestId,Title,Description,Requirements,Status,CreatedByUserId,CreatedAt,EstimatedValue,Currency,IsCritical)
  VALUES (@Request,@Title,N'Test Top Committee review, clarification and electronic decisions.',N'3-year warranty; delivery within 21 days',N'TopCommitteeReview',@Officer,DATEADD(day,-12,@Now),10000,N'JOD',0);
  SET @Tender=SCOPE_IDENTITY();
  INSERT dbo.TenderItems (TenderId,Name,Specifications,Quantity,Unit) VALUES (@Tender,N'Business laptop',N'16GB RAM; 512GB SSD; 3-year warranty',10,N'Each');
  SET @Item=SCOPE_IDENTITY();
  INSERT dbo.TenderRounds (TenderId,RoundNumber,Status,OpensAt,Deadline,ClosedAt,CreatedByUserId,CreatedAt)
  VALUES (@Tender,1,N'SentToEvaluation',DATEADD(day,-11,@Now),DATEADD(day,-6,@Now),DATEADD(day,-6,@Now),@Officer,DATEADD(day,-11,@Now));
  SET @Round=SCOPE_IDENTITY();
  INSERT dbo.CommitteeMembers (TenderId,CommitteeType,Responsibility,UserId,AssignedAt)
  SELECT @Tender,CommitteeType,Responsibility,UserId,DATEADD(day,-10,@Now) FROM @Seats;
  DECLARE @Offer int=1;
  WHILE @Offer<=3
  BEGIN
   DECLARE @Registration nvarchar(80)=N'TOP-SQL-DEMO-'+CONVERT(nvarchar(10),@Offer),@Supplier int=NULL,@Invitation int,@Bid int;
   SELECT @Supplier=Id FROM dbo.Suppliers WHERE RegistrationNumber=@Registration;
   IF @Supplier IS NULL
   BEGIN
    INSERT dbo.Suppliers (CompanyName,RegistrationNumber,Email,Category,Status,CreatedAt)
    VALUES (N'Top SQL Demo Supplier '+CONVERT(nvarchar(10),@Offer),@Registration,N'top-sql-'+CONVERT(nvarchar(10),@Offer)+N'@example.test',N'Other',N'Approved',DATEADD(day,-20,@Now));
    SET @Supplier=SCOPE_IDENTITY();
   END;
   INSERT dbo.TenderInvitations (TenderRoundId,SupplierId,InvitedByUserId,Status,InvitedAt,RespondedAt)
   VALUES (@Round,@Supplier,@Officer,N'Accepted',DATEADD(day,-10,@Now),DATEADD(day,-9,@Now));
   SET @Invitation=SCOPE_IDENTITY();
   INSERT dbo.Bids (TenderInvitationId,RevisionNumber,Status,TechnicalProposal,CurrencyCode,DeliveryDays,PaymentTerms,ValidUntil,
    ShippingCost,InstallationCost,MaintenanceCost,TaxAmount,OtherCost,CreatedAt,SubmittedAt,ComplianceStatus,ComplianceNotes,ComplianceCheckedByUserId,ComplianceCheckedAt)
   VALUES (@Invitation,1,N'Submitted',N'All specifications met; warranty included.',N'JOD',14,N'30 days after delivery',DATEADD(day,60,@Now),
    100,0,0,0,0,DATEADD(day,-8,@Now),DATEADD(day,-7,@Now),N'Compliant',N'Synthetic compliance check.',@Officer,DATEADD(day,-6,@Now));
   SET @Bid=SCOPE_IDENTITY();
   INSERT dbo.BidItems (BidId,TenderItemId,Quantity,UnitPrice,OfferedSpecifications) VALUES (@Bid,@Item,10,750+(@Offer-1)*70,N'16GB RAM; 512GB SSD; 3-year warranty');
   IF @Offer=1 SET @Recommended=@Bid;
   SET @Offer+=1;
  END;
  INSERT dbo.TenderReports (TenderRoundId,ReportType,VersionNumber,Content,RecommendedBidId,Status,CreatedByUserId,CreatedAt,SubmittedAt,ReviewedByUserId,ReviewComment,ReviewedAt)
  VALUES (@Round,N'FullEvaluation',1,N'Synthetic evaluation: three compliant offers. Supplier 1 recommended: JOD 7,600 total, 14-day delivery, three-year warranty.',@Recommended,N'Accepted',@Officer,DATEADD(day,-5,@Now),DATEADD(day,-4,@Now),@Manager,N'Accepted by responsible Manager 2 for Top Committee review.',DATEADD(day,-3,@Now));
  SET @Full=SCOPE_IDENTITY();
  INSERT dbo.CommitteeRounds (TenderRoundId,CommitteeType,RoundNumber,TenderReportId,Status,TechnicalWeight,FinancialWeight,StartedAt,CompletedAt)
  VALUES (@Round,N'Evaluation',1,@Full,N'Approved',60,40,DATEADD(day,-6,@Now),DATEADD(day,-4,@Now));
  SET @Evaluation=SCOPE_IDENTITY();
  INSERT dbo.CommitteeVotes (CommitteeRoundId,CommitteeMemberId,Decision,Comment,VotedAt)
  SELECT @Evaluation,Id,N'Approve',N'Synthetic evaluation approval.',DATEADD(day,-4,@Now) FROM dbo.CommitteeMembers WHERE TenderId=@Tender AND CommitteeType=N'Evaluation';
  INSERT dbo.TenderReports (TenderRoundId,ReportType,VersionNumber,Content,RecommendedBidId,Status,CreatedByUserId,CreatedAt,SubmittedAt)
  VALUES (@Round,N'ExecutiveSummary',1,N'Manager 2 executive summary: purchase 10 business laptops from Top SQL Demo Supplier 1. Total JOD 7,600 including shipping. Three compliant offers reviewed; 14-day delivery; three-year warranty. This is synthetic training data.',@Recommended,N'Submitted',@Manager,DATEADD(day,-2,@Now),DATEADD(day,-2,@Now));
  SET @Summary=SCOPE_IDENTITY();
  INSERT dbo.CommitteeRounds (TenderRoundId,CommitteeType,RoundNumber,TenderReportId,Status,StartedAt)
  VALUES (@Round,N'Top',1,@Summary,N'InProgress',DATEADD(day,-1,@Now));
  SET @Top=SCOPE_IDENTITY();
  INSERT dbo.Notifications (UserId,Title,Message,Link,IsRead,CreatedAt)
  SELECT UserId,N'Top Committee — Report Ready',@Title+N': ready for review and electronic signature.',N'/TopCommittee/Decisions/Decision/'+CONVERT(nvarchar(20),@Top),0,@Now FROM @Seats WHERE CommitteeType=N'Top';
  INSERT @Results VALUES (@Tender,@Top,@Title);
  SET @Case+=1;
 END;
 COMMIT;
 SELECT s.Seat,u.UserName,u.Email,u.FullName,s.CommitteeType,s.Responsibility FROM @Seats s JOIN dbo.AspNetUsers u ON u.Id=s.UserId ORDER BY s.Seat;
 SELECT *,N'/TopCommittee/Decisions/Decision/'+CONVERT(nvarchar(20),RoundId) AS ReviewUrl FROM @Results;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
