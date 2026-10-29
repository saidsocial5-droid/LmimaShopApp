"""
بناء تطبيق الهاتف LmimaShop.apk من مجلد LmimaShopMobile.

الاستعمال:
    python build_apk.py                      # بناء محلي ونسخ الملف إلى مجلدات التوزيع المعتادة
    python build_apk.py --version 3.4.1      # تحديد رقم الإصدار (يُكتب داخل التطبيق وفي versionCode)
    python build_apk.py --version 3.4.1 --out dist   # وضع GitHub Actions: الناتج في dist/LmimaShop.apk فقط

التوقيع يتم عبر uber-apk-signer بمفتاح debug المدمج فيه، وهو نفس المفتاح على كل الأجهزة،
لذلك يقبل الهاتف تثبيت التحديث فوق النسخة القديمة.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys

REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
TOOLS_DIR = os.path.join(REPO_ROOT, "ApkBuilderTools")
TEMPLATE_DIR = os.path.join(TOOLS_DIR, "template")
ASSETS_DIR = os.path.join(TEMPLATE_DIR, "assets")
MOBILE_DIR = os.path.join(REPO_ROOT, "LmimaShopMobile")
FINAL_DIR = os.path.join(TOOLS_DIR, "final")
UNSIGNED_APK = os.path.join(TOOLS_DIR, "unsigned.apk")

ALLOWED_EXTS = {".js", ".html", ".css", ".png", ".jpg", ".jpeg", ".ttf", ".json"}
# ملفات البيانات (lmima_mobile_sync.*) تحتوي معلومات الزبائن ولا يجوز وضعها داخل APK
IGNORE_FILES = {"start_mobile_server.bat", "start_server.ps1", "README_MOBILE.md",
                "lmima_mobile_sync.json", "lmima_mobile_sync.js"}


def find_java():
    java_home = os.environ.get("JAVA_HOME")
    if java_home and os.path.exists(os.path.join(java_home, "bin", "java.exe" if os.name == "nt" else "java")):
        return os.path.join(java_home, "bin", "java")
    local_jdk = r"C:\Program Files\Microsoft\jdk-17.0.20.101-hotspot\bin\java.exe"
    if os.path.exists(local_jdk):
        return local_jdk
    bundled = os.path.join(TOOLS_DIR, "jre", "bin", "java.exe")
    if os.path.exists(bundled):
        return bundled
    return "java"


def version_code(version):
    parts = [int(p) for p in re.findall(r"\d+", version)[:3]] + [0, 0, 0]
    return parts[0] * 10000 + parts[1] * 100 + parts[2]


def set_template_version(version):
    code = version_code(version)
    yml_path = os.path.join(TEMPLATE_DIR, "apktool.yml")
    with open(yml_path, encoding="utf-8") as f:
        yml = f.read()
    yml = re.sub(r"versionCode: .*", f"versionCode: {code}", yml)
    yml = re.sub(r"versionName: .*", f"versionName: '{version}'", yml)
    with open(yml_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(yml)

    manifest_path = os.path.join(TEMPLATE_DIR, "AndroidManifest.xml")
    with open(manifest_path, encoding="utf-8") as f:
        manifest = f.read()
    manifest = re.sub(r'android:versionCode="\d+"', f'android:versionCode="{code}"', manifest)
    manifest = re.sub(r'android:versionName="[^"]*"', f'android:versionName="{version}"', manifest)
    with open(manifest_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(manifest)
    print(f"      versionName={version} versionCode={code}")


def stamp_app_version(version):
    app_js = os.path.join(ASSETS_DIR, "app.js")
    with open(app_js, encoding="utf-8") as f:
        src = f.read()
    src, n = re.subn(r"const CURRENT_APP_VERSION = '[^']*';", f"const CURRENT_APP_VERSION = '{version}';", src)
    if n != 1:
        print("Error: CURRENT_APP_VERSION not found in app.js")
        sys.exit(1)
    with open(app_js, "w", encoding="utf-8", newline="") as f:
        f.write(src)


def build_apk(version, out_dir):
    java = find_java()

    print("[1/6] Cleaning template assets and temporary files...")
    if os.path.exists(ASSETS_DIR):
        shutil.rmtree(ASSETS_DIR)
    os.makedirs(ASSETS_DIR, exist_ok=True)
    if os.path.exists(FINAL_DIR):
        shutil.rmtree(FINAL_DIR)
    os.makedirs(FINAL_DIR, exist_ok=True)
    if os.path.exists(UNSIGNED_APK):
        os.remove(UNSIGNED_APK)

    print("[2/6] Copying web assets to template/assets/...")
    for root, _dirs, files in os.walk(MOBILE_DIR):
        rel_path = os.path.relpath(root, MOBILE_DIR)
        target_root = os.path.join(ASSETS_DIR, rel_path) if rel_path != "." else ASSETS_DIR
        os.makedirs(target_root, exist_ok=True)
        for file in files:
            if file in IGNORE_FILES or file.endswith(".apk"):
                continue
            if os.path.splitext(file)[1].lower() in ALLOWED_EXTS:
                shutil.copy2(os.path.join(root, file), os.path.join(target_root, file))
    asset_count = sum(len(f) for _, _, f in os.walk(ASSETS_DIR))
    print(f"      Copied {asset_count} asset files.")

    if version:
        stamp_app_version(version)
        set_template_version(version)

    print("[3/6] Building APK using apktool...")
    framework_dir = os.path.join(TOOLS_DIR, "framework")
    os.makedirs(framework_dir, exist_ok=True)
    res = subprocess.run([java, "-jar", os.path.join(TOOLS_DIR, "apktool.jar"), "b", "template", "-p", framework_dir, "-o", "unsigned.apk"],
                         cwd=TOOLS_DIR, capture_output=True, text=True)
    if res.returncode != 0 or not os.path.exists(UNSIGNED_APK):
        print("Apktool error:\n", res.stderr or res.stdout)
        sys.exit(1)
    print(f"      unsigned.apk created ({os.path.getsize(UNSIGNED_APK) / (1024*1024):.2f} MB)")

    print("[4/6] Aligning (zipalign 4) and Signing APK (v1, v2, v3)...")
    sign_res = subprocess.run([java, "-jar", os.path.join(TOOLS_DIR, "uber-apk-signer.jar"), "-a", "unsigned.apk", "--out", "final"],
                              cwd=TOOLS_DIR, capture_output=True, text=True)
    if sign_res.returncode != 0:
        print("uber-apk-signer error:\n", sign_res.stdout, sign_res.stderr)
        sys.exit(1)

    print("[5/6] Locating signed and aligned APK...")
    candidates = [f for f in os.listdir(FINAL_DIR) if f.endswith(".apk")]
    candidates.sort(key=lambda f: "aligned" not in f)
    if not candidates:
        print("Error: Could not find signed APK in final directory!")
        sys.exit(1)
    signed_apk = os.path.join(FINAL_DIR, candidates[0])
    print(f"      Found {candidates[0]} ({os.path.getsize(signed_apk) / (1024*1024):.2f} MB)")

    print("[6/6] Distributing APK...")
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
        destinations = [os.path.join(out_dir, "LmimaShop.apk")]
    else:
        destinations = [
            os.path.join(REPO_ROOT, "LmimaShop.apk"),
            os.path.join(REPO_ROOT, "Lmima Shop APK", "LmimaShop.apk"),
            os.path.join(REPO_ROOT, "LmimaShopApp", "bin", "Release", "net10.0-windows", "LmimaShop.apk"),
            os.path.join(REPO_ROOT, "LmimaShopApp", "bin", "Debug", "net10.0-windows", "LmimaShop.apk"),
        ]
    for dest in destinations:
        if os.path.exists(os.path.dirname(dest)):
            shutil.copy2(signed_apk, dest)
            print(f"      -> Copied to: {dest}")

    print("\n[SUCCESS] APK build, alignment, and signing completed successfully!")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default="", help="رقم الإصدار مثل 3.4.1")
    parser.add_argument("--out", default="", help="مجلد الناتج (وضع GitHub Actions)")
    args = parser.parse_args()
    build_apk(args.version.strip().lstrip("vV"), args.out)
