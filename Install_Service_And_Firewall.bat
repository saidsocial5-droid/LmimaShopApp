@echo off
echo ==============================================
echo Installing Windows Firewall Rules for LmimaShop
echo ==============================================

netsh advfirewall firewall add rule name="LmimaShop Mobile Sync API" dir=in action=allow protocol=TCP localport=8080
netsh advfirewall firewall add rule name="LmimaShop Barcode Hub SignalR" dir=in action=allow protocol=TCP localport=8081

echo.
echo ==============================================
echo Setting up Auto-Start on Boot
echo ==============================================
set "exePath=%~dp0LmimaShopApp\bin\Debug\net10.0-windows\LmimaShopApp.exe"
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "LmimaShopApp" /t REG_SZ /d "\"%exePath%\"" /f

echo.
echo Installation Complete! The App will start automatically on boot and allow mobile traffic.
pause
