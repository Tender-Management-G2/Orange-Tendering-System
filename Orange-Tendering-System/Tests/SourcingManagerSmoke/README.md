# Sourcing Manager smoke checks

This console harness creates synthetic users, a tender, three submitted full-evaluation reports, and four Top Committee members in an isolated SQL Server database. It then verifies review, approve, reject, return-for-review, executive-summary creation, and forwarding.

Run it from the web project directory against a database name beginning with `OrangeManagerCheck_`:

```powershell
$env:SOURCING_MANAGER_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=OrangeManagerCheck_Local;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true'
dotnet build Tests/SourcingManagerSmoke/SourcingManagerSmoke.csproj -c Release -p:UseAppHost=false
dotnet Tests/SourcingManagerSmoke/bin/Release/net10.0/SourcingManagerSmoke.dll
```

The test database is deliberately left in place for inspection. The harness creates its own schema from the current model, so it can still test the workflow while a new application migration is pending. Do not point this harness at a production or shared development database.
