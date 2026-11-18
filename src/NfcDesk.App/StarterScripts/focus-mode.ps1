<#
.SYNOPSIS
    Focus mode: closes distracting apps and silences notifications / audio.

.DESCRIPTION
    1. Closes every running process whose name is in $DistractingApps. It first
       asks politely (CloseMainWindow), waits $GraceSeconds, then force-kills
       whatever is still running.
    2. Silences the PC according to $Mode:
         'DndThenMute'  try Do-Not-Disturb; if that fails, mute system audio
         'DndOnly'      try Do-Not-Disturb only
         'MuteOnly'     mute system audio only

    ABOUT DO NOT DISTURB (best effort)
    Windows 11 has no supported public API to toggle Do Not Disturb. This script
    sets HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings
    value NOC_GLOBAL_SETTING_TOASTS_ENABLED = 0 (DWORD), which suppresses toast
    banners. It may not update the Focus/Do-Not-Disturb toggle in the UI, and
    some Windows builds may ignore it. To revert, set that value back to 1
    (or delete it), e.g.:
      Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings' NOC_GLOBAL_SETTING_TOASTS_ENABLED 1
    This script does NOT turn notifications back on by itself.

    ABOUT MUTE
    Sends VK_VOLUME_MUTE (0xAD) via user32!keybd_event. That key TOGGLES mute,
    so running the script while already muted will unmute.

    CUSTOMIZE
    Edit the "==== CUSTOMIZE ====" block: $DistractingApps (process names
    without .exe, see Task Manager > Details), $GraceSeconds, and $Mode.
#>

# ==== CUSTOMIZE ====
$DistractingApps = @(
    'discord',
    'spotify',
    'steam',
    'epicgameslauncher',
    'whatsapp',
    'telegram'
)
$GraceSeconds = 3                 # time to let apps close themselves before force-kill
$Mode = 'DndThenMute'             # 'DndThenMute' | 'MuteOnly' | 'DndOnly'
# ==== END CUSTOMIZE ====

$ErrorActionPreference = 'Continue'

# ---- 1. Close distracting apps ----
$targets = @()
foreach ($name in $DistractingApps) {
    if (-not $name) { continue }
    $n = $name -replace '\.exe$', ''
    try {
        $procs = @(Get-Process -Name $n -ErrorAction SilentlyContinue)
        if ($procs.Count -eq 0) { Write-Output "${n}: not running." }
        foreach ($p in $procs) {
            try { $null = $p.CloseMainWindow() } catch { }
            $targets += $p
        }
    }
    catch { Write-Output "${n}: error ($($_.Exception.Message))" }
}

if ($targets.Count -gt 0) {
    Start-Sleep -Seconds $GraceSeconds
    foreach ($p in $targets) {
        try {
            $p.Refresh()
            if (-not $p.HasExited) {
                Stop-Process -Id $p.Id -Force -ErrorAction Stop
                Write-Output ("{0} (pid {1}): force closed." -f $p.ProcessName, $p.Id)
            } else {
                Write-Output ("{0} (pid {1}): closed." -f $p.ProcessName, $p.Id)
            }
        }
        catch { Write-Output ("{0} (pid {1}): could not close ({2})" -f $p.ProcessName, $p.Id, $_.Exception.Message) }
    }
}

# ---- 2. Do Not Disturb / mute ----
function Set-DoNotDisturb {
    try {
        $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
        if (-not (Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
        New-ItemProperty -Path $key -Name 'NOC_GLOBAL_SETTING_TOASTS_ENABLED' -Value 0 -PropertyType DWord -Force -ErrorAction Stop | Out-Null
        $v = (Get-ItemProperty -Path $key -Name 'NOC_GLOBAL_SETTING_TOASTS_ENABLED' -ErrorAction Stop).NOC_GLOBAL_SETTING_TOASTS_ENABLED
        if ($v -eq 0) {
            Write-Output 'DND: toast banners disabled (registry, best effort).'
            return $true
        }
        Write-Output 'DND: registry value did not stick.'
        return $false
    }
    catch {
        Write-Output ("DND: failed ({0})" -f $_.Exception.Message)
        return $false
    }
}

function Send-MuteKey {
    try {
        if (-not ('NfcDesk.FocusKeyNative' -as [type])) {
            Add-Type -Namespace NfcDesk -Name FocusKeyNative -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, System.UIntPtr dwExtraInfo);
'@
        }
        $VK_VOLUME_MUTE = [byte]0xAD
        [NfcDesk.FocusKeyNative]::keybd_event($VK_VOLUME_MUTE, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 50
        [NfcDesk.FocusKeyNative]::keybd_event($VK_VOLUME_MUTE, 0, 2, [UIntPtr]::Zero)
        Write-Output 'Mute: volume-mute key sent (toggles mute).'
        return $true
    }
    catch {
        Write-Output ("Mute: failed ({0})" -f $_.Exception.Message)
        return $false
    }
}

switch ($Mode) {
    'DndThenMute' {
        if (-not (Set-DoNotDisturb)) { $null = Send-MuteKey }
    }
    'DndOnly'  { $null = Set-DoNotDisturb }
    'MuteOnly' { $null = Send-MuteKey }
    default    { Write-Output "Unknown `$Mode '$Mode' (use DndThenMute, MuteOnly or DndOnly)." }
}

Write-Output 'focus-mode: done.'
