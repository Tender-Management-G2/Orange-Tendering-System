using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

// Top Committee rejection and Re-Tendering on real SQL Server, real PDF preparation/signing and real Identity HTTP checks.
// Rules under test (workflow redesign): one authorized Reject stops the Top approval at once and goes straight to the Sourcing Officer
// (the Manager is informed, no acknowledgement), who notifies the suppliers and either cancels the tender or reissues it as a new round
// (never beyond round 4). Four distinct approvals of the same report version award the tender automatically, with no Manager step.
public static class TopDecisionChecks
{
 public static async Task Run()
 {
  var connection=Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")!;
  var catalog=new SqlConnectionStringBuilder(connection).InitialCatalog;
  if(!catalog.StartsWith("OrangeAuthCheck_TopReasons_"))throw new InvalidOperationException("Isolated Top reason database required.");
  var root=Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;
  Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection",connection);
  Environment.SetEnvironmentVariable("DemoData__Enabled","false");
  using var app=new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b=>{
   b.UseContentRoot(root).UseEnvironment("Development");
   b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
    ["ConnectionStrings:Default Connection"]=connection,["DemoData:Enabled"]="false",["BootstrapAdmin:Email"]="",["BootstrapAdmin:Password"]=""}));
  });
  int passes=0;
  void Check(bool ok,string name){if(!ok)throw new InvalidOperationException("FAIL "+name);passes++;Console.WriteLine("PASS "+name);}
  async Task Db(Func<ApplicationDbContext,Task> work){using var sc=app.Services.CreateScope();await work(sc.ServiceProvider.GetRequiredService<ApplicationDbContext>());}
  async Task<T> Flow<T>(Func<TenderWorkflowService,Task<T>> work){using var sc=app.Services.CreateScope();return await work(sc.ServiceProvider.GetRequiredService<TenderWorkflowService>());}
  async Task RunFlow(Func<TenderWorkflowService,Task> work){using var sc=app.Services.CreateScope();await work(sc.ServiceProvider.GetRequiredService<TenderWorkflowService>());}
  async Task Refuse(Func<TenderWorkflowService,Task> work,string label,string? contains=null){try{await RunFlow(work);}catch(WorkflowException e){Check(contains==null||e.Message.Contains(contains),label);return;}throw new InvalidOperationException("FAIL "+label+" (was allowed)");}
  ClaimsPrincipal Principal(string id)=>new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id)],"fixture"));
  var pw="T!7a"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
  string officer="",manager="",outsider="",requester="";var evaluators=new List<string>();var tops=new List<string>();int dept=0;
  async Task<string> Account(string name,string role,int? department=null,int? supplierId=null){
   using var sc=app.Services.CreateScope();var users=sc.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
   var u=new ApplicationUser{UserName=name+"@topreason.invalid",Email=name+"@topreason.invalid",FullName=name,EmailConfirmed=true,IsActive=true,DepartmentId=department,SupplierId=supplierId};
   var created=await users.CreateAsync(u,pw);if(!created.Succeeded||!(await users.AddToRoleAsync(u,role)).Succeeded)throw new Exception("Identity provisioning failed");return u.Id;}
  await Db(async db=>Check(!await db.Users.AnyAsync(),"new isolated SQL database"));
  officer=await Account("officer",SystemRoles.SourcingOfficer);manager=await Account("manager",SystemRoles.SourcingManager);evaluators.Add(manager);
  for(int i=1;i<=3;i++)evaluators.Add(await Account("evaluator"+i,SystemRoles.EvaluationCommittee));
  for(int i=1;i<=4;i++)tops.Add(await Account("top"+i,SystemRoles.TopCommittee));
  outsider=await Account("outsider",SystemRoles.TopCommittee);
  await Db(async db=>{var d=new Department{Name="Isolated Top reason fixtures"};db.Add(d);await db.SaveChangesAsync();dept=d.Id;});
  requester=await Account("requester",SystemRoles.RequestingDepartment,dept);
  var supplierUsers=new Dictionary<int,string>();int supplierSeq=0;
  async Task<int> NewSupplier(string label){
   int id=0;await Db(async db=>{var s=new Supplier{CompanyName=label,Email="s"+Guid.NewGuid().ToString("N")+"@topreason.invalid",RegistrationNumber=Guid.NewGuid().ToString(),Status=SupplierStatus.Approved};db.Add(s);await db.SaveChangesAsync();id=s.Id;});
   supplierUsers[id]=await Account("supplier"+(++supplierSeq),SystemRoles.Supplier,null,id);return id;}

  // A tender whose report is signed by the four evaluators and approved and signed by the Manager, waiting for the Top Committee.
  async Task<(int Tender,int Report,string Hash,List<int> Suppliers)> Fixture(string label,int roundNumber=1){
   int tenderId=0;var supplierIds=new List<int>();for(int i=0;i<3;i++)supplierIds.Add(await NewSupplier(label+" supplier "+i));
   await Db(async db=>{
    var now=DateTime.UtcNow;
    var pr=new PurchaseRequest{DepartmentId=dept,CreatedByUserId=requester,Title=label,Justification="SQL-only fixture",Status=PurchaseRequestStatus.Accepted};db.Add(pr);await db.SaveChangesAsync();
    var t=new Tender{Title=label,PurchaseRequestId=pr.Id,CreatedByUserId=officer,Status=TenderStatus.Evaluation,CreatedAt=now};db.Add(t);await db.SaveChangesAsync();tenderId=t.Id;
    var round=new TenderRound{TenderId=t.Id,RoundNumber=roundNumber,Status=TenderRoundStatus.Closed,CreatedByUserId=officer,CreatedAt=now,Deadline=now.AddDays(-1)};db.Add(round);
    var weights=new WeightRound{TenderId=t.Id,Version=1,State=WeightState.Approved,ApprovedFinancial=40,ApprovedTechnical=60,ApprovedByUserId=manager,ApprovedAt=now,RequirementsJson="{}"};db.Add(weights);
    var seats=evaluators.Select((u,i)=>new CommitteeMember{TenderId=t.Id,UserId=u,CommitteeType=CommitteeType.Evaluation,Responsibility=(CommitteeResponsibility)i,AssignedAt=now}).ToArray();db.AddRange(seats);
    db.AddRange(tops.Select(u=>new CommitteeMember{TenderId=t.Id,UserId=u,CommitteeType=CommitteeType.Top,AssignedAt=now}));await db.SaveChangesAsync();
    var cr=new CommitteeRound{TenderRoundId=round.Id,CommitteeType=CommitteeType.Evaluation,RoundNumber=1,StartedAt=now};db.Add(cr);await db.SaveChangesAsync();
    var criteria=new[]{new EvaluationCriterion{CommitteeRoundId=cr.Id,Name="Financial",Type=CriterionType.Financial,Weight=40,MaxScore=100},new EvaluationCriterion{CommitteeRoundId=cr.Id,Name="Technical",Type=CriterionType.Technical,Weight=60,MaxScore=100}};db.AddRange(criteria);
    var ai=new AiAnalysis{TenderRoundId=round.Id,Status="Completed",Model="fixture-not-live-ai",Findings="Isolated source fixture",EvidenceJson="{}",ReviewedAt=now};db.Add(ai);await db.SaveChangesAsync();
    var policy=new TenderApprovalPolicy{Version="isolated",TopCommitteeRequirement="All",TopQuorum=4,TopApprovalsRequired=4,RejectVoteBlocksAward=true,TopTieOutcome="Block"};
    var session=new EvaluationSession{TenderRoundId=round.Id,WeightRoundId=weights.Id,CommitteeRoundId=cr.Id,AiAnalysisId=ai.Id,State=SessionState.Scheduled,StartsAt=now.AddHours(-1),EndsAt=now.AddDays(1),FinalApprovalPolicyJson=JsonSerializer.Serialize(policy)};db.Add(session);await db.SaveChangesAsync();
    foreach(var seat in seats)db.Add(new EvaluationParticipation{EvaluationSessionId=session.Id,CommitteeMemberId=seat.Id,SubmittedAt=now});
    for(int i=0;i<3;i++){
     var invite=new TenderInvitation{TenderRoundId=round.Id,SupplierId=supplierIds[i],InvitedByUserId=officer,InvitedAt=now,Status=InvitationStatus.Accepted};db.Add(invite);await db.SaveChangesAsync();
     var bid=new Bid{TenderInvitationId=invite.Id,Status=BidStatus.Submitted,ComplianceStatus=ComplianceStatus.Compliant,CreatedAt=now,SubmittedAt=now};db.Add(bid);await db.SaveChangesAsync();
     db.Add(new SessionBid{EvaluationSessionId=session.Id,BidId=bid.Id});foreach(var seat in seats)foreach(var criterion in criteria)db.Add(new EvaluationScore{BidId=bid.Id,CommitteeMemberId=seat.Id,EvaluationCriterionId=criterion.Id,Score=90-i*10,Comment="Fixture original score",ScoredAt=now});
    }
    await db.SaveChangesAsync();
   });
   await RunFlow(w=>w.EndEvaluation(tenderId,true,"Isolated complete scores",Principal(officer)));
   int report=0;string hash="";await Db(async db=>{var s=await db.Set<EvaluationSession>().Include(x=>x.Report).SingleAsync(x=>x.TenderRound.TenderId==tenderId);report=s.ReportId!.Value;hash=s.Report!.PdfSha256!;});
   foreach(var evaluator in evaluators){await Flow(w=>w.ReportPdf(report,Principal(evaluator)));await RunFlow(w=>w.FinalizeReport(tenderId,"Reviewed",Principal(evaluator),hash,true));}
   await RunFlow(w=>w.Forward(tenderId,Principal(officer)));await Flow(w=>w.ReportPdf(report,Principal(manager)));
   await Refuse(w=>w.ManagerDecision(tenderId,"Approve","Reviewed",Principal(manager),hash,true),label+": Manager approval without an executive summary is refused","executive summary");
   await RunFlow(w=>w.SubmitExecutiveSummary(tenderId,Summary(),Principal(manager),hash,true,null));
   foreach(var top in tops)await Flow(w=>w.ReportPdf(report,Principal(top)));
   Check(true,label+": real generated PDF, four evaluation signatures and Manager signature, waiting for Top");return(tenderId,report,hash,supplierIds);
  }
  ExecutiveSummaryInput Summary()=>new(){BusinessNeed="The department needs the requested equipment for daily work.",KeyFindings="Three compliant offers were evaluated with the approved weights.",
   RecommendationReasons="The recommended offer has the highest final weighted score.",ManagerAssessment="I support the committee's recommendation for approval."};
  Task Vote((int Tender,int Report,string Hash,List<int> Suppliers) f,int n,VoteDecision decision,TopRejectionReason? reason)=>RunFlow(w=>w.TopVote(f.Tender,decision,"Signed fixture justification",Principal(tops[n]),f.Hash,true,rejectionReason:reason));
  async Task<FinalDecisionNotice?> Notice(int tender){FinalDecisionNotice? n=null;await Db(async db=>n=await db.Set<FinalDecisionNotice>().AsNoTracking().OrderByDescending(x=>x.Id).FirstOrDefaultAsync(x=>x.TenderId==tender));return n;}
  async Task<int> Notes(string userId,string title){int c=0;await Db(async db=>c=await db.Notifications.CountAsync(x=>x.UserId==userId&&x.Title==title));return c;}

  // ---------- 1. REJECT: one member stops the approval; the tender is cancelled ----------
  var cancel=await Fixture("Cancellation");
  await Refuse(w=>w.TopVote(cancel.Tender,VoteDecision.Reject," ",Principal(tops[0]),cancel.Hash,true),"reject without a documented reason is refused");
  await Refuse(w=>w.TopVote(cancel.Tender,VoteDecision.Retender,"",Principal(tops[0]),cancel.Hash,true),"re-tender without a documented reason is refused");
  await Refuse(w=>w.TopVote(cancel.Tender,VoteDecision.Reject,"Old reason",Principal(tops[0]),cancel.Hash,true,rejectionReason:TopRejectionReason.UnacceptableOffers),"'No Supplier Offers Are Acceptable' is no longer a Reject reason","Re-Tender");
  await Refuse(w=>w.TopVote(cancel.Tender,(VoteDecision)99,"Unknown",Principal(tops[0]),cancel.Hash,true),"only Approve, Reject or Re-Tender are accepted");
  await Refuse(w=>w.TopVote(cancel.Tender,VoteDecision.Reject,"Wrong role",Principal(officer),cancel.Hash,true,rejectionReason:TopRejectionReason.NotNeeded),"Officer cannot vote");
  await Refuse(w=>w.TopVote(cancel.Tender,VoteDecision.Reject,"Unassigned",Principal(outsider),cancel.Hash,true,rejectionReason:TopRejectionReason.NotNeeded),"unassigned Top member cannot vote");
  await Vote(cancel,0,VoteDecision.Approve,null);
  await Vote(cancel,1,VoteDecision.Reject,TopRejectionReason.NotNeeded);
  var n1=await Notice(cancel.Tender);
  Check(n1 is { Outcome: FinalDecisionOutcome.Cancel, Status: FinalDecisionStatus.ForwardedToOfficer, RejectionReason: TopRejectionReason.NotNeeded, ManagerAcknowledgedAt: null }&&n1.RejectedByUserId==tops[1],"one Reject immediately sends the cancellation to the Officer (no Manager acknowledgement)");
  await Db(async db=>{
   Check(await db.CommitteeRounds.AnyAsync(x=>x.TenderReportId==cancel.Report&&x.CommitteeType==CommitteeType.Top&&x.Status==CommitteeRoundStatus.Rejected&&x.CompletedAt!=null),"Top round locked as Rejected after one Reject (even after an approval)");
   Check(await db.Tenders.AnyAsync(x=>x.Id==cancel.Tender&&x.Status==TenderStatus.Rejected&&x.AwardedBidId==null),"tender stopped: no award possible");
   Check(await db.CommitteeVotes.CountAsync(x=>x.CommitteeRound.TenderReportId==cancel.Report&&x.CommitteeRound.CommitteeType==CommitteeType.Top)==2,"earlier approval and the rejection are both preserved");
  });
  await Refuse(w=>w.TopVote(cancel.Tender,VoteDecision.Approve,"Late",Principal(tops[2]),cancel.Hash,true),"further Top votes refused after the rejection","stopped");
  await Refuse(w=>w.TopVote(cancel.Tender,VoteDecision.Retender,"Late",Principal(tops[3]),cancel.Hash,true),"a later Re-Tender cannot change the decision","stopped");
  Check(await Notes(manager,"Top Committee rejected the tender")==1,"Manager informed once of the rejection");
  Check(await Notes(officer,"Top Committee rejected the tender")==1,"Officer receives the actionable rejection once");
  await Db(async db=>Check(await db.CommitteeVotes.Where(x=>x.CommitteeRound.TenderReportId==cancel.Report&&x.CommitteeRound.CommitteeType==CommitteeType.Top&&x.Decision==VoteDecision.Approve).AllAsync(x=>x.Comment==null),"an approval never stores a (hidden) justification text"));
  var details=await Flow(w=>w.TopRejectionDetails(cancel.Report,Principal(tops[0])));Check(details.FinalReason==TopRejectionReason.NotNeeded&&!details.CanAgree,"rejection details show the single final reason");
  await Refuse(w=>w.Award(cancel.Tender,Principal(officer)),"award refused after a rejection");
  await Refuse(w=>w.ManagerAcknowledge(cancel.Tender,"Forwarded",Principal(manager)),"no Manager acknowledgement step exists for a new rejection","No acknowledgement");
  await Refuse(w=>w.NotifySuppliers(cancel.Tender,Principal(manager)),"Manager cannot notify suppliers");
  await RunFlow(w=>w.NotifySuppliers(cancel.Tender,Principal(officer)));
  await Refuse(w=>w.NotifySuppliers(cancel.Tender,Principal(officer)),"cancellation runs once (idempotent)","already notified");
  await Db(async db=>{
   Check(await db.Tenders.AnyAsync(x=>x.Id==cancel.Tender&&x.Status==TenderStatus.Cancelled&&x.ClosedAt!=null&&x.AwardedBidId==null),"reason 1 permanently cancels the tender");
   Check(await db.TenderRounds.CountAsync(x=>x.TenderId==cancel.Tender)==1,"cancellation creates no new round");
   Check(await db.Set<SupplierResultDelivery>().CountAsync(x=>x.TenderId==cancel.Tender&&x.Outcome=="Cancelled")==3,"one cancellation delivery per participating supplier user");
  });
  Check(cancel.Suppliers.All(s=>true)&&(await Task.WhenAll(cancel.Suppliers.Select(s=>Notes(supplierUsers[s],"Tender cancelled")))).All(c=>c==1),"each participating supplier notified exactly once");
  Check(await Notes(requester,"Tender cancelled")==1,"original Requesting Department notified of the cancellation");
  await Refuse(w=>w.OpenBidding(cancel.Tender,DateTimeOffset.UtcNow.AddDays(5),Principal(officer)),"no further bidding on a cancelled tender");
  await Refuse(w=>w.Retender(cancel.Tender,"Try again",Principal(officer)),"no re-tender of a cancelled tender");
  await Refuse(w=>w.Award(cancel.Tender,Principal(officer)),"no award of a cancelled tender");

  // ---------- 2. RE-TENDER after three approvals: Manager forwards, Officer edits and publishes round 2 ----------
  var reissue=await Fixture("Re-tendering");
  for(int i=0;i<3;i++)await Vote(reissue,i,VoteDecision.Approve,null);
  Check(await Notice(reissue.Tender)==null,"three approvals are not a decision: 4 of 4 is required");
  await Db(async db=>Check(await db.Tenders.AnyAsync(x=>x.Id==reissue.Tender&&x.AwardedBidId==null&&x.Status==TenderStatus.TopCommitteeReview),"no award after three approvals"));
  await RunFlow(w=>w.TopVote(reissue.Tender,VoteDecision.Retender,"All offers exceed the budget by 40%",Principal(tops[3]),reissue.Hash,true));
  var n2=await Notice(reissue.Tender);
  Check(n2 is { Outcome: FinalDecisionOutcome.Retender, Status: FinalDecisionStatus.ForwardedToOfficer, ManagerAcknowledgedAt: null, RejectionReason: null }&&n2.DecisionReason=="All offers exceed the budget by 40%","one Re-Tender after three approvals stops the cycle and goes straight to the Officer with its reason");
  Check(await Notes(manager,"Top Committee requested a Re-Tender")==1,"Manager informed of the Re-Tender request once");
  await Refuse(w=>w.TopVote(reissue.Tender,VoteDecision.Reject,"Changed my mind",Principal(tops[0]),reissue.Hash,true),"approval cycle locked: an earlier signed vote cannot be changed after the Re-Tender");
  await Db(async db=>Check(await db.CommitteeVotes.CountAsync(x=>x.CommitteeRound.TenderReportId==reissue.Report&&x.CommitteeRound.CommitteeType==CommitteeType.Top)==4,"no extra vote recorded after the Re-Tender"));
  await Refuse(w=>w.Award(reissue.Tender,Principal(officer)),"award refused although three members approved");
  await Db(async db=>Check(await db.AuditLogs.AnyAsync(x=>x.EntityId==reissue.Tender.ToString()&&x.Action=="TopRetenderStoppedApproval"),"the Re-Tender stop is audited"));
  var input=new RetenderInput{Title="Re-tendering (round 2)",Description="Updated scope for the reissued round",Deadline=DateTimeOffset.UtcNow.AddDays(5),SupplierIds=[reissue.Suppliers[0],reissue.Suppliers[1]]};
  Check(await Notes(officer,"Top Committee requested a Re-Tender")==1,"Officer receives a visible actionable Re-Tender request directly");
  await Refuse(w=>w.NotifySuppliers(reissue.Tender,Principal(officer)),"Re-Tender is not executed by Notify Suppliers","Prepare the new round");
  var plan=await Flow(w=>w.RetenderPlan(reissue.Tender,Principal(officer)));
  Check(plan.Blocked==null&&plan.RoundNumber==1&&reissue.Suppliers.All(id=>plan.Suppliers.Any(x=>x.Id==id&&x.Previous)),"preparation page retains all previous participants");
  await Refuse(w=>w.RetenderPlan(reissue.Tender,Principal(manager)),"Manager cannot open the Officer preparation");
  await Refuse(w=>w.PublishRetender(reissue.Tender,input,Principal(officer)),"fewer than 3 distinct suppliers refused","at least 3");
  var freshA=await NewSupplier("New supplier A");
  input.SupplierIds=[reissue.Suppliers[0],reissue.Suppliers[1],reissue.Suppliers[1],freshA];
  Check(input.SupplierIds.Distinct().Count()==3,"invitation list edited: one previous participant removed, one new supplier added");
  await Refuse(w=>w.PublishRetender(reissue.Tender,new RetenderInput{Title=input.Title,Description=input.Description,Deadline=DateTimeOffset.UtcNow.AddDays(-1),SupplierIds=input.SupplierIds},Principal(officer)),"past deadline refused","future");
  await Refuse(w=>w.PublishRetender(reissue.Tender,new RetenderInput{Title=" ",Description=input.Description,Deadline=input.Deadline,SupplierIds=input.SupplierIds},Principal(officer)),"empty title refused","title");
  await Refuse(w=>w.PublishRetender(reissue.Tender,input,Principal(manager)),"Manager cannot publish the new round");
  await Refuse(w=>w.PublishRetender(reissue.Tender,input,Principal(supplierUsers[freshA])),"supplier cannot publish the new round");
  await RunFlow(w=>w.PublishRetender(reissue.Tender,input,Principal(officer)));
  await Refuse(w=>w.PublishRetender(reissue.Tender,input,Principal(officer)),"publishing twice is refused (idempotent)","already published");
  int oldRound=0,newRound=0;
  await Db(async db=>{
   var rounds=await db.TenderRounds.Where(x=>x.TenderId==reissue.Tender).OrderBy(x=>x.RoundNumber).ToListAsync();
   oldRound=rounds[0].Id;newRound=rounds[^1].Id;
   Check(rounds.Count==2&&rounds[0].Status==TenderRoundStatus.Failed&&rounds[1].RoundNumber==2&&rounds[1].Status==TenderRoundStatus.Open&&rounds[1].Deadline>DateTime.UtcNow,"round 2 opened under the same tender id with the new deadline");
   Check(await db.Tenders.AnyAsync(x=>x.Id==reissue.Tender&&x.Status==TenderStatus.Bidding&&x.Title=="Re-tendering (round 2)"&&x.Description=="Updated scope for the reissued round"),"Officer's permitted tender edits saved");
   Check(await db.AuditLogs.AnyAsync(x=>x.EntityId==reissue.Tender.ToString()&&x.Action=="TenderDetailsEditedForRetender"),"tender edits audited with old values");
   var invited=await db.TenderInvitations.Where(x=>x.TenderRoundId==newRound).Select(x=>x.SupplierId).ToListAsync();
   Check(invited.Count==3&&invited.Contains(freshA)&&!invited.Contains(reissue.Suppliers[2]),"exactly the edited invitation list was invited");
   Check(await db.Bids.CountAsync(x=>x.TenderInvitation.TenderRoundId==oldRound&&x.Status==BidStatus.Submitted)==3&&await db.Bids.CountAsync(x=>x.TenderInvitation.TenderRoundId==newRound)==0,"previous bids preserved; no bid carried into round 2");
   Check(await db.Set<RoundDecision>().AnyAsync(x=>x.TenderRoundId==oldRound&&x.Kind==RoundDecisionKind.TopRetender)&&await db.Set<EvaluationSession>().AnyAsync(x=>x.TenderRoundId==oldRound&&x.State==SessionState.Rejected),"round history and the old evaluation kept as history");
   Check(await db.Set<ReportSignature>().CountAsync(x=>x.TenderReportId==reissue.Report&&x.Stage==ReportSigningStage.Top)==4,"all earlier Top signatures preserved");
  });
  Check(await Notes(supplierUsers[reissue.Suppliers[0]],"Tender reissued")==1&&await Notes(supplierUsers[reissue.Suppliers[2]],"Tender reissued")==1,"previous participants notified when the new round opens (invited or not)");
  Check(await Notes(supplierUsers[freshA],"Tender invitation")==1,"newly invited supplier notified");
  // Complete restarted workflow: fresh bids, compliance, AI review, evaluation, Manager approval, Top decision, award.
  var round2Suppliers=new[]{reissue.Suppliers[0],reissue.Suppliers[1],freshA};
  foreach(var sup in round2Suppliers){
   int inv=0;await Db(async db=>inv=await db.TenderInvitations.Where(x=>x.TenderRoundId==newRound&&x.SupplierId==sup).Select(x=>x.Id).SingleAsync());
   await RunFlow(w=>w.Respond(reissue.Tender,inv,true,Principal(supplierUsers[sup])));
   await RunFlow(w=>w.SubmitBid(reissue.Tender,new BidInput{InvitationId=inv,TechnicalProposal="Fresh round 2 offer",Currency="JOD",DeliveryDays=10,PaymentTerms="30 days"},Principal(supplierUsers[sup])));
  }
  await Refuse(w=>w.SubmitBid(reissue.Tender,new BidInput{InvitationId=0,TechnicalProposal="x",Currency="JOD",DeliveryDays=1,PaymentTerms="x"},Principal(supplierUsers[reissue.Suppliers[2]])),"supplier not invited to round 2 cannot bid");
  await RunFlow(w=>w.CloseBidding(reissue.Tender,Principal(officer)));
  var newBids=new List<int>();
  await Db(async db=>{newBids=await db.Bids.Where(x=>x.TenderInvitation.TenderRoundId==newRound).Select(x=>x.Id).ToListAsync();
   foreach(var b in newBids)db.Documents.Add(new Document{BidId=b,FileName="round2-offer.txt",StoragePath="fixture/round2-offer.txt",ContentType="text/plain",SizeBytes=10,UploadedByUserId=officer,UploadedAt=DateTime.UtcNow});await db.SaveChangesAsync();});
  Check(newBids.Count==3,"three NEW bids submitted for round 2");
  foreach(var b in newBids)await RunFlow(w=>w.Compliance(reissue.Tender,b,ComplianceStatus.Compliant,"Round 2 compliance","",Principal(officer)));
  await RunFlow(w=>w.Proceed(reissue.Tender,Principal(officer)));
  int ai2=0;await Db(async db=>{var a=new AiAnalysis{TenderRoundId=newRound,Status="Completed",Model="fixture-not-live-ai",Findings="Round 2 fixture analysis",EvidenceJson="{}"};db.Add(a);await db.SaveChangesAsync();ai2=a.Id;});
  await RunFlow(w=>w.ReviewAi(reissue.Tender,ai2,Principal(officer)));
  await RunFlow(w=>w.Setup(reissue.Tender,new SessionInput{AiAnalysisId=ai2,StartsAt=DateTimeOffset.UtcNow.AddSeconds(3),EndsAt=DateTimeOffset.UtcNow.AddHours(1),MeetingUrl="https://meet.jit.si/OrangeTenderRound2Fixture"},Principal(officer)));
  await Task.Delay(4500);
  foreach(var evaluator in evaluators)await RunFlow(w=>w.SubmitEvaluation(reissue.Tender,newBids.Select((b,i)=>new ScoreInput{BidId=b,Financial=80-i*5,Technical=85-i*5,Justification="Round 2 score"}).ToList(),Principal(evaluator)));
  int report2=0;string hash2="";await Db(async db=>{var s2=await db.Set<EvaluationSession>().Include(x=>x.Report).SingleAsync(x=>x.TenderRoundId==newRound);report2=s2.ReportId!.Value;hash2=s2.Report!.PdfSha256!;});
  Check(report2!=reissue.Report,"round 2: the fourth evaluation generates a new report automatically (no Officer step)");
  foreach(var evaluator in evaluators){await Flow(w=>w.ReportPdf(report2,Principal(evaluator)));await RunFlow(w=>w.FinalizeReport(reissue.Tender,"Reviewed round 2",Principal(evaluator),hash2,true));}
  await RunFlow(w=>w.Forward(reissue.Tender,Principal(officer)));await Flow(w=>w.ReportPdf(report2,Principal(manager)));
  await RunFlow(w=>w.SubmitExecutiveSummary(reissue.Tender,Summary(),Principal(manager),hash2,true,null));
  await Db(async db=>Check(await db.CommitteeRounds.AnyAsync(x=>x.TenderReportId==report2&&x.CommitteeType==CommitteeType.Top&&x.Status==CommitteeRoundStatus.InProgress)&&await db.CommitteeRounds.AnyAsync(x=>x.TenderReportId==reissue.Report&&x.Status==CommitteeRoundStatus.Rejected),"fresh Top decision cycle; the old cycle's approvals are not carried over"));
  foreach(var top in tops)await Flow(w=>w.ReportPdf(report2,Principal(top)));
  for(int i=0;i<4;i++)await RunFlow(w=>w.TopVote(reissue.Tender,VoteDecision.Approve,"",Principal(tops[i]),hash2,true));
  await Db(async db=>{
   var t=await db.Tenders.SingleAsync(x=>x.Id==reissue.Tender);
   Check(t.Status==TenderStatus.Awarded&&newBids.Contains(t.AwardedBidId!.Value),"restarted workflow ends in an award from a round-2 bid");
   Check(await db.Set<FinalDecisionNotice>().CountAsync(x=>x.TenderId==reissue.Tender)==2,"both decision cycles kept: the Re-Tender and the new approval");
  });

  // ---------- 3. Round limit: Re-Tender never creates round 5 ----------
  var limit=await Fixture("Round limit",4);
  await Refuse(w=>w.TopVote(limit.Tender,VoteDecision.Retender,"Try again with other suppliers",Principal(tops[0]),limit.Hash,true),"Re-Tender refused in round 4 of 4 with the next permissible action","Approve or Reject");
  await Db(async db=>Check(await db.CommitteeRounds.AnyAsync(x=>x.TenderReportId==limit.Report&&x.CommitteeType==CommitteeType.Top&&x.Status==CommitteeRoundStatus.InProgress),"refused Re-Tender leaves the round-4 approval open"));
  await Vote(limit,0,VoteDecision.Reject,null);
  // Even a Re-Tender request recorded in round 4 (for example older data) cannot be executed.
  await Db(async db=>{var n=await db.Set<FinalDecisionNotice>().SingleAsync(x=>x.TenderId==limit.Tender);n.Outcome=FinalDecisionOutcome.Retender;n.RejectionReason=null;await db.SaveChangesAsync();});
  var limitPlan=await Flow(w=>w.RetenderPlan(limit.Tender,Principal(officer)));
  Check(limitPlan.Blocked!=null&&limitPlan.Blocked.Contains("Next permissible action"),"preparation page shows the round-limit reason and next permissible action");
  await Refuse(w=>w.PublishRetender(limit.Tender,new RetenderInput{Title="x",Description="y",Deadline=DateTimeOffset.UtcNow.AddDays(3),SupplierIds=limit.Suppliers},Principal(officer)),"re-tender execution blocked at the global 4-round maximum","last permitted");
  await Db(async db=>Check(!await db.TenderRounds.AnyAsync(x=>x.TenderId==limit.Tender&&x.RoundNumber==5),"no round 5 was ever created"));

  // ---------- 4. Concurrent votes: the tender row lock serializes them ----------
  var race=await Fixture("Concurrent votes");
  var attempts=new[]{(1,VoteDecision.Approve,(TopRejectionReason?)null),(0,VoteDecision.Reject,TopRejectionReason.NotNeeded),(2,VoteDecision.Approve,null),(3,VoteDecision.Approve,null)};
  var results=await Task.WhenAll(attempts.Select(async a=>{try{await Vote(race,a.Item1,a.Item2,a.Item3);return "ok";}catch(WorkflowException e){return "refused: "+e.Message;}}));
  foreach(var r in results)Console.WriteLine("  concurrent vote: "+r);
  await Db(async db=>{
   var votes=await db.CommitteeVotes.Where(x=>x.CommitteeRound.TenderReportId==race.Report&&x.CommitteeRound.CommitteeType==CommitteeType.Top).ToListAsync();
   var reject=votes.SingleOrDefault(x=>x.Decision==VoteDecision.Reject);
   Check(reject!=null&&!votes.Any(x=>x.Id>reject.Id),"no vote is accepted after the concurrent rejection");
   Check(await db.Set<FinalDecisionNotice>().CountAsync(x=>x.TenderId==race.Tender)==1&&await db.Set<FinalDecisionNotice>().AnyAsync(x=>x.TenderId==race.Tender&&x.Outcome==FinalDecisionOutcome.Cancel),"exactly one notice, carrying the rejection");
   Check(await db.Tenders.AnyAsync(x=>x.Id==race.Tender&&x.AwardedBidId==null&&x.Status==TenderStatus.Rejected),"concurrent approvals cannot award a rejected tender");
  });

  // ---------- 5. APPROVE: existing successful path when nobody stops it ----------
  var approve=await Fixture("Approval preserved");
  for(int i=0;i<3;i++)await Vote(approve,i,VoteDecision.Approve,null);
  await Refuse(w=>w.Award(approve.Tender,Principal(officer)),"Officer cannot award after three approvals");
  await RunFlow(w=>w.TopVote(approve.Tender,VoteDecision.Approve,"",Principal(tops[0]),approve.Hash,true)); // retry of the same signed vote
  await Db(async db=>Check(await db.CommitteeVotes.CountAsync(x=>x.CommitteeRound.TenderReportId==approve.Report&&x.CommitteeRound.CommitteeType==CommitteeType.Top)==3
    &&await db.Tenders.AnyAsync(x=>x.Id==approve.Tender&&x.AwardedBidId==null),"a repeated approval by the same member is idempotent: no second vote, no award"));
  await RunFlow(w=>w.TopVote(approve.Tender,VoteDecision.Approve,"",Principal(tops[3]),approve.Hash,true));
  await Db(async db=>{
   var t=await db.Tenders.SingleAsync(x=>x.Id==approve.Tender);
   var report=await db.TenderReports.SingleAsync(x=>x.Id==approve.Report);
   Check(t.Status==TenderStatus.Awarded&&t.AwardedBidId==report.RecommendedBidId&&t.AwardedAt!=null&&t.ClosedAt!=null,"the fourth approval awards the recommended offer automatically");
   Check(t.AwardApprovalEvidenceJson!.Contains("AutomaticOnUnanimousTopApproval"),"award evidence records the automatic unanimous decision");
   var n=await db.Set<FinalDecisionNotice>().SingleAsync(x=>x.TenderId==approve.Tender);
   Check(n.Status==FinalDecisionStatus.SuppliersNotified&&n.TopApprovals==4&&n.ManagerAcknowledgedAt==null,"no Sourcing Manager approval after the unanimous Top decision");
   Check(await db.Set<SupplierResultDelivery>().CountAsync(x=>x.TenderId==approve.Tender&&(x.Outcome=="Won"||x.Outcome=="Unsuccessful"))==3,"each supplier gets its own result automatically");
   var link="/Workflow/Details/"+approve.Tender;
   Check(await db.Notifications.CountAsync(x=>x.UserId==requester&&x.Title=="Purchase request awarded"&&x.Message.Contains("Approval preserved"))==1&&await db.Notifications.CountAsync(x=>x.UserId==officer&&x.Title=="Tender awarded"&&x.Link==link)==1
     &&await db.Notifications.CountAsync(x=>x.UserId==manager&&x.Title=="Tender awarded"&&x.Link==link)==1&&await db.Notifications.CountAsync(x=>x.UserId==tops[0]&&x.Title=="Tender awarded"&&x.Link==link)==1,"department, Officer, Manager and Top members notified once");
   var supplierMessages=await db.Notifications.Where(x=>x.Title=="Tender result").Select(x=>x.Message).ToListAsync();
   Check(supplierMessages.All(m=>!m.Contains("score")&&!m.Contains("vote")&&!m.Contains("JOD")),"supplier messages reveal no competitor, score, vote or price");
  });
  await Refuse(w=>w.TopVote(approve.Tender,VoteDecision.Reject,"Changed my mind after the award",Principal(tops[1]),approve.Hash,true),"a signed approval cannot be turned into a rejection after the award");
  await Refuse(w=>w.ManagerAcknowledge(approve.Tender,"Approved",Principal(manager)),"no Manager acknowledgement after the award","No acknowledgement");
  await Refuse(w=>w.NotifySuppliers(approve.Tender,Principal(officer)),"suppliers are not notified twice","already notified");

  // ---------- HTTP: real Identity login, role and CSRF filters ----------
  using var client=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});
  async Task<string> Token(string url){var html=await(await client.GetAsync(url)).Content.ReadAsStringAsync();var match=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(!match.Success)throw new Exception("No CSRF token");return WebUtility.HtmlDecode(match.Groups[1].Value);}
  var anon=await client.GetAsync("/TopCommittee/Rejections/Details/"+cancel.Report);Check(anon.StatusCode==HttpStatusCode.Redirect,"anonymous Top decision page denied");
  var csrf=await Token("/Identity/Account/Login");var login=await client.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email","top1@topreason.invalid"},{"Input.Password",pw},{"__RequestVerificationToken",csrf}}));Check(login.StatusCode==HttpStatusCode.Redirect,"real Identity Top login");
  var page=await client.GetAsync("/TopCommittee/Rejections/Details/"+cancel.Report);var body=await page.Content.ReadAsStringAsync();Check(page.IsSuccessStatusCode&&body.Contains("Top rejection finalized")&&body.Contains("Company No Longer Needs This Tender"),"Top decision page renders the SQL outcome and reason");
  var officerPage=await client.GetAsync("/SourcingOfficer/FinalDecisions");Check(officerPage.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden,"Top member cannot open the Officer final decisions page");
  var post=await client.PostAsync("/SourcingOfficer/FinalDecisions/Notify/"+limit.Tender,new FormUrlEncodedContent(new Dictionary<string,string>()));Check(post.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Redirect or HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed,"Top member cannot post the Officer notify action");
  Console.WriteLine($"RESULT PASS={passes} FAIL=0 DATABASE={catalog}. Real SQL Server; no production database used.");
 }
}
