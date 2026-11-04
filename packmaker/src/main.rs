#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod app;
mod download_view;
mod editor_view;
mod engine;
mod loop_view;
mod model;
mod packs_view;
mod player;
mod settings_view;
mod theme;
mod ui;

use gpui_kit::*;

fn main() {
    gpui_kit::application()
        .with_assets(gpui_kit::assets::AllAssets)
        .run(|cx| {
            gpui_kit::init(cx);
            theme::apply(&model::Settings::load(), cx);
            app::bind_keys(cx);

            let bounds = Bounds::centered(None, size(px(1320.), px(820.)), cx);
            let options = WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                window_min_size: Some(size(px(900.), px(540.))),
                titlebar: Some(TitlebarOptions {
                    title: Some("Pack Maker".into()),
                    ..Default::default()
                }),
                ..Default::default()
            };
            gpui_kit::open_window(options, cx, |window, cx| cx.new(|cx| app::PackMaker::new(window, cx)))
                .expect("failed to open the window");
            cx.activate(true);
        });
}
