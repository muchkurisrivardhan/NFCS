<#
.SYNOPSIS
    Toggles media play/pause for whatever app currently owns media keys.

.DESCRIPTION
    Sends the VK_MEDIA_PLAY_PAUSE (0xB3) key (key down, then key up) via
    user32!keybd_event. Works with Spotify, browsers, Media Player, etc.

    CUSTOMIZE
    Nothing to edit. Assign this script to an NFC tag as-is.
#>

# ==== CUSTOMIZE ====
# (nothing to customize)
# ==== END CUSTOMIZE ====

try {
    if (-not ('NfcDesk.MediaKeyNative' -as [type])) {
        Add-Type -Namespace NfcDesk -Name MediaKeyNative -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, System.UIntPtr dwExtraInfo);
'@
    }
    $VK_MEDIA_PLAY_PAUSE = [byte]0xB3
    $KEYEVENTF_KEYUP = [uint32]0x0002
    [NfcDesk.MediaKeyNative]::keybd_event($VK_MEDIA_PLAY_PAUSE, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [NfcDesk.MediaKeyNative]::keybd_event($VK_MEDIA_PLAY_PAUSE, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
    Write-Output 'media-play-pause: play/pause key sent.'
}
catch {
    Write-Output ("media-play-pause: failed: " + $_.Exception.Message)
}
