use std::f32::consts::{FRAC_PI_2, PI};

use anyhow::Result;
use gpui::{
    Context, Div, MouseButton, Render, ScrollWheelEvent, Stateful, Task, Window, div, prelude::*,
    px,
};

use crate::{
    bridge::{
        ActionId, BridgeClient, CommandResponse, ResponseStatus, SessionStatus, StateSnapshot,
    },
    browser, controls,
    controls::VisualState,
    theme,
};

#[derive(Clone, Debug, PartialEq, Eq)]
enum ControlId {
    Surface,
    Session(usize),
    Action(ActionId),
    Dial,
    Joystick,
    Model,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum ConnectionState {
    Offline,
    AdapterReady,
    BrowserReady,
    Error,
}

#[derive(Clone, Debug)]
struct Feedback {
    control: ControlId,
    success: bool,
}

pub struct DeepSeekKeypad {
    bridge: BridgeClient,
    state: Option<StateSnapshot>,
    connection: ConnectionState,
    pending: Option<ControlId>,
    feedback: Option<Feedback>,
    launch_pending: bool,
    last_error: Option<String>,
    _state_poll: Task<()>,
}

impl DeepSeekKeypad {
    pub fn new(
        bridge: BridgeClient,
        platform_error: Option<String>,
        cx: &mut Context<Self>,
    ) -> Self {
        let poll_bridge = bridge.clone();
        let state_poll = cx.spawn(async move |this, cx| {
            loop {
                let should_poll = match this.read_with(cx, |view, _| view.pending.is_none()) {
                    Ok(value) => value,
                    Err(_) => break,
                };
                if should_poll {
                    let bridge = poll_bridge.clone();
                    let result = cx
                        .background_spawn(async move { bridge.read_state() })
                        .await;
                    if this
                        .update(cx, |view, cx| view.apply_state_result(result, cx))
                        .is_err()
                    {
                        break;
                    }
                }
                cx.background_executor()
                    .timer(crate::bridge::STATE_POLL_INTERVAL)
                    .await;
            }
        });

        let connection = if platform_error.is_some() {
            ConnectionState::Error
        } else {
            ConnectionState::Offline
        };
        Self {
            bridge,
            state: None,
            connection,
            pending: None,
            feedback: None,
            launch_pending: false,
            last_error: platform_error,
            _state_poll: state_poll,
        }
    }

    fn apply_state_result(&mut self, result: Result<StateSnapshot>, cx: &mut Context<Self>) {
        match result {
            Ok(state) => {
                let browser_connected = state.browser_connected();
                self.connection = if browser_connected {
                    ConnectionState::BrowserReady
                } else {
                    ConnectionState::AdapterReady
                };
                if browser_connected {
                    self.launch_pending = false;
                }
                self.state = Some(state);
                self.last_error = None;
            }
            Err(error) => {
                self.connection = ConnectionState::Offline;
                self.state = None;
                self.last_error = Some(format!("{error:#}"));
            }
        }
        cx.notify();
    }

    fn run_request(
        &mut self,
        control: ControlId,
        operation: impl FnOnce(BridgeClient) -> Result<CommandResponse> + Send + 'static,
        cx: &mut Context<Self>,
    ) {
        if self.pending.is_some() {
            return;
        }

        self.pending = Some(control.clone());
        self.feedback = None;
        cx.notify();

        let bridge = self.bridge.clone();
        cx.spawn(async move |this, cx| {
            let result = cx.background_spawn(async move { operation(bridge) }).await;
            if this
                .update(cx, |view, cx| view.finish_request(control, result, cx))
                .is_err()
            {
                // The window can close while the loopback request is in flight.
            }
        })
        .detach();
    }

    fn finish_request(
        &mut self,
        control: ControlId,
        result: Result<CommandResponse>,
        cx: &mut Context<Self>,
    ) {
        self.pending = None;
        match result {
            Ok(response) if response.success => {
                if control == ControlId::Surface && response.status == ResponseStatus::Opening {
                    self.launch_surface(cx);
                    return;
                }
                if control == ControlId::Surface
                    && matches!(
                        response.status,
                        ResponseStatus::Background | ResponseStatus::Foreground
                    )
                {
                    self.connection = ConnectionState::BrowserReady;
                }
                self.set_feedback(control, true, cx);
            }
            Ok(response) => {
                self.last_error = Some(response.message);
                if control == ControlId::Surface {
                    self.connection = ConnectionState::Error;
                }
                self.set_feedback(control, false, cx);
            }
            Err(error) => {
                self.last_error = Some(format!("{error:#}"));
                if control == ControlId::Surface {
                    self.connection = ConnectionState::Offline;
                }
                self.set_feedback(control, false, cx);
            }
        }
    }

