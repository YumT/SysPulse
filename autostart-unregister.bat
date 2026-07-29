@echo off
rem Unregister SysPulsar autostart (deletes SysPulsar.lnk from the Startup folder).
rem No admin required. A running SysPulsar instance is left as-is.
powershell -NoProfile -Command "Remove-Item ([IO.Path]::Combine([Environment]::GetFolderPath('Startup'),'SysPulsar.lnk')) -ErrorAction SilentlyContinue"
echo Unregistered: SysPulsar will no longer start on logon.
pause
