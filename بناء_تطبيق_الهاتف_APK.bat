@echo off
chcp 65001 >nul
title بناء تطبيق الهاتف APK - LMIMA SHOP
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0build_apk.ps1"
echo.
pause