# Safe opt-in copy for project skills: dry-run unless -Apply.
# powershell -ExecutionPolicy Bypass -File .\08_TOOLS\Install-Skills.ps1 -RepoPath "C:\path\to\repo"
# powershell -ExecutionPolicy Bypass -File .\08_TOOLS\Install-Skills.ps1 -RepoPath "C:\path\to\repo" -Apply
param([Parameter(Mandatory=$true)][string]$RepoPath, [switch]$Apply)
$kitRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $kitRoot '07_CLAUDE_SKILLS\.claude\skills'
$target = Join-Path $RepoPath '.claude\skills'
if (!(Test-Path -LiteralPath $source)) { throw 'Kit skills source not found. Run from the extracted kit.' }
if (!(Test-Path -LiteralPath $RepoPath -PathType Container)) { throw "Repository missing: $RepoPath" }
Get-ChildItem -LiteralPath $source -Directory | ForEach-Object {
    $to = Join-Path $target $_.Name
    if (Test-Path -LiteralPath $to) {
        Write-Warning "Skip existing project skill, will NOT overwrite: $to"
    } else {
        Write-Host "COPY proposed: $($_.FullName) => $to"
        if ($Apply) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $to -Recurse
            Write-Host "Installed: $to" -ForegroundColor Green
        }
    }
}
if (!$Apply) { Write-Host 'DRY RUN ONLY. Repeat with -Apply to install missing skills.' -ForegroundColor Yellow }
