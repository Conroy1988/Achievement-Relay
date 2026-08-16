# Third-party notices

## Software

| Component | Author | Version | Licence | Source | Use |
|---|---|---:|---|---|---|
| Facepunch.Steamworks | Garry Newman / Facepunch | 2.5.2 | [MIT](third_party/Facepunch.Steamworks.LICENSE.txt) | [GitHub](https://github.com/Facepunch/Facepunch.Steamworks) | Local, read-only Steamworks bridge for Windows x64. |
| `steam_api64.dll` | Valve Corporation | bundled with Facepunch.Steamworks 2.5.2 | Steamworks SDK redistribution terms | [Steamworks SDK](https://partner.steamgames.com/doc/sdk/api) | Native local Steam client interface required by the bridge. |

The reviewed NuGet package is committed under `third_party/packages`; its expected SHA-256 is documented and enforced in `scripts/Test-Repository.ps1`. The MIT licence covers Facepunch's wrapper code, not Valve's separately owned native Steamworks binary or trademarks. Steam and Steamworks are trademarks and technology of Valve Corporation. Valve does not endorse Achievement Relay.

## Art

Achievement Relay's cinematic relay artwork and installer artwork were generated with OpenAI image tooling from project-specific prompts, then cropped and optimized for the Windows interface. The logo and brand graphics were also created for this project. These assets do not copy or include third-party game characters, logos, screenshots, or distinctive franchise assets.

The following interface icons are redistributed under their authors' stated licence:

| Asset | Author | Licence | Source | Changes |
|---|---|---|---|---|
| `trophy-cup.svg` / `TrophyCup.png` | Delapouite | [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/) | [Game-icons.net](https://game-icons.net/1x1/delapouite/trophy-cup.html) | Background removed; rasterized and resized for the interface. |
| `radar-sweep.svg` / `RadarSweep.png` | Lorc | [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/) | [Game-icons.net](https://game-icons.net/1x1/lorc/radar-sweep.html) | Background removed; rasterized and resized for the interface. |

The editable SVG copies are kept in `assets/third-party/game-icons`. The compiled PNG copies are in `src/AchievementRelay.App/Assets`.

These CC BY assets are not relicensed under the repository's MIT licence; they remain available under CC BY 3.0. All product and company names mentioned by the app belong to their respective owners and are used only to identify compatible services.

## Original music

`installer/assets/CRNY - Relay Online.mp3` is an original track by CRNY, included in the Achievement Relay installer at the artist and copyright holder's direction. Copyright © 2026 CRNY. All rights reserved. The track is not licensed under the repository's MIT licence; that licence applies to the software and documentation, not this recording or composition.
