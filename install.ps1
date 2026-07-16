# DevHub Quick Installer
# Simple, interactive, and automatic installation of DevHub (certificate + MSIX package).
#
# Usage (run in PowerShell):
#   irm https://raw.githubusercontent.com/AymericChaverot/devhub/main/install.ps1 | iex

param(
    [string]$CerPath,
    [string]$MsixPath
)

$ErrorActionPreference = "Stop"

# Helper for drawing headers
function Write-Header {
    param([string]$Text)
    Write-Host ""
    Write-Host " ╔$([string]::new('═', $Text.Length + 4))╗" -ForegroundColor Cyan
    Write-Host " ║  $Text  ║" -ForegroundColor Cyan
    Write-Host " ╚$([string]::new('═', $Text.Length + 4))╝" -ForegroundColor Cyan
    Write-Host ""
}

# Helper for standard status messages
function Write-Success { param([string]$Text) Write-Host "  [SUCCESS] $Text" -ForegroundColor Green }
function Write-Info    { param([string]$Text) Write-Host "  [INFO]    $Text" -ForegroundColor Cyan }
function Write-Warning { param([string]$Text) Write-Host "  [WARN]    $Text" -ForegroundColor Yellow }
function Write-ErrorMsg { param([string]$Text) Write-Host "  [ERROR]   $Text" -ForegroundColor Red }

# Check current admin status
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$isElevatedMode = -not [string]::IsNullOrEmpty($CerPath) -and -not [string]::IsNullOrEmpty($MsixPath)

# --- ELEVATED HELPER MODE ---
# This mode is called by the parent script to perform the admin operations (trusting cert + installing MSIX)
if ($isElevatedMode) {
    if (-not $isAdmin) {
        Write-ErrorMsg "Elevated helper mode requires administrator privileges."
        Write-Host ""
        Read-Host "Press Enter to close..."
        exit 1
    }
    
    Write-Header "DevHub Installer (Elevated)"
    
    # 1. Trust Certificate
    Write-Info "Trusting package signing certificate..."
    try {
        Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Write-Success "Certificate successfully added to LocalMachine\TrustedPeople."
    } catch {
        Write-ErrorMsg "Failed to import certificate: $_"
        Write-Host ""
        Read-Host "Press Enter to close..."
        exit 1
    }
    
    # 2. Register Package
    Write-Info "Registering DevHub application package..."
    try {
        Unblock-File -Path $MsixPath
        Add-AppxPackage -Path $MsixPath
        Write-Success "DevHub has been installed successfully!"
    } catch {
        Write-ErrorMsg "Installation failed: $_"
        Write-Host ""
        Read-Host "Press Enter to close..."
        exit 1
    }
    
    # 3. Prompt to run
    Write-Host ""
    $runNow = Read-Host "  Launch DevHub now? (Y/n)"
    if ($runNow -eq "" -or $runNow -like "y*") {
        $package = Get-AppxPackage -Name "DevHub"
        if ($package) {
            Write-Info "Launching DevHub..."
            Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
        }
    }
    
    Write-Host ""
    Write-Success "All tasks completed!"
    Read-Host "Press Enter to close..."
    exit 0
}

# --- STANDARD USER MODE / MAIN WORKFLOW ---

Write-Header "DevHub Installer"

# Warn the user of what the script does and ask for permission
Write-Host "This script will download and install the latest version of DevHub." -ForegroundColor Gray
Write-Host "It will perform the following actions:" -ForegroundColor Gray
Write-Host "  1. Verify system requirements." -ForegroundColor Gray
Write-Host "  2. Query GitHub for the latest release." -ForegroundColor Gray
Write-Host "  3. Check if the certificate is already trusted (avoids Admin elevation if possible)." -ForegroundColor Gray
Write-Host "  4. Register the certificate & install the package." -ForegroundColor Gray
Write-Host ""

$proceed = Read-Host "Do you want to proceed with the installation? (Y/n)"
if ($proceed -ne "" -and $proceed -notlike "y*") {
    Write-Info "Installation cancelled by the user."
    exit 0
}

