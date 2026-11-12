# Team setup: local SQL Server and EF Core

Run these commands in PowerShell. Each developer uses their own local database. The checked-in EF Core migrations create the schema; no SQL backup or SSMS script is needed.

## Prerequisites

- Git and the .NET **10 SDK** (`dotnet --version`). The project targets `net10.0` and uses EF Core **10.0.14**; no `global.json` pins an SDK patch.
- A running SQL Server **2019 or newer** instance: Developer, Express, or LocalDB. [EF Core SQL Server provider compatibility](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/). Verification here used SQL Server 2025 Developer Edition.
- `dotnet-ef` 10.0.14: check `dotnet ef --version`. If missing, run `dotnet tool install --global dotnet-ef --version 10.0.14`. The EF Core package references do not install this CLI tool.

## First-time setup

1. Clone and enter the **project directory**:

   ```powershell
   git clone https://github.com/Tender-Management-G2/Orange-Tendering-System.git
   cd Orange-Tendering-System/Orange-Tendering-System
   ```

2. Configure your local SQL Server connection with **user secrets**, which stay outside Git. The committed `appsettings.json` uses Windows Authentication on `localhost` as an example:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:Default Connection" "Server=localhost;Database=OrangeTenderingSystem;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
   ```

   Replace `localhost` with your instance name. Use `Database=OrangeTenderingSystem` unless that local database already contains work. The key has a space: `Default Connection`. For `dotnet ef` to read user secrets, set Development in this shell:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Development'
   ```

   Instance examples: default `Server=localhost`, Express `Server=.\SQLEXPRESS`, LocalDB `Server=(localdb)\MSSQLLocalDB`. Use `Trusted_Connection=True` for Windows Authentication. If you need SQL Authentication, put `User ID=...;Password=...` only in user secrets, never in tracked `appsettings*.json`. `TrustServerCertificate=True` is for local development certificates.

3. Restore, build, and apply migrations:

   ```powershell
   dotnet restore
   dotnet build --no-restore
   dotnet ef database update
   ```

   **This exact EF command works from the project directory.** The DbContext, migrations, and startup app are in the same project. No `--project`, `--startup-project`, or `--context` options are needed.

   EF creates the database if your login can create databases (for example, `dbcreator`). If it already exists, your login needs schema permissions there (typically `db_owner` for local development). Check the database name before applying migrations.

4. Verify in SSMS or Azure Data Studio against **your local database**:

   ```sql
   SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
   SELECT name FROM sys.tables ORDER BY name;
   ```

   Expect three migration rows and 30 tables, including the seven `AspNet*` tables. Re-running `dotnet ef database update` should report that the database is already up to date.

5. Launch the MVC app:

   ```powershell
   dotnet run --launch-profile http
   ```

   Open `http://localhost:5254`. Public pages should load. The seven Identity roles and an initial admin are **not seeded** by migrations or startup, so role-protected workspaces will show Access denied until the authentication owner implements provisioning. This does not prevent database creation or application launch.

## After pulling changes

From the project directory, with your Development connection string still configured:

```powershell
git pull
dotnet restore
dotnet ef database update
dotnet run --launch-profile http
```

`database update` applies only pending migrations recorded in `__EFMigrationsHistory`. Do not drop or reset a database to catch up.

## Migration workflow for seven developers

1. Create a migration only when the entity model changes: `dotnet ef migrations add MeaningfulName`. Commit the model change, migration `.cs` and `.Designer.cs`, and model snapshot together.
2. Coordinate simultaneous schema changes before merging. Review `Up` and `Down` for data loss, keys, and indexes. If branches conflict in the snapshot, agree on migration order and regenerate only your **unmerged, unapplied** migration against current `main`.
3. Never edit an already shared/applied migration or another developer's migration history. Add a corrective migration instead, then run `dotnet ef database update` on your own database after pulling.

## Common setup errors

| Symptom | Check |
| --- | --- |
| `dotnet ef` not found | Install the separate `dotnet-ef` 10.0.14 tool and reopen the shell. |
| Cannot connect or login fails | Start the correct SQL Server instance and verify its name, authentication mode, and permissions. |
| Certificate error | For a local self-signed certificate, include `TrustServerCertificate=True`. |
| Tables already exist but migration fails | Inspect the target database and `__EFMigrationsHistory`; do not delete tables to force migration. |
| Workspace says Access denied | Roles and user assignments are not seeded yet; see [team handoff](TEAM_HANDOFF.md). |
