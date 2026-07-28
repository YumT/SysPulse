# SysPulse インストールスクリプト
# publish フォルダの内容を %LOCALAPPDATA%\Programs\SysPulse にコピーし、
# スタートアップフォルダにショートカットを作成する(Windows ログオン時に自動起動)。
$ErrorActionPreference = 'Stop'

$src = Join-Path $PSScriptRoot 'publish'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\SysPulse'

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path (Join-Path $src 'SysPulse.exe') -Destination $dest -Force
Copy-Item -Path (Join-Path $src 'config.json') -Destination $dest -Force

$startupDir = [Environment]::GetFolderPath('Startup')
$lnkPath = Join-Path $startupDir 'SysPulse.lnk'
$exePath = Join-Path $dest 'SysPulse.exe'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnkPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $dest
$shortcut.Description = 'SysPulse - lightweight system monitor'
$shortcut.Save()

Write-Host "Installed to: $dest"
Write-Host "Startup shortcut: $lnkPath"
