param([switch]$Browser, [switch]$CsrfOnly, [switch]$BrowserOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$expected = 'C:\Users\user\Desktop\Orange-Tendering-System'
if ((Resolve-Path -LiteralPath $repoRoot).Path -ne $expected) { throw 'Run only in the authoritative Orange repository.' }
if ((git -C $repoRoot rev-parse --show-toplevel).Replace('/','\') -ne $expected) { throw 'Wrong repository.' }
if ((git -C $repoRoot branch --show-current) -ne 'tariq22222') { throw 'Wrong branch: expected tariq22222.' }
git -C $repoRoot status --short
$oldRoot = $env:AUTH_PROJECT_ROOT
try {
    Set-Location -LiteralPath $repoRoot
    $env:AUTH_PROJECT_ROOT = Join-Path $repoRoot 'Orange-Tendering-System'
    $assembly = Join-Path $env:AUTH_PROJECT_ROOT 'Tests\AuthSmoke\bin\AgentVerify\net10.0\AuthSmoke.dll'
    if ($BrowserOnly) {
        $sessionPath = Join-Path $env:AUTH_PROJECT_ROOT 'App_Data\ExecutiveAgentTest\session.json'
        $checkLog = Join-Path $repoRoot '.build\executive-agent\sql-checks.log'
        if (!(Test-Path -LiteralPath $sessionPath) -or !(Test-Path -LiteralPath $checkLog) -or !(Test-Path -LiteralPath $assembly)) { throw 'Run the complete isolated suite first.' }
        $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
        $passed = 'RESULT PASS=\d+ FAIL=0 DATABASE=' + [regex]::Escape($session.database) + '\.'
        if (!(Select-String -LiteralPath $checkLog -Pattern $passed -Quiet)) { throw 'The retained database has no passing full-suite result. Browser host was not started.' }
        if ((Get-Item -LiteralPath $assembly).LastWriteTimeUtc -gt (Get-Item -LiteralPath $checkLog).LastWriteTimeUtc) { throw 'The test build changed after the last successful suite. Run the complete checks again.' }
        $knowledgeLog = Join-Path $repoRoot '.build\executive-agent\knowledge-live-checks.log'
        if (!(Test-Path -LiteralPath $knowledgeLog) -or !(Select-String -LiteralPath $knowledgeLog -Pattern 'RESULT KNOWLEDGE_PASS=\d+ FAIL=0 LIVE_MODEL=True' -Quiet)) { throw 'The real bilingual site knowledge suite has not passed. Browser host was not started.' }
        if ((Get-Item -LiteralPath $knowledgeLog).LastWriteTimeUtc -lt (Get-Item -LiteralPath $checkLog).LastWriteTimeUtc) { throw 'Run the complete suite: site knowledge evidence predates this SQL fixture.' }
        $occupied = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object LocalPort -in 5254,7182
        if ($occupied) { throw 'Ports 5254/7182 are still occupied. Stop the existing app/debug host first.' }
        dotnet $assembly --executive-agent-browser-host
        return
    }
    $testProject = Join-Path $env:AUTH_PROJECT_ROOT 'Tests\AuthSmoke\AuthSmoke.csproj'
    dotnet build $testProject --configuration AgentVerify
    if ($LASTEXITCODE -ne 0) { throw 'Executive Agent build failed.' }
    $assembly = Join-Path $env:AUTH_PROJECT_ROOT 'Tests\AuthSmoke\bin\AgentVerify\net10.0\AuthSmoke.dll'
    # The harness creates a NEW OrangeAuthCheck_Executive_<UTC> SQL Server database.
    # It never migrates an existing team or production database and sends no supplier email.
    $evidenceFolder = Join-Path $repoRoot '.build\executive-agent'
    New-Item -ItemType Directory -Path $evidenceFolder -Force | Out-Null
    dotnet $assembly --executive-model-failure-checks 2>&1 | Tee-Object -FilePath (Join-Path $evidenceFolder 'model-failure-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Model failure/retry security checks failed.' }
    dotnet $assembly --executive-navigation-checks 2>&1 | Tee-Object -FilePath (Join-Path $evidenceFolder 'navigation-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Voice navigation checks failed.' }
    dotnet $assembly --executive-knowledge-checks 2>&1 | Tee-Object -FilePath (Join-Path $evidenceFolder 'knowledge-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Site knowledge/route checks failed.' }
    dotnet $assembly --executive-agent-csrf-probe --expect-fixed 2>&1 | Tee-Object -FilePath (Join-Path $evidenceFolder 'csrf-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Executive Agent CSRF checks failed. SQL checks and browser host were not started.' }
    if ($CsrfOnly) { return }
    dotnet $assembly --executive-agent-checks 2>&1 | Tee-Object -FilePath (Join-Path $evidenceFolder 'sql-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Executive Agent checks failed. Browser host was not started.' }
    dotnet $assembly --executive-knowledge-checks --live-model 2>&1 | Tee-Object -FilePath (Join-Path $evidenceFolder 'knowledge-live-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Real bilingual site knowledge checks failed. Browser host was not started.' }
    if ($Browser) {
        $occupied = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object LocalPort -in 5254,7182
        if ($occupied) { throw 'Stop Visual Studio debugging/the normal Orange app on ports 5254/7182, then use the --executive-agent-browser-host command with the built AuthSmoke.dll. Test data is retained.' }
        dotnet $assembly --executive-agent-browser-host
    }
} finally { $env:AUTH_PROJECT_ROOT = $oldRoot }
