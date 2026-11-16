# Starts a match the way a player does: Play -> main menu PLAY -> lobby Ready -> Start, then waits for the round.
# powershell -File session.ps1 start | stop | state | press <button> | run <file.cs> | wait-clip
param([string]$Action = "state", [string]$Arg)
$d = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = "D:\Unity\Projects\Boids\Boids"
function Ev([string]$f) { powershell -NoProfile -ExecutionPolicy Bypass -File "$d\ev.ps1" $f }
function State { Ev "$d\state.cs" }
function Press([string]$b) {
  $f = "$d\_press_$b.cs"
  (Get-Content "$d\press.cs.tmpl" -Raw).Replace("__BUTTON__", $b) | Set-Content $f -Encoding utf8
  Ev $f
}
function WaitFor([string]$pattern, [int]$seconds) {
  $t = [DateTime]::Now.AddSeconds($seconds)
  while ([DateTime]::Now -lt $t) {
    $s = State
    if ($s -match $pattern) { return $s }
    Start-Sleep -Milliseconds 1500
  }
  return "TIMEOUT waiting for $pattern; last: $s"
}
switch ($Action) {
  "state" { State }
  "press" { Press $Arg }
  "run"   { Ev $Arg }
  "event" {
    # -Arg "Kind|takefile|delaySeconds|clipName"
    $p = $Arg.Split("|")
    $f = "$d\_start_$($p[0]).cs"
    (Get-Content "$d\prelude.cs" -Raw) + "`n" + (Get-Content "$d\ev_start.cs.tmpl" -Raw).Replace("__KIND__", $p[0]) | Set-Content $f -Encoding utf8
    Ev $f
    Start-Sleep -Seconds ([int]$p[2])
    & powershell -NoProfile -ExecutionPolicy Bypass -File $MyInvocation.MyCommand.Path take "$d\$($p[1])"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $MyInvocation.MyCommand.Path wait-clip
    python -I "$d\sheet.py" $p[3]
  }
  "take"  {
    $f = "$d\_take_" + [IO.Path]::GetFileName($Arg)
    (Get-Content "$d\prelude.cs" -Raw) + "`n" + (Get-Content $Arg -Raw) | Set-Content $f -Encoding utf8
    Ev $f
  }
  "stop"  { Ev "$d\perf_off.cs"; & unity command editor_stop --project-path $proj --format json 2>$null | Out-Null; Start-Sleep 3; State }
  "start" {
    $s = State
    if ($s -match "playing=True") { "already playing: $s"; break }
    & unity command editor_play --project-path $proj --format json 2>$null | Out-Null
    WaitFor "playing=True scene=01 - MainMenu" 120
    Start-Sleep 4
    Press "play-btn"
    WaitFor "scene=02 - ReefRunLobby" 60
    Start-Sleep 4
    Press "ready-btn"
    Start-Sleep 2
    Press "start-btn"
    WaitFor "phase=Playing" 180
    Ev "$d\perf.cs"
  }
  "wait-clip" {
    $t = [DateTime]::Now.AddSeconds(240)
    while ([DateTime]::Now -lt $t) {
      $s = State
      if ($s -match "clip=done" -or $s -notmatch "clip=") { $s; break }
      Start-Sleep 2
    }
  }
}

