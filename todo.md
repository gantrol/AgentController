- Opensource to github: `gantrol/codex-controller`
- [x] Test in Xbox Series, Flydigi Vader 4 Pro, and 8BitDo Ultimate 2
- Support more than Codex, starting with a researched Claude Code adapter
- Not only windows? Change `app` directory to `windows`
- Copy? e.g. controller assets in https://github.com/univrsal/input-overlay ?
- v0.7 shipped behavior / remaining validation
  - [x] Use live UI controls and readback for Simple Power, Standard / Fast, and RB+Y Fast toggle
  - [x] Ramp right-stick repeat speed over about two seconds and scale the final rate by stick magnitude
  - [x] Navigate Advanced options in visual screen order, with lower values at the top
  - [x] Add Y → D-pad Up for New task and remove the unstable Plan controller entry
  - [x] Require a three-second B hold with countdown to cancel an active turn
  - [x] Add short chat-turn navigation plus four-second Top / three-second Bottom holds without success popups
  - [ ] Run physical end-to-end validation of Power and Fast on a non-Max model; current Sol Max exposes no native Power control
  - [ ] Physically verify the shortcut-first Power / Fast transport (F17 / F18 / F20 with composer-button readback), its menu fallback, and the 30 s suspect cooldown; see docs/power-simple-advanced-consultation-response.zh-CN.md
  - [ ] Physically verify Simple-mode routing: the right stick keeps Power and Standard / Fast while the R3 picker stays visible, and RB+Y toggles Fast with the picker open
  - [ ] Validate the self-contained v0.7 package on a clean Windows account
- v0.7 deferred / experimental work
  - [ ] Re-enable Plan mode only after the route can verify the visible Codex state before and after every change; v0.7 exposes no controller binding for it
  - [ ] Prototype a virtual HID identity compatible with `v.oai.rad` only in an isolated experimental build; follow `public/docs/codex-micro-virtual-hid-bridge-plan.md`
  - [ ] Require Center → Direction → Center reports, device-fingerprint/version guards, explicit opt-in, and result verification before any virtual-HID action can report success
  - [ ] Do not ship or install a virtual HID driver in v0.7
- Historical v0.4b trial notes
  - [x] Stable controller-owned sidebar directory; running-task recency
        updates no longer reorder the active navigation session
  - [x] Bottom-center previous / current / next sidebar wheel with
        cross-section boundaries
  - [x] A confirms the focused wheel entry; left-stick right is inert in
        Base and left returns from project tasks
  - [x] LT push-to-talk uses 0.35 / 0.20 hysteresis and is suppressed by
        radial and open Virtual Dial contexts
  - [x] LB Agent, RB Command, and RT Turn radial hint layers with
        Candidate freezing and release-drain protection
  - [x] Right-stick `DialStep`, R3 `DialPress`, 500 ms settings hold,
        and dedicated `DialCancel`
  - [x] Probe the real Codex picker/menu state before routing conflicting
        controller actions; drain held inputs when the picker closes
  - [x] Coalesce high-frequency dial steps while UI Automation is busy
  - [x] Preserve short digital and trigger threshold edges while
        coalescing only redundant analog controller snapshots
  - [x] Exclude Send, Stop, Dictate, and other immediate actions from
        the generic composer-control dial
  - [ ] Live-validate the new-task Project picker accessibility tree
        across Codex languages and releases
  - [ ] Replace best-effort keyboard traversal inside an open Project
        picker with verified option identity and result feedback
- v0.4 architecture follow-up
  - [x] Runtime zh-CN / en-US catalogs and language setting
  - [x] Localized structured feedback, tray, footer, and primary actions
  - [x] Config / Settings View + ViewModel split
  - [x] Extract Device page presentation state and view from `MainWindow`
  - [ ] Extract controller gesture orchestration into a dedicated coordinator
  - [x] Controller profile registry, identity matching, glyphs, and Raw HID mapping
  - [ ] Profile-specific controller visuals and first-run tuning defaults
  - [x] Agent capability interfaces, registry, Codex adapter, and safe fallbacks
  - [ ] Target Agent selector plus a second production adapter
  - [ ] Replace the remaining temporary `diagnostic.legacy.message` adapter with typed events
  - [ ] Coalesce high-frequency sidebar focus and composer preview events

