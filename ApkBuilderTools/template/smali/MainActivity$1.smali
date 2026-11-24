.class Lcom/pipiscrew/webview/MainActivity$1;
.super Landroid/webkit/WebChromeClient;
.source "MainActivity.java"


# annotations
.annotation system Ldalvik/annotation/EnclosingMethod;
    value = Lcom/pipiscrew/webview/MainActivity;->onCreate(Landroid/os/Bundle;)V
.end annotation

.annotation system Ldalvik/annotation/InnerClass;
    accessFlags = 0x0
    name = null
.end annotation


# instance fields
.field final synthetic this$0:Lcom/pipiscrew/webview/MainActivity;


# direct methods
.method constructor <init>(Lcom/pipiscrew/webview/MainActivity;)V
    .locals 0

    .prologue
    .line 1
    iput-object p1, p0, Lcom/pipiscrew/webview/MainActivity$1;->this$0:Lcom/pipiscrew/webview/MainActivity;

    .line 34
    invoke-direct {p0}, Landroid/webkit/WebChromeClient;-><init>()V

    return-void
.end method


# virtual methods
.method public onExceededDatabaseQuota(Ljava/lang/String;Ljava/lang/String;JJJLandroid/webkit/WebStorage$QuotaUpdater;)V
    .locals 2
    .param p1, "url"    # Ljava/lang/String;
    .param p2, "databaseIdentifier"    # Ljava/lang/String;
    .param p3, "currentQuota"    # J
    .param p5, "estimatedSize"    # J
    .param p7, "totalUsedQuota"    # J
    .param p9, "quotaUpdater"    # Landroid/webkit/WebStorage$QuotaUpdater;

    .prologue
    .line 40
    const-wide/16 v0, 0x2

    mul-long/2addr v0, p5

    invoke-interface {p9, v0, v1}, Landroid/webkit/WebStorage$QuotaUpdater;->updateQuota(J)V

    .line 41
    return-void
.end method

.method public onPermissionRequest(Landroid/webkit/PermissionRequest;)V
    .locals 1
    .param p1, "request"    # Landroid/webkit/PermissionRequest;

    .prologue
    invoke-virtual {p1}, Landroid/webkit/PermissionRequest;->getResources()[Ljava/lang/String;

    move-result-object v0

    invoke-virtual {p1, v0}, Landroid/webkit/PermissionRequest;->grant([Ljava/lang/String;)V

    return-void
.end method

.method public onShowFileChooser(Landroid/webkit/WebView;Landroid/webkit/ValueCallback;Landroid/webkit/WebChromeClient$FileChooserParams;)Z
    .locals 4
    .param p1, "webView"
    .param p2, "filePathCallback"
    .param p3, "fileChooserParams"

    .prologue
    iget-object v0, p0, Lcom/pipiscrew/webview/MainActivity$1;->this$0:Lcom/pipiscrew/webview/MainActivity;

    iget-object v0, v0, Lcom/pipiscrew/webview/MainActivity;->mUploadCallbackAboveL:Landroid/webkit/ValueCallback;

    if-eqz v0, :cond_0

    iget-object v0, p0, Lcom/pipiscrew/webview/MainActivity$1;->this$0:Lcom/pipiscrew/webview/MainActivity;

    iget-object v0, v0, Lcom/pipiscrew/webview/MainActivity;->mUploadCallbackAboveL:Landroid/webkit/ValueCallback;

    const/4 v1, 0x0

    invoke-interface {v0, v1}, Landroid/webkit/ValueCallback;->onReceiveValue(Ljava/lang/Object;)V

    :cond_0
    iget-object v0, p0, Lcom/pipiscrew/webview/MainActivity$1;->this$0:Lcom/pipiscrew/webview/MainActivity;

    iput-object p2, v0, Lcom/pipiscrew/webview/MainActivity;->mUploadCallbackAboveL:Landroid/webkit/ValueCallback;

    :try_start_0
    new-instance v0, Landroid/content/Intent;

    const-string v1, "android.intent.action.GET_CONTENT"

    invoke-direct {v0, v1}, Landroid/content/Intent;-><init>(Ljava/lang/String;)V

    const-string v1, "android.intent.category.OPENABLE"

    invoke-virtual {v0, v1}, Landroid/content/Intent;->addCategory(Ljava/lang/String;)Landroid/content/Intent;

    const-string v1, "*/*"

    invoke-virtual {v0, v1}, Landroid/content/Intent;->setType(Ljava/lang/String;)Landroid/content/Intent;

    iget-object v1, p0, Lcom/pipiscrew/webview/MainActivity$1;->this$0:Lcom/pipiscrew/webview/MainActivity;

    const-string v2, "اختيار ملف المزامنة"

    invoke-static {v0, v2}, Landroid/content/Intent;->createChooser(Landroid/content/Intent;Ljava/lang/CharSequence;)Landroid/content/Intent;

    move-result-object v0

    const/16 v2, 0x65

    invoke-virtual {v1, v0, v2}, Lcom/pipiscrew/webview/MainActivity;->startActivityForResult(Landroid/content/Intent;I)V
    :try_end_0
    .catch Ljava/lang/Exception; {:try_start_0 .. :try_end_0} :catch_0

    const/4 v0, 0x1

    return v0

    :catch_0
    move-exception v0

    iget-object v1, p0, Lcom/pipiscrew/webview/MainActivity$1;->this$0:Lcom/pipiscrew/webview/MainActivity;

    const/4 v2, 0x0

    iput-object v2, v1, Lcom/pipiscrew/webview/MainActivity;->mUploadCallbackAboveL:Landroid/webkit/ValueCallback;

    const/4 v0, 0x0

    return v0
.end method


