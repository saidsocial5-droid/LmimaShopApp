# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

LmimaShop is an Arabic (RTL) point-of-sale system for a Moroccan shop: a WPF/.NET 10 desktop cashier backed by MySQL 8, plus a companion phone app (a PWA, also packaged as an Android APK) that talks to an HTTP server embedded in the desktop app. User-facing strings, docs, and log messages are mostly in Arabic. Save files as UTF-8.

The folder holds **two separate generations**. Before you edit anything, work out which one the task is about:

| | V1 (in production) | V2 (from-scratch rewrite) |
|---|---|---|
| Desktop | `LmimaShopApp/` (`LmimaShopApp.slnx`) | `LmimaShopV2/desktop/LmimaShopV2.Desktop.csproj` |
| Mobile | `LmimaShopMobile/` (flat `app.js`, `db.js`, `sync_v2.js`) | `LmimaShopV2/mobile/` (modular `js/*.js`) |
| Database | `LmimaShopDB` | `LmimaShopV2DB` (`LmimaShopV2/db/schema.sql`) |
| Embedded server | port 8080 (raw `TcpListener`), SignalR on 8081 | port 8090 (`HttpListener` + SSE) |
| Docs | `README.md`, `SYSTEM_ARCHITECTURE.md`, `FEATURES_BASELINE.md` | `LmimaShopV2/README.md`, `docs/ARCHITECTURE.md`, `docs/API_SPEC.md` |

`FEATURES_BASELINE.md` lists every V1 feature that must survive refactors. The repository is pushed to a **public** GitHub repo (`saidsocial5-droid/LmimaShopApp`), so never commit secrets; `.gitignore` keeps shop data (`settings.json`, `lmima_mobile_sync.*`, `ProductImages/`, backups, APKs) out of it — keep it that way. `Backups/`, `BackupScripts/`, `Temp*/`, `LmimaShopApk_temp/`, `TranscriptReader/`, `ProductImages/`, and the `*.apk`/`*.zip` files at the root are artifacts or one-off tools, not source.

There are no automated tests and no linter configured in either generation.

## Commands

