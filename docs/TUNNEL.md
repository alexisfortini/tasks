# Private cloud tunnel

Production here uses stdio on Windows. Obtain custom MCP access in your target ChatGPT account/workspace and permission for your own OpenAI Platform tunnel. Local stdio configuration in another client does not make the cloud chat connected. Availability is account/workspace dependent; do not assume another client or Spark account supports the same custom connection.

The official route associates your tunnel with the correct Platform organization and ChatGPT workspace. Runtime permissions are Tunnels Read + Use; creating/managing a tunnel requires separate management rights. Use a Restricted runtime key and keep it distinct from the Google grant. The client connects outbound over HTTPS; no inbound firewall/public unauthenticated endpoint is required. See [official Secure MCP Tunnel instructions](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels).

Download/install the official OpenAI tunnel-client only after you approve that software installation. The included launcher has been exercised with v0.0.15. Place your chosen compatible binary at tools/tunnel-client-v0.0.15/tunnel-client.exe; the repository does not vendor the binary. Verify release provenance according to the official distribution instructions.

After building helpers, create an ignored machine-specific profile:

```powershell
node scripts/create-tunnel-profile.mjs --tunnel-id YOUR_OWN_TUNNEL_ID
```

Use the actual tunnel_... identifier. This writes quoted local Node/server paths, an environment key reference, loopback health binding and disabled raw HTTP logging. It neither creates nor starts a tunnel. For a temporary session, run connection/foreground-tunnel.exe personally and paste the existing runtime key only at its masked prompt. This mode calls doctor first and keeps the key in child-process memory.

## Automatic desktop startup

For unattended startup, run connection/background-tunnel.exe --configure-key and enter the same Restricted runtime key in the local password dialog. The key is encrypted with Windows CurrentUser DPAPI under .private/tunnel-runtime/key.dpapi, in a directory accessible only to that Windows account. No key is written to a profile, command argument or log. Re-run this dialog when rotating the key.

For a `.env` setup instead, copy the placeholder-only `.env.example` to `.env` once and use `connection/background-tunnel.exe --configure-env-key` after approving credential storage. The dialog saves `CONTROL_PLANE_API_KEY` as plaintext with a Windows account-only file ACL, preserving other settings. It never displays the entered key in diagnostics. The background launcher checks its process environment first, then this single-line `.env` key, then DPAPI. Empty local values use DPAPI; malformed or duplicate key entries fail closed. Google OAuth data stays encrypted separately. A saved runtime key grants continuing tunnel access; use only Tunnels Read + Use, without management or unrelated API permissions.

Then run pnpm install:startup. It registers and starts the per-user Google Tasks MCP scheduled task, delayed 15 seconds after Windows sign-in. It runs without elevation or a stored Windows password, has no time limit, and starts on battery too. Windows sign-in is required because the key belongs to the user's desktop account. The wrapper restarts an exited tunnel with bounded backoff; Task Scheduler also restarts the wrapper after a failure. A missing key waits for local setup. Missing profile/binary exits with a fixed diagnostic in connection/background-status.txt. That status is a launcher diagnostic, not proof of cloud readiness.

The foreground and background launchers share a mutex for this installation. Windows job objects terminate the tunnel and its stdio descendants when a launcher is stopped, preventing orphan processes during restart. Stop the scheduled task before using the foreground launcher. Do not run another installation against the same tunnel ID.

Check the task in Windows Task Scheduler, the local /readyz health endpoint using connection/health.url, and a harmless cloud tool read. After changing code, stop/start the scheduled task. To disable automatic startup, disable the Google Tasks MCP scheduled task; to remove it, unregister that task. Stopping/disabling the task preserves the encrypted key, Google grant and mutation journals.

## Move an existing installation

Stop the scheduled task and wait for it to reach Ready. Close any launcher password dialog, then move the entire installation including its ignored local state within the same Windows account and machine. Preserve Git history, `.private`, `.env`, journals and downloaded binaries. Reinstall the exact dependencies with `pnpm install --frozen-lockfile` in the new folder because Windows dependency junctions can retain the old absolute paths. Rebuild helpers in the new location, run `node scripts/create-tunnel-profile.mjs --refresh-paths` to update the stdio command while preserving the existing tunnel and settings, and run `node scripts/install-startup.mjs --previous-root OLD_DIRECTORY` to replace only that account's matching startup action. Update the Git remote after a GitHub rename. Verify `pnpm doctor`, startup settings, offline tests and a cloud read. Repository/folder renaming does not require a new Google client, tunnel or connection.

Create a private custom MCP connection in ChatGPT with Connection=Tunnel and your tunnel. Google authorization is already handled server-side; this stdio implementation does not expose browser-facing MCP OAuth endpoints. Choose the platform configuration appropriate to that route rather than inventing a public OAuth server. Tunnel authorization and Google OAuth remain separate.

Verify fresh discovery of 16 tools, a harmless read and the mutation receipt on an explicitly authorized isolated test. A successful doctor/health status alone does not prove end-to-end cloud schema behavior. Use [official connection refresh steps](https://developers.openai.com/plugins/deploy/connect-chatgpt) after metadata changes. Account/product usage costs are not promised free by this repository; no paid middleware is part of its design.
