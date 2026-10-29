param(
    [int]$Port = 8080
)

$Host.UI.RawUI.WindowTitle = "LMIMA SHOP - خادم تطبيق الهاتف المتنقل"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   🚀 متجر LMIMA SHOP - تشغيل خادم تطبيق الهاتف المتنقل" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

# الحصول على عنوان الـ IP المحلي في شبكة الواي فاي
$localIp = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { 
    $_.InterfaceAlias -notmatch 'vEthernet|Loopback|Virtual' -and $_.IPAddress -notmatch '127\.0\.0\.1|169\.254\.' 
} | Select-Object -First 1).IPAddress

if (-not $localIp) { $localIp = "127.0.0.1" }

$localUrl = "http://localhost:$Port"
$wifiUrl = "http://${localIp}:$Port"

Write-Host "`n📱 لفتح التطبيق على حاسوبك:" -ForegroundColor Green
Write-Host "   $localUrl" -ForegroundColor White

Write-Host "`n📲 لفتح التطبيق على هاتفك (المتصل بنفس شبكة Wi-Fi للمحل):" -ForegroundColor Green
Write-Host "   $wifiUrl" -ForegroundColor Yellow -BackgroundColor Black

Write-Host "`n💡 يمكنك أيضاً فتح الملف مباشرة من متصفح الهاتف أو ربطه بـ Google Drive مجاناً." -ForegroundColor DarkGray
Write-Host "`n[ الخادم يعمل الآن... اضغط Ctrl+C لإيقاف الخادم في أي وقت ]`n" -ForegroundColor Cyan

$appDir = $PSScriptRoot
if (-not $appDir) { $appDir = (Get-Location).Path }

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://+:$Port/")

try {
    $listener.Start()
} catch {
    # Fallback if admin rights for http://+:8080/ aren't granted
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add("http://localhost:$Port/")
    $listener.Prefixes.Add("http://127.0.0.1:$Port/")
    try {
        $listener.Start()
        Write-Host "تم تشغيل الخادم على localhost (للمشاركة عبر الشبكة يرجى تشغيل الملف كمسؤول Administrator)." -ForegroundColor Yellow
    } catch {
        Write-Host "خطأ في تشغيل المنفذ: $($_.Exception.Message)" -ForegroundColor Red
        return
    }
}

# فتح المتصفح على الحاسوب تلقائياً
Start-Process $localUrl

while ($listener.IsListening) {
    $context = $listener.GetContext()
    $request = $context.Request
    $response = $context.Response

    $urlPath = $request.Url.LocalPath.TrimStart('/')
    if ([string]::IsNullOrWhiteSpace($urlPath)) {
        $urlPath = "index.html"
    }

    $filePath = Join-Path $appDir $urlPath

    if (Test-Path $filePath -PathType Leaf) {
        $ext = [System.IO.Path]::GetExtension($filePath).ToLower()
        $contentType = switch ($ext) {
            ".html" { "text/html; charset=utf-8" }
            ".css"  { "text/css; charset=utf-8" }
            ".js"   { "application/javascript; charset=utf-8" }
            ".json" { "application/json; charset=utf-8" }
            ".png"  { "image/png" }
            ".jpg"  { "image/jpeg" }
            ".jpeg" { "image/jpeg" }
            ".svg"  { "image/svg+xml" }
            ".ico"  { "image/x-icon" }
            default { "application/octet-stream" }
        }

        $bytes = [System.IO.File]::ReadAllBytes($filePath)
        $response.ContentType = $contentType
        $response.ContentLength64 = $bytes.Length
        $response.Headers.Add("Access-Control-Allow-Origin", "*")
        $response.Headers.Add("Cache-Control", "no-cache")
        $response.OutputStream.Write($bytes, 0, $bytes.Length)
    } else {
        $response.StatusCode = 404
        $msg = [System.Text.Encoding]::UTF8.GetBytes("404 Not Found")
        $response.OutputStream.Write($msg, 0, $msg.Length)
    }
    $response.OutputStream.Close()
}