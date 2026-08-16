[CmdletBinding()]
param(
    [string]$InstallRoot,
    [switch]$Quiet,
    [switch]$RemoveManaged
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Windows.Forms

$productId = "deepseek-harness-keypad"
$markerName = ".deepseek-harness-keypad.install.json"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepseekHarnessKeypad"
$managedDistribution = "CodexMicro-DeepSeek"
$installDirectoryName = "Deepseek Harness Keypad"

function Resolve-ValidatedInstallRoot([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $localAppData = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw "The current user's LocalAppData directory is unavailable."
    }
    $expected = [System.IO.Path]::GetFullPath(
        (Join-Path $localAppData "Programs\$installDirectoryName"))
    if (-not [string]::Equals(
            $fullPath,
            $expected,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to uninstall from an unexpected directory: $fullPath"
    }
    return $fullPath
}

function Show-Error([string]$Message) {
    [System.Windows.Forms.MessageBox]::Show(
        $Message,
        "Deepseek Harness Keypad",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
}

try {
    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        $sourceRoot = Resolve-ValidatedInstallRoot $PSScriptRoot
        $tempScript = Join-Path `
            ([System.IO.Path]::GetTempPath()) `
            ("DeepseekHarnessKeypad-Uninstall-{0}.ps1" -f `
                [Guid]::NewGuid().ToString("N"))
        Copy-Item -LiteralPath $PSCommandPath -Destination $tempScript
        $windowsPowerShell = Join-Path $env:WINDIR `
            "System32\WindowsPowerShell\v1.0\powershell.exe"
        $launchArguments =
            "-NoProfile -ExecutionPolicy Bypass -File `"$tempScript`" " +
            "-InstallRoot `"$sourceRoot`""
        if ($Quiet) {
            $launchArguments += " -Quiet"
        }
        if ($RemoveManaged) {
            $launchArguments += " -RemoveManaged"
        }
        Start-Process `
            -FilePath $windowsPowerShell `
            -ArgumentList $launchArguments `
            -WindowStyle Hidden
        exit 0
    }

    $InstallRoot = Resolve-ValidatedInstallRoot $InstallRoot
    $markerPath = Join-Path $InstallRoot $markerName
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Refusing to uninstall: the install directory has no product marker."
    }
    $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    if ($marker.productId -ne $productId) {
        throw "Refusing to uninstall: the install directory belongs to another product."
    }

    if (-not $Quiet) {
        $choice = [System.Windows.Forms.MessageBox]::Show(
            "Uninstall Deepseek Harness Keypad?`n`nDSH sessions, settings, and the managed WSL environment are preserved by default.",
            "Uninstall Deepseek Harness Keypad",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
        if ($choice -ne [System.Windows.Forms.DialogResult]::Yes) {
            exit 0
        }
    }

    $removeManagedData = if ($Quiet) {
        $RemoveManaged.IsPresent
    }
    else {
        [System.Windows.Forms.MessageBox]::Show(
            "Permanently delete the managed DSH WSL environment too?`n`nChoosing Yes deletes its Harness sessions. Other Ubuntu distributions and external DSH installations are not affected.",
            "Delete managed DSH data?",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button2) -eq `
            [System.Windows.Forms.DialogResult]::Yes
    }

    $executable = Join-Path $InstallRoot "CodexMicro.exe"
    Get-CimInstance Win32_Process |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
            [string]::Equals(
                [System.IO.Path]::GetFullPath($_.ExecutablePath),
                $executable,
                [System.StringComparison]::OrdinalIgnoreCase)
        } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }

    if (Test-Path -LiteralPath $runKey) {
        $runProperties = Get-ItemProperty `
            -LiteralPath $runKey `
            -ErrorAction SilentlyContinue
        $runValue = if ($null -ne $runProperties -and
            $runProperties.PSObject.Properties.Name -contains "CodexMicroKeypad") {
            $runProperties.CodexMicroKeypad
        }
        else {
            $null
        }
        if ($runValue -is [string] -and
            $runValue.IndexOf(
                $executable,
                [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Remove-ItemProperty `
                -LiteralPath $runKey `
                -Name "CodexMicroKeypad" `
                -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
    $shortcut = Join-Path `
        ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) `
        "Programs\Deepseek Harness Keypad.lnk"
    Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue

    if ($removeManagedData) {
        $wsl = Join-Path $env:WINDIR "System32\wsl.exe"
        if (Test-Path -LiteralPath $wsl -PathType Leaf) {
            $names = (& $wsl --list --quiet | Out-String) -replace "`0", ""
            if (@($names -split "`r?`n" | ForEach-Object { $_.Trim() }) -contains `
                $managedDistribution) {
                & $wsl --terminate $managedDistribution 2>$null
                & $wsl --unregister $managedDistribution
                if ($LASTEXITCODE -ne 0) {
                    throw "The managed DSH WSL environment could not be unregistered. Application files were not deleted."
                }
            }
        }
    }

    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
    if (-not $Quiet) {
        [System.Windows.Forms.MessageBox]::Show(
            $(if ($removeManagedData) {
                "Deepseek Harness Keypad and its managed DSH environment were removed."
            }
            else {
                "Deepseek Harness Keypad was removed. DSH sessions, settings, and the managed WSL environment were preserved."
            }),
            "Uninstall complete",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
    }
}
catch {
    if (-not $Quiet) {
        Show-Error "Uninstall failed: $($_.Exception.Message)"
    }
    else {
        Write-Error "Uninstall failed: $($_.Exception.Message)"
    }
    exit 1
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($InstallRoot) -and
        $PSCommandPath.StartsWith(
            [System.IO.Path]::GetTempPath(),
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
    }
}
