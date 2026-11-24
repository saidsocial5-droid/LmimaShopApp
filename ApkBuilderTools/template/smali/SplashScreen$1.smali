.class Lcom/pipiscrew/webview/SplashScreen$1;
.super Ljava/lang/Object;
.source "SplashScreen.java"

# interfaces
.implements Ljava/lang/Runnable;


# annotations
.annotation system Ldalvik/annotation/EnclosingMethod;
    value = Lcom/pipiscrew/webview/SplashScreen;->onCreate(Landroid/os/Bundle;)V
.end annotation

.annotation system Ldalvik/annotation/InnerClass;
    accessFlags = 0x0
    name = null
.end annotation


# instance fields
.field final synthetic this$0:Lcom/pipiscrew/webview/SplashScreen;


# direct methods
.method constructor <init>(Lcom/pipiscrew/webview/SplashScreen;)V
    .locals 0

    .prologue
    .line 1
    iput-object p1, p0, Lcom/pipiscrew/webview/SplashScreen$1;->this$0:Lcom/pipiscrew/webview/SplashScreen;

    .line 25
    invoke-direct {p0}, Ljava/lang/Object;-><init>()V

    return-void
.end method


# virtual methods
.method public run()V
    .locals 3

    .prologue
    .line 29
    new-instance v0, Landroid/content/Intent;

    iget-object v1, p0, Lcom/pipiscrew/webview/SplashScreen$1;->this$0:Lcom/pipiscrew/webview/SplashScreen;

    const-class v2, Lcom/pipiscrew/webview/MainActivity;

    invoke-direct {v0, v1, v2}, Landroid/content/Intent;-><init>(Landroid/content/Context;Ljava/lang/Class;)V

    .line 30
    .local v0, "mainIntent":Landroid/content/Intent;
    iget-object v1, p0, Lcom/pipiscrew/webview/SplashScreen$1;->this$0:Lcom/pipiscrew/webview/SplashScreen;

    invoke-virtual {v1, v0}, Lcom/pipiscrew/webview/SplashScreen;->startActivity(Landroid/content/Intent;)V

    .line 31
    iget-object v1, p0, Lcom/pipiscrew/webview/SplashScreen$1;->this$0:Lcom/pipiscrew/webview/SplashScreen;

    invoke-virtual {v1}, Lcom/pipiscrew/webview/SplashScreen;->finish()V

    .line 32
    return-void
.end method
