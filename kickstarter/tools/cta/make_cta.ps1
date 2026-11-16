# Renders the call-to-action banners (cta.html) to kickstarter/assets/visuals/ with headless Chrome.
# Run from anywhere: powershell -File kickstarter\tools\cta\make_cta.ps1
$chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here "..\..\assets\visuals" | Resolve-Path
$page = "file:///" + ((Join-Path $here "cta.html") -replace "\\", "/")
$jobs = @(
  @{ v = "steam_top";     w = 1360; h = 380; name = "ks_cta_wishlist_steam_top_1360x380.png" },
  @{ v = "steam";         w = 1360; h = 380; name = "ks_cta_wishlist_steam_1360x380.png" },
  @{ v = "discord";       w = 1360; h = 380; name = "ks_cta_discord_1360x380.png" },
  @{ v = "badge_steam";   w = 420;  h = 156; name = "ks_badge_wishlist_steam_840x312.png"; scale = 2 },
  @{ v = "badge_discord"; w = 420;  h = 156; name = "ks_badge_discord_840x312.png"; scale = 2 }
)
foreach ($j in $jobs) {
  $file = Join-Path $out $j.name
  $scale = if ($j.scale) { $j.scale } else { 1 }
  $args = @("--headless=new", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
    "--default-background-color=00000000", "--force-device-scale-factor=$scale",
    "--window-size=$($j.w),$($j.h)", "--virtual-time-budget=4000", "--screenshot=$file", "$page`?v=$($j.v)")
  Start-Process -FilePath $chrome -ArgumentList $args -Wait -NoNewWindow
  Write-Output "wrote $($j.name)"
}
