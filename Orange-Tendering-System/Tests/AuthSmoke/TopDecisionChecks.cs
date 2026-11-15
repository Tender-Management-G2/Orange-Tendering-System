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

// Real SQL Server, real PDF preparation/signing and real Identity HTTP checks, isolated fixture records only.
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
  async Task Refuse(Func<TenderWorkflowService,Task> work,string label){try{await RunFlow(work);}catch(WorkflowException){Check(true,label);return;}throw new InvalidOperationException("FAIL "+label);}
  ClaimsPrincipal Principal(string id)=>new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id)],"fixture"));
  var pw="T!7a"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
  string officer="",manager="",outsider="";var evaluators=new List<string>();var tops=new List<string>();int dept=0;
  using(var sc=app.Services.CreateScope()){
   var db=sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(!await db.Users.AnyAsync(),"new isolated SQL database");
   var users=sc.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
   async Task<string> Account(string name,string role){var u=new ApplicationUser{UserName=name+"@topreason.invalid",Email=name+"@topreason.invalid",FullName=name,EmailConfirmed=true,IsActive=true};
    var created=await users.CreateAsync(u,pw);if(!created.Succeeded||!(await users.AddToRoleAsync(u,role)).Succeeded)throw new Exception("Identity provisioning failed");return u.Id;}
   officer=await Account("officer",SystemRoles.SourcingOfficer);manager=await Account("manager",SystemRoles.SourcingManager);evaluators.Add(manager);
   for(int i=1;i<=3;i++)evaluators.Add(await Account("evaluator"+i,SystemRoles.EvaluationCommittee));
   for(int i=1;i<=4;i++)tops.Add(await Account("top"+i,SystemRoles.TopCommittee));
   outsider=await Account("outsider",SystemRoles.TopCommittee);
   var d=new Department{Name="Isolated Top reason fixtures"};db.Add(d);await db.SaveChangesAsync();dept=d.Id;
  }
  async Task<(int Tender,int Report,string Hash)> Fixture(string label){
   int tenderId=0;
   await Db(async db=>{
    var now=DateTime.UtcNow;
    var pr=new PurchaseRequest{DepartmentId=dept,CreatedByUserId=officer,Title=label,Justification="SQL-only fixture",Status=PurchaseRequestStatus.Accepted};db.Add(pr);await db.SaveChangesAsync();
    var t=new Tender{Title=label,PurchaseRequestId=pr.Id,CreatedByUserId=officer,Status=TenderStatus.Evaluation,CreatedAt=now};db.Add(t);await db.SaveChangesAsync();tenderId=t.Id;
    var round=new TenderRound{TenderId=t.Id,RoundNumber=1,Status=TenderRoundStatus.Closed,CreatedByUserId=officer,CreatedAt=now,Deadline=now.AddDays(-1)};db.Add(round);
    var weights=new WeightRound{TenderId=t.Id,Version=1,State=WeightState.Approved,ApprovedFinancial=40,ApprovedTechnical=60,ApprovedByUserId=manager,ApprovedAt=now,RequirementsJson="{}"};db.Add(weights);
    var seats=evaluators.Select((u,i)=>new CommitteeMember{TenderId=t.Id,UserId=u,CommitteeType=CommitteeType.Evaluation,Responsibility=(CommitteeResponsibility)i,AssignedAt=now}).ToArray();db.AddRange(seats);
    db.AddRange(tops.Select(u=>new CommitteeMember{TenderId=t.Id,UserId=u,CommitteeType=CommitteeType.Top,AssignedAt=now}));await db.SaveChangesAsync();
    var cr=new CommitteeRound{TenderRoundId=round.Id,CommitteeType=CommitteeType.Evaluation,RoundNumber=1,StartedAt=now};db.Add(cr);await db.SaveChangesAsync();
    var criteria=new[]{new EvaluationCriterion{CommitteeRoundId=cr.Id,Name="Financial",Type=CriterionType.Financial,Weight=40,MaxScore=100},new EvaluationCriterion{CommitteeRoundId=cr.Id,Name="Technical",Type=CriterionType.Technical,Weight=60,MaxScore=100}};db.AddRange(criteria);
    var ai=new AiAnalysis{TenderRoundId=round.Id,Status="Completed",Model="fixture-not-live-ai",Findings="Isolated source fixture",EvidenceJson="{}",ReviewedAt=now};db.Add(ai);await db.SaveChangesAsync();
    var policy=new TenderApprovalPolicy{Version="isolated",TopCommitteeRequirement="All",TopQuorum=3,TopApprovalsRequired=3,RejectVoteBlocksAward=false,TopTieOutcome="Block"};
    var session=new EvaluationSession{TenderRoundId=round.Id,WeightRoundId=weights.Id,CommitteeRoundId=cr.Id,AiAnalysisId=ai.Id,State=SessionState.Scheduled,StartsAt=now.AddHours(-1),EndsAt=now.AddDays(1),FinalApprovalPolicyJson=JsonSerializer.Serialize(policy)};db.Add(session);await db.SaveChangesAsync();
    foreach(var seat in seats)db.Add(new EvaluationParticipation{EvaluationSessionId=session.Id,CommitteeMemberId=seat.Id,SubmittedAt=now});
    for(int i=0;i<3;i++){
     var supplier=new Supplier{CompanyName=label+" supplier "+i,Email="s"+Guid.NewGuid().ToString("N")+"@topreason.invalid",RegistrationNumber=Guid.NewGuid().ToString(),Status=SupplierStatus.Approved};db.Add(supplier);await db.SaveChangesAsync();
     var invite=new TenderInvitation{TenderRoundId=round.Id,SupplierId=supplier.Id,InvitedByUserId=officer,InvitedAt=now};db.Add(invite);await db.SaveChangesAsync();
     var bid=new Bid{TenderInvitationId=invite.Id,Status=BidStatus.Submitted,ComplianceStatus=ComplianceStatus.Compliant,CreatedAt=now,SubmittedAt=now};db.Add(bid);await db.SaveChangesAsync();
     db.Add(new SessionBid{EvaluationSessionId=session.Id,BidId=bid.Id});foreach(var seat in seats)foreach(var criterion in criteria)db.Add(new EvaluationScore{BidId=bid.Id,CommitteeMemberId=seat.Id,EvaluationCriterionId=criterion.Id,Score=90-i*10,Comment="Fixture original score",ScoredAt=now});
    }
    await db.SaveChangesAsync();
   });
   await RunFlow(w=>w.EndEvaluation(tenderId,true,"Isolated complete scores",Principal(officer)));
   await RunFlow(w=>w.SendReportToCommittee(tenderId,Principal(officer)));
   int report=0;string hash="";await Db(async db=>{var s=await db.Set<EvaluationSession>().Include(x=>x.Report).SingleAsync(x=>x.TenderRound.TenderId==tenderId);report=s.ReportId!.Value;hash=s.Report!.PdfSha256!;});
   foreach(var evaluator in evaluators){await Flow(w=>w.ReportPdf(report,Principal(evaluator)));await RunFlow(w=>w.FinalizeReport(tenderId,"Reviewed",Principal(evaluator),hash,true));}
   await RunFlow(w=>w.Forward(tenderId,Principal(officer)));await Flow(w=>w.ReportPdf(report,Principal(manager)));
   await RunFlow(w=>w.ManagerDecision(tenderId,"Approve","Reviewed",Principal(manager),hash,true));
   foreach(var top in tops)await Flow(w=>w.ReportPdf(report,Principal(top)));
   Check(true,label+": real generated PDF, four evaluation signatures and separate Manager signature");return(tenderId,report,hash);
  }
  var first=await Fixture("Matching reasons");
  Task Vote((int Tender,int Report,string Hash) f,int n,VoteDecision decision,TopRejectionReason? reason)=>RunFlow(w=>w.TopVote(f.Tender,decision,"Signed fixture justification",Principal(tops[n]),f.Hash,true,rejectionReason:reason));
  await Refuse(w=>w.TopVote(first.Tender,VoteDecision.Reject,"Missing",Principal(tops[0]),first.Hash,true),"missing reject reason denied");
  await Refuse(w=>w.TopVote(first.Tender,VoteDecision.Reject,"Invalid",Principal(tops[0]),first.Hash,true,rejectionReason:(TopRejectionReason)99),"unknown reject reason denied");
  await Refuse(w=>w.TopVote(first.Tender,VoteDecision.Reject,"Wrong role",Principal(officer),first.Hash,true,rejectionReason:TopRejectionReason.NotNeeded),"Officer cannot vote");
  await Refuse(w=>w.TopVote(first.Tender,VoteDecision.Reject,"Unassigned",Principal(outsider),first.Hash,true,rejectionReason:TopRejectionReason.NotNeeded),"unassigned Top member cannot vote");
  await Vote(first,0,VoteDecision.Reject,TopRejectionReason.NotNeeded);
  var one=await Flow(w=>w.TopRejectionDetails(first.Report,Principal(tops[0])));Check(one.Status=="Voting in progress"&&!one.FinalReason.HasValue,"single rejection preserves existing voting cycle");
  await Vote(first,1,VoteDecision.Reject,TopRejectionReason.NotNeeded);await Vote(first,2,VoteDecision.Reject,TopRejectionReason.NotNeeded);await Vote(first,3,VoteDecision.Approve,null);
  var same=await Flow(w=>w.TopRejectionDetails(first.Report,Principal(tops[0])));Check(same.FinalReason==TopRejectionReason.NotNeeded,"matching reasons finalize Top rejection");
  await Vote(first,0,VoteDecision.Reject,TopRejectionReason.NotNeeded);
  await Refuse(w=>w.TopVote(first.Tender,VoteDecision.Reject,"Changed",Principal(tops[0]),first.Hash,true,rejectionReason:TopRejectionReason.UnacceptableOffers),"signed original reason cannot be overwritten");
  var conflict=await Fixture("Conflicting reasons");
  await Vote(conflict,0,VoteDecision.Reject,TopRejectionReason.NotNeeded);await Vote(conflict,1,VoteDecision.Reject,TopRejectionReason.UnacceptableOffers);await Vote(conflict,2,VoteDecision.Reject,TopRejectionReason.NotNeeded);await Vote(conflict,3,VoteDecision.Approve,null);
  var pending=await Flow(w=>w.TopRejectionDetails(conflict.Report,Principal(tops[0])));Check(pending.CanAgree&&!pending.FinalReason.HasValue,"conflicting reasons stay pending");
  await Refuse(w=>w.AgreeTopRejectionReason(conflict.Report,TopRejectionReason.NotNeeded,"Hash tampering","bad",true,Principal(tops[0])),"reason agreement hash validation");
  await Refuse(w=>w.AgreeTopRejectionReason(conflict.Report,TopRejectionReason.NotNeeded,"Unassigned",conflict.Hash,true,Principal(outsider)),"unassigned reason confirmation denied");
  for(int i=0;i<3;i++)await RunFlow(w=>w.AgreeTopRejectionReason(conflict.Report,TopRejectionReason.UnacceptableOffers,"Committee agreement",conflict.Hash,true,Principal(tops[i])));
  Check((await Flow(w=>w.TopRejectionDetails(conflict.Report,Principal(tops[0])))).CanAgree,"three confirmations cannot choose for the fourth");
  await RunFlow(w=>w.AgreeTopRejectionReason(conflict.Report,TopRejectionReason.UnacceptableOffers,"Committee agreement",conflict.Hash,true,Principal(tops[3])));
  await RunFlow(w=>w.AgreeTopRejectionReason(conflict.Report,TopRejectionReason.UnacceptableOffers,"Duplicate retry",conflict.Hash,true,Principal(tops[3])));
  var agreed=await Flow(w=>w.TopRejectionDetails(conflict.Report,Principal(tops[0])));Check(agreed.FinalReason==TopRejectionReason.UnacceptableOffers&&!agreed.CanAgree,"four matching confirmations finalize without rewriting votes");
  var approval=await Fixture("Approval preserved");for(int i=0;i<3;i++)await Vote(approval,i,VoteDecision.Approve,null);
  await Db(async db=>Check(await db.Set<FinalDecisionNotice>().AnyAsync(n=>n.TenderReportId==approval.Report&&n.Status==FinalDecisionStatus.AwaitingManager),"original 3-of-4 approval relay unchanged"));
  await Vote(approval,3,VoteDecision.Reject,TopRejectionReason.NotNeeded);
  Check((await Flow(w=>w.TopRejectionDetails(approval.Report,Principal(tops[0])))).Status.StartsWith("Approved"),"3 approvals and 1 reject retain frozen non-veto approval policy");
  await Db(async db=>{
   Check(await db.CommitteeVotes.CountAsync(x=>x.CommitteeRound.TenderReportId==conflict.Report&&x.CommitteeRound.CommitteeType==CommitteeType.Top)==4,"exactly four original Top votes persisted");
   Check(await db.AuditLogs.CountAsync(x=>x.EntityId==conflict.Tender.ToString()&&x.Action=="TopReasonAgreementEvidence")==4,"idempotent reason confirmations persisted once");
   Check(await db.AuditLogs.CountAsync(x=>x.EntityId==conflict.Tender.ToString()&&x.Action=="TopRejectionFinalized")==1,"one final rejection audit");
   Check(await db.Tenders.AllAsync(x=>x.Status==TenderStatus.TopCommitteeReview&&x.AwardedBidId==null&&x.ClosedAt==null),"no downstream tender cancellation, award or status changes");
   Check(await db.TenderRounds.CountAsync()==3&&await db.Bids.CountAsync()==9,"rounds and bid history preserved; no re-tender created");
   Check(await db.Set<ReportSignature>().CountAsync()==27,"all original evaluation, Manager and Top signatures preserved");
  });
  // HTTP checks use the actual Identity login and role/CSRF filters.
  using var client=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});
  async Task<string> Token(string url){var html=await(await client.GetAsync(url)).Content.ReadAsStringAsync();var match=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(!match.Success)throw new Exception("No CSRF token");return WebUtility.HtmlDecode(match.Groups[1].Value);}
  var anon=await client.GetAsync("/TopCommittee/Rejections/Details/"+conflict.Report);Check(anon.StatusCode==HttpStatusCode.Redirect,"anonymous Top reasons page denied");
  var csrf=await Token("/Identity/Account/Login");var login=await client.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email","top1@topreason.invalid"},{"Input.Password",pw},{"__RequestVerificationToken",csrf}}));Check(login.StatusCode==HttpStatusCode.Redirect,"real Identity Top login");
  var page=await client.GetAsync("/TopCommittee/Rejections/Details/"+conflict.Report);var body=await page.Content.ReadAsStringAsync();Check(page.IsSuccessStatusCode&&body.Contains("Top rejection finalized"),"actual Top decision Razor page renders SQL outcome");
  var replay=await client.PostAsync("/TopCommittee/Rejections/Agree",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",conflict.Report.ToString()},{"reason","NotNeeded"}}));Console.WriteLine("HTTP CSRF refusal status="+(int)replay.StatusCode);Check(replay.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.MethodNotAllowed,"reason agreement HTTP CSRF enforced (existing status-page re-execution may return 405)");
  Console.WriteLine($"RESULT PASS={passes} FAIL=0 DATABASE={catalog}. No production database used; fixtures are not live Ollama/Zoom/SMTP tests.");
 }
}
