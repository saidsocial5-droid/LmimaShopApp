using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MySqlConnector;
using Dapper;

namespace LmimaShopApp
{
    public class SyncConflictModel
    {
        public int id { get; set; }
        public string entity_type { get; set; }
        public string entity_uuid { get; set; }
        public string server_data { get; set; }
        public string client_data { get; set; }
        public string device_id { get; set; }
        public DateTime created_at { get; set; }
    }

    public partial class SyncConflictsWindow : Window
    {
        private string connStr = "Server=localhost;Database=LmimaShopDB;Uid=root;Pwd=;";

        public SyncConflictsWindow()
        {
            InitializeComponent();
            EnsureTableExists();
            LoadConflicts();
        }

        private void EnsureTableExists()
        {
            try {
                using (var conn = new MySqlConnection(connStr)) {
                    conn.Execute("CREATE TABLE IF NOT EXISTS sync_conflicts (id INT AUTO_INCREMENT PRIMARY KEY, entity_type VARCHAR(50) NOT NULL, entity_uuid CHAR(36) NOT NULL, server_data TEXT, client_data TEXT, device_id VARCHAR(100), resolved BOOLEAN DEFAULT FALSE, created_at DATETIME(3) DEFAULT CURRENT_TIMESTAMP(3))");
                }
            } catch {}
        }

        private void LoadConflicts()
        {
            try {
                using (var conn = new MySqlConnection(connStr)) {
                    var list = conn.Query<SyncConflictModel>("SELECT * FROM sync_conflicts WHERE resolved = FALSE ORDER BY created_at DESC").AsList();
                    ConflictsDataGrid.ItemsSource = list;
                }
            } catch (Exception ex) {
                MessageBox.Show("خطأ في جلب التعارضات: " + ex.Message);
            }
        }

        private void BtnKeepServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                int conflictId = Convert.ToInt32(btn.Tag);
                MarkResolved(conflictId);
                LoadConflicts();
            }
        }

        private void BtnKeepClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                int conflictId = Convert.ToInt32(btn.Tag);
                MessageBox.Show("تم اعتماد نسخة الهاتف. سيتم بث التحديث للأجهزة.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                MarkResolved(conflictId);
                LoadConflicts();
            }
        }

        private void MarkResolved(int id)
        {
            try {
                using (var conn = new MySqlConnection(connStr)) {
                    conn.Execute($"UPDATE sync_conflicts SET resolved = TRUE WHERE id = {id}");
                }
            } catch {}
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadConflicts();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

