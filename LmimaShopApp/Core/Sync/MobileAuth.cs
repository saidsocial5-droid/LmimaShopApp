using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using MySqlConnector;
using Serilog;

namespace LmimaShopApp.Core.Sync
{
    /// <summary>
    /// مصادقة أجهزة الهاتف: رمز اقتران مؤقت + توكن Bearer مخزن كتجزئة SHA-256 في جدول devices
    /// </summary>
    public static class MobileAuth
    {
        public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan TokenCacheLifetime = TimeSpan.FromSeconds(30);
        private const int MaxFailedPairAttempts = 10;
        private static readonly TimeSpan FailedAttemptsWindow = TimeSpan.FromMinutes(5);

        private static readonly object _pairLock = new object();
        private static string _pairingCode = GenerateCode();
        private static DateTime _pairingCodeExpiryUtc = DateTime.UtcNow.Add(PairingCodeLifetime);
        private static readonly Queue<DateTime> _failedAttempts = new Queue<DateTime>();
        private static readonly ConcurrentDictionary<string, (string deviceId, DateTime checkedAt)> _tokenCache = new();

        public static string CurrentPairingCode
        {
            get { lock (_pairLock) return _pairingCode; }
        }

        public static DateTime PairingCodeExpiryUtc
        {
            get { lock (_pairLock) return _pairingCodeExpiryUtc; }
        }

        /// <summary>
        /// يعيد رمز الاقتران الحالي، أو يولد رمزاً جديداً إذا انتهت صلاحيته
        /// </summary>
        public static string EnsureFreshPairingCode()
        {
            lock (_pairLock)
            {
                if (DateTime.UtcNow >= _pairingCodeExpiryUtc)
                {
                    _pairingCode = GenerateCode();
                    _pairingCodeExpiryUtc = DateTime.UtcNow.Add(PairingCodeLifetime);
                }
                return _pairingCode;
            }
        }

        public static string RegeneratePairingCode()
        {
            lock (_pairLock)
            {
                _pairingCode = GenerateCode();
                _pairingCodeExpiryUtc = DateTime.UtcNow.Add(PairingCodeLifetime);
                return _pairingCode;
            }
        }

        /// <summary>
        /// تعيين رمز يختاره المستخدم يدوياً (يبقى صالحاً لمدة محدودة فقط)
        /// </summary>
        public static void SetPairingCode(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length < 4) throw new ArgumentException("رمز الاقتران يجب أن يتكون من 4 أرقام على الأقل");
            lock (_pairLock)
            {
                _pairingCode = code;
                _pairingCodeExpiryUtc = DateTime.UtcNow.Add(PairingCodeLifetime);
            }
        }

        /// <summary>
        /// التحقق من رمز الاقتران بزمن ثابت مع حد للمحاولات الفاشلة
        /// </summary>
        public static bool ValidatePairingCode(string code, out string error)
        {
            error = "";
            lock (_pairLock)
            {
                DateTime now = DateTime.UtcNow;
                while (_failedAttempts.Count > 0 && now - _failedAttempts.Peek() > FailedAttemptsWindow)
                {
                    _failedAttempts.Dequeue();
                }

                if (_failedAttempts.Count >= MaxFailedPairAttempts)
                {
                    error = "محاولات كثيرة خاطئة. انتظر 5 دقائق ثم أنشئ رمزاً جديداً من الحاسوب.";
                    return false;
                }

                if (now >= _pairingCodeExpiryUtc)
                {
                    error = "انتهت صلاحية رمز الاقتران. افتح نافذة الاقتران في الحاسوب للحصول على رمز جديد.";
                    return false;
                }

                byte[] a = Encoding.UTF8.GetBytes((code ?? "").Trim());
                byte[] b = Encoding.UTF8.GetBytes(_pairingCode);
                if (a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b))
                {
                    return true;
                }

                _failedAttempts.Enqueue(now);
                if (_failedAttempts.Count >= MaxFailedPairAttempts)
                {
                    // تغيير الرمز تلقائياً بعد محاولات تخمين متكررة
                    _pairingCode = GenerateCode();
                    _pairingCodeExpiryUtc = now.Add(PairingCodeLifetime);
                }
                error = "رمز الاقتران غير صحيح";
                return false;
            }
        }

        public static string IssueToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        }

        /// <summary>
        /// استخراج التوكن من ترويسة Authorization (أو من الرابط عند السماح بذلك، لأن EventSource لا يدعم الترويسات)
        /// </summary>
        public static string ExtractToken(Dictionary<string, string> headers, string rawUrl, bool allowQueryToken)
        {
            if (headers.TryGetValue("Authorization", out var auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return auth.Substring(7).Trim();
            }

            if (allowQueryToken && !string.IsNullOrEmpty(rawUrl))
            {
                int q = rawUrl.IndexOf('?');
                if (q >= 0)
                {
                    foreach (var pair in rawUrl.Substring(q + 1).Split('&'))
                    {
                        if (pair.StartsWith("token=", StringComparison.OrdinalIgnoreCase))
                        {
                            return Uri.UnescapeDataString(pair.Substring(6)).Trim();
                        }
                    }
                }
            }
            return "";
        }

        public static bool TryAuthenticate(Dictionary<string, string> headers, string rawUrl, bool allowQueryToken, out string deviceId)
        {
            deviceId = "";
            string token = ExtractToken(headers, rawUrl, allowQueryToken);
            if (string.IsNullOrEmpty(token)) return false;

            string hash = Sha256Hex(token);
            if (_tokenCache.TryGetValue(hash, out var cached) && DateTime.UtcNow - cached.checkedAt < TokenCacheLifetime)
            {
                deviceId = cached.deviceId;
                return true;
            }

            try
            {
                using var conn = new MySqlConnection(SecurityHelper.GetConnectionString());
                var device = conn.QueryFirstOrDefault("SELECT device_id, revoked FROM devices WHERE token_hash = @Hash LIMIT 1", new { Hash = hash });
                if (device != null && Convert.ToInt32(device.revoked) == 0)
                {
                    deviceId = (string)device.device_id;
                    _tokenCache[hash] = (deviceId, DateTime.UtcNow);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MobileAuth.TryAuthenticate error");
            }

            _tokenCache.TryRemove(hash, out _);
            return false;
        }

        /// <summary>
        /// يُستدعى بعد إلغاء جهاز حتى يُرفض توكنه فوراً
        /// </summary>
        public static void ClearTokenCache() => _tokenCache.Clear();

        public static string Sha256Hex(string raw)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        }

        private static string GenerateCode()
        {
            return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }
    }
}
