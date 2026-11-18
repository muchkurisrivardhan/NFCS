# Security

## Scope

NFC Desk Agent accepts signed HTTP requests from devices on your home LAN and runs scripts from a local folder. This document covers that path: the network endpoint, the signature protocol, the secret storage and the script launcher. It does not cover the security of your iPhone, your router or the scripts you write.

## Threat model

| Threat | Control | What it stops / residual risk |
|---|---|---|
| LAN attacker forges a tap | Every request must carry `SHA256(tag\|ts\|secret)`. Sources outside 10/8, 172.16/12, 192.168/16 and loopback are rejected. The firewall rule covers the Private profile only. | Stops anyone without the secret. Anyone who has the secret can trigger any registered tag. |
| Sniffing | The request holds the tag name, timestamp and signature, never the secret. | An observer on the LAN learns tag names and tap timing. Plain HTTP, no TLS in v1. |
| Replay | Timestamp must be within 30 s of PC time, and greater than the last accepted timestamp for that tag. The last timestamp is persisted across restarts and kept even after a tag is removed. | Stops resending a captured request. A forged request still needs the secret. |
| Cloned or copied NFC tag | The tag holds only a name. The phone's secret authorizes the tap. | A cloned tag gives an attacker nothing. Someone with your unlocked phone can run the shortcut; that is out of scope. |
| Path traversal, command injection | The request never carries a command, path or arguments. Scripts run only from the library. Absolute paths, `..`, `:`, the characters `& \| < > ^ % "` and symlinks/junctions are rejected, and the path is re-resolved right before running. | A request cannot choose what runs; it can only pick a registered tag. |
| Brute force, DoS | 10 requests per second per IP, 1 KB body cap, a generic `403 {"status":"denied"}` with no detail. | Slows guessing and floods. The signature is 256 bits and compared in constant time. A host on the LAN can still consume some CPU. |
| Secret at rest | `secret.bin` is encrypted with DPAPI for the current Windows user. | Other users and offline disk copies cannot read it. Any process running as you can. |
| Log injection | CR, LF, tab and other control characters are replaced in log fields. The secret and signatures are never logged. | The log cannot be forged with fake lines. |

## Why SHA256(tag|ts|secret) and not HMAC

Shortcuts on iOS has hash actions but no HMAC action. The secret is placed last in the hashed string, which avoids the length-extension weakness of `SHA256(secret + message)`. Strict formats for `tag` (`^[a-z0-9-]{1,64}$`) and `ts` (digits only, no leading zeros) mean the `|` separator cannot be abused to shift fields. A later version can move to HMAC.

## Known limitations

- No TLS in v1. Traffic is plain HTTP.
- Anyone on the LAN can see tag names and when they are tapped.
- Anyone who holds the secret can trigger any registered tag.
- Scripts run with your user rights, hidden, with a 60 s timeout. A script that is unsafe stays unsafe.
- Regenerating the secret is the only revocation; it invalidates every paired phone at once.

## Reporting a vulnerability

Open a private security advisory on the repository: https://github.com/muchkurisrivardhan/NFCS/security/advisories/new. Please do not file public issues for vulnerabilities.