# 1. Verify system compatibility
Write-Info "Verifying system requirements..."
try {
    $os = Get-CimInstance Win32_OperatingSystem
    Write-Success "OS: $($os.Caption) (Version $($os.Version))"
} catch {
    Write-Warning "Could not retrieve operating system version details."
}

$winget = Get-Command winget -ErrorAction SilentlyContinue
if ($winget) {
    Write-Success "Winget is available."
} else {
    Write-Warning "Winget is missing. DevHub's tool installation catalog will not function."
}

# 2. Fetch latest release from GitHub API
Write-Info "Fetching latest release details from GitHub..."
$apiURL = "https://api.github.com/repos/AymericChaverot/devhub/releases/latest"
try {
    $release = Invoke-RestMethod -Uri $apiURL -UseBasicParsing
    $latestVerStr = $release.tag_name
    $latestVer = $latestVerStr.TrimStart('v')
    Write-Success "Latest version available: $latestVerStr"
} catch {
    Write-ErrorMsg "Failed to reach GitHub API: $_"
    Write-Warning "Please check your internet connection and try again."
    Write-Host ""
    Read-Host "Press Enter to exit..."
    exit 1
}

# 3. Check if DevHub is already installed and up-to-date
$existing = Get-AppxPackage -Name "DevHub"
$forceInstall = $false
if ($existing) {
    $installedVer = $existing.Version
    Write-Info "DevHub is already installed (Installed: v$installedVer, Latest: $latestVerStr)"
    
    try {
        if ([version]$installedVer -ge [version]$latestVer) {
            Write-Success "DevHub is already up-to-date (v$installedVer)."
            $choice = Read-Host "  Would you like to reinstall/downgrade anyway? (y/N)"
            if ($choice -eq "" -or $choice -notlike "y*") {
                Write-Info "Nothing to do. Exiting."
                exit 0
            }
            $forceInstall = $true
        }
    } catch {
        Write-Warning "Could not compare versions cleanly."
    }
}

# 4. Check for local files first
$useLocal = $false
$localCer = Get-ChildItem -Path . -Filter "*.cer" | Where-Object { $_.Name -like "*signing*" -or $_.Name -like "*DevHub*" } | Select-Object -First 1
$localMsix = Get-ChildItem -Path . -Filter "*.msix" | Where-Object { $_.Name -like "*DevHub*" } | Select-Object -First 1

if ($localCer -and $localMsix) {
    Write-Info "Found local installation files in current directory:"
    Write-Host "  - Certificate: $($localCer.Name)" -ForegroundColor Gray
    Write-Host "  - Package:     $($localMsix.Name)" -ForegroundColor Gray
    
    $choice = Read-Host "  Use these local files instead of downloading? (Y/n)"
    if ($choice -eq "" -or $choice -like "y*") {
        $useLocal = $true
        $cerPath = $localCer.FullName
        $msixPath = $localMsix.FullName
    }
}

$tempDir = $null
if (-not $useLocal) {
    # Setup temp folder
    $tempDir = Join-Path $env:TEMP "DevHubInstall-$([Guid]::NewGuid().ToString().Substring(0,8))"
    $null = New-Item -ItemType Directory -Path $tempDir -Force
    
    $msixAsset = $release.assets | Where-Object { $_.name -like "*.msix" } | Select-Object -First 1
    $cerAsset = $release.assets | Where-Object { $_.name -like "*.cer" } | Select-Object -First 1
    
    if ($null -eq $msixAsset -or $null -eq $cerAsset) {
        Write-ErrorMsg "Failed to locate .msix or .cer in the latest release assets."
        Write-Host ""
        Read-Host "Press Enter to exit..."
        exit 1
    }
    
    $cerPath = Join-Path $tempDir $cerAsset.name
    $msixPath = Join-Path $tempDir $msixAsset.name
    
    try {
        Write-Info "Downloading certificate: $($cerAsset.name)..."
        Invoke-WebRequest -Uri $cerAsset.browser_download_url -OutFile $cerPath -UseBasicParsing
        
        Write-Info "Downloading package: $($msixAsset.name)..."
        Invoke-WebRequest -Uri $msixAsset.browser_download_url -OutFile $msixPath -UseBasicParsing
        
        Write-Success "Downloads completed."
    } catch {
        Write-ErrorMsg "Failed to download files: $_"
        if ($localCer -and $localMsix) {
            Write-Warning "Attempting fallback to local files..."
            $useLocal = $true
            $cerPath = $localCer.FullName
            $msixPath = $localMsix.FullName
        } else {
            Write-Host ""
            Read-Host "Press Enter to exit..."
            exit 1
        }
    }
}

