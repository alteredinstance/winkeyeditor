@echo off
rem Builds WinKey Editor. Any arguments are passed to build.ps1, e.g.:
rem   build.cmd -FrameworkDependent
rem   build.cmd -Runtime win-arm64 -Run
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set EXITCODE=%ERRORLEVEL%
rem Keep the window open when double-clicked from Explorer.
echo %CMDCMDLINE% | find /i "/c" >nul && pause
exit /b %EXITCODE%
