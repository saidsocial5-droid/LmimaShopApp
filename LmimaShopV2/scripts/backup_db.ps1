#Requires -Version 5.1
<#
.SYNOPSIS
    Creates a mysqldump backup of the LmimaShopV2DB database.

.DESCRIPTION
    Backups are written OUTSIDE the repository by default:
        %USERPROFILE%\Documents\LmimaShopV2Backups

    The password is passed through the MYSQL_PWD environment variable (never on
    the command line) or through a .my.cnf file. The script keeps the newest 14
    backups and deletes older ones.

.PARAMETER Database
    Database name. Default: LmimaShopV2DB.

.PARAMETER BackupDir
    Destination folder. Default: Documents\LmimaShopV2Backups.

.PARAMETER Keep
    Number of backups to keep. Default: 14.

.PARAMETER DbHost
    MySQL host. Default: localhost.

.PARAMETER Port
    MySQL port. Default: 3306.

.PARAMETER User
    MySQL user. Default: root.

.PARAMETER Password
    Password as SecureString or plain string. Passed via MYSQL_PWD.

.PARAMETER MyCnfPath
    Path to a MySQL option file (.my.cnf) containing [mysqldump] credentials.

.PARAMETER NoPassword
    Allow connecting without a password (for passwordless local accounts).

.PARAMETER MySqlDumpPath
    Explicit path to mysqldump.exe. Default: search PATH and Program Files.

.PARAMETER AllowInsideRepo
    Permit writing backups inside LmimaShopV2 (not recommended).

.EXAMPLE
    $pwd = Read-Host -AsSecureString 'MySQL password'
    .\scripts\backup_db.ps1 -Password $pwd

.EXAMPLE
    .\scripts\backup_db.ps1 -MyCnfPath "$env:USERPROFILE\.my.cnf"

.EXAMPLE
    .\scripts\backup_db.ps1 -BackupDir D:\Backups\LmimaShop -Keep 30
