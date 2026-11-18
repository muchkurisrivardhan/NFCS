<#
.SYNOPSIS
    Opens your work apps and websites: VS Code, Outlook, Teams and a list of URLs.

.DESCRIPTION
    Launches each enabled app. Anything that is not installed is skipped with a
    status line; the script always continues to the next item.

    - VS Code: `code` on PATH, else %LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe
      (also tries the machine-wide Program Files install).
    - Outlook: classic OUTLOOK.EXE located through the App Paths registry key;
      if not found, falls back to the new Outlook (`olk.exe` alias / "ms-outlook:"
      protocol, then the "outlook:" protocol).
    - Teams: new Teams via the "msteams:" protocol.
    - URLs: each entry in $Urls is opened in your default browser.

    CUSTOMIZE
    Edit the variables inside the "==== CUSTOMIZE ====" block below:
      $OpenVSCode / $OpenOutlook / $OpenTeams / $OpenUrls   $true or $false
      $VSCodeFolder   optional folder or workspace to open in VS Code ('' = none)
      $Urls           list of web addresses (one quoted string each)
      $UrlDelayMs     pause between URLs so the browser opens them in order
#>

# ==== CUSTOMIZE ====
$OpenVSCode  = $true
$OpenOutlook = $true
$OpenTeams   = $true
$OpenUrls    = $true

$VSCodeFolder = ''      # e.g. 'C:\Projects\MyApp' (leave '' to just open VS Code)

$Urls = @(
    'https://outlook.office.com/mail/'
    'https://github.com/'
)
$UrlDelayMs = 500
# ==== END CUSTOMIZE ====

$ErrorActionPreference = 'Continue'

function Start-Safe {
    param([string]$Name, [string]$FilePath, [string[]]$ArgumentList)
    try {
        if ($ArgumentList -and $ArgumentList.Count -gt 0) {
            Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -ErrorAction Stop
        } else {
            Start-Process -FilePath $FilePath -ErrorAction Stop
        }
        Write-Output "$Name`: started."
        return $true
    }
    catch {
        Write-Output "$Name`: failed to start ($($_.Exception.Message))"
        return $false
    }
}

# ---- VS Code ----
if ($OpenVSCode) {
    try {
        $exe = $null
        $cmd = Get-Command code -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cmd) { $exe = $cmd.Source }
        if (-not $exe) {
            $candidates = @(
                (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\Code.exe'),
                (Join-Path $env:ProgramFiles 'Microsoft VS Code\Code.exe')
            )
            foreach ($c in $candidates) { if ($c -and (Test-Path -LiteralPath $c)) { $exe = $c; break } }
        }
        if ($exe) {
            $vsArgs = @()
            if ($VSCodeFolder) { $vsArgs += ('"{0}"' -f $VSCodeFolder) }
            # code.cmd is a batch file; launch via cmd so Start-Process handles it everywhere.
            if ($exe -match '\.(cmd|bat)$') {
                $null = Start-Safe 'VS Code' $env:ComSpec (@('/c', ('"{0}"' -f $exe)) + $vsArgs)
            } else {
                $null = Start-Safe 'VS Code' $exe $vsArgs
            }
        } else {
            Write-Output 'VS Code: not found, skipped.'
        }
    }
    catch { Write-Output "VS Code: error ($($_.Exception.Message))" }
} else { Write-Output 'VS Code: disabled.' }

# ---- Outlook ----
if ($OpenOutlook) {
    try {
        $started = $false
        $outlookExe = $null
        foreach ($key in @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE',
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE')) {
            try {
                $p = (Get-ItemProperty -LiteralPath $key -ErrorAction Stop).'(default)'
                if (-not $p) { $p = (Get-ItemProperty -LiteralPath $key -ErrorAction Stop).PSObject.Properties['(default)'].Value }
                if ($p -and (Test-Path -LiteralPath $p)) { $outlookExe = $p; break }
            } catch { }
        }
        if ($outlookExe) {
            $started = Start-Safe 'Outlook (classic)' $outlookExe @()
        }
        if (-not $started) {
            # New Outlook for Windows
            foreach ($proto in @('ms-outlook:', 'outlook:')) {
                if (Start-Safe "Outlook (new, $proto)" $proto @()) { $started = $true; break }
            }
        }
        if (-not $started) { Write-Output 'Outlook: not found, skipped.' }
    }
    catch { Write-Output "Outlook: error ($($_.Exception.Message))" }
} else { Write-Output 'Outlook: disabled.' }

# ---- Teams (new Teams) ----
if ($OpenTeams) {
    try {
        if (-not (Start-Safe 'Teams' 'msteams:' @())) { Write-Output 'Teams: not installed? skipped.' }
    }
    catch { Write-Output "Teams: error ($($_.Exception.Message))" }
} else { Write-Output 'Teams: disabled.' }

# ---- URLs in default browser ----
if ($OpenUrls) {
    foreach ($u in $Urls) {
        if (-not $u) { continue }
        try {
            Start-Process -FilePath $u -ErrorAction Stop
            Write-Output "URL: opened $u"
            Start-Sleep -Milliseconds $UrlDelayMs
        }
        catch { Write-Output "URL: failed to open $u ($($_.Exception.Message))" }
    }
} else { Write-Output 'URLs: disabled.' }

Write-Output 'start-work-day: done.'
