@echo off
setlocal enabledelayedexpansion

rem Launcher for Dev-StartFunctionHost.ps1
rem Double-click this .cmd file instead of the .ps1 directly.
rem It ensures the console window stays open on errors or when the host stops.
rem 
rem This version is robust when launched from Visual Studio's "Open Command Line"
rem or "Execute File" context menu on the file in Solution Explorer.
rem VS often opens the console with the current directory set to the solution root
rem or even System32. We immediately switch to the script's own directory.

rem Change to this script's directory no matter what the caller's current directory is.
cd /d "%~dp0"

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%Dev-StartFunctionHost.ps1"

echo Starting My.Workspace Function Host...
echo (Defaults to HTTPS on port 7074 using explicit localhost.pfx)
echo (Use -UseHttps:$false to force plain HTTP if needed)
echo.
echo NOTE: If you get "Access denied" when creating the certificate,
echo       right-click this .cmd file and choose "Run as administrator".
echo.
echo NOTE: Running as admin also runs 'dotnet dev-certs https --trust' to help with
echo       any https://localhost browser certificate warnings (which often look like CORS).
echo.

rem Use pwsh (PowerShell 7) if available, otherwise fall back to Windows PowerShell.
where pwsh >nul 2>&1
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
)

echo.
echo The script has finished or the host was stopped.
pause
