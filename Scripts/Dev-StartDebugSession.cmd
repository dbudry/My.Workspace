@echo off
setlocal

rem One script to rule them all for My.Workspace local debugging.
rem Double-click this .cmd (or run from VS "Open Command Line" on the file in Solution Explorer).
rem 
rem It will automatically:
rem   - Start LocalDB
rem   - Start Azurite in a new window (if needed)
rem   - Start the Functions host (func start) in a clean window on port 7074
rem     (Google API / OAuth redirect URIs are configured for 7074)
rem   - Kill any previous process on the client port 7047 (avoids "address already in use")
rem   - Start the Blazor client (dotnet watch) in a separate window on port 7047
rem     (Google login / redirect URLs are configured for 7047)
rem   - Wait for the Functions worker process
rem   - Attach the Visual Studio debugger to the worker for breakpoints
rem
rem This avoids the startup timeout issues when the debugger is attached too early.
rem It also handles the client port conflict that happens when previous dotnet watch
rem instances were not shut down cleanly.

cd /d "%~dp0"

echo Starting full debug session for My.Workspace (Functions 7074 + Client 7047)...
echo (Defaults to HTTPS on 7074 using explicit localhost.pfx)
echo (Use -UseHttps:$false to disable.)
echo.
echo NOTE: If you get "Access denied" when creating the certificate,
echo       right-click this .cmd file and choose "Run as administrator".
echo.
echo NOTE: Running as admin also auto-trusts the ASP.NET dev cert (for client port 7047)
echo       and forces trust of the Functions localhost cert via certutil.
echo       After it prints the trust messages:
echo         - Close ALL browser windows (Task Manager: end msedge.exe + chrome.exe *completely*)
echo         - Hard refresh (Ctrl+Shift+R) or restart the browser
echo.
echo NOTE: The script kills stale Blazor clients on ports 7047/5047 and starts ONE
echo       dotnet watch via Dev-StartClient.ps1 (shared boot helper). WASM asset
echo       fingerprinting is OFF in Debug (My.Client.csproj), so stable filenames
echo       prevent the classic integrity / 99%% boot errors after rebuilds. The client
echo       window does a plain client-only clean (shared projects were already built).
echo       Pass -ClientFullReset to the .ps1 if errors persist (wipes client bin/obj).
echo       After start, hard-refresh the browser (Ctrl+Shift+R) if needed.
echo.
echo NOTE: The script now adds the project folder to Defender exclusions (permanent fix),
echo       disables realtime during cleanup, kills old windows + compilers aggressively,
echo       force-removes locked obj folders (with retries), pre-cleans + rebuilds shared
echo       projects once in the main script, cleans only the function app in the host window,
echo       and waits 12s after launching the host before starting the client (client window
echo       does a client-only clean — it no longer re-cleans My.Shared/My.DAL).
echo       This sequences the builds so the host and client are no longer choking each other
echo       on My.Shared.dll via VBCSCompiler + Defender.
echo.

where pwsh >nul 2>&1
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Dev-StartDebugSession.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Dev-StartDebugSession.ps1" %*
)

echo.
pause
