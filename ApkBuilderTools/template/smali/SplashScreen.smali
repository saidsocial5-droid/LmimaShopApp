.class public Lcom/pipiscrew/webview/SplashScreen;
.super Landroid/app/Activity;
.source "SplashScreen.java"


# direct methods
.method public constructor <init>()V
    .locals 0

    .prologue
    .line 12
    invoke-direct {p0}, Landroid/app/Activity;-><init>()V

    return-void
.end method


# virtual methods
.method protected onCreate(Landroid/os/Bundle;)V
    .locals 5
    .param p1, "savedInstanceState"    # Landroid/os/Bundle;

    .prologue
    .line 16
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V

    .line 17
    const v1, 0x7f030001

    invoke-virtual {p0, v1}, Lcom/pipiscrew/webview/SplashScreen;->setContentView(I)V

    .line 19
    const v1, 0x7f080001

    invoke-virtual {p0, v1}, Lcom/pipiscrew/webview/SplashScreen;->findViewById(I)Landroid/view/View;

    move-result-object v0

    check-cast v0, Landroid/widget/LinearLayout;

    .line 20
    .local v0, "l":Landroid/widget/LinearLayout;
    const v1, 0x7f020001

    invoke-virtual {v0, v1}, Landroid/widget/LinearLayout;->setBackgroundResource(I)V

    .line 25
    new-instance v1, Landroid/os/Handler;

    invoke-direct {v1}, Landroid/os/Handler;-><init>()V

    new-instance v2, Lcom/pipiscrew/webview/SplashScreen$1;

    invoke-direct {v2, p0}, Lcom/pipiscrew/webview/SplashScreen$1;-><init>(Lcom/pipiscrew/webview/SplashScreen;)V

    .line 33
    const-wide/16 v3, 0x7d0

    .line 25
    invoke-virtual {v1, v2, v3, v4}, Landroid/os/Handler;->postDelayed(Ljava/lang/Runnable;J)Z

    .line 34
    return-void
.end method
