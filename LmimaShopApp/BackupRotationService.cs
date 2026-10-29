using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LmimaShopApp
{
    public static class BackupRotationService
    {
        public static async Task PerformScheduledBackupAsync(string backupDirectory)
        {
            if (!Directory.Exists(backupDirectory))
                Directory.CreateDirectory(backupDirectory);

            string dailyDir = Path.Combine(backupDirectory, "Daily");
            string weeklyDir = Path.Combine(backupDirectory, "Weekly");
            string monthlyDir = Path.Combine(backupDirectory, "Monthly");

            Directory.CreateDirectory(dailyDir);
            Directory.CreateDirectory(weeklyDir);
            Directory.CreateDirectory(monthlyDir);

            string backupFileName = $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
            string backupPath = Path.Combine(dailyDir, backupFileName);

            // Generate Backup
            await Task.Run(() => BackupService.GenerateBackup(backupPath));

            // Clean up old backups based on retention policy
            CleanUpOldBackups(dailyDir, 7);    // 7 Daily
            
            // If today is Sunday, copy to weekly
            if (DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
            {
                File.Copy(backupPath, Path.Combine(weeklyDir, backupFileName));
                CleanUpOldBackups(weeklyDir, 4); // 4 Weekly
            }

            // If it is the 1st of the month, copy to monthly
            if (DateTime.Now.Day == 1)
            {
                File.Copy(backupPath, Path.Combine(monthlyDir, backupFileName));
                CleanUpOldBackups(monthlyDir, 6); // 6 Monthly
            }
        }

        private static void CleanUpOldBackups(string directory, int maxFiles)
        {
            var files = new DirectoryInfo(directory).GetFiles("*.sql")
                                                    .OrderByDescending(f => f.CreationTime)
                                                    .ToList();

            if (files.Count > maxFiles)
            {
                foreach (var file in files.Skip(maxFiles))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch { /* ignore */ }
                }
            }
        }
    }
}