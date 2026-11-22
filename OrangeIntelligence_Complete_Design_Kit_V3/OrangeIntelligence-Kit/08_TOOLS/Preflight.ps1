# Read-only preflight. Run from PowerShell:
# powershell -ExecutionPolicy Bypass -File .\08_TOOLS\Preflight.ps1 -RepoPath "C:\path\to\repo"
param([string]$RepoPath = (Get-Location).Path)
$ErrorActionPreference = 'Continue'
Write-Host "Orange Intelligence: read-only preflight" -ForegroundColor Cyan
if (-not (Test-Path -LiteralPath $RepoPath -PathType Container)) { throw "RepoPath does not exist: $RepoPath" }
Push-Location -LiteralPath $RepoPath
try {
    Write-Host "Repository: $(Get-Location)"
    if (Get-Command git -ErrorAction SilentlyContinue) {
        git status --short --branch
        git worktree list
    } else { Write-Warning 'Git executable not found' }
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --version } else { Write-Warning 'dotnet not available on PATH' }
    if (Get-Command ollama -ErrorAction SilentlyContinue) {
        ollama --version
        ollama list
        ollama ps
    } else { Write-Warning 'Ollama unavailable on PATH' }
    try {
       $r = Invoke-WebRequest -Uri 'http://127.0.0.1:11434/api/tags' -UseBasicParsing -TimeoutSec 3
       Write-Host "Ollama HTTP status: $($r.StatusCode)"
    } catch { Write-Warning 'Ollama API is not answering at 127.0.0.1:11434' }
    $piper = Join-Path $HOME 'AI-Models\Piper'
    if (Test-Path $piper) { Get-ChildItem -LiteralPath $piper -Filter '*.onnx' | Select-Object -ExpandProperty Name }
    if (Get-Command python -ErrorAction SilentlyContinue) { python --version }
    if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) { nvidia-smi --query-gpu=name,memory.total --format=csv }
    Write-Host 'Finished; no files changed.' -ForegroundColor Green
} finally { Pop-Location }