# 5. Check if certificate is already trusted
Write-Info "Checking certificate trust status..."
$needElevation = $true
try {
    $certObj = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
    $store.Open("ReadOnly")
    $isTrusted = $store.Certificates | Where-Object { $_.Thumbprint -eq $certObj.Thumbprint }
    $store.Close()
    
    if ($isTrusted) {
        Write-Success "Certificate is already trusted on this machine."
        $needElevation = $false
    } else {
        Write-Warning "The package signing certificate is not yet trusted."
    }
} catch {
    Write-Warning "Could not inspect certificate trust status. Assuming elevation is required."
}

# Clean up temp folder wrapper
function Cleanup {
    if ($null -ne $tempDir -and (Test-Path $tempDir)) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 6. Trust certificate & Register Package
if ($needElevation) {
    if (-not $isAdmin) {
        Write-Host ""
        Write-Warning "The DevHub certificate is not trusted yet."
        Write-Host "  To trust this certificate, the script needs Administrator privileges." -ForegroundColor Gray
        $choice = Read-Host "  Would you like to elevate to Administrator to complete the installation? (Y/n)"
        if ($choice -ne "" -and $choice -notlike "y*") {
            Write-ErrorMsg "Installation aborted. The signing certificate must be trusted to install DevHub."
            Cleanup
            Write-Host ""
            Read-Host "Press Enter to exit..."
            exit 1
        }
        
        $scriptPath = $MyInvocation.MyCommand.Path
        if ($null -eq $scriptPath -or $scriptPath -eq "" -or -not (Test-Path -Path $scriptPath -PathType Leaf)) {
            $scriptPath = Join-Path $env:TEMP "install-devhub.ps1"
            try {
                $url = "https://raw.githubusercontent.com/AymericChaverot/devhub/main/install.ps1"
                Invoke-WebRequest -Uri $url -OutFile $scriptPath -UseBasicParsing
            } catch {
                Write-ErrorMsg "Failed to save script for elevation: $_"
                Cleanup
                Write-Host ""
                Read-Host "Press Enter to exit..."
                exit 1
            }
        }
        
        # Launch elevated helper
        try {
            Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -CerPath `"$cerPath`" -MsixPath `"$msixPath`"" -Verb RunAs -Wait
            Cleanup
            exit 0
        } catch {
            Write-ErrorMsg "Elevation failed: $_"
            Cleanup
            Write-Host ""
            Read-Host "Press Enter to exit..."
            exit 1
        }
    } else {
        # Already running as admin, import directly
        Write-Info "Importing certificate..."
        try {
            Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
            Write-Success "Certificate trusted."
        } catch {
            Write-ErrorMsg "Failed to import certificate: $_"
            Cleanup
            Write-Host ""
            Read-Host "Press Enter to exit..."
            exit 1
        }
    }
}

# 7. Register package (Standard/User mode install)
Write-Info "Registering application package..."
try {
    Unblock-File -Path $msixPath
    Add-AppxPackage -Path $msixPath
    Write-Success "DevHub has been installed successfully!"
} catch {
    Write-ErrorMsg "Installation failed: $_"
    Cleanup
    Write-Host ""
    Read-Host "Press Enter to exit..."
    exit 1
}

# Cleanup downloaded files
Cleanup

# 8. Prompt to run
Write-Host ""
$runNow = Read-Host "  Launch DevHub now? (Y/n)"
if ($runNow -eq "" -or $runNow -like "y*") {
    $package = Get-AppxPackage -Name "DevHub"
    if ($package) {
        Write-Info "Launching DevHub..."
        Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
    }
}

Write-Host ""
Write-Success "All tasks completed! Thanks for installing DevHub."
Write-Host ""
Read-Host "Press Enter to close..."
exit 0
