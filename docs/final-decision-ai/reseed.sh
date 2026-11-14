cd "C:/Users/user/Desktop/Orange-Tendering-System/Orange-Tendering-System"
export UI_FIXTURE_CONNECTION="Server=localhost;Database=OrangeAuthCheck_UiE2E_20261113;Trusted_Connection=True;TrustServerCertificate=True"
dotnet build -c Debug 2>&1 | grep -E " error " | head -3
dotnet build Tests/AuthSmoke/AuthSmoke.csproj -c Debug -p:UseAppHost=false 2>&1 | grep -E " error " | head -3
sqlcmd -S localhost -E -C -Q "IF DB_ID(N'OrangeAuthCheck_UiE2E_20261113') IS NOT NULL BEGIN ALTER DATABASE [OrangeAuthCheck_UiE2E_20261113] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [OrangeAuthCheck_UiE2E_20261113]; END" >/dev/null 2>&1
dotnet ef database update --no-build --connection "$UI_FIXTURE_CONNECTION" 2>&1 | tail -1
export UI_FIXTURE_STATE="C:/Users/user/AppData/Local/Temp/claude/d55eb3acf3955c2b/4eaa1fea-2fec-464d-89bf-7d0b975904ee/scratchpad/ui-state.json" AUTH_PROJECT_ROOT="$PWD"
dotnet Tests/AuthSmoke/bin/Debug/net10.0/AuthSmoke.dll --seed-ui-fixture 2>&1 | grep -E "fixture ready|Exception|FAILED" | head -3
