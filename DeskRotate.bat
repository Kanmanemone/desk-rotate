@echo off
rem Kept ASCII-only on purpose - see scripts\launch.ps1 for why.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\launch.ps1"
if errorlevel 1 pause
