use std::{borrow::Cow, fs, io::ErrorKind, path::PathBuf};

use anyhow::{Context as _, Result};
use gpui::{
    App, AppContext as _, Application, AssetSource, Bounds, KeyBinding, SharedString,
    TitlebarOptions, WindowBackgroundAppearance, WindowBounds, WindowKind, WindowOptions, actions,
    px, size,
};

use crate::{
    bridge::{BridgeClient, BridgeEndpoint},
    keypad::DeepSeekKeypad,
    platform, theme,
};

actions!(deepseek_keypad, [Quit]);

pub fn run() -> Result<()> {
    let endpoint = BridgeEndpoint::discover()?;
    let bridge = BridgeClient::new(endpoint);
    let assets = Assets::new(PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("assets"))?;

    Application::new()
        .with_assets(assets)
        .run(move |cx: &mut App| {
            let bounds = Bounds::centered(
                None,
                size(px(theme::WINDOW_WIDTH), px(theme::WINDOW_HEIGHT)),
                cx,
            );
            let bridge = bridge.clone();
            let open_result = cx.open_window(
                WindowOptions {
                    window_bounds: Some(WindowBounds::Windowed(bounds)),
                    titlebar: Some(TitlebarOptions {
                        title: Some("DeepSeek Keypad".into()),
                        appears_transparent: true,
                        ..Default::default()
                    }),
                    focus: false,
                    kind: WindowKind::PopUp,
                    is_movable: true,
                    is_resizable: false,
                    is_minimizable: false,
                    window_background: WindowBackgroundAppearance::Transparent,
                    ..Default::default()
                },
                move |window, cx| {
                    let platform_error = platform::make_window_topmost(window)
                        .err()
                        .map(|error| format!("{error:#}"));
                    cx.new(|cx| DeepSeekKeypad::new(bridge, platform_error, cx))
                },
            );

            if let Err(error) = open_result {
                platform::show_startup_error(&error);
                cx.quit();
                return;
            }

            cx.on_action(|_: &Quit, cx| cx.quit());
            cx.bind_keys([KeyBinding::new("escape", Quit, None)]);
            cx.activate(true);
        });

    Ok(())
}

struct Assets {
    base: PathBuf,
}

impl Assets {
    fn new(base: PathBuf) -> Result<Self> {
        if !base.is_dir() {
            anyhow::bail!("asset directory does not exist: {}", base.display());
        }
        Ok(Self { base })
    }
}

impl AssetSource for Assets {
    fn load(&self, path: &str) -> Result<Option<Cow<'static, [u8]>>> {
        match fs::read(self.base.join(path)) {
            Ok(bytes) => Ok(Some(Cow::Owned(bytes))),
            Err(error) if error.kind() == ErrorKind::NotFound => Ok(None),
            Err(error) => Err(error).with_context(|| format!("failed to load asset: {path}")),
        }
    }

    fn list(&self, path: &str) -> Result<Vec<SharedString>> {
        fs::read_dir(self.base.join(path))?
            .map(|entry| {
                let entry = entry?;
                entry
                    .file_name()
                    .into_string()
                    .map(SharedString::from)
                    .map_err(|_| anyhow::anyhow!("asset path is not UTF-8"))
            })
            .collect()
    }
}
