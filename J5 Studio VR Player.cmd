@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Start-Studio.ps1" -Application VRPlayer
if errorlevel 1 pause
