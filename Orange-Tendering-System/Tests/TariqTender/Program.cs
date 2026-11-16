using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

var title = Environment.GetEnvironmentVariable("TARIQ_TENDER_TITLE")
    ?? "TARIQ DEMO - 20 Business Laptops - Ready for Top Submission";
if (!title.StartsWith("TARIQ DEMO - ") || title.Length > 200)
    throw new Exception("A demo title starting with TARIQ DEMO - and at most 200 characters is required.");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var settings = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath=root, EnvironmentName="Development", ApplicationName=typeof(WebAppMarker).Assembly.GetName().Name });
settings.Configuration["TeamDev:Database"] = TeamDevDatabase.DefaultName;
TeamDevDatabase.Apply(settings);
var connection = settings.Configuration.GetConnectionString(TeamDevDatabase.ConnectionName)!;
if (!TeamDevDatabase.IsLocalTeamDatabase(connection)) throw new Exception("Local team database required.");
var policy = settings.Configuration.GetSection("TenderApprovalPolicy").Get<TenderApprovalPolicy>()!;
if (!policy.FinalConfigured || policy.TopCommitteeRequirement != "All") throw new Exception("Existing policy must require Top review for this demonstration.");
Console.WriteLine("Database: " + TeamDevDatabase.Describe(connection));
Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection",connection);
Environment.SetEnvironmentVariable("BootstrapAdmin__Email", "");
Environment.SetEnvironmentVariable("BootstrapAdmin__Password", "");
using var app = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b => {
    b.UseContentRoot(root).UseEnvironment("Production");
    b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?> {
        ["ConnectionStrings:Default Connection"]=connection, ["BootstrapAdmin:Email"]="", ["BootstrapAdmin:Password"]="", ["Logging:LogLevel:Default"]="Error"
    }));
});
async Task Db(Func<ApplicationDbContext,Task> action) { using var s=app.Services.CreateScope(); await action(s.ServiceProvider.GetRequiredService<ApplicationDbContext>()); }
async Task Flow(Func<TenderWorkflowService,Task> action) { using var s=app.Services.CreateScope(); await action(s.ServiceProvider.GetRequiredService<TenderWorkflowService>()); }
ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id)],"demo-fixture"));
if(Environment.GetEnvironmentVariable("TARIQ_RETRY_TOP")=="4") { await RetryTop.Run(app); return; }
if(Environment.GetEnvironmentVariable("TARIQ_INSPECT_TOP")=="4") {
    await Db(async db=>{
        var t=await db.Tenders.AsNoTracking().SingleAsync(x=>x.Id==4);
        var sessions=await db.Set<EvaluationSession>().AsNoTracking().Where(x=>x.TenderRound.TenderId==4).Select(x=>new{x.Id,x.State,x.ReportId,x.TenderRoundId}).ToArrayAsync();
        var reports=await db.TenderReports.AsNoTracking().Where(x=>x.TenderRound.TenderId==4).Select(x=>new{x.Id,x.Status,x.VersionNumber,x.ReviewedAt,x.ReviewedByUserId}).ToArrayAsync();
        var rounds=await db.CommitteeRounds.AsNoTracking().Where(x=>x.TenderRound.TenderId==4).Select(x=>new{x.Id,x.CommitteeType,x.Status,x.TenderReportId,x.RoundNumber}).ToArrayAsync();
        var votes=await db.CommitteeVotes.AsNoTracking().Where(x=>x.CommitteeRound.TenderRound.TenderId==4).Select(x=>new{x.Id,x.CommitteeRoundId,x.Decision,x.RejectionReason,x.Comment}).ToArrayAsync();
        var notices=await db.Set<FinalDecisionNotice>().AsNoTracking().Where(x=>x.TenderId==4).Select(x=>new{x.Id,x.Status,x.Outcome,x.TenderReportId,x.SuppliersNotifiedAt}).ToArrayAsync();
        Console.WriteLine(JsonSerializer.Serialize(new{t.Id,t.Title,t.Status,t.AwardedBidId,Sessions=sessions,Reports=reports,Rounds=rounds,Votes=votes,Notices=notices},new JsonSerializerOptions{WriteIndented=true}));
    });return;
}
string manager="",officer="",requester=""; int dept=0,tenderId=0,reportId=0; string hash="";
var evaluation=new List<(string Id,CommitteeResponsibility Seat)>(); var top=new List<(string Id,TopExecutivePosition Seat)>();
await Db(async db=> {
    async Task<ApplicationUser> User(string email) => await db.Users.AsNoTracking().SingleAsync(x=>x.Email==email && x.IsActive && x.EmailConfirmed);
    manager=(await User("sourcing.manager@orange-demo.local")).Id;
    officer=(await User("sourcing.officer@orange-demo.local")).Id;
    var req=await User("department@orange-demo.local"); requester=req.Id; dept=req.DepartmentId ?? throw new Exception("Requester has no department.");
    evaluation.Add((manager,CommitteeResponsibility.SourcingManager));
    evaluation.Add(((await User("evaluation.department@orange-demo.local")).Id,CommitteeResponsibility.DepartmentRepresentative));
    evaluation.Add(((await User("evaluation.financial@orange-demo.local")).Id,CommitteeResponsibility.FinancialRepresentative));
    evaluation.Add(((await User("evaluation.technical@orange-demo.local")).Id,CommitteeResponsibility.TechnicalRepresentative));
    foreach(var seat in Enum.GetValues<TopExecutivePosition>()) top.Add(((await User("top."+seat.ToString().ToLowerInvariant()+"@orange-demo.local")).Id,seat));
    var existing=await db.Tenders.SingleOrDefaultAsync(x=>x.Title==title); if(existing!=null){ tenderId=existing.Id; return; }
    var suppliers=new List<Supplier>();
    for(int n=30;n<=32;n++) { var u=await User($"SupplierTariq{n}@Demo-orange.loacl"); suppliers.Add(await db.Suppliers.SingleAsync(x=>x.Id==u.SupplierId && x.Status==SupplierStatus.Approved)); }
    var now=DateTime.UtcNow;
    await using var tx=await db.Database.BeginTransactionAsync();
    var pr=new PurchaseRequest { DepartmentId=dept,CreatedByUserId=requester,Title=title,Justification="Synthetic training scenario: replace 20 outdated employee laptops. All evaluation and signing steps in this dataset are demo actions, not real procurement approvals.",Status=PurchaseRequestStatus.ConvertedToTender,Priority=RequestPriority.Normal,CreatedAt=now.AddDays(-14),SubmittedAt=now.AddDays(-13),NeededBy=now.AddDays(30),Items={new PurchaseRequestItem{Name="Business laptop",Quantity=20,Unit="Each",Specifications="Core i7, 16 GB RAM, 512 GB SSD, Windows 11 Pro, 3-year warranty"}} };
    db.Add(pr); await db.SaveChangesAsync();
    db.Add(new PurchaseRequestReview{PurchaseRequestId=pr.Id,SubmissionNumber=1,SubmittedAt=pr.SubmittedAt.Value,SubmittedSnapshotJson=JsonSerializer.Serialize(new{pr.Title,pr.Justification,Items=pr.Items.Select(x=>new{x.Name,x.Quantity,x.Unit,x.Specifications})}),Decision=ReviewDecision.Accepted,ReviewedByUserId=officer,ReviewedAt=now.AddDays(-12),Comment="Demo fixture: requirements complete and budget available."});
    var t=new Tender{PurchaseRequestId=pr.Id,Title=title,Description="Synthetic demonstration tender for 20 business laptops. Three Tariq suppliers; completed evaluation; stop before manager approval and Top submission.",Requirements="20 laptops, Core i7, 16 GB RAM, 512 GB SSD, Windows 11 Pro; three-year warranty; delivery within 21 days; JOD inclusive pricing.",CreatedByUserId=officer,CreatedAt=now.AddDays(-12),EstimatedValue=16000,Currency="JOD",Status=TenderStatus.Evaluation,Items={new TenderItem{Name="Business laptop",Quantity=20,Unit="Each",Specifications="Core i7 / 16 GB RAM / 512 GB SSD / Windows 11 Pro / 3-year warranty"}}};
    db.Add(t); await db.SaveChangesAsync(); tenderId=t.Id;
    var seats=evaluation.Select(x=>new CommitteeMember{TenderId=t.Id,UserId=x.Id,Responsibility=x.Seat,CommitteeType=CommitteeType.Evaluation,AssignedAt=now.AddDays(-11)}).ToArray(); db.AddRange(seats);
    db.AddRange(top.Select(x=>new CommitteeMember{TenderId=t.Id,UserId=x.Id,CommitteeType=CommitteeType.Top,ExecutivePosition=x.Seat,Responsibility=(CommitteeResponsibility)Enum.Parse(typeof(CommitteeResponsibility),"Chief"+(x.Seat switch{TopExecutivePosition.CEO=>"Executive",TopExecutivePosition.CFO=>"Financial",TopExecutivePosition.CTO=>"Technology",_=>"Operations"})+"Officer"),AssignedAt=now.AddDays(-11)}));
    var round=new TenderRound{TenderId=t.Id,RoundNumber=1,Status=TenderRoundStatus.SentToEvaluation,CreatedByUserId=officer,CreatedAt=now.AddDays(-11),OpensAt=now.AddDays(-10),Deadline=now.AddDays(-3),ClosedAt=now.AddDays(-3)};db.Add(round);
    var weights=new WeightRound{TenderId=t.Id,Version=1,State=WeightState.Approved,CreatedAt=now.AddDays(-11),RequirementsJson=JsonSerializer.Serialize(new{t.Title,t.Description,t.Requirements,Items=t.Items.Select(x=>new{x.Name,x.Quantity,x.Unit,x.Specifications})}),FinancialAverage=40,TechnicalAverage=60,ApprovedFinancial=40,ApprovedTechnical=60,ApprovedByUserId=officer,ApprovedAt=now.AddDays(-10),ApprovalPolicyJson=JsonSerializer.Serialize(policy)};db.Add(weights);
    await db.SaveChangesAsync();
    foreach(var s in seats) db.Add(new WeightProposal{WeightRoundId=weights.Id,CommitteeMemberId=s.Id,MemberUserId=s.UserId,Financial=40,Technical=60,SubmittedAt=now.AddDays(-11)});
    var cr=new CommitteeRound{TenderRoundId=round.Id,CommitteeType=CommitteeType.Evaluation,RoundNumber=1,StartedAt=now.AddDays(-2),TechnicalWeight=60,FinancialWeight=40};db.Add(cr);await db.SaveChangesAsync();
    var criteria=new[]{new EvaluationCriterion{CommitteeRoundId=cr.Id,Name="Financial value",Type=CriterionType.Financial,Weight=40,MaxScore=100,DisplayOrder=1},new EvaluationCriterion{CommitteeRoundId=cr.Id,Name="Technical compliance and warranty",Type=CriterionType.Technical,Weight=60,MaxScore=100,DisplayOrder=2}};db.AddRange(criteria);
    var ai=new AiAnalysis{TenderRoundId=round.Id,Status="Completed",Model="synthetic-demo-analysis-not-live-AI",Findings="DEMO analysis: all three offers meet the required specifications. Supplier Tariq 30 offers the lowest total cost, delivery in 10 days, and the highest combined score. No external AI call was made.",EvidenceJson="{\"syntheticDemo\":true,\"supplierNumbers\":[30,31,32]}",StartedAt=now.AddDays(-2),ReviewedAt=now.AddDays(-2),ReviewedByUserId=officer};db.Add(ai);await db.SaveChangesAsync();
    var session=new EvaluationSession{TenderRoundId=round.Id,WeightRoundId=weights.Id,CommitteeRoundId=cr.Id,AiAnalysisId=ai.Id,State=SessionState.Scheduled,StartsAt=now.AddHours(-3),EndsAt=now.AddHours(1),MeetingUrl="https://example.invalid/tariq-demo-no-live-meeting",FinalApprovalPolicyJson=JsonSerializer.Serialize(policy)};db.Add(session);await db.SaveChangesAsync();
    foreach(var seat in seats)db.Add(new EvaluationParticipation{EvaluationSessionId=session.Id,CommitteeMemberId=seat.Id,SentAt=now.AddHours(-4),SubmittedAt=now.AddHours(-1)});
    for(int i=0;i<3;i++){
        var inv=new TenderInvitation{TenderRoundId=round.Id,SupplierId=suppliers[i].Id,InvitedByUserId=officer,InvitedAt=now.AddDays(-10),Status=InvitationStatus.Accepted,RespondedAt=now.AddDays(-9)};db.Add(inv);await db.SaveChangesAsync();
        var bid=new Bid{TenderInvitationId=inv.Id,RevisionNumber=1,Status=BidStatus.Submitted,CreatedAt=now.AddDays(-8),SubmittedAt=now.AddDays(-7),CurrencyCode="JOD",DeliveryDays=10+i*3,PaymentTerms="30 days after delivery",ValidUntil=now.AddDays(60),TechnicalProposal="DEMO OFFER: Core i7, 16 GB RAM, 512 GB SSD, Windows 11 Pro, three-year warranty and installation included.",ShippingCost=100+i*25,ComplianceStatus=ComplianceStatus.Compliant,ComplianceCheckedByUserId=officer,ComplianceCheckedAt=now.AddDays(-3),ComplianceNotes="Synthetic demo compliance review: all mandatory specifications satisfied.",Items=t.Items.Select(item=>new BidItem{TenderItemId=item.Id,Quantity=item.Quantity,UnitPrice=650+i*50,OfferedSpecifications=item.Specifications}).ToList()};db.Add(bid);await db.SaveChangesAsync();
        db.Add(new SessionBid{EvaluationSessionId=session.Id,BidId=bid.Id});
        foreach(var seat in seats)foreach(var criterion in criteria)db.Add(new EvaluationScore{BidId=bid.Id,CommitteeMemberId=seat.Id,EvaluationCriterionId=criterion.Id,Score=(criterion.Type==CriterionType.Financial?100m*13100m/(13100m+i*1025m):95m-i*4m),Comment="Synthetic training score; not an actual member decision.",ScoredAt=now.AddHours(-1)});
    }
    db.Add(new AuditLog{UserId=officer,Action="TariqDemoFixtureCreated",EntityName="Tender",EntityId=t.Id.ToString(),CreatedAt=now,NewValues="Synthetic owner-requested demo; only Tariq30-32; stop at ManagerReview; no Top approval or award."});
    await db.SaveChangesAsync();await tx.CommitAsync();
});
if (Environment.GetEnvironmentVariable("TARIQ_REOPEN_DEMO_REJECTION") == "4") {
    await Db(async db=> {
        await using var tx=await db.Database.BeginTransactionAsync();
        var t=await db.Tenders.SingleAsync(x=>x.Id==tenderId);
        var s=await db.Set<EvaluationSession>().Include(x=>x.Report).SingleAsync(x=>x.TenderRound.TenderId==tenderId);
        if(t.Id!=4 || !t.Title.StartsWith("TARIQ DEMO - ") || t.Status!=TenderStatus.Rejected || s.State!=SessionState.Rejected || s.Report?.Status!=ReportStatus.Rejected || s.Report.ReviewedByUserId!=manager || t.AwardedBidId!=null || await db.CommitteeRounds.AnyAsync(x=>x.TenderRoundId==s.TenderRoundId&&x.CommitteeType==CommitteeType.Top) || await db.Set<ReportSignature>().AnyAsync(x=>x.TenderReportId==s.ReportId&&x.Stage!=ReportSigningStage.Evaluation)) throw new Exception("Only the rejected demo #4, without downstream decisions, may be reopened.");
        db.AuditLogs.Add(new AuditLog{UserId=manager,Action="DemoManagerRejectionReopened",EntityName="Tender",EntityId=t.Id.ToString(),CreatedAt=DateTime.UtcNow,NewValues=JsonSerializer.Serialize(new{Reason="Owner explicitly requested reopening the rejected demo for Manager review and subsequent Top submission.",ReportId=s.ReportId,PreviousReview=new{s.Report.Status,s.Report.ReviewedByUserId,s.Report.ReviewedAt,s.Report.ReviewComment},PreviousSessionState=s.State,PreviousTenderStatus=t.Status})});
        t.Status=TenderStatus.ManagerReview;s.State=SessionState.Finalized;
        s.Report.Status=ReportStatus.Submitted;s.Report.ReviewedByUserId=null;s.Report.ReviewedAt=null;s.Report.ReviewComment=null;
        await db.SaveChangesAsync();await tx.CommitAsync();
        Console.WriteLine("Demo #4 reopened for Manager review; previous rejection preserved in audit history.");
    });
}
await Db(async db=>{ var s=await db.Set<EvaluationSession>().SingleAsync(x=>x.TenderRound.TenderId==tenderId); reportId=s.ReportId??0; });
if(reportId==0)await Flow(w=>w.EndEvaluation(tenderId,true,"Owner-requested synthetic Tariq demonstration. Recommend Tariq30 at JOD 13,100; do not submit to Top until the owner approves.",Principal(officer)));
await Db(async db=>{var s=await db.Set<EvaluationSession>().Include(x=>x.Report).SingleAsync(x=>x.TenderRound.TenderId==tenderId);reportId=s.ReportId!.Value;hash=s.Report!.PdfSha256!;});
bool needsSigning=false;await Db(async db=>needsSigning=await db.Set<EvaluationSession>().AnyAsync(x=>x.TenderRound.TenderId==tenderId&&x.State==SessionState.ReportDraft));
if(needsSigning){
    await Flow(w=>w.SendReportToCommittee(tenderId,Principal(officer)));
    foreach(var member in evaluation){await Flow(async w=>{await w.ReportPdf(reportId,Principal(member.Id));});await Flow(w=>w.FinalizeReport(tenderId,"Synthetic training approval for the owner-requested demo only.",Principal(member.Id),hash,true));}
}
bool needsForward=false;await Db(async db=>needsForward=await db.Tenders.AnyAsync(x=>x.Id==tenderId&&x.Status==TenderStatus.OfficerReview));
if(needsForward)await Flow(w=>w.Forward(tenderId,Principal(officer)));
await Flow(async w=>{await w.VerifyReport(reportId,Principal(manager));await w.ReviewReport(reportId,Principal(manager));});
await Db(async db=>{
    var t=await db.Tenders.AsNoTracking().SingleAsync(x=>x.Id==tenderId);
    var s=await db.Set<EvaluationSession>().AsNoTracking().SingleAsync(x=>x.TenderRound.TenderId==tenderId);
    var r=await db.TenderReports.AsNoTracking().SingleAsync(x=>x.Id==reportId);
    var actual=await db.TenderInvitations.Where(x=>x.TenderRoundId==s.TenderRoundId).Select(x=>x.Supplier.Email).OrderBy(x=>x).ToArrayAsync();
    var expected=Enumerable.Range(30,3).Select(x=>$"SupplierTariq{x}@Demo-orange.loacl").ToArray();
    if(!actual.SequenceEqual(expected)||t.Status!=TenderStatus.ManagerReview||s.State!=SessionState.Finalized||r.ReviewedAt!=null||t.AwardedBidId!=null||await db.CommitteeRounds.AnyAsync(x=>x.TenderRoundId==s.TenderRoundId&&x.CommitteeType==CommitteeType.Top)||await db.Set<ReportSignature>().CountAsync(x=>x.TenderReportId==reportId&&x.Stage==ReportSigningStage.Evaluation)!=4||await db.Set<ReportSignature>().AnyAsync(x=>x.TenderReportId==reportId&&x.Stage!=ReportSigningStage.Evaluation))throw new Exception("Stop-state verification failed.");
    Console.WriteLine($"PASS Tender {tenderId}; Report {reportId}; 3 exact suppliers; complete evaluation and 4 demo signatures; ManagerReview; NOT sent to Top; NO award.");
    Console.WriteLine("Manager: sourcing.manager@orange-demo.local");
    Console.WriteLine($"Review: https://localhost:7182/SourcingManager/Reports/Review/{reportId}");
});
