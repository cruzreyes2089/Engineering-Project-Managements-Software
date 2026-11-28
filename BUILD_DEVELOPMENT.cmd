@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_AND_TEST.ps1" -Publish
if errorlevel 1 (
  echo Build failed. Keep the error text; no release is certified.
  pause
  exit /b 1
)
echo Development build produced. Do not use production client data.
pause
