# NOVORA Android Visual Refresh Design

## Objective

Redesign every NOVORA Android surface using the visual language described in
`apk.md`, while preserving the real .NET Android application, package identity,
protocols, engine ownership, and functionality available through the 1.4.26
baseline.

The reference document is visual guidance only. Its Kotlin project tree,
`com.novora.link` package, 1.4.19 version, invented metrics, claimed validation,
and unsupported Android permissions are not implementation requirements.

## Product Boundaries

- Keep `com.novora.appcontrol`, .NET Android, API 26 minimum, and the current
  trusted USB/LAN control protocol.
- Preserve independent LinkEngine, VisionEngine, ExInEngine, and STEngine
  lifecycle and recovery behavior.
- Do not replace real state with demo values. Unknown or unavailable data must
  remain visibly unavailable.
- Keep one active phone per PC and retain event-driven updates without adding
  polling.
- Treat the 1.4.26 feature set as the functional baseline. Version metadata may
  advance for the redesigned APK; it must never be downgraded to 1.4.19.
- Keep the floating control optional. Hiding, disabling, or denying overlay
  permission must not change ExIn mode or stop any engine.

## Recommended Architecture

Retain the existing native programmatic Android UI and evolve it into a small
shared design system. This avoids a framework migration and keeps the current
service bindings, lifecycle callbacks, activity contracts, and accessibility
behavior.

The presentation layer will be divided into:

1. Theme tokens: color, typography, spacing, shape, state, and elevation.
2. Reusable controls: app bar, status chip, section heading, engine row,
   action tile, command button, field row, segmented control, and bottom tab.
3. Page composition: Home, Connect, Engines, ExIn, Files, Multimedia, Settings,
   QR, floating settings, received files, sharing, and the floating overlay.
4. State binding: existing snapshots and callbacks remain the only source of
   engine, connection, media, and transfer state.

No page or visual component may own a transport, engine, recovery loop, or
session.

## Visual Language

### Dark theme

- Background: `#0A0F11` and `#0F1416`.
- Primary surface: `#171C1E`.
- Raised surface: `#20282C`.
- Border: `#2B3A40`.
- Primary text: `#FFFFFF`.
- Secondary text: `#91A2AA`.
- Primary accent: `#00DCE8`.
- Success: `#20D982`.
- Warning: `#F2B84B`.
- Error: `#FF6670`.

### Light theme

- Background: `#F6FAFA` and `#FFFFFF`.
- Primary surface: `#FFFFFF`.
- Raised surface: `#EDF3F4`.
- Border: `#D2DEE1`.
- Primary text: `#191C1D`.
- Secondary text: `#53646C`.
- Primary accent: `#087F91`.
- Success: `#16845B`.
- Warning: `#976C00`.
- Error: `#B3262E`.

The palette must remain balanced; cyan is an accent rather than the color of
every surface. Cards use a maximum 8 dp radius. Text uses Android system sans
families with medium and semibold weights; Montserrat is not added unless a
licensed, locally bundled font is approved separately.

## Shared Shell

Every main page uses the same compact shell:

- Top app bar with NOVORA logo, `NOVORA-LINK`, current APK version, connection
  state, active engine count, and theme icon.
- A constrained scrolling content region with 16 dp horizontal padding.
- A fixed five-destination bottom navigation: Home, Connect, Engines, Files,
  and Multimedia.
- Settings remains accessible from the app bar and from relevant workflows; it
  is not promoted to a sixth bottom destination.
- Selected navigation uses icon, label, accent color, and an accessible selected
  state without resizing the layout.

All labels must fit at supported Android font scales. Status text may wrap;
buttons and fixed controls must not grow or shift because of dynamic content.

## Page Designs

### Home

- Branded welcome region using the existing NOVORA bitmap asset.
- Four compact action tiles: Connect, Engines, Files, Multimedia.
- Engine summary list showing only live snapshot values.
- Connection strip for USB/LAN and trusted PC identity.
- No fake host name, latency, FPS, or active engine counts.

### Connect

- USB is the first and strongest section, with physical status and explicit
  LinkEngine requirement.
- QR and LAN are grouped as auxiliary control/trust paths.
- Stored PCs and invitation paste remain available in a secondary menu.
- Engine discovery reports each engine independently.
- Android USB host claims from the reference are not added; the current PC ADB
  trust bootstrap remains authoritative.

### Engines

