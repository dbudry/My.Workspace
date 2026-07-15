@echo off
setlocal enabledelayedexpansion

rem Launcher for Dev-StartClient.ps1 — restart Blazor client only (port 7047).
rem Use when the full Dev-StartDebugSession stack is already running and you
rem only need to restart dotnet watch after a client-side fix.

cd /d "%~dp0"

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%Dev-StartClient.ps1"

echo Restarting My.Workspace Blazor client only (https://localhost:7047)...
echo.
echo Default: kill stale client processes + dotnet clean + watch.
echo.
echo Options (pass to this .cmd or the .ps1):
echo   -NoClean       skip dotnet clean (fast restart)
echo   -CleanShared    also clean My.Shared + My.DAL
echo   -FullReset      delete client bin/obj (stubborn SRI / 99%% boot errors)
echo   -NewWindow      open watch in a separate console window
echo.
echo If the browser sticks at 99%%, hard-refresh (Ctrl+Shift+R) after restart.
echo NOTE: SQL, Azurite, and the Functions host on 7074 can stay running.
echo.

where pwsh >nul 2>&1
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
)

echo.
echo Client watch stopped.
pause