    fn launch_surface(&mut self, cx: &mut Context<Self>) {
        if self.launch_pending {
            self.set_feedback(ControlId::Surface, true, cx);
            return;
        }

        self.launch_pending = true;
        self.pending = Some(ControlId::Surface);
        let surface_uri = self.bridge.endpoint().surface_uri();
        cx.notify();

        cx.spawn(async move |this, cx| {
            let result = cx
                .background_spawn(async move { browser::launch_dedicated_surface(&surface_uri) })
                .await;
            if this
                .update(cx, |view, cx| {
                    view.pending = None;
                    match result {
                        Ok(_) => view.set_feedback(ControlId::Surface, true, cx),
                        Err(error) => {
                            view.launch_pending = false;
                            view.connection = ConnectionState::Error;
                            view.last_error = Some(format!("{error:#}"));
                            view.set_feedback(ControlId::Surface, false, cx);
                        }
                    }
                })
                .is_err()
            {
                // The window can close while the launch guard is sleeping.
            }

            cx.background_executor()
                .timer(theme::SURFACE_LAUNCH_GUARD)
                .await;
            if this
                .update(cx, |view, cx| {
                    if view.connection != ConnectionState::BrowserReady {
                        view.launch_pending = false;
                        cx.notify();
                    }
                })
                .is_err()
            {
                // The window can close before transient feedback expires.
            }
        })
        .detach();
    }

    fn set_feedback(&mut self, control: ControlId, success: bool, cx: &mut Context<Self>) {
        self.feedback = Some(Feedback {
            control: control.clone(),
            success,
        });
        cx.notify();

        cx.spawn(async move |this, cx| {
            cx.background_executor()
                .timer(theme::FEEDBACK_DURATION)
                .await;
            if this
                .update(cx, |view, cx| {
                    if view
                        .feedback
                        .as_ref()
                        .is_some_and(|feedback| feedback.control == control)
                    {
                        view.feedback = None;
                        cx.notify();
                    }
                })
                .is_err()
            {
                // The window can close before transient feedback expires.
            }
        })
        .detach();
    }

    fn activate_surface(&mut self, cx: &mut Context<Self>) {
        self.run_request(ControlId::Surface, |bridge| bridge.activate(), cx);
    }

    fn activate_session(&mut self, index: usize, cx: &mut Context<Self>) {
        if !self.browser_ready() {
            return;
        }
        let Some(session_id) = self
            .state
            .as_ref()
            .and_then(|state| state.sessions.get(index))
            .map(|session| session.id.clone())
        else {
            return;
        };
        self.run_request(
            ControlId::Session(index),
            move |bridge| bridge.activate_session(&session_id),
            cx,
        );
    }

    fn activate_adjacent_session(&mut self, offset: isize, cx: &mut Context<Self>) {
        let Some(state) = self.state.as_ref() else {
            return;
        };
        let session_count = state.sessions.len();
        if session_count < 2 {
            return;
        }
        let current_index = state
            .current_session_id
            .as_ref()
            .and_then(|current| {
                state
                    .sessions
                    .iter()
                    .position(|session| &session.id == current)
            })
            .unwrap_or(0);
        let next_index =
            (current_index as isize + offset).rem_euclid(session_count as isize) as usize;
        self.activate_session(next_index, cx);
    }

    fn execute_action(&mut self, action_id: ActionId, control: ControlId, cx: &mut Context<Self>) {
        if !self.supports(action_id) {
            return;
        }
        self.run_request(control, move |bridge| bridge.execute_action(action_id), cx);
    }

    fn handle_dial_scroll(&mut self, event: &ScrollWheelEvent, cx: &mut Context<Self>) {
        let action_id = if event.delta.pixel_delta(px(16.0)).y < px(0.0) {
            ActionId::ComposerSelectNext
        } else {
            ActionId::ComposerSelectPrevious
        };
        self.execute_action(action_id, ControlId::Dial, cx);
    }

    fn browser_ready(&self) -> bool {
        self.connection == ConnectionState::BrowserReady
    }

    fn supports(&self, action_id: ActionId) -> bool {
        self.browser_ready()
            && self
                .state
                .as_ref()
                .is_some_and(|state| state.capabilities.supports(action_id))
    }

