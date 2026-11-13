# Authentication smoke checks

This console harness exercises the Identity pages through ASP.NET Core's test host. It uses an in-memory `IEmailSender`; it validates generated OTPs, activation links, and reset links without contacting an SMTP server. SMTP provider delivery must be checked separately with each deployment's credentials.

Use a disposable, locally owned SQL Server database whose name begins with `OrangeAuthCheck_`. Verify the connection and apply migrations to that database first. The harness refuses other database names, creates synthetic accounts and documents, and leaves the data in place for inspection.

```powershell
$env:AUTH_TEST_CONNECTION = 'Server=localhost;Database=OrangeAuthCheck_Local;Trusted_Connection=True;TrustServerCertificate=True'
$env:AUTH_PROJECT_ROOT = $PWD.Path
dotnet build Tests/AuthSmoke/AuthSmoke.csproj -c Release -p:UseAppHost=false
dotnet ef database update --configuration Release --no-build --connection $env:AUTH_TEST_CONNECTION
dotnet Tests/AuthSmoke/bin/Release/net10.0/AuthSmoke.dll
```

Run the commands from the web project directory. No production database should be used.
