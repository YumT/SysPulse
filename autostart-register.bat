@echo off
rem Register SysPulse to run at Windows logon (creates SysPulse.lnk in the Startup folder).
rem Registers SysPulse.exe located in the same folder as this bat. No admin required.
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Startup'),'SysPulse.lnk')); $s.TargetPath='%~dp0SysPulse.exe'; $s.WorkingDirectory='%~dp0'; $s.Description='SysPulse - lightweight system monitor'; $s.Save()"
if errorlevel 1 (
  echo Failed to register autostart.
  pause
  exit /b 1
)
echo Registered: SysPulse will start on logon.
pause
