# Accessibility

Achievement Relay uses a fixed dark command palette with explicit foregrounds on every app-owned surface. The interface is designed against WCAG 2.2 contrast thresholds as a practical desktop-app benchmark.

## Readability contract

The repository check calculates these combinations directly from `App.xaml` and fails when a future theme change drops below the stated threshold:

| Use | Contrast |
| --- | ---: |
| Primary text on raised cards | 15.56:1 |
| Muted text on raised cards | 8.36:1 |
| Accessible red text on raised cards | 6.62:1 |
| Success / warning / error text on raised cards | 8.60:1 / 8.69:1 / 5.85:1 |
| White text on the primary red button | 4.91:1 |
| White text on the Discord button | 4.61:1 |
| Button-hover text | 6.69:1 |
| Disabled-control text | 7.82:1 |
| Card and control boundaries | 3.21:1 and 5.24:1 |

Normal text and meaningful control states target at least 4.5:1. Meaningful non-text boundaries target at least 3:1. Small red labels use a separate lighter token; the darker brand red remains available for large branding, fills and progress indicators.

## Interaction and assistive technology

- App-owned pages, cards, lists, fields, tooltips, hover states and disabled states define their colors explicitly; Windows light-theme defaults cannot leak into the content canvas.
- Explicit interface text is never smaller than 11 device-independent pixels, while body text defaults to 13 and input text to 14.
- Buttons have a minimum 40-pixel target height. Keyboard focus uses a two-pixel warm-white boundary rather than color alone.
- Statuses always include text in addition to color. Important changing status regions use polite UI Automation live notifications.
- Navigation, activity lists, progress indicators and free-text fields expose descriptive UI Automation names. Standard buttons and check boxes retain their visible labels as accessible names.
- The layout uses WPF device-independent sizing and scrollable pages so Windows display scaling does not clip the primary workflow.
- The notification-area menu, message boxes, title bar and Setup/updater controls remain native Windows controls and therefore use the operating system's accessibility behavior.

## Discord Collector Cards

Each Collector Card attachment includes an author-controlled description summarising the achievement, game, platform and rarity. Because Discord client support for attachment descriptions can vary, the card is never the only copy of essential information: the achievement name, game, challenge, reward, player, platform, percentage/tier and timestamp also remain available as ordinary Discord embed text when supplied.

- Game artwork always receives a deterministic dark readability treatment before text is drawn over it.
- The no-artwork fallback uses the same controlled palette rather than inheriting colors from a game image.
- Bronze, Silver, Gold, Platinum and Unranked use different emblem silhouettes, internal marks and written tier names; color is not the sole distinction.
- A missing percentage is written as **Unranked** instead of being represented by color or a false numeric value.
- Provider strings are bounded and wrapped so long localized names cannot cover the rarity or platform information.
- If a complete safe card cannot be rendered, the ordinary text embed is still delivered.

## Signal Strip overlay

The local Signal Strip is a brief, non-interactive visual complement to the durable Activity entry and Discord post; it is never the only record of an achievement.

- The strip does not activate, take keyboard focus or intercept pointer input from the game beneath it.
- No achievement sound is played, so the feature does not override game audio or rely on an audio cue.
- Achievement and game names use bounded layouts, while rarity is communicated through the emblem silhouette, percentage and written tier treatment rather than colour alone.
- Missing percentages use the explicit **Unranked** state instead of an ambiguous blank or false numeric value.
- Consecutive unlocks are shown sequentially for five seconds each rather than stacked, flashing or rapidly replacing one another.
- The feature can be disabled independently in Settings without disabling monitoring or Discord delivery.

## Reporting a problem

If a label is hard to read or a control is difficult to use with keyboard or assistive technology, open a [GitHub issue](https://github.com/Conroy1988/Achievement-Relay/issues) or join [Community & Support on Discord](https://discord.gg/3ZdXhYjgDm). Do not include an OpenXBL key or Discord webhook in a report.