    fn visual_state(&self, control: &ControlId, selected: bool, disabled: bool) -> VisualState {
        if disabled {
            return VisualState::Disabled;
        }
        if self.pending.as_ref() == Some(control) {
            return VisualState::Pending;
        }
        if let Some(feedback) = self
            .feedback
            .as_ref()
            .filter(|feedback| &feedback.control == control)
        {
            return if feedback.success {
                VisualState::Success
            } else {
                VisualState::Failure
            };
        }
        if selected {
            VisualState::Selected
        } else {
            VisualState::Default
        }
    }

    fn session_signal(status: SessionStatus) -> gpui::Rgba {
        match status {
            SessionStatus::Running => theme::running_signal(),
            SessionStatus::Completed => theme::completed_signal(),
            SessionStatus::Waiting => theme::waiting_signal(),
            SessionStatus::Error => theme::error_signal(),
            SessionStatus::Idle => theme::idle_signal(),
        }
    }

    fn session_key(&self, index: usize, cx: &mut Context<Self>) -> Stateful<Div> {
        let session = self
            .state
            .as_ref()
            .and_then(|state| state.sessions.get(index));
        let selected = session.is_some_and(|session| {
            self.state
                .as_ref()
                .and_then(|state| state.current_session_id.as_ref())
                == Some(&session.id)
        });
        let disabled = !self.browser_ready()
            || session.is_none()
            || !self
                .state
                .as_ref()
                .is_some_and(|state| state.capabilities.session_activation);
        let control = ControlId::Session(index);
        let visual_state = self.visual_state(&control, selected, disabled);
        let signal = session
            .map(|session| Self::session_signal(session.status))
            .unwrap_or_else(theme::idle_signal);
        let key = controls::keycap(format!("session-{index}"), 1, visual_state)
            .child(controls::signal(signal, selected));
        if disabled {
            key
        } else {
            key.on_click(cx.listener(move |view, _, _, cx| view.activate_session(index, cx)))
        }
    }

    fn action_key(
        &self,
        id: &'static str,
        icon: &'static str,
        action_id: ActionId,
        cx: &mut Context<Self>,
    ) -> Stateful<Div> {
        let control = ControlId::Action(action_id);
        let disabled = !self.supports(action_id);
        let visual_state = self.visual_state(&control, false, disabled);
        let key = controls::keycap(id, 1, visual_state).child(controls::icon(
            icon,
            25.0,
            visual_state.icon(),
        ));
        if disabled {
            key
        } else {
            key.on_click(cx.listener(move |view, _, _, cx| {
                view.execute_action(action_id, ControlId::Action(action_id), cx)
            }))
        }
    }

    fn dial(&self, cx: &mut Context<Self>) -> Stateful<Div> {
        let control = ControlId::Dial;
        let disabled = !self.supports(ActionId::ComposerActivateSelection);
        let visual_state = self.visual_state(&control, false, disabled);
        let key = controls::keycap("dial", 1, visual_state)
            .child(controls::knob("dial-indicator.svg", visual_state));
        if disabled {
            key
        } else {
            key.on_click(cx.listener(|view, _, _, cx| {
                view.execute_action(ActionId::ComposerActivateSelection, ControlId::Dial, cx)
            }))
            .on_scroll_wheel(cx.listener(|view, event, _, cx| view.handle_dial_scroll(event, cx)))
        }
    }

    fn joystick(&self, cx: &mut Context<Self>) -> Stateful<Div> {
        let has_adjacent_sessions = self.browser_ready()
            && self
                .state
                .as_ref()
                .is_some_and(|state| state.sessions.len() > 1);
        let left_enabled = self.supports(ActionId::ToggleSidebar);
        let right_enabled = self.supports(ActionId::OpenDetails);
        let disabled = !has_adjacent_sessions && !left_enabled && !right_enabled;
        let visual_state = self.visual_state(&ControlId::Joystick, false, disabled);

        let up = controls::direction_button("joystick-up", 0.0, has_adjacent_sessions)
            .top(px(7.0))
            .left(px(29.0));
        let up = if has_adjacent_sessions {
            up.on_click(cx.listener(|view, _, _, cx| view.activate_adjacent_session(-1, cx)))
        } else {
            up
        };

        let right = controls::direction_button("joystick-right", FRAC_PI_2, right_enabled)
            .right(px(7.0))
            .top(px(29.0));
        let right = if right_enabled {
            right.on_click(cx.listener(|view, _, _, cx| {
                view.execute_action(ActionId::OpenDetails, ControlId::Joystick, cx)
            }))
        } else {
            right
        };

        let down = controls::direction_button("joystick-down", PI, has_adjacent_sessions)
            .bottom(px(7.0))
            .left(px(29.0));
        let down = if has_adjacent_sessions {
            down.on_click(cx.listener(|view, _, _, cx| view.activate_adjacent_session(1, cx)))
        } else {
            down
        };

        let left = controls::direction_button("joystick-left", PI + FRAC_PI_2, left_enabled)
            .left(px(7.0))
            .top(px(29.0));
        let left = if left_enabled {
            left.on_click(cx.listener(|view, _, _, cx| {
                view.execute_action(ActionId::ToggleSidebar, ControlId::Joystick, cx)
            }))
        } else {
            left
        };

        controls::keycap("joystick", 1, visual_state)
            .child(
                div()
                    .size(px(27.0))
                    .rounded_full()
                    .bg(theme::joystick_surface()),
            )
            .child(up)
            .child(right)
            .child(down)
            .child(left)
    }

