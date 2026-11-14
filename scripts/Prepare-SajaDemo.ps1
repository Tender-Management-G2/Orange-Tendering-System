$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'Orange-Tendering-System'
$testProject = Join-Path $projectRoot 'Tests/AuthSmoke/AuthSmoke.csproj'
$buildRoot = Join-Path $repoRoot '.build/saja-demo/'
$previousRoot = $env:AUTH_PROJECT_ROOT
try {
    $env:AUTH_PROJECT_ROOT = $projectRoot
    & dotnet build $testProject "-p:BaseOutputPath=$buildRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Demo tool build failed.' }
    & dotnet (Join-Path $buildRoot 'Debug/net10.0/AuthSmoke.dll') --seed-saja-demo
    if ($LASTEXITCODE -ne 0) { throw 'Demo preparation failed.' }
    Write-Host "Accounts: $projectRoot/App_Data/SajaDemo/accounts.md"
    Write-Host 'Run from the repository directory: dotnet run --project Orange-Tendering-System --launch-profile https'
} finally {
    $env:AUTH_PROJECT_ROOT = $previousRoot
}
