# Starts the application from the authoritative workspace only.
# Usage (from anywhere):  powershell -ExecutionPolicy Bypass -File <WorkV2>\scripts\Run-WorkV2.ps1
# Refuses to start when the folder is not the WorkV2 repository, the branch is not WorkV3 (or the WorkV2 backup),
# or another process already holds the official ports (5254 / 7182).
param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$expectedRoot = 'C:/Users/user/Desktop/Orange/Projects/Orange-Tendering/Orange-Tendering-System-WorkV2'
$repo = Split-Path -Parent $PSScriptRoot

$root = (git -C $repo rev-parse --show-toplevel).Trim()
$branch = (git -C $repo branch --show-current).Trim()
if ($root -ne $expectedRoot) { throw "Wrong repository: $root (expected $expectedRoot). Nothing was started." }
if ($branch -notin 'WorkV3', 'WorkV2') { throw "Wrong branch: '$branch' (expected WorkV3, or WorkV2 as the backup). Nothing was started." }

foreach ($port in 5254, 7182) {
    $busy = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($busy) {
        $owner = (Get-CimInstance Win32_Process -Filter "ProcessId=$($busy.OwningProcess)").ExecutablePath
        throw "Port $port is already used by $owner (PID $($busy.OwningProcess)). Stop it first; nothing was started."
    }
}

$status = git -C $repo status --short
if ($status) { Write-Host "Note: uncommitted changes in WorkV2:`n$status" -ForegroundColor Yellow }

$project = Join-Path $repo 'Orange-Tendering-System'
Write-Host "Starting $project ($branch) on https://localhost:7182 and http://localhost:5254"
$runArgs = @('run', '--project', $project, '--launch-profile', 'https')
if ($NoBuild) { $runArgs += '--no-build' }
& dotnet @runArgs
