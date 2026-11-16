using System.Net;
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

// Global event spotlight + My Offers result checks (--spotlight-checks). Real SQL Server (isolated OrangeAuthCheck_Spotlight_*
// database with migrations applied), real Identity logins through the test host, real HTTP endpoints.
public static class SpotlightChecks
{
 public static async Task Run()
 {
  var connection=Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")!;
  if(!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_Spotlight_"))throw new Exception("Isolated spotlight database required");
  var root=Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;
  Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection",connection);Environment.SetEnvironmentVariable("DemoData__Enabled","false");
  using var app=new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b=>{b.UseContentRoot(root).UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
   ["ConnectionStrings:Default Connection"]=connection,["DemoData:Enabled"]="false",["BootstrapAdmin:Email"]="",["BootstrapAdmin:Password"]="",["Biometric:SupplierPasswordOnly"]="true",["Logging:LogLevel:Default"]="Warning"}));});
  int count=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);count++;Console.WriteLine("PASS "+name);}
  var password="S!9b"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
  string winner="",loser="",officer="";int bidA=0,bidB=0,tenderId=0,winNote=0,loseNote=0,forgedNote=0,officerNote=0,oldNote=0,readNote=0;var tenderTitle="Spotlight laptops "+Guid.NewGuid().ToString("N")[..6];
  using(var scope=app.Services.CreateScope()){
   var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(!await db.Users.AnyAsync(),"isolated SQL Server database initially empty");
   var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
   var dept=new Department{Name="Spotlight department",IsActive=true};db.Add(dept);
   var sa=new Supplier{CompanyName="Winner Co",Email="a@spot.invalid",RegistrationNumber=Guid.NewGuid().ToString(),Status=SupplierStatus.Approved};
   var sb=new Supplier{CompanyName="Other Co",Email="b@spot.invalid",RegistrationNumber=Guid.NewGuid().ToString(),Status=SupplierStatus.Approved};db.AddRange(sa,sb);await db.SaveChangesAsync();
   async Task<string> User(string email,string role,int? supplier){var u=new ApplicationUser{Email=email,UserName=email,EmailConfirmed=true,IsActive=true,FullName=email,SupplierId=supplier};
    if(!(await users.CreateAsync(u,password)).Succeeded||!(await users.AddToRoleAsync(u,role)).Succeeded)throw new Exception("Identity setup failed");return u.Id;}
   winner=await User("winner@spot.invalid",SystemRoles.Supplier,sa.Id);loser=await User("loser@spot.invalid",SystemRoles.Supplier,sb.Id);officer=await User("officer@spot.invalid",SystemRoles.SourcingOfficer,null);
   var pr=new PurchaseRequest{Title="Spotlight PR",DepartmentId=dept.Id,CreatedByUserId=officer,Status=PurchaseRequestStatus.Accepted,Justification="fixture"};db.Add(pr);await db.SaveChangesAsync();
   var t=new Tender{Title=tenderTitle,Description="fixture",PurchaseRequestId=pr.Id,CreatedByUserId=officer,CreatedAt=DateTime.UtcNow,Status=TenderStatus.Awarded};db.Add(t);await db.SaveChangesAsync();tenderId=t.Id;
   var round=new TenderRound{TenderId=t.Id,RoundNumber=1,CreatedByUserId=officer,CreatedAt=DateTime.UtcNow};db.Add(round);await db.SaveChangesAsync();
   var ia=new TenderInvitation{TenderRoundId=round.Id,SupplierId=sa.Id,InvitedByUserId=officer,InvitedAt=DateTime.UtcNow};var ib=new TenderInvitation{TenderRoundId=round.Id,SupplierId=sb.Id,InvitedByUserId=officer,InvitedAt=DateTime.UtcNow};db.AddRange(ia,ib);await db.SaveChangesAsync();
   var ba=new Bid{TenderInvitationId=ia.Id,Status=BidStatus.Submitted,CreatedAt=DateTime.UtcNow,SubmittedAt=DateTime.UtcNow};var bb=new Bid{TenderInvitationId=ib.Id,Status=BidStatus.Submitted,CreatedAt=DateTime.UtcNow,SubmittedAt=DateTime.UtcNow};db.AddRange(ba,bb);await db.SaveChangesAsync();bidA=ba.Id;bidB=bb.Id;
   t.AwardedBidId=ba.Id;t.AwardedAt=DateTime.UtcNow;t.AwardedByUserId=officer;
   var notice=new FinalDecisionNotice{TenderId=t.Id,Status=FinalDecisionStatus.SuppliersNotified,CreatedAt=DateTime.UtcNow,SuppliersNotifiedAt=DateTime.UtcNow};db.Add(notice);
   Notification N(string user,string title,string msg,string? link,DateTime? at=null,bool read=false){var n=new Notification{UserId=user,Title=title,Message=msg,Link=link,CreatedAt=at??DateTime.UtcNow,IsRead=read};db.Add(n);return n;}
   var nw=N(winner,"Tender result","Congratulations: your company has been selected for this tender after all mandatory approvals.",$"/Supplier/Offers/Details/{ba.Id}");
   var nl=N(loser,"Tender result","The tender has been awarded. Your company was not selected. Thank you for participating.",$"/Supplier/Offers/Details/{bb.Id}");
   // Forged: a "Tender result" row for the loser that points at the winner's offer must never become a celebration.
   var nf=N(loser,"Tender result","Congratulations: you won",$"/Supplier/Offers/Details/{ba.Id}");
   var no=N(officer,"Purchase request decision","The purchase request was approved by the department head.","/SourcingOfficer");
   var nold=N(officer,"Purchase request decision","Old approval",null,DateTime.UtcNow.AddDays(-40));
   var nread=N(officer,"Supplier clarification","Already read reply",null,null,true);
   await db.SaveChangesAsync();
   db.AddRange(new SupplierResultDelivery{FinalDecisionNoticeId=notice.Id,TenderId=t.Id,SupplierId=sa.Id,BidId=ba.Id,RecipientUserId=winner,Outcome="Won",CreatedAt=DateTime.UtcNow,NotificationId=nw.Id},
               new SupplierResultDelivery{FinalDecisionNoticeId=notice.Id,TenderId=t.Id,SupplierId=sb.Id,BidId=bb.Id,RecipientUserId=loser,Outcome="Unsuccessful",CreatedAt=DateTime.UtcNow,NotificationId=nl.Id});
   await db.SaveChangesAsync();winNote=nw.Id;loseNote=nl.Id;forgedNote=nf.Id;officerNote=no.Id;oldNote=nold.Id;readNote=nread.Id;
  }
  // classification (pure)
  Check(EventSpotlightService.Classify("Purchase request decision","The request was approved")=="approval","classify approval");
  Check(EventSpotlightService.Classify("Tender result","Your company was not selected")=="rejection","classify not-selected as rejection (no celebration)");
  Check(EventSpotlightService.Classify("Evaluation Report Decision","Returned for review by the manager")=="returned","classify returned");
  Check(EventSpotlightService.Classify("Clarification reply","New reply")=="message","classify message");
  Check(EventSpotlightService.Classify("Top Committee — Report Ready","Please sign")=="action","classify action required");
  Check(EventSpotlightService.SafeLink("javascript:alert(1)")==null&&EventSpotlightService.SafeLink("//evil.example")==null&&EventSpotlightService.SafeLink("/Supplier")=="/Supplier","only relative in-app links");

  async Task<string> Token(HttpClient c,string url){var html=await(await c.GetAsync(url)).Content.ReadAsStringAsync();var m=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(!m.Success)m=Regex.Match(html,"data-csrf=\"([^\"]+)\"");if(!m.Success)throw new Exception("No CSRF token: "+url);return WebUtility.HtmlDecode(m.Groups[1].Value);}
  async Task<HttpClient> Login(string email){var c=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});var tok=await Token(c,"/Identity/Account/Login");
   var r=await c.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email",email},{"Input.Password",password},{"__RequestVerificationToken",tok}}));Check(r.StatusCode==HttpStatusCode.Redirect,email+": real Identity login");return c;}
  async Task<JsonElement> Pending(HttpClient c){var r=await c.GetAsync("/Spotlight/Pending");r.EnsureSuccessStatusCode();return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  async Task<HttpResponseMessage> Ack(HttpClient c,int id,string? csrf){var m=new HttpRequestMessage(HttpMethod.Post,"/Spotlight/Acknowledge/"+id);if(csrf!=null)m.Headers.Add("RequestVerificationToken",csrf);return await c.SendAsync(m);}

  using(var anon=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false})){var r=await anon.GetAsync("/Spotlight/Pending");Check(!r.IsSuccessStatusCode,"anonymous spotlight feed refused");}

  var w=await Login("winner@spot.invalid");
  var dash=await(await w.GetAsync("/Supplier")).Content.ReadAsStringAsync();
  Check(dash.Contains("id=\"event-spotlight\"")&&dash.Contains("event-spotlight.js")&&dash.Contains("data-lang=\"en\""),"supplier dashboard renders the global spotlight host (English)");
  var csrfW=Regex.Match(dash,"id=\"event-spotlight\"[^>]*data-csrf=\"([^\"]+)\"",RegexOptions.Singleline).Groups[1].Value;Check(csrfW.Length>10,"spotlight host carries an antiforgery token");
  var pw=await Pending(w);var wins=pw.EnumerateArray().Where(e=>e.GetProperty("kind").GetString()=="win").ToList();
  Check(wins.Count==1&&wins[0].GetProperty("id").GetInt32()==winNote,"winner: exactly one verified win event");
  Check(wins[0].GetProperty("tenderTitle").GetString()==tenderTitle&&wins[0].GetProperty("tenderReference").GetString()==$"T-{tenderId:0000}","win carries the real tender name and reference from SQL Server");
  Check(wins[0].GetProperty("link").GetString()==$"/Supplier/Offers/Details/{bidA}","win links to the existing authorized award details page");
  Check((await w.GetAsync($"/Supplier/Offers/Details/{bidA}")).IsSuccessStatusCode,"award details page opens for the winner");

  var l=await Login("loser@spot.invalid");var pl=await Pending(l);var plText=pl.GetRawText();
  Check(!pl.EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="win"),"other supplier: no celebration (forged 'Tender result' text ignored)");
  Check(pl.EnumerateArray().Any(e=>e.GetProperty("id").GetInt32()==loseNote&&e.GetProperty("kind").GetString()=="rejection"),"other supplier: calm 'not selected' rejection event");
  Check(!plText.Contains("Winner Co")&&!pl.EnumerateArray().Any(e=>e.TryGetProperty("tenderReference",out var r)&&r.ValueKind==JsonValueKind.String),"no winner company or award data exposed to the other supplier");
  Check((await l.GetAsync($"/Supplier/Offers/Details/{bidA}")).StatusCode==HttpStatusCode.NotFound,"other supplier cannot open the winner's offer");
  var csrfL=await Token(l,"/Supplier");
  var crossAck=await Ack(l,winNote,csrfL);Console.WriteLine("cross-user ack status: "+(int)crossAck.StatusCode);
  Check(crossAck.StatusCode==HttpStatusCode.NotFound,"cannot acknowledge another user's notification");

  Check((await Ack(w,winNote,null)).StatusCode==HttpStatusCode.BadRequest,"acknowledge without antiforgery token rejected");
  Check((await Ack(w,winNote,csrfW)).IsSuccessStatusCode,"winner acknowledges the celebration");
  Check((await Ack(w,winNote,csrfW)).IsSuccessStatusCode,"repeat acknowledgement is idempotent");
  Check(!(await Pending(w)).EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="win"),"celebration not offered again in the same session");
  var w2=await Login("winner@spot.invalid");
  Check(!(await Pending(w2)).EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="win"),"celebration not shown again after a new login (persisted)");
  using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
   Check(await db.NotificationAcknowledgements.CountAsync(a=>a.UserId==winner&&a.NotificationId==winNote&&a.Kind=="win")==1,"one acknowledgement row stored in SQL Server");
   Check(!await db.NotificationAcknowledgements.AnyAsync(a=>a.UserId==loser&&a.NotificationId==winNote),"no row stored for the refused cross-user acknowledgement");
   Check(await db.Notifications.AnyAsync(n=>n.Id==winNote),"notification itself kept for history");}
  var hist=await(await w2.GetAsync("/Supplier/Notifications")).Content.ReadAsStringAsync();Check(hist.Contains("Tender result"),"notification history still lists the acknowledged result");

  // My Offers: list result, popup partial, full page fallback
  var list=await(await w2.GetAsync("/Supplier/Offers")).Content.ReadAsStringAsync();
  Check(list.Contains("so-row-won")&&list.Contains("data-result=\"Won\"")&&list.Contains("data-offer-details"),"My Offers marks the won offer and wires the details popup");
  var popReq=new HttpRequestMessage(HttpMethod.Get,$"/Supplier/Offers/Details/{bidA}");popReq.Headers.Add("X-Requested-With","XMLHttpRequest");
  var pop=await(await w2.SendAsync(popReq)).Content.ReadAsStringAsync();Check(pop.Contains("so-pop")&&pop.Contains("data-so-state=\"won\"")&&!pop.Contains("<html"),"details popup partial shows the won state");
  var lpop=new HttpRequestMessage(HttpMethod.Get,$"/Supplier/Offers/Details/{bidB}");lpop.Headers.Add("X-Requested-With","XMLHttpRequest");
  var lpopHtml=await(await l.SendAsync(lpop)).Content.ReadAsStringAsync();Check(lpopHtml.Contains("data-so-state=\"lost\"")&&lpopHtml.Contains("حظاً أوفر"),"not-awarded offer popup shows the 'better luck next time' state");
  var llist=await(await l.GetAsync("/Supplier/Offers")).Content.ReadAsStringAsync();Check(llist.Contains("data-result=\"Not awarded\"")&&!llist.Contains("so-row-won"),"other supplier's list shows 'Not awarded' only");

  // Other roles: calm events, window and read filter, Arabic/RTL host language
  var o=await Login("officer@spot.invalid");var po=await Pending(o);
  Check(po.EnumerateArray().Any(e=>e.GetProperty("id").GetInt32()==officerNote&&e.GetProperty("kind").GetString()=="approval"),"officer: approval event card");
  Check(!po.EnumerateArray().Any(e=>e.GetProperty("id").GetInt32()==oldNote||e.GetProperty("id").GetInt32()==readNote),"old and already-read notifications are not spotlighted");
  Check(!po.EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="win"),"internal role never receives a celebration");
  var arReq=new HttpRequestMessage(HttpMethod.Get,"/SourcingOfficer");arReq.Headers.Add("Cookie","orange-lang=ar");
  var arHtml=await(await o.SendAsync(arReq)).Content.ReadAsStringAsync();Check(arHtml.Contains("id=\"event-spotlight\"")&&arHtml.Contains("data-lang=\"ar\""),"Arabic workspace renders the spotlight in Arabic mode");
  Console.WriteLine($"Spotlight checks passed: {count}");
 }
}
