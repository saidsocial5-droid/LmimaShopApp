$ErrorActionPreference = "Stop"
Write-Host "Running Python APK Builder..." -ForegroundColor Cyan
python "$PSScriptRoot\build_apk.py"
