using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

// Admin account creation end to end (--admin-account-checks): real SQL Server (isolated OrangeAuthCheck_AdminAccounts_*),
// real Identity sign-in, the real /Admin/Users/Create form with antiforgery, and a captured e-mail sender.
public static class AdminAccountChecks
{
 sealed class Mail : IEmailSender
 {
  public List<(string To,string Subject,string Body)> Sent=[];public string? FailWith;
  public Task SendEmailAsync(string email,string subject,string html)
  {
   var f=FailWith;FailWith=null;
   if(f=="smtp")throw new System.Net.Mail.SmtpException("simulated");
   if(f=="format")throw new FormatException("simulated invalid mailbox");
   Sent.Add((email,subject,html));return Task.CompletedTask;
  }
 }
 public static async Task Run()
 {
  var connection=Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")!;
  if(!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_AdminAccounts_"))throw new Exception("Isolated admin-account database required");
  var root=Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;var mail=new Mail();
  Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection",connection);Environment.SetEnvironmentVariable("DemoData__Enabled","false");
  using var app=new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b=>{b.UseContentRoot(root).UseEnvironment("Development");
   b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Default Connection"]=connection,["DemoData:Enabled"]="false",["TeamAccounts:Enabled"]="false",["TeamDev:Database"]="",["BootstrapAdmin:Email"]="",["BootstrapAdmin:Password"]="",["Logging:LogLevel:Default"]="Warning"}));
   b.ConfigureTestServices(s=>{s.RemoveAll<IEmailSender>();s.AddSingleton<IEmailSender>(mail);});});
  int count=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);count++;Console.WriteLine("PASS "+name);}
  string Pw()=>"A!7b"+Convert.ToHexString(RandomNumberGenerator.GetBytes(14));
  var adminPw=Pw();var deptId=0;
  using(var scope=app.Services.CreateScope()){
   var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
   Check(!await db.Users.AnyAsync(),"isolated SQL Server database initially empty");
   var d=new Department{Name="Information Technology",IsActive=true};db.Add(d);db.Add(new Department{Name="Closed department",IsActive=false});await db.SaveChangesAsync();deptId=d.Id;
   async Task Make(string email,string role,bool active=true){var u=new ApplicationUser{Email=email,UserName=email,EmailConfirmed=true,IsActive=active,FullName=email};
    if(!(await users.CreateAsync(u,adminPw)).Succeeded||!(await users.AddToRoleAsync(u,role)).Succeeded)throw new Exception("setup");}
   await Make("admin@acct.test",SystemRoles.Admin);await Make("officer@acct.test",SystemRoles.SourcingOfficer);await Make("manager@acct.test",SystemRoles.SourcingManager);
  }
  var inactiveDept=0;using(var scope=app.Services.CreateScope())inactiveDept=await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Departments.Where(x=>!x.IsActive).Select(x=>x.Id).SingleAsync();
  HttpClient NewClient()=>app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});
  async Task<string> Token(HttpClient c,string url){var h=await(await c.GetAsync(url)).Content.ReadAsStringAsync();var m=Regex.Match(h,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(!m.Success)throw new Exception("No token "+url);return WebUtility.HtmlDecode(m.Groups[1].Value);}
  async Task<HttpClient> Login(string email,string pw){var c=NewClient();var t=await Token(c,"/Identity/Account/Login");var r=await c.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email",email},{"Input.Password",pw},{"__RequestVerificationToken",t}}));Check(r.StatusCode==HttpStatusCode.Redirect,email+": signs in");return c;}
  var admin=await Login("admin@acct.test",adminPw);
  var page=await(await admin.GetAsync("/Admin/Users/Create")).Content.ReadAsStringAsync();
  Check(page.Contains("data-user-create")&&page.Contains("name=\"Role\" value=\""+SystemRoles.RequestingDepartment+"\"")&&page.Contains("data-password-generate")&&page.Contains("admin-user-create.js"),"Create User page renders role cards, password tools and its script");
  Check(!page.Contains("value=\""+SystemRoles.Supplier+"\""),"Supplier is not offered (suppliers register themselves)");
  Check(!page.Contains("data-admin-search"),"form page has no stray page-search bar");
  async Task<(HttpResponseMessage R,string Html)> Create(Dictionary<string,string> f,HttpClient? c=null,bool token=true){c??=admin;if(token)f["__RequestVerificationToken"]=await Token(c,"/Admin/Users/Create");var r=await c.PostAsync("/Admin/Users/Create",new FormUrlEncodedContent(f));return(r,await r.Content.ReadAsStringAsync());}
  Dictionary<string,string> F(string name,string email,string role,string pw,int? dept=null,bool active=true){var f=new Dictionary<string,string>{{"FullName",name},{"Email",email},{"Role",role},{"Password",pw},{"IsActive",active?"true":"false"}};if(dept!=null)f["DepartmentId"]=dept.ToString()!;return f;}
  async Task<ApplicationUser?> Find(string email){using var s=app.Services.CreateScope();return await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Email==email);}
  async Task<IList<string>> Roles(string email){using var s=app.Services.CreateScope();var um=s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();return await um.GetRolesAsync((await um.FindByEmailAsync(email))!);}

  // success paths for every role family
  var reqPw=Pw();var r1=await Create(F("  Rana Requester  ","rana@acct.test",SystemRoles.RequestingDepartment,reqPw,deptId));
  Check(r1.R.StatusCode==HttpStatusCode.Redirect&&r1.R.Headers.Location!.ToString().Contains("/Admin/Users"),"Requesting Department account created and redirected to Users");
  var rana=await Find("rana@acct.test");Check(rana!=null&&rana.FullName=="Rana Requester"&&rana.DepartmentId==deptId&&rana.MustChangePassword&&rana.IsActive&&rana.EmailConfirmed,"stored with trimmed name, department, active and password change required");
  Check((await Roles("rana@acct.test")).SequenceEqual([SystemRoles.RequestingDepartment]),"exactly the selected role assigned");
  Check(mail.Sent.Any(m=>m.To=="rana@acct.test"&&m.Subject.Contains("Welcome")&&m.Body.Contains("ActivateEmployee")),"welcome e-mail with setup link sent to the new user");
  using(var s=app.Services.CreateScope()){var log=await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditLogs.AsNoTracking().Where(a=>a.EntityId==rana!.Id&&a.Action=="Created").SingleAsync();Check(log.NewValues!.Contains("Requesting Department")&&!log.NewValues.Contains(reqPw),"audit entry recorded without the password");}
  var r2=await Create(F("Tariq Technical","tech@acct.test",SystemRoles.Technical,Pw()));Check(r2.R.StatusCode==HttpStatusCode.Redirect&&(await Roles("tech@acct.test")).OrderBy(x=>x).SequenceEqual(new[]{SystemRoles.EvaluationCommittee,SystemRoles.Technical}.OrderBy(x=>x)),"Technical account also joins the Evaluation Committee role");
  var r3=await Create(F("Chief Exec","ceo@acct.test",SystemRoles.ChiefExecutiveOfficer,Pw()));Check(r3.R.StatusCode==HttpStatusCode.Redirect&&(await Roles("ceo@acct.test")).Contains(SystemRoles.TopCommittee),"CEO account also joins the Top Committee role");
  var r4=await Create(F("Second Admin","admin2@acct.test",SystemRoles.Admin,Pw(),null,false));Check(r4.R.StatusCode==HttpStatusCode.Redirect&&(await Find("admin2@acct.test"))!.IsActive==false,"inactive Admin account created as inactive");
  var seatPage=await(await admin.GetAsync("/Admin/Users/Create?committeeSeat=top-cfo")).Content.ReadAsStringAsync();Check(seatPage.Contains("auc-seat")&&seatPage.Contains(SystemRoles.ChiefFinancialOfficer),"committee seat link pre-selects the seat role");
  var sf=F("Chief Fin","cfo@acct.test",SystemRoles.ChiefFinancialOfficer,Pw());sf["CommitteeSeat"]="top-cfo";var r5=await Create(sf);Check(r5.R.StatusCode==HttpStatusCode.Redirect,"committee seat account created");
  var sf2=F("Wrong Seat","seat2@acct.test",SystemRoles.Admin,Pw());sf2["CommitteeSeat"]="top-cfo";var r6=await Create(sf2);Check(r6.Html.Contains("committee category does not match")&&await Find("seat2@acct.test")==null,"seat/role mismatch rejected");

  // validation and business rules (nothing is created)
  var e1=await Create(F("No Dept","nodept@acct.test",SystemRoles.RequestingDepartment,Pw()));Check(e1.R.StatusCode==HttpStatusCode.OK&&e1.Html.Contains("Department is required for this role")&&await Find("nodept@acct.test")==null,"department required for Requesting Department");
  var e2=await Create(F("Closed Dept","closed@acct.test",SystemRoles.RequestingDepartment,Pw(),inactiveDept));Check(e2.Html.Contains("Please select an active department")&&await Find("closed@acct.test")==null,"inactive department rejected");
  var e3=await Create(F("Dup","RANA@ACCT.TEST",SystemRoles.SourcingManager,Pw()));Check(e3.Html.Contains("already registered"),"duplicate e-mail rejected regardless of case");
  var e4=await Create(F("Officer Two","officer2@acct.test",SystemRoles.SourcingOfficer,Pw()));Check(e4.Html.Contains("Only one active Sourcing Officer")&&await Find("officer2@acct.test")==null,"second active Sourcing Officer rejected");
  var e5=await Create(F("Officer Spare","officer3@acct.test",SystemRoles.SourcingOfficer,Pw(),null,false));Check(e5.R.StatusCode==HttpStatusCode.Redirect,"inactive Sourcing Officer may be created");
  var e6=await Create(F("Sup","sup@acct.test",SystemRoles.Supplier,Pw()));Check(e6.Html.Contains("role is invalid")&&await Find("sup@acct.test")==null,"Supplier role refused");
  var e7=await Create(F("Ghost","ghost@acct.test","Super Admin",Pw()));Check(e7.Html.Contains("role is invalid"),"unknown role refused");
  var e8=await Create(F("Weak","weak@acct.test",SystemRoles.SourcingManager,"short"));Check(e8.R.StatusCode==HttpStatusCode.OK&&await Find("weak@acct.test")==null,"too-short password refused");
  var e9=await Create(F("Weak2","weak2@acct.test",SystemRoles.SourcingManager,"alllowercase1"));Check(await Find("weak2@acct.test")==null&&e9.Html.Contains("auc-alert"),"password policy errors shown on the page");
  var e10=await Create(F("","bad-email",SystemRoles.SourcingManager,Pw()));Check(e10.R.StatusCode==HttpStatusCode.OK&&await Find("bad-email")==null,"missing name and invalid e-mail refused");
  var e11=await Create(new Dictionary<string,string>{{"FullName","No Role"},{"Email","norole@acct.test"},{"Password",Pw()},{"IsActive","true"}});Check(e11.Html.Contains("Please select a role")&&await Find("norole@acct.test")==null,"role is required");
  var keep=e3.Html;Check(keep.Contains("value=\"Dup\"")&&keep.Contains("value=\""+SystemRoles.SourcingManager+"\" checked"),"entered values and chosen role kept after a server error");

  // e-mail failures roll back completely
  mail.FailWith="smtp";var m1=await Create(F("Mail Fail","mailfail@acct.test",SystemRoles.SourcingManager,Pw()));Check(m1.Html.Contains("welcome email could not be delivered")&&await Find("mailfail@acct.test")==null,"SMTP failure: clear message, no account left behind");
  mail.FailWith="format";var m2=await Create(F("Odd Fail","oddfail@acct.test",SystemRoles.SourcingManager,Pw()));Check(m2.R.StatusCode==HttpStatusCode.OK&&m2.Html.Contains("could not be delivered")&&await Find("oddfail@acct.test")==null,"unexpected delivery error: no error page, no account left behind");
  var after=await Create(F("Mail Fail","mailfail@acct.test",SystemRoles.SourcingManager,Pw()));Check(after.R.StatusCode==HttpStatusCode.Redirect,"same e-mail can be created after a failed attempt");

  // security
  var noToken=await Create(F("No Csrf","nocsrf@acct.test",SystemRoles.SourcingManager,Pw()),null,false);Check(!noToken.R.IsSuccessStatusCode&&noToken.R.StatusCode!=HttpStatusCode.Redirect&&await Find("nocsrf@acct.test")==null,"POST without antiforgery token refused");
  var manager=await Login("manager@acct.test",adminPw);var mg=await manager.GetAsync("/Admin/Users/Create");Check(mg.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect&&!(await mg.Content.ReadAsStringAsync()).Contains("data-user-create"),"non-Admin cannot open Create User");
  using(var anon=NewClient()){var an=await anon.GetAsync("/Admin/Users/Create");Check(an.StatusCode==HttpStatusCode.Redirect&&an.Headers.Location!.ToString().Contains("Login"),"anonymous user redirected to sign in");}

  // the new account works: temporary password -> forced change
  var newbie=NewClient();var lt=await Token(newbie,"/Identity/Account/Login");
  var first=await newbie.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email","rana@acct.test"},{"Input.Password",reqPw},{"__RequestVerificationToken",lt}}));
  Check(first.Headers.Location?.ToString().Contains("FirstLoginPasswordChange")==true,"new account signs in with the temporary password and must change it");
  var list=await(await admin.GetAsync("/Admin/Users")).Content.ReadAsStringAsync();Check(list.Contains("rana@acct.test")&&list.Contains("data-admin-search"),"Users list shows the new account and keeps its search bar");
  var dash=await(await admin.GetAsync("/Admin")).Content.ReadAsStringAsync();Check(dash.Contains("<strong>admin@acct.test</strong>")||dash.Contains("admin@acct.test"),"dashboard greets the signed-in admin by name");
  Console.WriteLine($"Admin account checks passed: {count}");
 }
}
