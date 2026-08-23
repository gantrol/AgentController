use anyhow::Error;

#[cfg(target_os = "windows")]
pub fn make_window_topmost(window: &gpui::Window) -> anyhow::Result<()> {
    use raw_window_handle::{HasWindowHandle, RawWindowHandle};
    use windows::Win32::{
        Foundation::HWND,
        UI::WindowsAndMessaging::{
            HWND_TOPMOST, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE, SetWindowPos,
        },
    };

    let handle = HasWindowHandle::window_handle(window)
        .map_err(|error| anyhow::anyhow!("GPUI returned no native window handle: {error:?}"))?;
    let RawWindowHandle::Win32(handle) = handle.as_raw() else {
        anyhow::bail!("GPUI returned a non-Windows native handle");
    };
    let hwnd = HWND(handle.hwnd.get() as *mut core::ffi::c_void);
    unsafe {
        SetWindowPos(
            hwnd,
            Some(HWND_TOPMOST),
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
        )?;
    }
    Ok(())
}

#[cfg(not(target_os = "windows"))]
pub fn make_window_topmost(_window: &gpui::Window) -> anyhow::Result<()> {
    Ok(())
}

#[cfg(target_os = "windows")]
pub fn show_startup_error(error: &Error) {
    use windows::{
        Win32::UI::WindowsAndMessaging::{MB_ICONERROR, MB_OK, MessageBoxW},
        core::HSTRING,
    };

    let message = HSTRING::from(format!("{error:#}"));
    let title = HSTRING::from("DeepSeek Keypad");
    unsafe {
        MessageBoxW(None, &message, &title, MB_OK | MB_ICONERROR);
    }
}

#[cfg(not(target_os = "windows"))]
pub fn show_startup_error(error: &Error) {
    eprintln!("DeepSeek Keypad: {error:#}");
}
