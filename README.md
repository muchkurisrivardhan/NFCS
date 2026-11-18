# NFC Desk Agent

NFC Desk Agent is a Windows 11 system-tray app that runs local scripts when you tap an NFC tag with your iPhone. The iPhone Shortcut "NFC · Send Tap to PC" sends a signed HTTP POST over your home Wi-Fi to the agent. The agent verifies the request and runs the script you mapped to that tag.

Site: https://muchkurisrivardhan.github.io/NFCS/ · Security: [SECURITY.md](SECURITY.md)

The app is C# on .NET 8: a WinForms tray UI plus an embedded Kestrel web server. It listens only on private LAN IPv4 addresses and 127.0.0.1.

## Requirements

- Windows 11 PC and an iPhone on the same Wi-Fi network (iPhone with NFC tag reading and the Shortcuts app).
- An NFC tag (any tag the iPhone can scan; it only needs to be recognized, it stores nothing the agent uses).
- To build from source: the .NET 8 SDK. The published `NFCDesk.exe` is self-contained and needs no .NET install.
- Optional: Python on PATH (`python` or `py`) if you want to run `.py` scripts.

## Build and test

```
dotnet build
dotnet test
```

## Publish

```
dotnet publish src/NfcDesk.App -c Release -r win-x64 -o publish
```

This produces `publish\NFCDesk.exe`, a single-file, self-contained executable.

## First run

1. Run `NFCDesk.exe`. A tray icon appears.
2. The app creates `%AppData%\NFCDesk\` with the starter scripts in `scripts\`.
3. The app offers to add a Windows Firewall rule (see [Firewall](#firewall)). Accept it; this takes one UAC prompt.
4. Right-click the tray icon to open the menu: Open Dashboard, Pair Phone, Pair New Tag (Learning Mode), Pause/Resume, Open Script Folder, View Log, Show Notifications, Start with Windows, Quit.

"Start with Windows" adds an entry under the HKCU Run key.

## Pairing walkthrough

You pair the phone once, then register each tag.

1. On the PC, choose **Pair Phone** from the tray menu. The window shows a 32-byte random secret as 64 hex characters, a QR code (it contains the secret hex only), and the LAN URL(s) the agent is listening on, such as `http://192.168.1.50:8765/tap`.
2. On the iPhone, open the shortcut **#50 "NFC · Send Tap to PC"** in the Shortcuts app.
3. Copy the secret into the **Secret** Text action (action 3). Paste exactly the 64 characters, with no spaces or line breaks.
4. Copy the URL into the **PC** Text action (action 2).
5. Set the **Tag** Text action (action 1) to a tag name. Allowed: lowercase letters, digits and hyphens, 1 to 64 characters, for example `desk-focus-01`.
6. Create the NFC automation: Shortcuts, **Automation**, **+**, **NFC**, **Scan**. Hold the tag to the top of the iPhone, name it, choose **Run Immediately**, and select **NFC · Send Tap to PC**.
7. On the PC, choose **Pair New Tag (Learning Mode)** from the tray menu.
8. Tap the tag with the iPhone. The agent shows: `New tag '<id>' — pick a script and a label.`
9. Pick a script from the library and enter a label. The tag is now registered.

Learning mode turns off after 2 minutes or after one tag, whichever comes first. Only a validly signed tap with an unknown tag is learned. Unsigned or invalid taps are never learned.

To use several tags, make a copy of the shortcut per tag with a different Tag value (the secret and URL stay the same), and repeat steps 6 to 9.

**Regenerate secret** in the Pair Phone window creates a new secret. The old one stops working immediately, so update the shortcut afterwards.

## What the shortcut sends

```
POST http://<pc-lan-ip>:8765/tap
{ "tag": "desk-focus-01", "ts": 1763441000, "sig": "<hex sha256>" }
```

`sig` is the lowercase hex SHA256 of `tag|ts|secret`. The request never carries a command, path or arguments. The default port is 8765.

## Scripts

The script library is `%AppData%\NFCDesk\scripts\` (menu: Open Script Folder). Starter scripts:

| Script | What it does |
|---|---|
| `start-work-day.ps1` | Opens VS Code, Outlook, Teams and a list of URLs. |
| `focus-mode.ps1` | Closes distracting apps and silences notifications or audio. |
| `lock-pc.ps1` | Locks the workstation. |
| `media-play-pause.ps1` | Sends the media play/pause key. |
| `end-of-day.ps1` | Shows a 50-second countdown, then puts the PC to sleep. |

Some starter scripts have a CUSTOMIZE section in their header comment (variables at the top to edit).

### Adding your own scripts

Put a file in the scripts folder, then map a tag to it with Pair New Tag (Learning Mode). Supported types:

| Type | How it runs |
|---|---|
| `.ps1` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <script>` |
| `.bat`, `.cmd` | `cmd.exe /d /c <script>` |
| `.exe` | Started directly. |
| `.lnk` | Launched through the shell; output is not captured. |
| `.py` | `python` or `py`, whichever is found on PATH. |

