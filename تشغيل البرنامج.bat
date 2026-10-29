@echo off
chcp 65001 >nul
title تشغيل برنامج LMIMA SHOP
cd /d "%~dp0LmimaShopApp\bin\Debug\net10.0-windows"
start "" "LmimaShopApp.exe"