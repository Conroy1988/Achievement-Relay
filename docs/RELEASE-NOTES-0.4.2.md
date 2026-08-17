# Achievement Relay v0.4.2

v0.4.2 is a focused updater-comfort and Xbox device-handoff safety release.

## Updater audio now starts muted

The verified updater still includes CRNY's **Relay Online**, but update mode no longer starts audio automatically.

- The first updater screen opens silently with **Play music** available.
- Selecting Play uses the same fixed 10% per-player volume.
- Pause/Play, looping, the Windows Media Player and MCI fallback paths, clean exit handling, and the direct SoundCloud button remain available.
- Fresh first-time installations keep their existing soundtrack behavior.

## Safer Xbox use across multiple PCs

Every app start creates a fresh Xbox live-delivery epoch. A long sleep, network outage, provider interruption, or backwards clock jump also creates a new epoch. On the first successful reconciliation, achievements unlocked before that epoch are stored in the PC's stable-ID baseline without another Discord post.

This fixes the normal device-handoff case: an achievement relayed while playing on one device is not posted again merely because another PC starts Achievement Relay later.

- A usable Xbox timestamp after the current epoch can prove an unlock on the first poll.
- An Xbox 360 identity with no usable timestamp is eligible only when its count change was directly observed after a successful poll in the same uninterrupted session.
- The identity is retained even when it is silently reconciled, preventing it from becoming new again on the next poll.
- Proven live work keeps its original epoch/evidence in the durable queue, so a failed Discord or provider delivery remains retryable after an updater/app restart.
- Queued work migrated from an older version receives no invented live proof and is baselined safely.

Achievement Relay has no hosted account service or cloud database. Its processed-event ledger and Xbox snapshots stay local to each Windows account, preserving the existing privacy model. Because two simultaneously active PCs cannot take an atomic shared lock, they can still race on the same live Xbox change; keep Xbox monitoring active on one PC at a time. Sequential device handoffs and inactive-device restarts are protected by the epoch rule above.

## Updating

Installed v0.4.0 and v0.4.1 copies use the existing certificate-pinned GitHub updater. This is an optional release under the v0.4.0 support floor; on launch, the app can automatically download, verify, and open it according to the established update policy.

The updater preserves encrypted connections, provider baselines, processed identities, pending deliveries, activity history, startup behavior, and desktop-shortcut choice.

## Release assets

- `AchievementRelay_Setup.exe` — recommended installer and verified updater
- `AchievementRelay_0.4.2.0_x64.msix` — x64 Windows package
- `AchievementRelay_0.4.2.0_arm64.msix` — Arm64 Windows package
- `AchievementRelay_0.4.2.0_installer.zip` — manual installation fallback
- `AchievementRelay_Update.json` and `AchievementRelay_Update.sig` — signed automatic-update contract
- `AchievementRelay.Publisher.cer` — public package certificate
