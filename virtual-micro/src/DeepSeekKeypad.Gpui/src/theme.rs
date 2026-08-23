use std::time::Duration;

use gpui::{BoxShadow, Rgba, point, px, rgb, rgba};

pub const WINDOW_WIDTH: f32 = 432.0;
pub const WINDOW_HEIGHT: f32 = 452.0;

pub const WINDOW_GUTTER: f32 = 12.0;
pub const FRAME_RADIUS: f32 = 22.0;
pub const FRAME_PADDING: f32 = 20.0;
pub const DRAG_HANDLE_HEIGHT: f32 = 26.0;

pub const KEY_SIZE: f32 = 82.0;
pub const KEY_GAP: f32 = 8.0;
pub const KEY_RADIUS: f32 = 10.0;
pub const GRID_SIZE: f32 = KEY_SIZE * 4.0 + KEY_GAP * 3.0;

pub const FEEDBACK_DURATION: Duration = Duration::from_millis(850);
pub const SURFACE_LAUNCH_GUARD: Duration = Duration::from_secs(20);

pub fn transparent() -> Rgba {
    rgba(0x00000000)
}

pub fn frame() -> Rgba {
    rgb(0xf0f3f1)
}

pub fn frame_border() -> Rgba {
    rgb(0xb9c3be)
}

pub fn key_surface() -> Rgba {
    rgb(0xfafbf9)
}

pub fn key_surface_hover() -> Rgba {
    rgb(0xf4f7f4)
}

pub fn key_surface_pressed() -> Rgba {
    rgb(0xe7ece8)
}

pub fn key_surface_disabled() -> Rgba {
    rgb(0xe2e7e4)
}

pub fn key_border() -> Rgba {
    rgb(0xc5cec9)
}

pub fn key_border_disabled() -> Rgba {
    rgb(0xd2d9d5)
}

pub fn ink() -> Rgba {
    rgb(0x18211d)
}

pub fn ink_muted() -> Rgba {
    rgb(0x87928c)
}

pub fn accent() -> Rgba {
    rgb(0x176b70)
}

pub fn accent_soft() -> Rgba {
    rgb(0xe2efed)
}

pub fn pending() -> Rgba {
    rgb(0xa56b18)
}

pub fn success() -> Rgba {
    rgb(0x3f7853)
}

pub fn danger() -> Rgba {
    rgb(0xa94444)
}

pub fn idle_signal() -> Rgba {
    rgb(0x95a09a)
}

pub fn running_signal() -> Rgba {
    rgb(0x237477)
}

pub fn completed_signal() -> Rgba {
    rgb(0x4d7f58)
}

pub fn waiting_signal() -> Rgba {
    rgb(0xb27924)
}

pub fn error_signal() -> Rgba {
    danger()
}

pub fn joystick_surface() -> Rgba {
    rgb(0x333b37)
}

pub fn device_shadow() -> Vec<BoxShadow> {
    vec![BoxShadow {
        color: rgba(0x17221d38).into(),
        offset: point(px(0.0), px(7.0)),
        blur_radius: px(18.0),
        spread_radius: px(-2.0),
    }]
}

pub fn key_shadow() -> Vec<BoxShadow> {
    vec![BoxShadow {
        color: rgba(0x26342d24).into(),
        offset: point(px(0.0), px(2.0)),
        blur_radius: px(4.0),
        spread_radius: px(-1.0),
    }]
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn grid_is_composed_of_square_cells() {
        assert_eq!(GRID_SIZE, KEY_SIZE * 4.0 + KEY_GAP * 3.0);
    }
}