- v0.4a -> v0.4b controller mapping preparation
  - Scope guard
    - [ ] Keep v0.4a planning-only: do not change runtime button behavior until the v0.4b physical mapping is accepted
    - [x] Treat the current A / X / B / Y and stick bindings as a prototype, not a compatibility contract
    - [x] Keep physical mapping out of firmware, Codex command IDs, F13-F24 meanings, menu order, task IDs, and window handles
    - [ ] Require the runtime chain: physical input -> physical gesture -> virtual Micro input -> context role -> `CodexAction` -> executor -> verified feedback

  - Current implementation to replace or isolate
    - [ ] Move `MainWindow.ProcessControllerState` button edges and stick routing into a testable controller coordinator
    - [ ] Stop calling `StartDictation`, `SendPrompt`, `CancelAction`, sidebar navigation, and composer adjustment directly from XInput button edges
    - [x] Queue digital button edges in order so a short Down/Up cannot be lost while the UI thread is busy; coalesce only analog snapshots
    - [ ] Keep ABXY names only at the XInput transport boundary; use physical-position IDs such as `FaceSouth` inside the gesture and profile layers
    - [ ] Replace the flat physical-input-to-business-action path with separate `ControllerProfile` and `VirtualMicroLayout` mappings
    - [ ] Expand optional rear inputs from `LeftAuxiliary` / `RightAuxiliary` to four positions: `RearLeftUpper`, `RearLeftLower`, `RearRightUpper`, and `RearRightLower`
    - [ ] Mark rear inputs as independent, mirrored, or unavailable after capability probing; never make a core action rear-button-only
    - [ ] Complete Raw HID coverage for hat/D-pad, shoulders, triggers, Guide, and optional rear inputs instead of silently dropping them
    - [ ] Apply profile-specific stick/trigger tuning in the active input path instead of always using the global dead-zone value
    - [ ] Remove hard-coded face-button glyph assumptions from `DevicePageViewModel`; render the active virtual slot, context role, and v0.4b binding instead

  - P0: semantic and input foundation
    - [ ] Add versioned contracts for `hardwareProfileVersion`, `gestureSchemaVersion`, `virtualLayoutVersion`, `actionCatalogVersion`, `adapterVersion`, and `userLayoutVersion`
    - [ ] Add a host-configurable `PhysicalGestureEngine` for down/up, tap, 350 ms double-tap, hold start/end, axis enter/repeat/exit, chord suppression, and device disconnect
    - [ ] Use an injectable `TimeProvider` for double-tap, 300 ms context arming, 500 ms dial hold, repeat, cooldown, and undo timing
    - [x] Preserve separate sources for left-stick directions and D-pad directions
    - [x] Add dead-zone hysteresis, dominant-direction locking, neutral-before-reverse, and per-action repeat policy
    - [ ] Add the stable virtual surface: six Agent slots, six Command slots, four analog directions, `DialStep`, `DialPress`, and `DialHold`
    - [ ] Add `controller.*` extension inputs for Primary, Secondary, Cancel, Navigate, action palette, and virtual-layer switching
    - [ ] Add a versioned `VirtualMicroLayout` with migration, unknown-field preservation, and user-layout protection
    - [ ] Add a `CodexAction` catalog with F0-F4 frequency, U0-U3 context priority, R0-R2 risk, prerequisites, repeat policy, executors, verification, and feedback metadata

  - P0: context, execution, and safety
    - [ ] Add `ContextResolver` priority: SafetyConfirmation > ApprovalRequest > Question > ModalDialog > MenuOrListbox > ComposerControl > Dictation > RunningTurn > Base
    - [ ] Implement `Idle -> Candidate -> Armed -> Executing -> Cooldown`; freeze conflicting Base actions as soon as Candidate appears and arm only after 300 ms of stable evidence
    - [ ] Wait for input release when Question, Menu, or ComposerControl exits so the same press cannot leak into Base
    - [ ] Keep Cancel separate from Decline, and allow Approve / Decline only in a verified approval context
    - [ ] Allow Stop and Steer only for a verified active turn belonging to the selected task
    - [x] Model navigation undo separately from Context Cancel, with verified target identity, expiry, invalidation, and result feedback
    - [x] Block Submit, Steer, and Queue when the composer is empty
    - [ ] Add an `ExecutorRegistry` with action-specific semantic, UIA, deeplink, official-shortcut, managed-shortcut, and guided-menu fallbacks
    - [ ] Add a Codex capability manifest with Supported / Degraded / Unavailable, selected executor, verification method, minimum version, and last probe result
    - [ ] Make managed shortcuts dynamic, conflict-aware, backed up, idempotent, reload-aware, and removable without overwriting user entries

  - P0: Dispatch, Steer, Queue, and feedback
    - [ ] Split `composer.dispatchDefault`, `turn.steer`, `turn.queue`, and `turn.stop` into separate adapter capabilities and actions
    - [ ] Probe the selected task's active-turn state and the user's Follow-up behavior without changing that preference
    - [ ] Add adapter operations equivalent to `TryGetTurnState`, `TryGetFollowUpMode`, `DispatchDefault`, `SteerCurrentTurn`, `QueueForNextTurn`, and `ManageQueuedMessages`
    - [ ] Verify whether Dispatch started a turn, steered the current turn, queued the next turn, stopped a turn, or remained unknown
    - [ ] Add canonical outcomes: Succeeded, Unavailable, Blocked, Conflict, Degraded, and Failed
    - [ ] Show virtual surface/slot, action, context, executor path, verified result, and recovery guidance in feedback
    - [ ] Never collapse Started, Steered, Queued, and Unknown into the same "sent" message

  - P1: Codex Micro completeness
    - [ ] Implement six immutable Agent slots with Most recent (default), Pinned, Priority, and Custom sources
    - [ ] Agent single-tap switches without forcing Codex foreground; 350 ms double-tap switches and foregrounds
    - [ ] Return Degraded with an explicit explanation if background task switching is technically unavailable
    - [ ] Implement Custom empty-slot create-then-bind without storing an unknown task on failure
    - [ ] Represent Idle, Thinking, Complete unread, Requires input, Error, Unassigned, and selected state independently
    - [ ] Implement six configurable Command slots and restore defaults: Fast, Approve, Decline, Fork, push-to-talk, and Dispatch default
    - [ ] Correct push-to-talk: hold starts, release stops, 350 ms double-tap latches hands-free, and a later activation stops the latch
    - [ ] Distinguish recording, processing, and transcript-ready feedback; safely stop dictation on disconnect, Codex exit, or context loss
    - [ ] Restore virtual analog defaults: Up Plan, Right Forward, Down Sidebar, Left Back, while keeping each direction configurable
    - [ ] Implement Composer-navigation and Reasoning-only dial modes; `DialHold(500 ms)` opens Micro-equivalent settings

  - P2: settings and presentation
    - [ ] Add settings pages for Agent source and custom slots, Command slots, analog directions, dial mode, gesture thresholds, layers, diagnostics, and capability status
    - [ ] Separate built-in Codex actions, Skills, managed shortcuts, controlled composer text, and external URLs in the mapping UI
    - [ ] Display unsupported or degraded actions with a reason instead of allowing an apparently successful binding
    - [x] Keep LT/LB and RT/RB visually and logically separate; triggers retain continuous values while bumpers remain digital
    - [ ] Provide a separate enhanced-controller rear-input view instead of crowding rear labels onto the front controller render

  - v0.4b implementation order
    - [ ] Translate the accepted standard-XInput table only into physical-gesture -> virtual-input/context-role profile entries
    - [ ] Add the enhanced four-rear-input profile as an optional acceleration layer after capability probing
    - [ ] Confirm all F0 actions are direct, U2 actions are direct in context, and every Agent/Command slot is reachable within the specified layer budget
    - [ ] Check Approve, Decline, Stop, and Steer conflict windows before enabling the profile
    - [ ] Apply long-hold, second confirmation, or a dedicated confirmation page to every R2 route
    - [ ] Update controller labels and the quick-operation guide from the accepted profile rather than hard-coded ABXY copy
    - [ ] Keep a reversible migration path back to the current prototype profile during the v0.4b trial

  - Required test gates
    - [ ] Deterministic timing tests for 300 ms context arming, 350 ms double-tap/PTT latch, 500 ms dial hold, repeat, cooldown, chord timeout, and disconnect
    - [ ] Gesture tests for hysteresis, dominant direction, neutral-before-reverse, D-pad/stick source separation, and non-repeatable toggles
    - [ ] Input-queue tests for short Down/Up pulses, duplicate packets, reconnect while held, and disconnect-generated releases
    - [ ] Context-leak tests for approval appearing during Cancel/Send, Question/Menu exit while held, and foreground/focus changes
    - [ ] Follow-up tests for idle Dispatch, running Steer default, running Queue default, explicit one-shot opposite behavior, empty composer, and unknown dispatch
    - [ ] Agent-slot tests for Most recent ordering, Pinned/Priority/Custom, single/double foreground behavior, empty-slot binding, and all status states
    - [ ] Profile tests for standard XInput reachability, enhanced rear-input independence/mirroring, layer conflicts, and full neutral reset on disconnect
    - [ ] Compatibility tests for shortcut conflicts/backups, Codex UIA changes, unsupported capabilities, config migration, and user-layout preservation

  - Research blockers to resolve before enabling v0.4b
    - [ ] Reliable Windows task switching without foreground activation
    - [ ] Foreground-gate policy that permits verified background Agent-slot switching without enabling unrelated background actions
    - [ ] Real-time selected-task status and selected thread/turn identity
    - [ ] Active-turn and Follow-up behavior detection
    - [ ] Stable multilingual UIA for menus, Steer, Queue, approvals, questions, and queue management
    - [ ] Whether managed keybinding changes require Codex reload/restart and how to detect it
    - [ ] Priority-source ordering inside projects
    - [ ] Reliable hands-free dictation latch execution and verification
    - [ ] Stable queue edit, reorder, send-now, and delete semantics
