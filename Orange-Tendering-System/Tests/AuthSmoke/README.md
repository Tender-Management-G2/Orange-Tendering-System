# Authentication smoke checks

This console harness exercises the Identity pages through ASP.NET Core's test host. It uses an in-memory `IEmailSender`; it validates generated OTPs, activation links, and reset links without contacting an SMTP server. SMTP provider delivery must be checked separately with each deployment's credentials.

To check that explicit SMTP mode cannot route to the unauthenticated local inbox, run `dotnet Tests/AuthSmoke/bin/Release/net10.0/AuthSmoke.dll --smtp-mode-checks` after building. This check does not send email; real Gmail delivery must be confirmed in the recipient's mailbox and through the verification page.

Use a fresh disposable, locally owned SQL Server database per run whose name begins with `OrangeAuthCheck_`. Verify the connection and apply migrations to that database first. The harness refuses other database names, creates synthetic accounts and documents, and leaves the data in place for inspection. Registration files live in a unique `OrangeAuthCheck_Files_*` temporary content root, never the real application's private upload folder. The harness also verifies all seven dashboards with real Identity cookies and the single-active-Officer guard; reusing a populated test database can violate that fixture's initial conditions.

```powershell
$env:AUTH_TEST_CONNECTION = 'Server=localhost;Database=OrangeAuthCheck_Local;Trusted_Connection=True;TrustServerCertificate=True'
$env:AUTH_PROJECT_ROOT = $PWD.Path
dotnet build Tests/AuthSmoke/AuthSmoke.csproj -c Release -p:UseAppHost=false
dotnet ef database update --configuration Release --no-build --connection $env:AUTH_TEST_CONNECTION
dotnet Tests/AuthSmoke/bin/Release/net10.0/AuthSmoke.dll
```

Run the commands from the web project directory. No production database should be used.
