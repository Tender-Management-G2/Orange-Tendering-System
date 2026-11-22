param([Parameter(Mandatory=$true)][string]$RepoPath)
$ErrorActionPreference = 'Continue'
$repo=(Resolve-Path -LiteralPath $RepoPath).Path
Write-Host '=== Repository (read only) ==='
& git -C $repo rev-parse --show-toplevel
& git -C $repo branch --show-current
& git -C $repo log -1 --oneline
& git -C $repo status --short
Write-Host '=== Runtimes ==='
& dotnet --version
& ollama --version
& ollama list
& ollama ps
& python --version
& python -m pip show piper-tts
Write-Host '=== Local model availability ==='
try { (Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 5).models | Select-Object name,size } catch { Write-Warning 'Ollama API not available.' }
$piper=Join-Path $HOME 'AI-Models\Piper'
$whisper=Join-Path $HOME 'AI-Models\Whisper\ggml-small.bin'
foreach($name in @('ar_JO-kareem-medium.onnx','ar_JO-kareem-medium.onnx.json','en_US-lessac-medium.onnx','en_US-lessac-medium.onnx.json')) {
    $p=Join-Path $piper $name
    [PSCustomObject]@{File=$p;Exists=(Test-Path -LiteralPath $p -PathType Leaf)}
}
[PSCustomObject]@{File=$whisper;Exists=(Test-Path -LiteralPath $whisper -PathType Leaf)}
Write-Host '=== Port listeners (no processes stopped) ==='
Get-NetTCPConnection -LocalPort 5254,7182 -State Listen -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,OwningProcess
Write-Host 'No database connections, secrets, files or app processes changed.'
