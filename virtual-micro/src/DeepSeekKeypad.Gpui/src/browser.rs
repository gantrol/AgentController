use std::{env, path::PathBuf, process::Command};

use anyhow::{Context as _, Result};
use url::Url;

#[cfg(target_os = "windows")]
pub fn launch_dedicated_surface(surface_uri: &Url) -> Result<u32> {
    let browser =
        find_app_browser().context("Microsoft Edge or Google Chrome app mode is unavailable")?;
    let child = Command::new(&browser)
        .arg(format!("--app={}", surface_uri.as_str()))
        .arg("--no-first-run")
        .arg("--no-default-browser-check")
        .spawn()
        .with_context(|| format!("failed to start {}", browser.display()))?;
    Ok(child.id())
}

#[cfg(not(target_os = "windows"))]
pub fn launch_dedicated_surface(_surface_uri: &Url) -> Result<u32> {
    anyhow::bail!("dedicated DeepSeek browser surfaces currently require Windows")
}

#[cfg(target_os = "windows")]
fn find_app_browser() -> Option<PathBuf> {
    let program_files_x86 = env::var_os("ProgramFiles(x86)").map(PathBuf::from);
    let program_files = env::var_os("ProgramFiles").map(PathBuf::from);
    let local_app_data = env::var_os("LOCALAPPDATA").map(PathBuf::from);

    [
        browser_path(
            program_files_x86.as_ref(),
            &["Microsoft", "Edge", "Application", "msedge.exe"],
        ),
        browser_path(
            program_files.as_ref(),
            &["Microsoft", "Edge", "Application", "msedge.exe"],
        ),
        browser_path(
            local_app_data.as_ref(),
            &["Microsoft", "Edge", "Application", "msedge.exe"],
        ),
        browser_path(
            program_files.as_ref(),
            &["Google", "Chrome", "Application", "chrome.exe"],
        ),
        browser_path(
            program_files_x86.as_ref(),
            &["Google", "Chrome", "Application", "chrome.exe"],
        ),
        browser_path(
            local_app_data.as_ref(),
            &["Google", "Chrome", "Application", "chrome.exe"],
        ),
    ]
    .into_iter()
    .flatten()
    .find(|path| path.is_file())
}

#[cfg(target_os = "windows")]
fn browser_path(root: Option<&PathBuf>, segments: &[&str]) -> Option<PathBuf> {
    let mut path = root?.clone();
    path.extend(segments);
    Some(path)
}
