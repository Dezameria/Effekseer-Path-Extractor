@echo off
if not exist "%~dp0release\app\ResourceManager.App.exe" (
    echo App not found. Build it with scripts\build.ps1 -Publish.
    pause
    exit /b 1
)
start "" "%~dp0release\app\ResourceManager.App.exe" %*
