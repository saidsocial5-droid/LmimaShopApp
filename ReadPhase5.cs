using System;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        string path = @"C:\Users\Admin\.gemini\antigravity\brain\80980bc8-47e0-4485-be70-30a6a4a48447\.system_generated\logs\transcript.jsonl";
        var lines = File.ReadAllLines(path);
        
        foreach (var line in lines)
        {
            if (line.Contains("المرحلة 5") || line.Contains("Phase 5"))
            {
                try {
                    using (JsonDocument doc = JsonDocument.Parse(line))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("content", out JsonElement content))
                        {
                            string text = content.GetString();
                            if (text.Contains("المرحلة 5") || text.Contains("Phase 5"))
                            {
                                Console.WriteLine(text);
                                Console.WriteLine("======================");
                            }
                        }
                    }
                } catch { }
            }
        }
    }
}
