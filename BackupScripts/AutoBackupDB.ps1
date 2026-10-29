$BackupDir = "C:\Users\Admin\source\repos\LmimaShopApp\Backups"
if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir | Out-Null }

$DateStr = Get-Date -Format "yyyyMMdd_HHmmss"
$SqlFile = "$BackupDir\LmimaShopDB_$DateStr.sql"
$ZipFile = "$BackupDir\LmimaShopDB_$DateStr.zip"

$MysqlDump = "mysqldump"
if (Test-Path "C:\xampp\mysql\bin\mysqldump.exe") { $MysqlDump = "C:\xampp\mysql\bin\mysqldump.exe" }

try {
    # Run mysqldump
    & cmd.exe /c ""$MysqlDump" -u root LmimaShopDB > "$SqlFile""
    
    if (Test-Path $SqlFile) {
        # Compress to ZIP
        Compress-Archive -Path $SqlFile -DestinationPath $ZipFile -Force
        # Remove the uncompressed .sql file
        Remove-Item -Path $SqlFile -Force
    }
    
    # Keep only the last 7 days of backups
    Get-ChildItem -Path $BackupDir -Filter "*.zip" | Where-Object { $_.CreationTime -lt (Get-Date).AddDays(-7) } | Remove-Item -Force
} catch {
    Write-Error "Backup failed: $_"
}
