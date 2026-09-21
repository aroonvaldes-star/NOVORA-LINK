# ExInEngine Controller Modes and Diagnostics Design

## Release Target

- PC product line: `1.4`.
- Android revision: `1.4.25` (`VersionCode 25`).
- Android must not be packaged or marked ready until the physical release gate in this document passes.

## Objective

Provide reliable controller input from NOVORA PC to one active Android phone across games, Android UI, and ordinary applications. Xbox-family and DualShock 4 controllers must retain distinct identities, calibration data, capabilities, and output behavior. Controller input must continue working across repeated matches and menu transitions without depending on VisionEngine lifecycle.

## Ownership and Boundaries

ExInEngine is the sole owner of:

- Physical controller discovery through SDL3 events.
- Controller family and capability identification.
- Calibration profiles and health diagnostics.
- Active input mode.
- Virtual gamepad and mouse output lifecycle.
- Battery state and controller-specific alert deduplication.

VisionEngine, LinkEngine, and STEngine remain independent. Starting, stopping, or recovering any of them must not restart or destroy ExIn virtual devices. ExIn uses its own persistent control-only session and recovery path.

Android and PC are views and command surfaces over the same ExIn state. They must never keep separate calibration, mode, diagnostic, or battery truth.

## Physical Controller Identity

ExIn identifies each physical controller using the strongest stable combination SDL exposes, including vendor ID, product ID, GUID, controller type, and any stable serial or path identity available. Profiles are selected automatically; users do not name or manually assign them.

- Xbox hardware uses an Xbox profile.
- DualShock 4 hardware uses a DualShock 4 profile.
- Unknown hardware uses an explicitly generic profile.
- No controller may be mislabeled or reuse another family's calibration or layout.
- Reconnecting a known controller automatically loads only its matching profile.

Persisted profile data contains identity, family, center, minimum and maximum axis values, trigger ranges, calculated deadzones, diagnostic result, capabilities, calibration timestamp, and a schema version. Passwords, accounts, input history, and private application content are never stored.

## Explicit Input Modes

The user selects exactly one mode from the Android bubble or the equivalent PC control.

### Game Mode

- Publish the family-appropriate gamepad UHID profile.
- Entering Game mode performs one controlled re-enumeration to create the hot-plug expected by games such as RB6.
- Xbox input uses the Xbox button and axis mapping.
- DualShock 4 input uses the DualShock 4 family profile.
- A DualShock 4 also publishes an independent mouse UHID device for its touchpad.
- One-finger touchpad movement controls the pointer.
- Physical touchpad click sends left click.
- Two-finger movement sends vertical and horizontal scroll.
- `L2` and `R2` remain gamepad triggers and are never consumed by mouse behavior.

### UI Mode

- Game-oriented UHID output is withdrawn so applications do not receive accidental gameplay input.
- Xbox D-pad and left stick navigate Android UI; `A` accepts and `B` goes back.
- DualShock 4 buttons provide equivalent navigation using its physical labels and layout.
- DualShock 4 touchpad remains an independent mouse with one-finger pointer movement, physical left click, and two-finger scrolling.
- Navigation is event-driven and does not inspect the foreground app through ADB or periodic polling.

### Mode Transition

Transitions are serialized and atomic:

1. Stop accepting new output events for the old generation.
2. Send a neutral report.
3. Destroy only the virtual outputs owned by the old mode.
4. Create the outputs for the new mode with the correct family identity.
5. Send a neutral initial report.
6. Resume events under a new generation identifier.

If a transition fails, ExIn removes partial outputs and restores the last valid mode. PC and Android display the same failure and active mode.

## Persistent Match Reliability

Once Game mode is active, match, menu, activity, and foreground transitions must not destroy or replace the virtual gamepad. The same UHID identity remains active and receives neutral reports while the physical controller is idle.

Re-enumeration is permitted only when:

- The user changes between Game and UI mode.
- The physical controller changes.
- The dedicated ExIn control channel experiences a real disconnect and completes recovery.
- The user invokes the explicit `Reactivate controller` recovery command.

Normal congestion, `WouldBlock`, high queue depth, an idle controller, VisionEngine state, or a game menu transition are not recovery failures.

On genuine control-channel recovery, ExIn recreates the correct family output, reapplies its calibration profile, emits a neutral report, then resumes live input. The manual recovery command is an emergency tool and does not count as successful automatic reliability.

## Shared Calibration

PC and Android expose the same guided calibration session owned by ExInEngine. Starting, completing, or resetting calibration on either surface updates one revisioned state and is immediately reflected on the other.

Calibration captures:

- Resting center stability for both sticks.
- Full positive and negative range for each stick axis.
- Minimum and maximum trigger values.
- Required deadzone per axis.
- Buttons observed during the guided test.

Calibration may correct stable center offset, usable asymmetric range, trigger range, and bounded deadzone. Raw and corrected values remain visible so calibration cannot hide hardware behavior.

Profiles persist per physical controller identity and load automatically after reconnect or restart.

## Automatic Health Diagnostics

Diagnostics use SDL events and bounded analysis windows, not continuous polling. A single movement is insufficient to declare drift.

The diagnostic engine classifies each controller as:

