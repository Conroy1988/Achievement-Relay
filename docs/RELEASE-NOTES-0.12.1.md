# Achievement Relay v0.12.1

## Steam observer reliability

Relay now ignores plain diagnostic text on the Steam helper's output pipe instead of treating it as a damaged achievement record and restarting the helper. This preserves the observer's live baseline across diagnostic messages. Both ends of the pipe now use UTF-8 explicitly for achievement and player text.

Genuine data errors still fail closed, with separate messages for oversized output and invalid JSON or field types. Raw helper output is not recorded in logs. Existing achievements remain historical; this update does not automatically recover or repost unlocks missed during an earlier observer failure.

The Signal Strip overlay, Collector Card showcase, Xbox PC Game Pass labels and optional encrypted account sync are preserved. The minimum supported update version remains v0.4.0.

## Installation

Use **AchievementRelay_Setup.exe**, or Relay's update prompt. The release includes **AchievementRelay_0.12.1.0_x64.msix**, ARM64 and fallback installer packages, the existing publisher certificate and a signed update manifest. Existing settings and history are retained.

## Validation scope

Regression coverage exercises native diagnostic text interleaved with valid records, Unicode and artwork, malformed JSON and fields, and oversized output. Testing an affected game on the user's Windows PC is still required to confirm the reported live failure is resolved; the original raw helper output was not available for diagnosis.
