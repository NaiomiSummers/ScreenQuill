@echo off
cd /d "%~dp0"
if exist "SopRecorder.exe" goto launch
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build.ps1"
if errorlevel 1 goto failed
:launch
start "" "%~dp0SopRecorder.exe"
exit /b 0
:failed
echo Could not build SOP Recorder. See the message above.
pause
exit /b 1