Scripts run with a hidden window, as the logged-in user, with a 60 second timeout. On timeout the agent kills only the script host process; apps the script started keep running. Output is captured to the log, capped at 64 KB. Keep long scripts under the timeout (`end-of-day.ps1` uses a 50 second countdown for this reason).

Path rules: a mapped script path must stay inside the library. These are rejected: absolute paths, `..`, `:`, the characters `& | < > ^ % "`, and any symlink or junction. The path is checked again right before the script runs.

## Testing without a phone

`tools/send-test-tap.ps1` builds a signed request the same way the shortcut does and prints the response.

```powershell
# Normal tap
.\tools\send-test-tap.ps1 -Secret <64-hex-secret>

# Bad signature (expect 403, log reason BadSignature)
.\tools\send-test-tap.ps1 -Secret <64-hex-secret> -BadSig

# Timestamp 31 s in the future (expect 403, ClockSkew)
.\tools\send-test-tap.ps1 -Secret <64-hex-secret> -TsOffset 31

# Same request twice (second is rejected, Replay)
.\tools\send-test-tap.ps1 -Secret <64-hex-secret> -Repeat 2

# Send ts as a JSON string instead of a number
.\tools\send-test-tap.ps1 -Secret <64-hex-secret> -TsAsString

# Different tag or URL
.\tools\send-test-tap.ps1 -Secret <64-hex-secret> -Tag my-tag -Url http://192.168.1.50:8765/tap
```

The test tag (default `desk-focus-01`) must be registered, or Learning Mode must be on, otherwise the agent answers 403 with reason UnknownTag. Run from the same PC, the default URL is `http://127.0.0.1:8765/tap`, which the agent also listens on.

## Firewall

On first run the app offers to add an inbound rule named "NFC Desk Agent (TCP 8765)" for the **Private** network profile only. It uses one UAC prompt and an elevated PowerShell `New-NetFirewallRule`.

The rule the app creates is limited to the exe at the path it was run from (`-Program`). If you move `NFCDesk.exe`, the rule stops matching: remove it (see Uninstall), delete `"firewallPrompted": true` from `config.json`, and restart the app so it offers to add the rule again.

To add it yourself, run in an administrator PowerShell:

```powershell
New-NetFirewallRule -DisplayName "NFC Desk Agent (TCP 8765)" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8765 -Profile Private
```

If your Wi-Fi is set to Public, the rule does not apply and the phone cannot connect. The tray tooltip warns when the network is Public. To change it: Settings, Network & internet, Wi-Fi, your network, Network profile type, **Private**.

## Troubleshooting

| Symptom | Check |
|---|---|
| iPhone says "PC didn't respond" | Is the app running (tray icon)? Are phone and PC on the same Wi-Fi? Is the network profile Private? Does the firewall rule exist? Is the IP in the shortcut still the PC's IP? DHCP can change it; reserve the address in your router. |
| Response is 403 denied | Open the log (tray: View Log). The reason is in the sixth column. See the next table. |
| Script did not run | Read the script's output in the log. Check the execution policy of your environment, and the path rules above. A script path the library rejects is logged as `Script rejected`. |
| App fails to start the server / port in use | Change `"port"` in `%AppData%\NFCDesk\config.json`, update the port in the shortcut URL, restart the app. |
| Tray tooltip says network is Public | Set the network profile to Private (see Firewall). |

Log reasons for a 403:

| Reason | Meaning and fix |
|---|---|
| `ClockSkew` | PC and phone clocks differ by more than 30 s. Sync time on both. |
| `BadSignature` | The secret in the shortcut does not match. Re-copy it with no spaces, or after Regenerate secret, update the shortcut. |
| `Replay` | The timestamp was not newer than the last accepted one for this tag. Usually a repeated or resent request. |
| `UnknownTag` | The tag is not registered. Turn on Pair New Tag (Learning Mode) and tap again. |
| `NonLanSource` | The request came from a non-private address. Typical causes: a VPN on the phone, or a guest network with client isolation. |
| `RateLimited` | More than 10 requests per second from one IP. |
| `BadFormat` | Body is not exactly the expected JSON (also: wrong method or body over 1 KB). |
| `NotPaired` | No secret is configured yet. |
| `Paused` | The agent is paused (tray: Resume). Nothing runs while paused. |

## Data locations

Everything is under `%AppData%\NFCDesk\`:

- `config.json`: tags, port, notification setting, last timestamp per tag.
- `secret.bin`: the pairing secret, encrypted with DPAPI (CurrentUser).
- `scripts\`: the script library.
- `logs\`: `nfcdesk-yyyyMMdd.log`, 14 days kept. Lines are tab-separated: time, `TAP`, source IP, tag, ok/denied, reason.

## Uninstall

1. Turn off **Start with Windows** in the tray menu first (this removes the Run key entry).
2. Choose **Quit**.
3. Delete `%AppData%\NFCDesk`.
4. Remove the firewall rule in an administrator PowerShell:
   ```powershell
   Remove-NetFirewallRule -DisplayName "NFC Desk Agent (TCP 8765)"
   ```
5. Delete `NFCDesk.exe`.

## Security

Plain HTTP on the LAN, signed requests, no TLS in v1. See [SECURITY.md](SECURITY.md) for the threat model and known limitations.
