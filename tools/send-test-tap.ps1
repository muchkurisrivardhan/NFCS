<#
.SYNOPSIS
    Simulates the iPhone Shortcut: sends a signed NFC tap to NFC Desk Agent.

.DESCRIPTION
    POSTs JSON {"tag":..., "ts":..., "sig":...} to the agent.
      ts  = Unix seconds (UTC)
      sig = lowercase hex SHA256 of UTF-8 "$Tag|$ts|$Secret"
    Prints the request body and the response status code + body. HTTP error
    responses (e.g. 403) are shown, not thrown. Works on Windows PowerShell 5.1
    and PowerShell 7 (uses System.Net.Http.HttpClient).

.PARAMETER Secret    Hex pairing secret (mandatory).
.PARAMETER Tag       Tag id. Default 'desk-focus-01'.
.PARAMETER Url       Agent endpoint. Default http://127.0.0.1:8765/tap
.PARAMETER TsOffset  Seconds added to the current time (test clock skew).
.PARAMETER Ts        Explicit timestamp; overrides the current time (test replay).
.PARAMETER TsAsString  Send ts as a JSON string instead of a number.
.PARAMETER BadSig    Flip the last hex char of the signature.
.PARAMETER Repeat    Send the identical request N times (demo replay rejection).

.EXAMPLE
    .\send-test-tap.ps1 -Secret 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
    Normal tap.
.EXAMPLE
    .\send-test-tap.ps1 -Secret <secret> -BadSig
    Bad signature (expect 403).
.EXAMPLE
    .\send-test-tap.ps1 -Secret <secret> -TsOffset 31
    Timestamp 31 s in the future (clock-skew test).
.EXAMPLE
    .\send-test-tap.ps1 -Secret <secret> -Repeat 2
    Identical request twice (second should be rejected as a replay).
.EXAMPLE
    .\send-test-tap.ps1 -Secret <secret> -TsAsString
    ts sent as a JSON string.
.EXAMPLE
    .\send-test-tap.ps1 -Secret <secret> -Url http://192.168.1.50:8765/tap
    From another PC on the LAN (use the agent PC's IP; firewall must allow it).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Secret,
    [string] $Tag = 'desk-focus-01',
    [string] $Url = 'http://127.0.0.1:8765/tap',
    [int] $TsOffset = 0,
    [long] $Ts,
    [switch] $TsAsString,
    [switch] $BadSig,
    [ValidateRange(1, 1000)] [int] $Repeat = 1
)

$ErrorActionPreference = 'Stop'

if ($PSBoundParameters.ContainsKey('Ts')) {
    $tsValue = [long]$Ts
} else {
    $tsValue = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + $TsOffset
}

$sha = [System.Security.Cryptography.SHA256]::Create()
$bytes = [System.Text.Encoding]::UTF8.GetBytes("$Tag|$tsValue|$Secret")
$sig = ([System.BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
$sha.Dispose()

if ($BadSig) {
    $last = $sig[$sig.Length - 1]
    $flipped = if ($last -eq '0') { '1' } else { '0' }
    $sig = $sig.Substring(0, $sig.Length - 1) + $flipped
}

$tagJson = $Tag.Replace('\', '\\').Replace('"', '\"')
if ($TsAsString) { $tsJson = '"' + $tsValue + '"' } else { $tsJson = [string]$tsValue }
$body = '{"tag":"' + $tagJson + '","ts":' + $tsJson + ',"sig":"' + $sig + '"}'

Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(10)

try {
    for ($i = 1; $i -le $Repeat; $i++) {
        Write-Host "--- Request $i of $Repeat : POST $Url"
        Write-Host $body
        $content = New-Object System.Net.Http.StringContent($body, [System.Text.Encoding]::UTF8, 'application/json')
        try {
            $resp = $client.PostAsync($Url, $content).GetAwaiter().GetResult()
            $respBody = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Write-Host ("Status: {0} {1}" -f [int]$resp.StatusCode, $resp.ReasonPhrase)
            Write-Host ("Body:   {0}" -f $respBody)
            $resp.Dispose()
        }
        catch {
            $m = $_.Exception.Message
            if ($_.Exception.InnerException) { $m = $_.Exception.InnerException.Message }
            Write-Host ("Request failed: {0}" -f $m)
        }
        finally {
            $content.Dispose()
        }
    }
}
finally {
    $client.Dispose()
}
