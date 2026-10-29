using System;
using System.IO;
using System.Reflection;

class Program {
    static void Main() {
        try {
            string connStr = "Server=localhost;Database=LmimaShopDB;Uid=root;Pwd=;";
            var assembly = Assembly.LoadFrom("LmimaShopApp/bin/Debug/net10.0-windows/MySql.Data.dll");
            var connType = assembly.GetType("MySql.Data.MySqlClient.MySqlConnection");
            dynamic conn = Activator.CreateInstance(connType, connStr);
            conn.Open();
            var cmdType = assembly.GetType("MySql.Data.MySqlClient.MySqlCommand");
            dynamic cmd = Activator.CreateInstance(cmdType, File.ReadAllText("V2_Conflicts.sql"), conn);
            cmd.ExecuteNonQuery();
            conn.Close();
            Console.WriteLine("Success");
        } catch(Exception ex) {
            Console.WriteLine(ex.Message);
        }
    }
}