    fn model_key(&self, cx: &mut Context<Self>) -> Stateful<Div> {
        let control = ControlId::Model;
        let disabled = !self.supports(ActionId::ToggleQuickModel);
        let visual_state = self.visual_state(&control, false, disabled);
        let key = controls::keycap("model", 1, visual_state)
            .child(controls::knob("model.svg", visual_state));
        if disabled {
            key
        } else {
            key.on_click(cx.listener(|view, _, _, cx| {
                view.execute_action(ActionId::ToggleQuickModel, ControlId::Model, cx)
            }))
        }
    }

    fn voice_key(&self) -> Stateful<Div> {
        controls::keycap("voice", 2, VisualState::Disabled).child(controls::icon(
            "microphone.svg",
            26.0,
            theme::ink_muted(),
        ))
    }

    fn deepseek_key(&self, cx: &mut Context<Self>) -> Stateful<Div> {
        let selected = self.connection == ConnectionState::BrowserReady;
        let visual_state = self.visual_state(&ControlId::Surface, selected, false);
        let signal = match self.connection {
            ConnectionState::Offline => theme::idle_signal(),
            ConnectionState::AdapterReady => theme::waiting_signal(),
            ConnectionState::BrowserReady => theme::running_signal(),
            ConnectionState::Error => theme::error_signal(),
        };
        controls::keycap("deepseek", 1, visual_state)
            .child(controls::icon("deepseek.svg", 29.0, visual_state.icon()))
            .child(
                div()
                    .absolute()
                    .right(px(9.0))
                    .top(px(9.0))
                    .size(px(8.0))
                    .rounded_full()
                    .bg(signal),
            )
            .on_click(cx.listener(|view, _, _, cx| view.activate_surface(cx)))
    }

    fn grid(&self, cx: &mut Context<Self>) -> Div {
        div()
            .size(px(theme::GRID_SIZE))
            .grid()
            .grid_cols(4)
            .grid_rows(4)
            .gap(px(theme::KEY_GAP))
            .child(self.dial(cx))
            .child(self.session_key(0, cx))
            .child(self.session_key(1, cx))
            .child(self.joystick(cx))
            .child(self.session_key(2, cx))
            .child(self.session_key(3, cx))
            .child(self.session_key(4, cx))
            .child(self.session_key(5, cx))
            .child(self.action_key("new-session", "session-new.svg", ActionId::NewSession, cx))
            .child(self.action_key(
                "conversation-view",
                "trajectory.svg",
                ActionId::ToggleConversationView,
                cx,
            ))
            .child(self.action_key("cancel-turn", "stop.svg", ActionId::CancelTurn, cx))
            .child(self.action_key("fork-session", "fork.svg", ActionId::ForkSession, cx))
            .child(self.model_key(cx))
            .child(self.voice_key())
            .child(self.deepseek_key(cx))
    }
}

impl Render for DeepSeekKeypad {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        div()
            .size_full()
            .p(px(theme::WINDOW_GUTTER))
            .bg(theme::transparent())
            .child(
                div()
                    .relative()
                    .size_full()
                    .flex()
                    .items_center()
                    .justify_center()
                    .p(px(theme::FRAME_PADDING))
                    .rounded(px(theme::FRAME_RADIUS))
                    .border_1()
                    .border_color(theme::frame_border())
                    .bg(theme::frame())
                    .shadow(theme::device_shadow())
                    .child(controls::drag_handle())
                    .child(
                        controls::close_button()
                            .on_mouse_down(MouseButton::Left, |_, _, cx| cx.stop_propagation())
                            .on_click(|_, _, cx| cx.quit()),
                    )
                    .child(self.grid(cx)),
            )
    }
}
