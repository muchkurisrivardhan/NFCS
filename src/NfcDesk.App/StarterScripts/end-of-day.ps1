<#
.SYNOPSIS
    End of day: shows a countdown prompt, then puts the PC to sleep.

.DESCRIPTION
    Shows a topmost "End of day" window: "Your PC will go to sleep in N seconds.
    Click Cancel to stay awake." with a live countdown and two buttons:
    "Sleep now" and "Cancel". At 0 it proceeds automatically. The PC is put to
    SLEEP (suspend), not shut down.

    Optionally closes the apps in $CloseApps (graceful close) right before sleeping.

    The NFC Desk Agent kills this script's host after 60 seconds, so the
    countdown must finish well before that. Keep $CountdownSeconds at 50 or less.

    Note: if the system has hibernate enabled but sleep disabled, or the device
    does not support sleep, Windows may hibernate or ignore the request.

    CUSTOMIZE
    Edit the "==== CUSTOMIZE ====" block:
      $CountdownSeconds   seconds before auto-sleep (max ~55; default 50)
      $CloseApps          process names (no .exe) to close before sleeping
#>

# ==== CUSTOMIZE ====
# The agent kills the script after 60 s, so keep this below that (50 leaves a safety margin).
$CountdownSeconds = 50
$CloseApps = @()          # e.g. @('onedrive','slack')
# ==== END CUSTOMIZE ====

$ErrorActionPreference = 'Continue'

if ($CountdownSeconds -gt 55) {
    Write-Output "end-of-day: CountdownSeconds $CountdownSeconds exceeds the 60 s agent timeout; using 55."
    $CountdownSeconds = 55
}
if ($CountdownSeconds -lt 1) { $CountdownSeconds = 1 }

try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()
}
catch {
    Write-Output ("end-of-day: cannot load WinForms ({0}). Aborting without sleeping." -f $_.Exception.Message)
    exit 1
}

$script:remaining = $CountdownSeconds
$script:proceed = $false

$form = New-Object System.Windows.Forms.Form
$form.Text = 'End of day'
$form.StartPosition = 'CenterScreen'
$form.TopMost = $true
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ShowInTaskbar = $true
$form.ClientSize = New-Object System.Drawing.Size(420, 170)

$label = New-Object System.Windows.Forms.Label
$label.Location = New-Object System.Drawing.Point(15, 15)
$label.Size = New-Object System.Drawing.Size(390, 70)
$label.Font = New-Object System.Drawing.Font('Segoe UI', 11)
$form.Controls.Add($label)

$btnSleep = New-Object System.Windows.Forms.Button
$btnSleep.Text = 'Sleep now'
$btnSleep.Location = New-Object System.Drawing.Point(100, 110)
$btnSleep.Size = New-Object System.Drawing.Size(100, 34)
$form.Controls.Add($btnSleep)

$btnCancel = New-Object System.Windows.Forms.Button
$btnCancel.Text = 'Cancel'
$btnCancel.Location = New-Object System.Drawing.Point(220, 110)
$btnCancel.Size = New-Object System.Drawing.Size(100, 34)
$form.Controls.Add($btnCancel)
$form.CancelButton = $btnCancel
$form.AcceptButton = $btnCancel

function Update-Label {
    $label.Text = "Your PC will go to sleep in $($script:remaining) seconds. Click Cancel to stay awake."
}
Update-Label

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000
$timer.Add_Tick({
    $script:remaining--
    if ($script:remaining -le 0) {
        $timer.Stop()
        $script:proceed = $true
        $form.Close()
    } else {
        Update-Label
    }
})

$btnSleep.Add_Click({ $timer.Stop(); $script:proceed = $true; $form.Close() })
$btnCancel.Add_Click({ $timer.Stop(); $script:proceed = $false; $form.Close() })
# Closing via the X button counts as cancel unless already proceeding.
$form.Add_FormClosing({ $timer.Stop() })
$form.Add_Shown({ $form.Activate(); $timer.Start() })

try {
    [void]$form.ShowDialog()
}
catch {
    Write-Output ("end-of-day: UI failed ({0}). Aborting without sleeping." -f $_.Exception.Message)
    exit 1
}
finally {
    $timer.Dispose()
    $form.Dispose()
}

if (-not $script:proceed) {
    Write-Output 'end-of-day: cancelled by user, staying awake.'
    exit 0
}

foreach ($name in $CloseApps) {
    if (-not $name) { continue }
    $n = $name -replace '\.exe$', ''
    try {
        $procs = @(Get-Process -Name $n -ErrorAction SilentlyContinue)
        foreach ($p in $procs) { try { $null = $p.CloseMainWindow() } catch { } }
        if ($procs.Count -gt 0) { Write-Output "${n}: asked to close." }
    }
    catch { Write-Output "${n}: error ($($_.Exception.Message))" }
}
if ($CloseApps.Count -gt 0) { Start-Sleep -Seconds 2 }

Write-Output 'end-of-day: going to sleep now.'
try {
    [void][System.Windows.Forms.Application]::SetSuspendState('Suspend', $false, $false)
}
catch {
    Write-Output ("end-of-day: sleep failed ({0})" -f $_.Exception.Message)
}
