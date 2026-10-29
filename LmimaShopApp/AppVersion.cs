using System;
using System.Linq;
using System.Reflection;

namespace LmimaShopApp
{
    public static class AppVersion
    {
        /// <summary>
        /// إصدار البرنامج كما حُدد عند البناء (يُضبط تلقائياً من وسم الإصدار في GitHub Actions)
        /// </summary>
        public static string Current { get; } = ReadVersion();

        public static string? GetMetadata(string key)
        {
            return Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value;
        }

        /// <summary>
        /// مقارنة إصدارين بصيغة 3.4.0 أو v3.4.0. تعيد قيمة موجبة إذا كان الأول أحدث.
        /// </summary>
        public static int Compare(string? a, string? b)
        {
            return Parse(a).CompareTo(Parse(b));
        }

        public static Version Parse(string? v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int cut = v.IndexOfAny(new[] { '+', '-', ' ' });
            if (cut >= 0) v = v.Substring(0, cut);
            if (!v.Contains('.')) v += ".0";
            return Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0);
        }

        private static string ReadVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                int plus = info.IndexOf('+');
                return plus >= 0 ? info.Substring(0, plus) : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}
