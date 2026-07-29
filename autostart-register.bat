@echo off
rem Register SysPulsar to run at Windows logon (creates SysPulsar.lnk in the Startup folder).
rem Registers SysPulsar.exe located in the same folder as this bat. No admin required.
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Startup'),'SysPulsar.lnk')); $s.TargetPath='%~dp0SysPulsar.exe'; $s.WorkingDirectory='%~dp0'; $s.Description='SysPulsar - lightweight system monitor'; $s.Save()"
if errorlevel 1 (
  echo Failed to register autostart.
  pause
  exit /b 1
)
echo Registered: SysPulsar will start on logon.
pause
