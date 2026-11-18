<#
.SYNOPSIS
    Locks the Windows workstation.

.DESCRIPTION
    Calls user32!LockWorkStation (same as Win+L). If that call fails, falls
    back to "rundll32.exe user32.dll,LockWorkStation".
    Runs as the logged-in user, so it locks that user's interactive session.

    CUSTOMIZE
    Nothing to edit. Assign this script to an NFC tag as-is.
#>

# ==== CUSTOMIZE ====
# (nothing to customize)
# ==== END CUSTOMIZE ====

$ErrorActionPreference = 'Stop'

$locked = $false
try {
    if (-not ('NfcDesk.LockNative' -as [type])) {
        Add-Type -Namespace NfcDesk -Name LockNative -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
public static extern bool LockWorkStation();
'@
    }
    $locked = [NfcDesk.LockNative]::LockWorkStation()
    if ($locked) { Write-Output 'lock-pc: workstation lock requested (LockWorkStation).' }
    else { Write-Output 'lock-pc: LockWorkStation returned false.' }
}
catch {
    Write-Output ("lock-pc: LockWorkStation failed: " + $_.Exception.Message)
}

if (-not $locked) {
    try {
        Start-Process -FilePath 'rundll32.exe' -ArgumentList 'user32.dll,LockWorkStation' -WindowStyle Hidden
        Write-Output 'lock-pc: fallback rundll32 lock requested.'
    }
    catch {
        Write-Output ("lock-pc: fallback failed: " + $_.Exception.Message)
    }
}