- `Healthy`.
- `Correctable by calibration`.
- `Calibration incomplete`.
- `Review recommended`.
- `Probable hardware fault`.

It evaluates:

- Stable resting offset and drift.
- Resting noise and abnormal jumps.
- Insufficient or asymmetric axis travel.
- Triggers that do not return to zero or reach usable range.
- Buttons that remain pressed unexpectedly.
- Touchpad presence and event consistency when supported.

The result explains which controls are affected, measured severity, and whether calibration can reasonably compensate. An erratic axis, stuck button, or severely reduced travel is not reported as fixed by calibration.

## Battery State and Notifications

Battery updates use `SDL_EVENT_JOYSTICK_BATTERY_UPDATED`. The initial state may be read once when the device opens. No battery polling loop is added.

Per-controller state includes percentage when available and one of: on battery, charging, charged, no battery, or unknown. SDL battery values are treated as hardware estimates, not absolute measurements.

PC and Android issue deduplicated notifications for:

- Low battery upon crossing downward to `15%`.
- Critical battery upon crossing downward to `5%`.
- Full charge upon reaching `100%` or the charged state.

An alert rearms only after the controller leaves its threshold sufficiently or begins a new charging cycle. Every alert names the physical controller. Unsupported battery reporting is shown as unavailable; no percentage is invented. Android requests notification permission where required.

## Android Bubble

The expanded bubble presents a segmented `Game | UI` mode control, current controller family, health summary, battery state, and a `Reactivate controller` recovery action. Busy transitions lock their controls until completion and surface an honest unavailable or failure state.

The collapsed bubble communicates the active mode with an icon and restrained color treatment. After four seconds without interaction it docks to the nearest screen edge and slides almost completely off-screen, leaving a narrow translucent handle.

- Touching or dragging the handle restores the bubble.
- Auto-hide pauses while the panel is expanded, an operation is pending, or the user is dragging it.
- The remembered edge and vertical position respect system bars and display cutouts.
- The hidden portion does not intercept touches intended for the foreground app.
- The overlay remains visible in VisionEngine output and recordings; hiding minimizes rather than falsely conceals it.

## PC Interface

The PC ExIn panel adds:

- Active mode and controller family.
- Physical identity and connection type when available.
- Raw and corrected live axis/trigger values.
- Guided calibration start, progress, finish, and reset controls.
- Per-axis deadzone and range results.
- Health classification and actionable explanation.
- Battery percentage and charging state when supported.
- Explicit controller reactivation with busy/error state.

PC and Android consume the same revisioned control snapshot. Stale commands are rejected and refreshed rather than overwriting newer state.

## Error Handling and Recovery

- Touchpad mouse failure must not stop gamepad output.
- Gamepad output failure must not stop VisionEngine, LinkEngine, STEngine, or NOVORA.
- A failed mode transition rolls back to the last valid mode.
- A failed calibration preserves the previous valid profile.
- Unsupported capabilities are displayed as unavailable and are not treated as engine failures.
- Recovery is triggered by actual device/control-channel failures, not load or normal idle behavior.
- All output generations ignore late events from a prior mode or prior connection.

## Protocol and Versioning

The shared control protocol adds revisioned commands for mode selection, reactivation, calibration, and acknowledgements. Snapshots add controller identity, family, capabilities, mode, transition state, diagnostic details, calibration details, and battery state. Unknown fields remain optional where compatibility requires it; unsupported commands return explicit unavailable responses.

Because Android source, UI, protocol behavior, and resources change, the Android project increments to `ApplicationVersion 25` and `ApplicationDisplayVersion 1.4.25` before packaging. The APK manifest, canonical PC-integrated APK, release descriptor, and SHA-256 must agree.

## Verification

Automated tests cover:

- Xbox, DualShock 4, and generic identity classification.
- Profile isolation and persistence by physical controller.
- Family-specific button, axis, and descriptor mappings.
- Atomic Game/UI transitions and rollback.
- DualShock 4 touchpad pointer, click, and two-finger scroll translation.
- Calibration validity, correction limits, and profile preservation on failure.
- Drift, noise, reduced travel, trigger, and stuck-button classification.
- Battery threshold crossing, deduplication, rearming, and unsupported state.
- Protocol validation, stale revision rejection, and snapshot round trips.
- Bubble auto-hide, restoration, positioning, and busy-state behavior.

Physical verification requires Xbox and DualShock 4 controllers and the active Android phone. It covers Launcher navigation, representative scrolling applications, RB6, DualShock touchpad mouse behavior, reconnects, mode changes, battery notifications when reproducible, and engine independence.

### Mandatory RB6 Release Gate

1. Enter RB6 in Game mode.
2. Complete a match.
3. Return to the game menu.
4. Repeat for at least five consecutive matches.
5. Start and stop VisionEngine between cycles.
6. Visit Launcher or another app, return to RB6, and continue.
7. Confirm buttons, sticks, triggers, and DualShock touchpad behavior without manual controller reconnection.

Any controller loss in these cycles fails the release gate. Using `Reactivate controller` does not convert a failed run into a pass. Android `1.4.25` is not ready for release until this gate passes physically.
