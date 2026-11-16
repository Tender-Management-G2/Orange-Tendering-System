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

public static class NotificationChecks
{
 public static async Task Run()
 {
  var connection=Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")!;
  var catalog=new SqlConnectionStringBuilder(connection).InitialCatalog;
  if(!catalog.StartsWith("OrangeAuthCheck_Notifications_"))throw new Exception("Isolated notification database required");
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
  using(var anonymous=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false})){
   Check(!(await anonymous.GetAsync("/Notifications/Feed")).IsSuccessStatusCode,"anonymous notification feed refused");
   Check(!(await anonymous.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1",new StringContent(""))).IsSuccessStatusCode,"anonymous SignalR negotiation refused");
  }
  for(int i=0;i<roles.Length;i++){
   var c=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});c.Timeout=TimeSpan.FromSeconds(25);clients.Add(c);
   var token=await Token(c,"/Identity/Account/Login");
   var response=await c.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email","role"+i+"@notification.invalid"},{"Input.Password",password},{"__RequestVerificationToken",token}}));
   Check(response.StatusCode==HttpStatusCode.Redirect,roles[i]+": real Identity credential login");

   var page=await c.GetAsync("/"+areas[i]);var html=await page.Content.ReadAsStringAsync();// Top Committee keeps its own bell: it opens notifications only through TopCommittee/Notifications/Open, which checks the
   // destination is a round assigned to the member (AuthorizedDestination). Every other role uses the shared live bell.
   var bellRendered=roles[i]==SystemRoles.TopCommittee?html.Contains("data-tc-bell")&&html.Contains("data-preview-url")&&html.Contains("data-open-url"):html.Contains("data-notification-bell")&&html.Contains("notification-bell.js");
   Check(page.IsSuccessStatusCode&&bellRendered,roles[i]+": dashboard bell and script rendered");
   tokens.Add(await Token(c,"/"+areas[i]));
   var negotiated=await c.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1",new StringContent(""));negotiated.EnsureSuccessStatusCode();using var data=JsonDocument.Parse(await negotiated.Content.ReadAsStringAsync());
   var url="/hubs/notifications?id="+Uri.EscapeDataString(data.RootElement.GetProperty("connectionToken").GetString()!);hubUrls.Add(url);
   (await c.GetAsync(url)).EnsureSuccessStatusCode();
   (await c.PostAsync(url,new StringContent("{\"protocol\":\"json\",\"version\":1}\u001e",Encoding.UTF8,"application/octet-stream"))).EnsureSuccessStatusCode();
   var handshake=await c.GetStringAsync(url);Check(handshake.Contains("{}"),roles[i]+": actual authenticated SignalR handshake");
  }
  async Task<JsonElement> Next(HttpClient c,string url){for(int attempt=0;attempt<4;attempt++){var body=await c.GetStringAsync(url);foreach(var part in body.Split('\u001e',StringSplitOptions.RemoveEmptyEntries)){using var doc=JsonDocument.Parse(part);var r=doc.RootElement;if(r.TryGetProperty("target",out var target)&&target.GetString()=="notification")return r.GetProperty("arguments")[0].Clone();}}throw new Exception("Notification invocation not received");}
  var polls=clients.Select((c,i)=>Next(c,hubUrls[i])).ToArray();
  using(var scope=app.Services.CreateScope()){
   var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await using var tx=await db.Database.BeginTransactionAsync();
   for(int i=0;i<7;i++){db.Add(new Notification{UserId=usersByRole[i],Title="Private role "+i,Message="SQL notification test",Link=i==0?"javascript:alert(1)":"/Notifications",CreatedAt=DateTime.UtcNow});await db.SaveChangesAsync();}
   await Task.Delay(150);Check(polls.All(p=>!p.IsCompleted),"no notification published before explicit SQL transaction commits");await tx.CommitAsync();
  }
  for(int i=0;i<7;i++){
   var n=await polls[i];Check(n.GetProperty("title").GetString()=="Private role "+i,roles[i]+": committed SignalR push isolated to its owner");
   var feed=await clients[i].GetAsync("/Notifications/Feed");using var data=JsonDocument.Parse(await feed.Content.ReadAsStringAsync());var rows=data.RootElement.GetProperty("items");
   Check(data.RootElement.GetProperty("unread").GetInt32()==1&&rows.GetArrayLength()==1&&rows[0].GetProperty("title").GetString()=="Private role "+i,roles[i]+": SQL feed contains only own notification");
   if(i==0)Check(rows[0].GetProperty("link").ValueKind==JsonValueKind.Null,"unsafe notification link removed from feed and hub payload");
  }
  int adminNotification,otherNotification;
  using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();adminNotification=await db.Notifications.Where(n=>n.UserId==usersByRole[0]).Select(n=>n.Id).SingleAsync();otherNotification=await db.Notifications.Where(n=>n.UserId==usersByRole[1]).Select(n=>n.Id).SingleAsync();}
  async Task<HttpResponseMessage> Post(int index,string path){var req=new HttpRequestMessage(HttpMethod.Post,path);req.Headers.Add("RequestVerificationToken",tokens[index]);return await clients[index].SendAsync(req);}
  Check(!(await Post(0,"/Notifications/Read?id="+otherNotification)).IsSuccessStatusCode,"cannot mark another account's notification read");
  Check(!(await clients[0].PostAsync("/Notifications/Read?id="+adminNotification,new StringContent(""))).IsSuccessStatusCode,"notification mutations require CSRF token");
  Check((await Post(0,"/Notifications/Read?id="+adminNotification)).IsSuccessStatusCode,"owner marks its notification read");
  for(int i=1;i<7;i++)Check((await Post(i,"/Notifications/ReadAll")).IsSuccessStatusCode,roles[i]+": mark all read works");
  var pending=Next(clients[6],hubUrls[6]);
  using(var scope=app.Services.CreateScope()){
   var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
   await using(var tx=await db.Database.BeginTransactionAsync()){db.Add(new Notification{UserId=usersByRole[6],Title="Rolled back",Message="Must never be pushed",CreatedAt=DateTime.UtcNow});await db.SaveChangesAsync();await tx.RollbackAsync();}
   db.ChangeTracker.Clear();await Task.Delay(150);Check(!pending.IsCompleted,"rolled-back transaction never sends a notification");
   await using(var tx=await db.Database.BeginTransactionAsync()){db.Add(new Notification{UserId=usersByRole[6],Title="Disposed transaction",Message="Must never be pushed",CreatedAt=DateTime.UtcNow});await db.SaveChangesAsync();}
   db.ChangeTracker.Clear();
   db.Add(new Notification{UserId=usersByRole[6],Title="Committed after disposal",Message="Real committed event",CreatedAt=DateTime.UtcNow});await db.SaveChangesAsync();
  }
  Check((await pending).GetProperty("title").GetString()=="Committed after disposal","implicit save publishes only committed rows after transaction disposal");
  using(var scope=app.Services.CreateScope()){
   var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(!await db.Notifications.AnyAsync(n=>n.Title=="Rolled back"||n.Title=="Disposed transaction"),"SQL contains no rolled-back notifications");
   Check(await db.Notifications.CountAsync(n=>n.IsRead)==7,"all seven roles' read states persisted in SQL");
  }
  foreach(var c in clients)c.Dispose();
  Console.WriteLine($"RESULT PASS={count} FAIL=0 SQL_DATABASE={catalog}. Actual HTTP Identity/SignalR and SQL Server; no production data touched.");
 }
}
