@echo off
setlocal
title Plotree Installer

echo ===================================================
echo   Plotree direct installer
echo ===================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Plotree.ps1"

if errorlevel 1 (
    echo.
    echo Installation failed. See the error above.
) else (
    echo.
    echo Installation completed.
)

echo.
pause