V1 desktop:
```powershell
dotnet build LmimaShopApp/LmimaShopApp.csproj            # Debug; "تشغيل البرنامج.bat" launches bin\Debug\net10.0-windows\LmimaShopApp.exe
dotnet publish LmimaShopApp/LmimaShopApp.csproj -c Release -r win-x64 --self-contained false -o out/app   # what CI zips into LmimaShopApp-win-x64.zip
```
Building Debug writes into `LmimaShopApp\bin\Debug\net10.0-windows\`, which is the folder the shop actually runs from (it holds the live `settings.json`, `ProductImages/`, `LmimaShop.apk`). Build to `-o <temp dir>` when you only want to check compilation.

Releases: push a tag `vX.Y.Z`. `.github/workflows/build.yml` publishes the desktop zip, builds the APK (`python build_apk.py --version X.Y.Z --out out`), writes `SHA256SUMS.txt`, and creates a GitHub Release. The repo is public, so `UpdateService` reads releases without a token and CI embeds none (a token embedded in a public release would leak). An optional token (`--set-update-token`, `LMIMA_UPDATE_TOKEN`) is still honoured if present.
To build the installer, run Inno Setup on `LmimaShopApp.iss`.

V1 APK: `python build_apk.py [--version X.Y.Z] [--out DIR]`. It copies `LmimaShopMobile/` into `ApkBuilderTools/template/assets` (never the `lmima_mobile_sync.*` customer-data files), stamps `CURRENT_APP_VERSION` in `app.js` and versionCode = major*10000+minor*100+patch, builds with `apktool.jar`, and signs with `uber-apk-signer.jar`'s embedded debug key. That key is the same everywhere, which is what lets phones install updates over the old app — don't switch signing keys. Without `--out` it copies the APK to the root, `Lmima Shop APK/` and `LmimaShopApp/bin/*`.

To change the V1 phone app, edit `LmimaShopMobile/` and then rebuild the APK. `LmimaShopAndroid/` (a Gradle WebView wrapper, `MainActivity.java`) keeps its own older copy of the assets.

V2 (run from `LmimaShopV2/`):
```powershell
dotnet build desktop/LmimaShopV2.Desktop.csproj -c Release
.\scripts\start_desktop.ps1 [-ForceBuild]   # build if needed, then run
.\scripts\start_mobile_server.ps1           # dev-only, unauthenticated PWA server on 8091
.\scripts\publish.ps1                       # self-contained single-file -> dist\publish\ (+ mobile/)
iscc installer\LmimaShopV2.iss
.\scripts\build_apk.ps1                     # needs a local templates\LmimaShopV2Template.apk (not checked in); uses ..\ApkBuilderTools
.\scripts\backup_db.ps1                     # mysqldump to %USERPROFILE%\Documents\LmimaShopV2Backups, keeps 14
```
On first run, V2 creates the DB and schema if the MySQL user is allowed to. The default login is `admin` / `admin123`.

## V1 architecture (LmimaShopApp)

- **Mostly code-behind.** Most logic lives in large `*Window.xaml.cs` files (MainWindow is the POS screen, plus Settings, Customers, Products, Reports; each is 1–2k lines). They query MySQL directly through `DatabaseHelper`/`MySqlCommand`.
- **Partial MVVM migration.** Some code has started moving to MVVM/DI: `AppHost.cs` (Microsoft.Extensions.Hosting) registers `Core/Domain/Repositories` interfaces with their `Infrastructure/Repositories` implementations (Dapper), plus `Presentation/ViewModels`. Both styles exist side by side.
- **Startup.** `App.OnStartup` starts `MobileSyncHttpServer` (8080), `SignalRServer` (8081), the Google Drive watcher in `CloudSyncService`, and `UpdateService` (GitHub releases check every 6 h). `LmimaShopApp.exe --set-update-token <PAT>` stores a read token locally with DPAPI instead of relying on the embedded one.
- **`MainWindow.xaml.cs` is decompiled code** (`IL_...` comments); edit it surgically.
- **Shutdown.** `OnExit` runs the auto-backup, the Google Sheets customer sync, and the mobile data export.
- **Configuration.**
  - `settings.json` next to the exe holds store info, printer, paper size, TVA, and backup folder.
  - The DB connection string comes from `SecurityHelper.GetConnectionString()`.
  - Serilog writes logs to `Logs/` next to the exe.
- **`MobileSyncHttpServer.cs`** is a hand-rolled HTTP server: routes are matched by string and responses are written to a `NetworkStream`. It exposes:
  - `/api/pair`, `/api/sync`, `/api/sync/push`, `/api/events` (SSE through `Core/Sync/SseManager`)
  - `/api/pos/checkout`, `/api/barcode`
  - CRUD under `/api/products|customers|suppliers|categories/...`

  New mobile endpoints go here. Every `/api/*` route needs a paired device's Bearer token (`Core/Sync/MobileAuth`) except `ping`, `health`, `pair`, `version`/`check-update` and `/LmimaShop.apk` (left open so old app versions can still update). `/api/events` also accepts `?token=` because EventSource can't send headers. Pairing codes are random, last 15 minutes, and lock after 10 bad attempts.
- **Phone sales** (`/api/pos/checkout` and the offline `pos_checkout` push op) all go through `Core/Sync/MobileCheckoutService`: op_id idempotency via `processed_operations`, stock and credit checked in the same transaction (rejected with 422 when live; accepted with a `sync_conflicts` warning when replayed from offline).
- **Soft delete.** The phone soft-deletes products/customers (`deleted_at`); every desktop list and the sync export must filter `deleted_at IS NULL`.
- **Delta sync.**
  - Pull: `updated_at > since`.
  - Push: ops carry an `op_id` that is de-duplicated through `processed_operations`. Conflicts go to `sync_conflicts`, which `SyncConflictsWindow` displays. Resolution is last-write-wins.
  - Migrations: `V1_DeltaSync.sql` (idempotent) and `Rollback_V1.sql`. The base schema is `schema.sql`.
- **Offline path.** On checkout and on exit, `CloudSyncService` writes `lmima_mobile_sync.json`/`.js` (atomically) into the Google Drive folders. The phone can load these files with no network link to the PC. In the other direction, the phone exports its pending queue as `lmima_pending_ops_*.json`; the Drive watcher imports it through `DeltaSyncApi.ProcessPushBody` and moves it to `Processed/`.
- **Hardware.** ESC/POS raw printing; the drawer kick code (`27,112,0,25,250`) is set in `settings.json`; printers are discovered through WMI (`System.Management`).
- `update_server.ps1` is a one-off script that patches `MobileSyncHttpServer.cs` in place. Don't run it as a build step.

## V2 architecture (LmimaShopV2)

The authoritative docs are `docs/ARCHITECTURE.md` and `docs/API_SPEC.md`. These invariants are the ones most often broken:

- Dependencies flow inward only: Presentation (WPF MVVM) → Core (services and models) → Infrastructure (Data, Server). No SQL or business rules in Views or ViewModels.
- **`CheckoutService.CheckoutAsync` is the only path that creates a sale.** The desktop UI, `POST /api/checkout`, and sync `sale_create` ops all go through it, in one transaction:
  - Stock is decremented atomically with `... WHERE StockQuantity >= @qty`; if 0 rows are affected, the whole sale is aborted.
  - Credit limit is checked; loyalty is `floor(paid/10)`.
  - `audit_logs` and `processed_operations` rows are written.
  - The SSE `data_changed` event is sent only after commit.
- Every business table has `uuid` (generated by the client), `updated_at`, `deleted_at` (soft delete), and `version`. DB triggers maintain `updated_at` and `version`. SSE events are only notifications; clients respond by re-pulling the delta.
- Naming: DB business columns are PascalCase and sync columns are snake_case. The API's JSON is snake_case.
- Security:
  - Every `/api/*` route except `ping` and `pair` requires a Bearer token; tokens are never accepted in the query string.
  - Device tokens are stored only as SHA-256 hashes.
  - Pairing codes have 6 digits, expire after 120 s, and are limited to 5 attempts per minute per IP.
  - The static file server has path-traversal checks.
  - DB credentials are encrypted with DPAPI.
- No real data, DB dumps, or backups belong inside the repo folders.
