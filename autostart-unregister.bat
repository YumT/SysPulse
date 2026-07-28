@echo off
rem Unregister SysPulse autostart (deletes SysPulse.lnk from the Startup folder).
rem No admin required. A running SysPulse instance is left as-is.
powershell -NoProfile -Command "Remove-Item ([IO.Path]::Combine([Environment]::GetFolderPath('Startup'),'SysPulse.lnk')) -ErrorAction SilentlyContinue"
echo Unregistered: SysPulse will no longer start on logon.
pause
