#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

mod app;
mod bridge;
mod browser;
mod controls;
mod keypad;
mod platform;
mod theme;

fn main() {
    if let Err(error) = app::run() {
        platform::show_startup_error(&error);
    }
}
