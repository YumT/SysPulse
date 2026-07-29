# SysPulsar インストールスクリプト
# publish フォルダの内容を %LOCALAPPDATA%\Programs\SysPulsar にコピーし、
# スタートアップフォルダにショートカットを作成する(Windows ログオン時に自動起動)。
$ErrorActionPreference = 'Stop'

# --- 旧版(SysPulse)からの移行処理 ---
# 旧プロセスが動いていれば停止する
Get-Process -Name 'SysPulse' -ErrorAction SilentlyContinue | Stop-Process -Force
# スタートアップの旧ショートカットを削除する
$startupDir = [Environment]::GetFolderPath('Startup')
$oldLnk = Join-Path $startupDir 'SysPulse.lnk'
if (Test-Path $oldLnk) { Remove-Item $oldLnk -Force }
# 旧インストールフォルダがあれば削除する
$oldDest = Join-Path $env:LOCALAPPDATA 'Programs\SysPulse'
if (Test-Path $oldDest) { Remove-Item $oldDest -Recurse -Force }

$src = Join-Path $PSScriptRoot 'publish'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\SysPulsar'

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path (Join-Path $src 'SysPulsar.exe') -Destination $dest -Force
Copy-Item -Path (Join-Path $src 'config.json') -Destination $dest -Force

$lnkPath = Join-Path $startupDir 'SysPulsar.lnk'
$exePath = Join-Path $dest 'SysPulsar.exe'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnkPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $dest
$shortcut.Description = 'SysPulsar - lightweight system monitor'
$shortcut.Save()

Write-Host "Installed to: $dest"
Write-Host "Startup shortcut: $lnkPath"
