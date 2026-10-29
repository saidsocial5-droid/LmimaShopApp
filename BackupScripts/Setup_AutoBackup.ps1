$TaskName = "LmimaShop_AutoBackup"
$ScriptPath = "C:\Users\Admin\source\repos\LmimaShopApp\BackupScripts\AutoBackupDB.ps1"

Write-Host "جاري إنشاء المهمة المجدولة..." -ForegroundColor Cyan
$Action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-WindowStyle Hidden -ExecutionPolicy Bypass -File \"$ScriptPath\""
$Trigger = New-ScheduledTaskTrigger -Daily -At 23:00

Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Description "Daily backup of Lmima Shop database" -Force
Write-Host "تم تفعيل النسخ الاحتياطي التلقائي يومياً الساعة 23:00 بنجاح!" -ForegroundColor Green
