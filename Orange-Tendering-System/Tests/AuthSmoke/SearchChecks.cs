using System.Net;
using System.Security.Cryptography;
using System.Text;
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

public static class SearchChecks
{
 public static async Task Run()
 {
  var connection=Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")!;
  var catalog=new SqlConnectionStringBuilder(connection).InitialCatalog;
  if(!catalog.StartsWith("OrangeAuthCheck_Search_"))throw new Exception("Isolated notification database required");
  var root=Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;
  Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection",connection);
  Environment.SetEnvironmentVariable("DemoData__Enabled","false");
  using var app=new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b=>{
   b.UseContentRoot(root).UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
    ["ConnectionStrings:Default Connection"]=connection,["DemoData:Enabled"]="false",["BootstrapAdmin:Email"]="",["BootstrapAdmin:Password"]="",["Biometric:SupplierPasswordOnly"]="true",["Logging:LogLevel:Default"]="Warning"}));
  });
  var count=0;void Check(bool yes,string name){if(!yes)throw new Exception("FAIL "+name);count++;Console.WriteLine("PASS "+name);}
  var password="N!8a"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
  string[] areas=["Admin","Department","SourcingOfficer","Supplier","EvaluationCommittee","SourcingManager","TopCommittee"];
  // The seven workspace roles, in the same order as areas. roles also lists job roles (CEO, Technical...) since the
  // WorkV3 merge, so it can no longer be indexed together with areas.
  string[] roles=[SystemRoles.Admin,SystemRoles.RequestingDepartment,SystemRoles.SourcingOfficer,SystemRoles.Supplier,SystemRoles.EvaluationCommittee,SystemRoles.SourcingManager,SystemRoles.TopCommittee];
  var usersByRole=new List<string>();var clients=new List<HttpClient>();var tokens=new List<string>();var hubUrls=new List<string>();
  using(var scope=app.Services.CreateScope()){
   var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(!await db.Users.AnyAsync(),"isolated SQL Server database initially empty");
   var department=new Department{Name="Notification test department",IsActive=true};db.Add(department);await db.SaveChangesAsync();
   var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
   for(int i=0;i<roles.Length;i++){
    int? supplierId=null;
    if(roles[i]==SystemRoles.Supplier){var s=new Supplier{CompanyName="Notification test company",Email="company@notification.invalid",RegistrationNumber=Guid.NewGuid().ToString(),Status=SupplierStatus.Approved};db.Add(s);await db.SaveChangesAsync();supplierId=s.Id;}
    var email="role"+i+"@notification.invalid";
    var u=new ApplicationUser{Email=email,UserName=email,EmailConfirmed=true,IsActive=true,FullName="Notification test "+roles[i],SupplierId=supplierId,DepartmentId=i==1?department.Id:null};
    if(!(await users.CreateAsync(u,password)).Succeeded||!(await users.AddToRoleAsync(u,roles[i])).Succeeded)throw new Exception("Identity setup failed");usersByRole.Add(u.Id);
   }
  }
  async Task<string> Token(HttpClient c,string url){var html=await(await c.GetAsync(url)).Content.ReadAsStringAsync();var m=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(!m.Success)throw new Exception("No CSRF token: "+url);return WebUtility.HtmlDecode(m.Groups[1].Value);}
  int tenderId=0,prId=0;
  using(var scope=app.Services.CreateScope()){
   var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var department=await db.Departments.SingleAsync();var other=new Department{Name="Other search department",IsActive=true};db.Add(other);await db.SaveChangesAsync();
   var own=new PurchaseRequest{Title="Search Alpha تجهيزات",DepartmentId=department.Id,CreatedByUserId=usersByRole[1],Status=PurchaseRequestStatus.Accepted,Justification="Isolated search fixture"};var foreign=new PurchaseRequest{Title="Search Secret",DepartmentId=other.Id,CreatedByUserId=usersByRole[2],Status=PurchaseRequestStatus.Accepted,Justification="Isolated search fixture"};db.AddRange(own,foreign);await db.SaveChangesAsync();prId=own.Id;
   var one=new Tender{Title="Search Alpha تجهيزات",PurchaseRequestId=own.Id,CreatedByUserId=usersByRole[2],Status=TenderStatus.Bidding};var two=new Tender{Title="Search Secret",PurchaseRequestId=foreign.Id,CreatedByUserId=usersByRole[2],Status=TenderStatus.Bidding};db.AddRange(one,two);await db.SaveChangesAsync();tenderId=one.Id;
   db.AddRange(new CommitteeMember{TenderId=one.Id,UserId=usersByRole[4],CommitteeType=CommitteeType.Evaluation,Responsibility=CommitteeResponsibility.TechnicalRepresentative},new CommitteeMember{TenderId=one.Id,UserId=usersByRole[5],CommitteeType=CommitteeType.Evaluation,Responsibility=CommitteeResponsibility.SourcingManager},new CommitteeMember{TenderId=one.Id,UserId=usersByRole[6],CommitteeType=CommitteeType.Top});
   var r1=new TenderRound{TenderId=one.Id,RoundNumber=1,Status=TenderRoundStatus.Open,Deadline=DateTime.UtcNow.AddDays(1),CreatedByUserId=usersByRole[2]};var r2=new TenderRound{TenderId=two.Id,RoundNumber=1,Status=TenderRoundStatus.Open,Deadline=DateTime.UtcNow.AddDays(1),CreatedByUserId=usersByRole[2]};db.AddRange(r1,r2);await db.SaveChangesAsync();
   var supplier=await db.Suppliers.SingleAsync();var otherSupplier=new Supplier{CompanyName="Other private company",RegistrationNumber=Guid.NewGuid().ToString(),Email="other@search.invalid",Status=SupplierStatus.Approved};db.Add(otherSupplier);await db.SaveChangesAsync();
   var i1=new TenderInvitation{TenderRoundId=r1.Id,SupplierId=supplier.Id,InvitedByUserId=usersByRole[2]};var i2=new TenderInvitation{TenderRoundId=r2.Id,SupplierId=otherSupplier.Id,InvitedByUserId=usersByRole[2]};db.AddRange(i1,i2);await db.SaveChangesAsync();db.AddRange(new Bid{TenderInvitationId=i1.Id,Status=BidStatus.Submitted},new Bid{TenderInvitationId=i2.Id,Status=BidStatus.Submitted});await db.SaveChangesAsync();
  }
  using(var anon=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false}))Check(!(await anon.GetAsync("/Search/Query?q=Search")).IsSuccessStatusCode,"anonymous search refused");
  async Task<JsonElement> Query(int i,string q){var response=await clients[i].GetAsync("/Search/Query?q="+Uri.EscapeDataString(q));response.EnsureSuccessStatusCode();Check(response.Headers.CacheControl?.NoStore==true,"search response prevents private caching");using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.GetProperty("items").Clone();}
  for(int i=0;i<7;i++){
   var c=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});clients.Add(c);var token=await Token(c,"/Identity/Account/Login");
   var login=await c.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email","role"+i+"@notification.invalid"},{"Input.Password",password},{"__RequestVerificationToken",token}}));Check(login.StatusCode==HttpStatusCode.Redirect,roles[i]+": actual Identity login");
   var page=await c.GetAsync("/"+areas[i]);var html=await page.Content.ReadAsStringAsync();// Top Committee's top bar searches only its assigned committee rounds (data-tc-search, own microphone); every other role,
   // Admin included, uses the shared global search. All roles have the shared language selector.
   var searchControls=html.Contains("global-search.js")&&html.Contains("data-voice-search"); // Top Committee moved onto the shared search bar (b4a2ac1); results stay scoped server-side
   Check(page.IsSuccessStatusCode&&searchControls&&html.Contains("data-voice-language")&&html.Contains("ar-JO"),roles[i]+": search, microphone and language selector present");
   var results=await Query(i,"Search");var titles=results.EnumerateArray().Select(x=>x.GetProperty("title").GetString()!).ToList();
   if(i==0)Check(!results.EnumerateArray().Any(x=>x.GetProperty("type").GetString()=="Tender"),"Admin does not receive inaccessible confidential tender links");
   else if(i==2)Check(titles.Contains("Search Alpha تجهيزات")&&titles.Contains("Search Secret"),"single Officer searches both tenders");
   else Check(titles.Contains("Search Alpha تجهيزات")&&!titles.Contains("Search Secret"),roles[i]+": assignment/company/department search isolation");
   if(i==1)Check(results.EnumerateArray().All(x=>x.GetProperty("url").GetString()!.StartsWith("/Department/PurchaseRequests/Details/")),"Department results open authorized request pages");
   foreach(var hit in results.EnumerateArray()){
    var url=hit.GetProperty("url").GetString()!;Check(url.StartsWith('/')&&!url.StartsWith("//"),"result URL is local");
    for(int redirects=0;redirects<5;redirects++){var target=await c.GetAsync(url);if(target.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently){url=target.Headers.Location!.ToString();continue;}Check(target.IsSuccessStatusCode,roles[i]+": search result opens permitted page");break;}
   }
  }
  Check((await Query(5,"T-"+tenderId.ToString("D4"))).GetArrayLength()==1,"Manager finds padded tender identifier");
  Check((await Query(1,"PR-"+prId.ToString("D4"))).GetArrayLength()==1,"Department finds padded request identifier");
  Check((await Query(3,"تجهيزات")).GetArrayLength()==2,"Arabic query finds own invitation and offer");
  Check((await Query(6,"x")).GetArrayLength()==0,"short search returns no results");
  Check(!(await clients[0].GetAsync("/Search/Query?q="+new string('x',101))).IsSuccessStatusCode,"oversized search refused");
  foreach(var c in clients)c.Dispose();
  Console.WriteLine($"RESULT PASS={count} FAIL=0 SQL_DATABASE={catalog}. Physical microphone/speech recognition NOT TESTED (browser permission and real audio required).");
 }
}
