# Runs a C# file (or inline code) in the open Boids Editor and prints only its result.
# powershell -File ev.ps1 <file.cs>     |   powershell -File ev.ps1 -Code "return 1;"
param([string]$File, [string]$Code, [int]$Timeout = 120)
$proj = "D:\Unity\Projects\Boids\Boids"
if ($Code) {
  $File = Join-Path $env:TEMP ("ev_" + [guid]::NewGuid().ToString("N") + ".cs")
  Set-Content -Path $File -Value $Code -Encoding utf8
}
$out = & unity command eval_file --file $File --project-path $proj --timeout $Timeout --format json 2>$null | Out-String
if ($Code) { Remove-Item $File -ErrorAction SilentlyContinue }
try {
  $j = $out | ConvertFrom-Json
  if (-not $j.success) { "FAILED: " + ($j.errors | ConvertTo-Json -Compress -Depth 5); exit 1 }
  $r = $j.data.result
  if ($r.PSObject.Properties.Name -contains "result") { $r.result } else { $r | ConvertTo-Json -Depth 6 }
} catch { $out }
