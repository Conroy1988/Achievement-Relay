# Optional Relay account sync (beta)

Open **Account** in the sidebar (also available from Settings) and choose **Continue with Discord**. Relay uses a system browser, authorization code with PKCE, and a loopback callback. Neither a Discord password nor the Discord application's client secret is shipped in Relay. The public Supabase publishable key is intentionally included in the desktop client.

On the first device, create a recovery key, copy it to a private password manager, and select **Sync now**. On a second device, sign in with the same Discord account, enter that recovery key, and sync. A new device adopts the existing account settings and merges its recent history and pins. The recovery key cannot be recovered from Supabase. Keep a local backup before changing devices.

Sync runs manually and every five minutes while the main window is hidden and the Companion window is closed. Visible settings editors are not refreshed underneath the user. Save or discard Settings edits before opening Account. Unsaved drafts also pause automatic account sync. After a restored connection, monitoring starts using the existing silent-baseline rules.

## What moves between devices

- Xbox account identity and OpenXBL key; Discord webhook destination and post name.
- General presentation, rarity, sound and per-game preferences; pins.
- Up to 300 recent journal entries and up to 100 recent library games, with up to 30 imported achievements per game. The encrypted profile is limited to 1.5 MB; oldest cloud history is omitted if needed. Full local history is preserved.

Windows startup/minimized preferences, Windows motion preference, overlay monitor and position, shared-folder paths, artwork bytes and Steam installation/detection stay local. Steam still uses the Steam account signed into each device; Relay does not copy Steam credentials or change that account.

## Encryption and conflicts

The entire profile, including connection credentials, is encrypted using AES-256-GCM with a fresh random nonce and an account-bound version marker. Supabase stores only the ciphertext. The recovery key, session tokens and merge baseline are protected by Windows DPAPI for the current Windows user. Use the same recovery key on both devices. Losing all local keys and the recovery copy makes cloud data unreadable.

Row-level security limits profiles and delivery claims to their authenticated owner. Conditional revision updates prevent stale writes. Locally changed fields are merged onto the current remote version; if both devices edit the same field, the last successful write wins. Independent pin additions/removals are merged. Xbox identity and its API key move together. Per-game preferences currently resolve as a group, not per game.

A Windows profile is paired to one Relay account. Signing into another account is refused to prevent mixing existing local history and secrets into another account. Sign-out keeps local settings/history, removes the local session and key, and returns the device to independent local posting. It does not delete cloud data or the account pairing marker.

## Delivery safety and Free limits

While signed in, devices request an atomic cloud claim before posting. Claims use an HMAC of the event identity and webhook destination; raw event IDs and webhook URLs are not stored in the claims table. Unavailable cloud coordination queues the post. A definite Discord 4xx rejection releases the claim for retry. Ambiguous sends keep the claim and require checking Discord rather than risking duplicate posts. No system can atomically commit both a Discord webhook and a database transaction; delivery is deliberately conservative.

Claims are bounded to 10,000 per account through the supported RPC and are not automatically expired. Reaching capacity stops new coordinated posts. History never authorizes delivery and downloaded history cannot enter the delivery queue. Account sync is an optional beta in v0.11.0; live two-device verification is pending.

This deployment is Supabase **Free only**. No paid branches, upgrades or add-ons are required. Unchanged profiles are checked by revision without downloading the ciphertext again, and unchanged data is not uploaded. Platform quotas and project pauses can still interrupt cloud access; local data is retained.

## Deployment

Project: `vniujteastkitrmucebv` in London (`eu-west-2`). Apply `supabase/migrations/20260913204125_account_sync.sql` to an empty project. Discord's callback is `https://vniujteastkitrmucebv.supabase.co/auth/v1/callback`. Supabase's redirect allowlist must include exactly `http://127.0.0.1:43821/auth/callback`. The local port must be free when sign-in starts.

For live verification, check real Discord sign-in and refresh, recovery on a second device, wrong-key rejection, two-user RLS isolation, concurrent changes, offline recovery, and duplicate-delivery claims with synthetic events and a dedicated test webhook. Never test by publishing old achievements to a real channel.



The account dialog guides sign-in, recovery and sync in order, and shows only available actions. Cancel sign-in returns control without clearing local data. Last successful sync is read from the local merge baseline after a restart. Discord display names are optional, bounded labels from user metadata; authenticated user IDs remain the sole ownership identity.
