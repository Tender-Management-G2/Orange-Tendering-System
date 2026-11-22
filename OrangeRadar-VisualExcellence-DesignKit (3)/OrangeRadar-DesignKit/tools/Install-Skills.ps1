param([Parameter(Mandatory=$true)][string]$RepoPath)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepoPath).Path
$top = & git -C $repo rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0) { throw 'Not a Git worktree.' }
$source = Join-Path (Split-Path $PSScriptRoot -Parent) '.claude\skills'
$target = Join-Path $top '.claude\skills'
$items = @(Get-ChildItem -LiteralPath $source -Directory)
foreach($item in $items) {
    $dest = Join-Path $target $item.Name
    if(Test-Path -LiteralPath $dest) { throw "Existing skill: $dest. No files copied; review manually." }
}
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach($item in $items) { Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse }
Write-Host "Copied $($items.Count) project skills to $target. Review git status; no commit was made."
