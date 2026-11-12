<#
.SYNOPSIS
  Smoke-test rilis AllSense: publish build terbaru, jalankan, pastikan jendela utama muncul.

.DESCRIPTION
  Mencegah regresi kelas "window tak pernah tampil" (contoh: InvariantGlobalization=true
  memutus data budaya -> font cache WPF gagal, app jalan diam-diam tanpa jendela).

  1. Publish AllSense.App (Release, win-x64, self-contained single-file).
  2. Jalankan exe.
  3. Tunggu ~10 dtk, cek MainWindowHandle != 0 (Get-Process).
  4. Kill proses. Exit non-zero bila window tak muncul.

.PARAMETER DotNet
  Path ke dotnet.exe. Default: deteksi otomatis (local ~/.dotnet dulu, lalu Program Files/PATH).

.PARAMETER ExePath
  Lewati publish, langsung uji exe yang sudah ada (khusus pengujian cepat artefak).

.EXAMPLE
  .\tools\smoke-test.ps1
  .\tools\smoke-test.ps1 -DotNet C:\Users\me\.dotnet\dotnet.exe
  .\tools\smoke-test.ps1 -ExePath app\_stage\payload\AllSense.exe
#>
param(
  [string]$DotNet = '',
  [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'

# --- deteksi dotnet: preferensi local (~/.dotnet / AppData), baru PATH ---
if (-not $DotNet) {
  $cands = @(
    "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
    "$env:USERPROFILE\.dotnet\dotnet.exe",
    (Get-Command dotnet -ErrorAction SilentlyContinue).Source
  ) | Where-Object { $_ -and (Test-Path $_) }
  $DotNet = $cands | Select-Object -First 1
}
if (-not $DotNet -or -not (Test-Path $DotNet)) { throw "dotnet tidak ditemukan. Beri -DotNet <path>." }

$root = Split-Path -Parent $PSScriptRoot

# --- publish fresh build, atau pakai exe yang sudah diberikan ---
if (-not $ExePath) {
  $proj = Join-Path $root 'app\AllSense.App\AllSense.App.csproj'
  $publishDir = Join-Path ([System.IO.Path]::GetTempPath()) "AllSense_smoke_$PID"
  & $DotNet publish $proj -c Release -r win-x64 --self-contained true -o $publishDir | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Publish gagal (exit $LASTEXITCODE)." }
  $ExePath = Join-Path $publishDir 'AllSense.exe'
}
if (-not (Test-Path $ExePath)) { throw "Exe tidak ada: $ExePath" }

Write-Host "== Smoke-test: $ExePath"
$p = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds 10
$p.Refresh()

if ($p.HasExited) {
  Write-Error "GAGAL: proses keluar sendiri (exit code $($p.ExitCode)) sebelum window muncul. Kemungkinan crash startup (cek InvariantGlobalization/trimming)."
  exit 1
}
if ($p.MainWindowHandle -eq [IntPtr]::Zero) {
  Write-Error "GAGAL: proses jalan tapi TIDAK ada main window (MainWindowHandle=0). Ini gejala font-cache/globalization rusak."
  Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
  exit 1
}

Write-Host "OK: main window muncul (handle=$($p.MainWindowHandle))."
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
exit 0