#>
[CmdletBinding()]
param(
    [string]$Database = 'LmimaShopV2DB',
    [string]$BackupDir,
    [ValidateRange(1, 10000)]
    [int]$Keep = 14,
    [string]$DbHost = 'localhost',
    [int]$Port = 3306,
    [string]$User = 'root',
    [object]$Password,
    [string]$MyCnfPath,
    [switch]$NoPassword,
    [string]$MySqlDumpPath,
    [switch]$AllowInsideRepo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-PlainText {
    param([Parameter(Mandatory = $true)][System.Security.SecureString]$SecureString)

    $bstr = [System.IntPtr]::Zero
    try {
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
        return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        if ($bstr -ne [System.IntPtr]::Zero) {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Find-MySqlDump {
    $command = Get-Command -Name 'mysqldump' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($command) {
        return $command.Source
    }

    if (-not [string]::IsNullOrEmpty($env:ProgramFiles)) {
        $mysqlRoot = Join-Path $env:ProgramFiles 'MySQL'
        if (Test-Path -LiteralPath $mysqlRoot -PathType Container) {
            $candidate = Get-ChildItem -LiteralPath $mysqlRoot -Filter 'mysqldump.exe' -Recurse -File -ErrorAction SilentlyContinue |
                Sort-Object -Property FullName -Descending |
                Select-Object -First 1
            if ($candidate) {
                return $candidate.FullName
            }
        }
    }

    return $null
}

try {
    if ($Database -notmatch '^[A-Za-z0-9_]+$') {
        throw "Invalid database name '$Database'. Only letters, digits and underscore are allowed."
    }
    if ([string]::IsNullOrWhiteSpace($User)) {
        throw 'MySQL user must not be empty.'
    }
    if (-not [string]::IsNullOrEmpty($MyCnfPath) -and $null -ne $Password) {
        throw 'Use either -MyCnfPath or -Password, not both.'
    }

    $v2Root = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
    $repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $v2Root)).TrimEnd('\')

    if ([string]::IsNullOrWhiteSpace($BackupDir)) {
        if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
            $documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
            if ([string]::IsNullOrWhiteSpace($documents)) {
                throw 'Could not determine the Documents folder. Pass -BackupDir explicitly.'
            }
            $BackupDir = Join-Path $documents 'LmimaShopV2Backups'
        }
        else {
            $BackupDir = Join-Path $env:USERPROFILE 'Documents\LmimaShopV2Backups'
        }
    }

    $backupRoot = [System.IO.Path]::GetFullPath($BackupDir)
    $repoPrefix = $repoRoot + '\'
    if ($backupRoot.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and -not $AllowInsideRepo) {
        throw "Refusing to write backups inside the repository: $backupRoot. Use a folder outside the repository or pass -AllowInsideRepo explicitly."
    }

    if (-not (Test-Path -LiteralPath $backupRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        Write-Host "Created backup folder: $backupRoot"
    }

    if ($MySqlDumpPath) {
        if (-not (Test-Path -LiteralPath $MySqlDumpPath -PathType Leaf)) {
            throw "mysqldump.exe not found at: $MySqlDumpPath"
        }
        $dumpPath = (Resolve-Path -LiteralPath $MySqlDumpPath).Path
    }
    else {
        $dumpPath = Find-MySqlDump
        if (-not $dumpPath) {
            throw @'
mysqldump.exe was not found.
Install MySQL 8.x (which includes the command-line tools) or pass -MySqlDumpPath:
    -MySqlDumpPath "C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe"
'@
        }
    }

    $effectiveMyCnf = $null
    if ($MyCnfPath) {
        if (-not (Test-Path -LiteralPath $MyCnfPath -PathType Leaf)) {
            throw "MySQL option file not found: $MyCnfPath"
        }
        $effectiveMyCnf = (Resolve-Path -LiteralPath $MyCnfPath).Path
    }
    elseif ($null -eq $Password -and -not $NoPassword -and [string]::IsNullOrEmpty($env:MYSQL_PWD)) {
        $homeMyCnf = if ($env:USERPROFILE) { Join-Path $env:USERPROFILE '.my.cnf' } else { $null }
        if ($homeMyCnf -and (Test-Path -LiteralPath $homeMyCnf -PathType Leaf)) {
            $effectiveMyCnf = (Resolve-Path -LiteralPath $homeMyCnf).Path
            Write-Host "Using MySQL option file: $effectiveMyCnf"
        }
    }

    if ($null -eq $Password -and -not $NoPassword -and -not $effectiveMyCnf -and [string]::IsNullOrEmpty($env:MYSQL_PWD)) {
        throw @'
No MySQL credentials were supplied.
Provide one of the following:
  -Password (Read-Host -AsSecureString 'MySQL password')
  -MyCnfPath "C:\path\to\.my.cnf"
  -NoPassword        (only for passwordless local accounts)
or set the MYSQL_PWD environment variable before running the script.
'@
    }

    $plainPassword = $null
    $usePasswordEnv = $false
    if ($null -ne $Password) {
        if ($Password -is [System.Security.SecureString]) {
            $plainPassword = ConvertTo-PlainText -SecureString $Password
        }
        elseif ($Password -is [string]) {
            $plainPassword = [string]$Password
        }
        else {
            throw 'The -Password parameter must be a SecureString or a plain string.'
        }
        $usePasswordEnv = $true
    }

    $timestamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
    $backupFile = Join-Path $backupRoot ("{0}_{1}.sql" -f $Database, $timestamp)

    $dumpArgs = @()
    if ($effectiveMyCnf) {
        # Must be the first option so MySQL reads it before other defaults.
        $dumpArgs += "--defaults-extra-file=$effectiveMyCnf"
    }
    $dumpArgs += @(
        "--host=$DbHost",
        "--port=$Port",
        "--user=$User",
        '--single-transaction',
        '--quick',
        '--routines',
        '--triggers',
        '--events',
        '--no-tablespaces',
        '--default-character-set=utf8mb4',
        '--hex-blob',
        '--databases',
        $Database,
        "--result-file=$backupFile"
    )

    $previousMysqlPwd = $env:MYSQL_PWD
    $exitCode = 0
    $dumpOutput = @()

    try {
        if ($usePasswordEnv) {
            $env:MYSQL_PWD = $plainPassword
        }

        Write-Host "Backing up '$Database' from ${DbHost}:$Port ..."
        $dumpOutput = & $dumpPath @dumpArgs 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($usePasswordEnv) {
            if ($null -ne $previousMysqlPwd) {
                $env:MYSQL_PWD = $previousMysqlPwd
            }
            else {
                Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue
            }
        }
    }

    if ($exitCode -ne 0) {
        if (Test-Path -LiteralPath $backupFile) {
            Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
        }
        $details = ($dumpOutput | Out-String).Trim()
        throw "mysqldump failed with exit code $exitCode.`n$details"
    }

    if (-not (Test-Path -LiteralPath $backupFile -PathType Leaf)) {
        throw "mysqldump reported success but no file was created: $backupFile"
    }

    $backupSize = (Get-Item -LiteralPath $backupFile).Length
    if ($backupSize -lt 1024) {
        Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
        throw "The backup file is suspiciously small ($backupSize bytes) and was removed. Check MySQL credentials and privileges."
    }

    $removed = 0
    $oldBackups = Get-ChildItem -LiteralPath $backupRoot -Filter "${Database}_*.sql" -File -ErrorAction SilentlyContinue |
        Sort-Object -Property LastWriteTimeUtc -Descending |
        Select-Object -Skip $Keep

    foreach ($old in @($oldBackups)) {
        if ($null -ne $old) {
            Remove-Item -LiteralPath $old.FullName -Force
            $removed++
        }
    }

    $sizeMb = [math]::Round($backupSize / 1MB, 2)
    Write-Host ''
    Write-Host 'Backup completed successfully.' -ForegroundColor Green
    Write-Host "File   : $backupFile"
    Write-Host "Size   : $sizeMb MB"
    Write-Host "Kept   : newest $Keep backup(s)"
    if ($removed -gt 0) {
        Write-Host "Removed: $removed old backup(s)"
    }

    exit 0
}
catch {
    Write-Host ''
    Write-Host "Backup failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
