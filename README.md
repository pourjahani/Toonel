# Toonel

A Windows client that combines **V2Ray/Xray** (VLESS, VMess, Trojan, Shadowsocks) with
**Psiphon** — chainable in both directions — with whole-system tunneling (TUN) and
Proxifier-style per-app split tunneling.

Built with WPF on .NET 8. Inspired by v2rayN, PattNG, MahsaNG and the Psiphon client.

## Features

- **V2Ray client**: profiles for VLESS / VMess / Trojan / Shadowsocks / SOCKS / HTTP,
  plus **Hysteria2** (`hysteria2://`/`hy2://`), **TUIC** and **WireGuard**. Import share
  links from clipboard or file (including `wireguard://` URLs and WireGuard `.conf`
  files); with Hysteria2/TUIC selected, sing-box hosts the local inbounds natively and
  xray is skipped. TCP ping per profile.
- **Subscriptions**: paste a subscription URL or base64 blob and it keeps itself fresh
  (auto-update on start plus every N hours). Parses base64 / plain link lists, **Clash
  YAML** (flow + block style; ss/trojan/vmess/vless) and **sing-box JSON**; shows the
  server's **traffic used / expiry** from `Subscription-Userinfo`; a re-update only
  replaces that subscription's profiles.
- **DNS choice + leak test**: pick automatic / 1.1.1.1 / 8.8.8.8 / 9.9.9.9 (UDP or DoH) —
  applied to both the Xray and sing-box configs — and run **Check IP** on the dashboard to
  compare your direct IP vs the proxied one and flag a leak.
- **Psiphon (official engine)**: runs the official `psiphon-tunnel-core` standalone client
  and exposes its local SOCKS/HTTP proxies. Settings for egress region, sponsor/propagation
  IDs, remote server list, and full custom `client.config` JSON overrides.
- **Four connection modes** (one-click switch):
  | Mode | Path |
  |---|---|
  | V2Ray only | apps → xray → your VLESS/VMess/Trojan server |
  | Psiphon alone | apps → xray router → Psiphon network |
  | **Psiphon after V2Ray** | apps → xray → Psiphon → internet *(Psiphon dials through your vless line via its official `UpstreamProxyURL`)* |
  | **Psiphon before V2Ray** | apps → xray → vless → internet *(the vless connection itself is dialed through Psiphon via xray `dialerProxy`)* |
  | **V2Ray over WARP** | apps → xray → vless → internet *(the vless connection itself is dialed through a Cloudflare WARP hop via a native Xray `wireguard` outbound + `dialerProxy`)* |
- **Whole-system traffic**: sing-box TUN sidecar (wintun adapter, auto-route) captures
  everything — terminals, git, pip, npm, Docker. Fallback: classic Windows system proxy
  (WinINET) plus generated terminal env snippets (`HTTP_PROXY`, `git`, `npm`, `pip`).
- **Split tunneling**: per-application rules at the TUN layer —
  *Include* (only listed apps tunnel) / *Exclude* (listed apps go direct), with
  always-direct and always-proxy exceptions by domain suffix, IP/CIDR and port range,
  plus automatic LAN/private-IP bypass.
- Per-engine live logs (xray / psiphon / sing-box), tray icon, minimize-to-tray,
  auto-connect on start, configurable ports.

## Build & run

**Easy way — single exe (no .NET install needed):**

```
build-exe.cmd          (or: dotnet publish src/Toonel.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist)
```

Then just run **`dist\Toonel.exe`**. It requests admin once at launch (UAC) — that's
what lets TUN mode capture the whole system, and it guarantees every engine is a killable
child process.

**Quitting resets everything:** closing the window (or Exit in the tray menu) stops xray,
Psiphon and sing-box, tears down the TUN adapter, and restores the Windows proxy setting.
The same cleanup runs on crash, logoff/shutdown, and on the next launch after an unclean
exit (stale engines are killed, our proxy setting reverted). Turn off "Minimize to tray"
(close = quit, the default) or use the tray Exit.

**Dev way:**

```
dotnet build Toonel.sln
dotnet run --project src/Toonel.App
```

On first connect the app offers to download the pinned engine binaries into the `cores/` folder next to the exe (or `src/Toonel.App/bin/...` in dev runs):

- `xray.exe` — [XTLS/Xray-core](https://github.com/XTLS/Xray-core) `v25.8.29`
- `sing-box.exe` + `wintun.dll` — [SagerNet/sing-box](https://github.com/SagerNet/sing-box) `v1.14.2`
- `psiphon-core.exe` — official [psiphon-tunnel-core-binaries](https://github.com/Psiphon-Labs/psiphon-tunnel-core-binaries) standalone client

You can drop your own binaries there instead; only missing files are fetched.

## How the chains work (no loops)

```
mode C (psiphon after v2ray):
  apps ──► xray :10808 ──► psiphon socks :10811 ──► psiphon servers
                                              └─(psiphon dials via UpstreamProxyURL)─► xray :10809 ──► vless out

mode D (psiphon before v2ray):
  apps ──► xray :10808 ──► vless out ─(dialerProxy)─► psiphon socks :10811 ──► psiphon servers
```

- In mode C, Psiphon's upstream traffic enters a **dedicated xray inbound (:10809)** that
  routes only to the VLESS outbound — it can never loop back into Psiphon.
- In TUN mode, the engine binaries themselves are pinned to `direct` by process name, so
  the tunnel never captures its own traffic. QUIC (UDP 443) is rejected in Psiphon modes
  because Psiphon's local proxy is TCP-only.

## Split tunneling

Edit on the **Split Tunnel** page (applies on next connect):

- One executable name per line (`chrome.exe`).
- **Include**: only listed apps are tunneled. **Exclude**: listed apps go direct.
- Exceptions — `bank.ir` (domain suffix), `10.0.0.0/8` (CIDR), `80` / `1000-2000` (ports)
  — are honored regardless of the app list.

Per-app routing requires TUN mode (admin/UAC), same as Proxifier itself.

## Tests

```
dotnet run --project tests/Toonel.Tests
```

140 checks covering config generation for every mode, anti-loop invariants, split
tunnel rule ordering, Psiphon config merging, all share-link parsers (both directions),
Clash YAML / sing-box JSON subscription parsing, and the leak tester.

## Honest limitations

- Psiphon chain modes are TCP-only (QUIC is blocked by design in those modes).
- The bundled Psiphon binary uses the official embedded defaults; the remote server list
  URL and sponsor/propagation IDs are configurable, but only values from a trusted source
  (e.g. a Psiphon partner distribution) will work.
- Unsigned binaries may trigger antivirus heuristics (TUN adapter + elevation).
- System-proxy (non-admin) mode cannot capture apps that ignore the Windows proxy.

## References

- https://github.com/2dust/v2rayn
- https://github.com/patterniha/PattNG
- https://github.com/psiphon-inc/psiphon (psiphon-tunnel-core)
- https://github.com/GFW-knocker/MahsaNG