- One un-nested row or compact card per engine.
- Each row displays real state, a concise capability summary, and available
  commands.
- VisionEngine provides start/stop and entry to its existing profile, bitrate,
  resolution, FPS, monitor, audio, and presentation settings.
- ExIn opens its dedicated page.
- LinkEngine and STEngine retain independent status and unavailable states.

### ExIn

- Hardware identity, family, transport, VID/PID, battery, and active profile.
- Game/UI segmented mode control and explicit reactivation command.
- Live raw and corrected input visualization using existing snapshot data.
- Diagnostics and calibration remain evidence-based; no fixed polling-rate or
  latency claims.
- The page remains usable with the bubble disabled.

### Files

- Distinct Received and Sent views backed by existing activities and storage.
- Real empty states instead of synthetic history.
- Transfer progress, size, rate, and failure states appear only when supplied by
  the transfer layer.

### Multimedia

- Audio routes use existing detected output options and read-only capabilities.
- Recording and screenshot controls preserve current busy and unavailable
  states.
- Floating control settings are a separate optional section with a master
  toggle. Dependent appearance controls are hidden when the toggle is off.

### Settings And Auxiliary Activities

- Settings, QR, files, share target, floating settings, and bootstrap feedback
  use the same app bar, typography, palette, spacing, buttons, and states.
- QR remains authorization/trust presentation, not engine authorization by
  itself.
- Destructive or privacy-sensitive actions require clear confirmation.

## Floating Control

- The folded bubble is a 56 dp stable icon with configurable opacity.
- The expanded panel uses the shared palette and compact tool rows.
- Hiding or disabling the overlay removes only the overlay view.
- ExIn controls shown in the panel are optional remote commands. The panel does
  not own ExIn mode, output, session, recovery, or availability.
- Missing overlay permission affects only the overlay and produces a clear
  settings action.
- The panel remains capturable by VisionEngine unless a separate, explicit
  privacy preference is implemented and validated.

## State And Error Handling

- Existing `NLControlSessionState` and `NLControlSnapshot` data drive all state.
- Loading, connected, busy, unavailable, failed, and stale states have distinct
  visual treatment.
- Commands disable only while their own mutation is pending or the session is
  globally busy.
- Connection loss preserves navigation and shows recoverable empty states.
- A failed engine never marks another engine failed and never collapses the
  whole Android UI.

## Accessibility

- Minimum 48 dp touch targets.
- Icons have content descriptions; decorative imagery is excluded from the
  accessibility tree.
- Dark and light themes meet readable contrast for primary and secondary text.
- State is never represented by color alone.
- Dynamic text and screen-reader order follow the visual hierarchy.
- Reduced animation is respected; no continuous decorative animation is added.

## Migration Strategy

1. Expand `NLAndroidUITheme` and `NLAndroidUIVisual` into stable shared tokens
   and controls without changing service behavior.
2. Recompose the main shell and five primary destinations while preserving all
   named fields, callbacks, and state updates.
3. Migrate ExIn, Settings, QR, Files, Share, and floating settings.
4. Restyle and decouple the floating overlay.
5. Remove superseded presentation helpers only after all callers migrate.
6. Package a new APK version, inspect its binary manifest, synchronize the
   canonical APK and release descriptor, and keep physical validation separate.

## Verification

- Desktop Release build: zero warnings and errors.
- Android Release compile: zero warnings and errors.
- Full automated test suite, including canonical APK/version/hash checks.
- Static check that the overlay removal path sends no ExIn command.
- Screenshots at phone and tablet widths in dark and light themes.
- Text-overflow and touch-target inspection at increased Android font scale.
- APK binary manifest inspection for package, version, min SDK, and target SDK.
- Physical USB install and launch.
- Physical checks for USB/LAN discovery, all five destinations, VE start/stop,
  ExIn Game/UI with and without the bubble, files, media, theme persistence,
  rotation/insets, and reconnect behavior.

Compilation, APK packaging, installation, and physical workflow validation are
reported as separate evidence layers.

## Explicit Non-Goals

- Kotlin/Compose or Gradle migration.
- Package rename to `com.novora.link`.
- Kernel, DMA, USB speed, 120 FPS, 1000 Hz, or latency telemetry without a real
  measured source.
- New engine features, payment, advertising, analytics, or private-data storage.
- Replacing current trust, ADB, VPN, or engine protocols merely for visual
  parity.
