use gpui::{Div, Rgba, SharedString, Stateful, Transformation, div, prelude::*, px, radians, svg};

use crate::theme;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum VisualState {
    Default,
    Selected,
    Pending,
    Success,
    Failure,
    Disabled,
}

impl VisualState {
    fn background(self) -> Rgba {
        match self {
            Self::Selected => theme::accent_soft(),
            Self::Disabled => theme::key_surface_disabled(),
            _ => theme::key_surface(),
        }
    }

    fn border(self) -> Rgba {
        match self {
            Self::Selected => theme::accent(),
            Self::Pending => theme::pending(),
            Self::Success => theme::success(),
            Self::Failure => theme::danger(),
            Self::Disabled => theme::key_border_disabled(),
            Self::Default => theme::key_border(),
        }
    }

    pub fn icon(self) -> Rgba {
        match self {
            Self::Selected => theme::accent(),
            Self::Pending => theme::pending(),
            Self::Success => theme::success(),
            Self::Failure => theme::danger(),
            Self::Disabled => theme::ink_muted(),
            Self::Default => theme::ink(),
        }
    }

    fn interactive(self) -> bool {
        self != Self::Disabled
    }
}

pub fn keycap(id: impl Into<SharedString>, column_span: u16, state: VisualState) -> Stateful<Div> {
    div()
        .id(id.into())
        .relative()
        .size_full()
        .col_span(column_span)
        .flex()
        .items_center()
        .justify_center()
        .rounded(px(theme::KEY_RADIUS))
        .border_1()
        .border_color(state.border())
        .bg(state.background())
        .shadow(theme::key_shadow())
        .when(state.interactive(), |this| {
            this.cursor_pointer()
                .hover(|this| this.bg(theme::key_surface_hover()))
                .active(|this| this.bg(theme::key_surface_pressed()))
        })
}

pub fn icon(path: &'static str, size: f32, color: Rgba) -> gpui::Svg {
    svg().path(path).size(px(size)).text_color(color)
}

pub fn knob(path: &'static str, state: VisualState) -> Div {
    div()
        .size(px(52.0))
        .flex()
        .items_center()
        .justify_center()
        .rounded_full()
        .border_1()
        .border_color(state.border())
        .bg(theme::key_surface())
        .child(icon(path, 36.0, state.icon()))
}

pub fn signal(color: Rgba, selected: bool) -> Div {
    div()
        .size(if selected { px(17.0) } else { px(14.0) })
        .rounded_full()
        .border_1()
        .border_color(if selected {
            theme::accent()
        } else {
            theme::frame_border()
        })
        .bg(color)
}

pub fn drag_handle() -> Stateful<Div> {
    div()
        .id("window-drag-handle")
        .absolute()
        .top(px(0.0))
        .left(px(0.0))
        .right(px(40.0))
        .h(px(theme::DRAG_HANDLE_HEIGHT))
        .cursor(gpui::CursorStyle::Arrow)
        .window_control_area(gpui::WindowControlArea::Drag)
}

pub fn close_button() -> Stateful<Div> {
    div()
        .id("window-close")
        .absolute()
        .right(px(8.0))
        .top(px(7.0))
        .size(px(26.0))
        .flex()
        .items_center()
        .justify_center()
        .cursor_pointer()
        .rounded(px(7.0))
        .text_color(theme::ink_muted())
        .hover(|this| {
            this.bg(theme::key_surface_pressed())
                .text_color(theme::danger())
        })
        .active(|this| this.bg(theme::key_border_disabled()))
        .child(icon("close.svg", 13.0, theme::ink_muted()))
}

pub fn direction_button(id: &'static str, rotation_radians: f32, enabled: bool) -> Stateful<Div> {
    div()
        .id(id)
        .absolute()
        .size(px(24.0))
        .flex()
        .items_center()
        .justify_center()
        .rounded(px(6.0))
        .when(enabled, |this| {
            this.cursor_pointer()
                .hover(|this| this.bg(theme::accent_soft()))
                .active(|this| this.bg(theme::key_surface_pressed()))
        })
        .child(
            icon(
                "chevron.svg",
                12.0,
                if enabled {
                    theme::ink()
                } else {
                    theme::ink_muted()
                },
            )
            .with_transformation(Transformation::rotate(radians(rotation_radians))),
        )
}
