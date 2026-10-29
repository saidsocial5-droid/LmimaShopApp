@echo off
echo Backing up LmimaShopDB...
mysqldump -u root LmimaShopDB > LmimaShopDB_Backup_%date:~-4,4%%date:~-10,2%%date:~-7,2%.sql
echo Backup complete!
pause
