@echo off
chcp 65001 >nul
title تشغيل خادم تطبيق الهاتف - LMIMA SHOP
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0start_server.ps1"
pause