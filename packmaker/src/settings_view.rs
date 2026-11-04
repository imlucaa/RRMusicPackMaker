//! Settings, shown in a dialog: folders, how songs are made, downloads, FFmpeg and the look.
//! Changes are saved straight away and shared with the classic window.

use gpui_kit::component::{
    ActiveTheme as _, Sizable as _,
    scroll::ScrollableElement as _,
    button::{Button, ButtonVariants as _},
    h_flex,
    input::{Input, InputEvent, InputState},
    switch::Switch,
    v_flex,
};
use gpui_kit::prelude::FluentBuilder as _;
use gpui_kit::*;
use serde_json::json;

use crate::app::PackMaker;
use crate::model::{LOUDNESS, QUALITIES, SAMPLE_RATES};
use crate::theme::{self, PRESETS, SIZES};
use crate::ui::{self, JobNews};

pub struct SettingsView {
    maker: WeakEntity<PackMaker>,
    rr_folder: Entity<InputState>,
    output: Entity<InputState>,
    game: Entity<InputState>,
    accent: Entity<InputState>,
    background: Entity<InputState>,
    author: Entity<InputState>,
    ffmpeg: Option<bool>,
    installing: Option<String>,
    _subscriptions: Vec<Subscription>,
}

const ACCENTS: &[&str] = &["#E8963E", "#F2C14E", "#4FB477", "#3FA7D6", "#7A6CF0", "#E05A9C", "#E5484D", "#FFFFFF"];

impl SettingsView {
    /// The current values come in as arguments: the main window is mid-update when this is made, so it can't be read.
    pub fn new(
        maker: WeakEntity<PackMaker>,
        settings: std::collections::HashMap<String, String>,
        project_rr: String,
        author: String,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> Self {
        let text = |key: &str| settings.get(key).cloned().unwrap_or_default();
        let input = |value: String, hint: &str, window: &mut Window, cx: &mut Context<Self>| {
            let hint = hint.to_string();
            cx.new(|cx| InputState::new(window, cx).placeholder(hint).default_value(value))
        };
        let rr_folder = input(project_rr, "Found by itself when empty", window, cx);
        let output = input(text("OutputFolder"), "Documents\\RR Music Packs", window, cx);
        let game = input(text("GameFile"), "Only needed to raise the music volume", window, cx);
        let accent = input(text("AccentColor"), "#RRGGBB, empty for the theme's own", window, cx);
        let background = input(text("BackgroundColor"), "#RRGGBB, empty for the theme's own", window, cx);
        let author = input(author, "Your name, for the pack's credits", window, cx);

        let mut subscriptions = Vec::new();
        for (state, key) in [(&output, "OutputFolder"), (&game, "GameFile")] {
            subscriptions.push(cx.subscribe_in(state, window, move |this, state, event, _, cx| {
                if matches!(event, InputEvent::Change) {
                    let value = state.read(cx).value().trim().to_string();
                    this.set(key, json!(value), cx);
                }
            }));
        }
        for (state, key) in [(&accent, "AccentColor"), (&background, "BackgroundColor")] {
            subscriptions.push(cx.subscribe_in(state, window, move |this, state, event, _, cx| {
                if matches!(event, InputEvent::Change) {
                    let value = state.read(cx).value().trim().to_string();
                    // Only a whole color, or nothing, changes the look; half-typed ones wait.
                    if value.is_empty() || theme::parse_hex(&value).is_some() {
                        this.set(key, json!(value), cx);
                        this.restyle(cx);
                    }
                }
            }));
        }
        subscriptions.push(cx.subscribe_in(&rr_folder, window, |this, state, event, window, cx| {
            if matches!(event, InputEvent::PressEnter { .. } | InputEvent::Blur) {
                let value = state.read(cx).value().trim().to_string();
                let _ = this.maker.update(cx, |m, cx| m.set_rr_folder(value, window, cx));
            }
        }));
        subscriptions.push(cx.subscribe_in(&author, window, |this, state, event, _, cx| {
            if matches!(event, InputEvent::Change) {
                let value = state.read(cx).value().to_string();
                let _ = this.maker.update(cx, |m, cx| m.set_project_option("Author", json!(value), cx));
            }
        }));

        Self { maker, rr_folder, output, game, accent, background, author, ffmpeg: None, installing: None, _subscriptions: subscriptions }
    }

    fn set(&mut self, key: &'static str, value: serde_json::Value, cx: &mut Context<Self>) {
        let _ = self.maker.update(cx, |m, cx| m.set_setting(key, value, cx));
        cx.notify();
    }

    fn restyle(&mut self, cx: &mut Context<Self>) {
        let _ = self.maker.update(cx, |m, cx| m.restyle(cx));
    }

    fn setting_text(&self, key: &str, cx: &App) -> String {
        self.maker.read_with(cx, |m, _| m.settings_snapshot().get(key).cloned().unwrap_or_default()).unwrap_or_default()
    }

    fn setting_number(&self, key: &str, fallback: i64, cx: &App) -> i64 {
        self.maker.read_with(cx, |m, _| m.setting_number(key, fallback)).unwrap_or(fallback)
    }

    fn setting_flag(&self, key: &str, fallback: bool, cx: &App) -> bool {
        self.maker.read_with(cx, |m, _| m.setting_flag(key, fallback)).unwrap_or(fallback)
    }

    fn option_flag(&self, key: &str, fallback: bool, cx: &App) -> bool {
        self.maker.read_with(cx, |m, _| m.project_flag(key, fallback)).unwrap_or(fallback)
    }

    /// Picks a file or folder, shows it, and saves it under the setting it belongs to.
    fn browse(&mut self, target: Entity<InputState>, key: &'static str, folder: bool, window: &mut Window, cx: &mut Context<Self>) {
        let options = if folder { ui::folder("Choose") } else { ui::files("Choose", false) };
        ui::pick_paths(cx.weak_entity(), options, window, cx, move |this, paths, window, cx| {
            let Some(path) = paths.into_iter().next() else {
                return;
            };
            let text = path.to_string_lossy().into_owned();
            target.update(cx, |input, cx| input.set_value(text.clone(), window, cx));
            if key == "RetroRewindFolder" {
                let _ = this.maker.update(cx, |m, cx| m.set_rr_folder(text, window, cx));
            } else {
                this.set(key, json!(text), cx);
            }
        });
    }

    fn install_ffmpeg(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        self.installing = Some("Starting…".into());
        cx.notify();
        ui::run_job(
            cx.weak_entity(),
            vec!["get-ffmpeg".into()],
            None,
            window,
            cx,
            |this, news, _| match news {
                JobNews::Progress(done, _) => this.installing = Some(format!("Downloading {done}%")),
                JobNews::Log(message) => this.installing = Some(message),
            },
            |this, result, _, cx| {
                this.installing = None;
                this.ffmpeg = Some(result.is_ok());
                let message = match result {
                    Ok(done) => done.message.unwrap_or_else(|| "FFmpeg is installed.".into()),
                    Err(error) => format!("Couldn't install FFmpeg. {error}"),
                };
                let _ = this.maker.update(cx, |m, cx| m.say(message, cx));
                cx.notify();
            },
        );
    }

    fn switch_row(
        &self,
        id: &'static str,
        label: &'static str,
        hint: &'static str,
        checked: bool,
        cx: &mut Context<Self>,
        set: impl Fn(&mut Self, bool, &mut Context<Self>) + 'static,
    ) -> impl IntoElement {
        v_flex()
            .py_1()
            .gap_0p5()
            .child(Switch::new(id).small().label(label).checked(checked).on_change(cx.listener(move |this, value: &bool, _, cx| set(this, *value, cx))))
            .when(!hint.is_empty(), |el| el.child(div().pl_10().text_xs().text_color(cx.theme().muted_foreground).child(hint)))
    }
}

impl Render for SettingsView {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let rate = self.maker.read_with(cx, |m, _| m.project_number("SampleRate", 32000)).unwrap_or(32000);
        let loudness = self.maker.read_with(cx, |m, _| m.project_text("Loudness", LOUDNESS[0])).unwrap_or_default();
        let kbps = self.setting_number("DownloadKbps", 0, cx);
        let size = self.setting_number("UiScale", 100, cx);
        let current = theme::preset_by_id(&self.setting_text("GpuiTheme", cx)).id;
        let picture = self.setting_text("BackgroundImage", cx);
        let ffmpeg = self.ffmpeg.unwrap_or_else(|| self.maker.read_with(cx, |m, _| m.ffmpeg_ready()).unwrap_or(false));

        let rate_ix = SAMPLE_RATES.iter().position(|r| *r == rate).unwrap_or(0);
        let loud_ix = LOUDNESS.iter().position(|l| *l == loudness).unwrap_or(0);
        let kbps_ix = QUALITIES.iter().position(|(_, k)| *k == kbps).unwrap_or(0);
        let size_ix = SIZES.iter().position(|s| *s == size).unwrap_or(2);
        let maker = self.maker.clone();

        let browse_row = |id: &'static str, label: &'static str, key: &'static str, state: &Entity<InputState>, folder: bool, cx: &mut Context<Self>| {
            let target = state.clone();
            ui::field(
                label,
                h_flex()
                    .gap_1()
                    .child(div().flex_1().child(Input::new(state).small().cleanable(true)))
                    .child(Button::new(id).small().outline().label("Browse").on_click(cx.listener(move |this, _, window, cx| {
                        this.browse(target.clone(), key, folder, window, cx)
                    }))),
                cx,
            )
        };

        v_flex()
            .id("settings")
            .w_full()
            .max_h(px(600.))
            .overflow_y_scrollbar()
            .pr_2()
            .gap_0p5()
            .child(ui::section("Look", cx))
            .child(
                h_flex().flex_wrap().gap_2().py_1().children(PRESETS.iter().map(|preset| {
                    let (back, accent) = theme::swatch(preset);
                    let id = preset.id;
                    let selected = id == current;
                    h_flex()
                        .id(SharedString::from(format!("preset-{id}")))
                        .gap_2()
                        .px_2()
                        .h_8()
                        .rounded(cx.theme().radius)
                        .border_1()
                        .border_color(if selected { cx.theme().primary } else { cx.theme().border })
                        .hover(|el| el.bg(cx.theme().list_hover))
                        .child(
                            div().size_4().rounded_full().bg(back).border_1().border_color(cx.theme().border).child(
                                div().size_2().m_0p5().rounded_full().bg(accent),
                            ),
                        )
                        .child(div().text_xs().child(preset.name))
                        .on_click(cx.listener(move |this, _, window, cx| {
                            this.set("GpuiTheme", json!(id), cx);
                            // A preset brings its own colors, so custom ones are cleared.
                            this.set("AccentColor", json!(""), cx);
                            this.set("BackgroundColor", json!(""), cx);
                            this.accent.update(cx, |input, cx| input.set_value("", window, cx));
                            this.background.update(cx, |input, cx| input.set_value("", window, cx));
                            this.restyle(cx);
                        }))
                })),
            )
            .child(ui::field(
                "Accent",
                v_flex()
                    .gap_1()
                    .h(px(64.))
                    .child(Input::new(&self.accent).small().cleanable(true))
                    .child(h_flex().gap_1().children(ACCENTS.iter().map(|hex| {
                        let color = theme::parse_hex(hex).unwrap_or_default();
                        let hex = hex.to_string();
                        div()
                            .id(SharedString::from(format!("accent-{hex}")))
                            .size_5()
                            .rounded(cx.theme().radius)
                            .bg(color)
                            .border_1()
                            .border_color(cx.theme().border)
                            .on_click(cx.listener(move |this, _, window, cx| {
                                let hex = hex.clone();
                                this.accent.update(cx, |input, cx| input.set_value(hex.clone(), window, cx));
                                this.set("AccentColor", json!(hex), cx);
                                this.restyle(cx);
                            }))
                    }))),
                cx,
            ))
            .child(ui::field("Background", Input::new(&self.background).small().cleanable(true), cx))
            .child(ui::field(
                "Picture",
                h_flex()
                    .gap_1()
                    .child(div().flex_1().truncate().text_xs().text_color(cx.theme().muted_foreground).child(if picture.is_empty() { "None".to_string() } else { picture.clone() }))
                    .child(Button::new("pic").small().outline().label("Choose").on_click(cx.listener(|_, _, window, cx| {
                        ui::pick_paths(cx.weak_entity(), ui::files("Background picture", false), window, cx, |this, paths, _, cx| {
                            if let Some(path) = paths.into_iter().next() {
                                this.set("BackgroundImage", json!(path.to_string_lossy()), cx);
                                this.restyle(cx);
                            }
                        });
                    })))
                    .when(!picture.is_empty(), |el| {
                        el.child(Button::new("pic-clear").small().ghost().label("Remove").on_click(cx.listener(|this, _, _, cx| {
                            this.set("BackgroundImage", json!(""), cx);
                            this.restyle(cx);
                        })))
                    }),
                cx,
            ))
            .child(ui::stepper("size", "App size", format!("{}%", SIZES[size_ix]), cx, {
                let me = cx.weak_entity();
                move |_, cx| {
                    let _ = me.update(cx, |this, cx| {
                        this.set("UiScale", json!(SIZES[size_ix.saturating_sub(1)]), cx);
                        this.restyle(cx);
                    });
                }
            }, {
                let me = cx.weak_entity();
                move |_, cx| {
                    let _ = me.update(cx, |this, cx| {
                        this.set("UiScale", json!(SIZES[(size_ix + 1).min(SIZES.len() - 1)]), cx);
                        this.restyle(cx);
                    });
                }
            }))
            .child(ui::section("Folders", cx))
            .child(browse_row("rr-browse", "Retro Rewind", "RetroRewindFolder", &self.rr_folder, true, cx))
            .child(browse_row("out-browse", "Packs go to", "OutputFolder", &self.output, true, cx))
            .child(browse_row("game-browse", "Game file", "GameFile", &self.game, false, cx))
            .child(ui::field("Author", Input::new(&self.author).small(), cx))
            .child(ui::section("Making songs", cx))
            .child(step_option("rate", "Sample rate", format!("{rate} Hz"), SAMPLE_RATES.len(), rate_ix, |m, ix, cx| m.set_project_option("SampleRate", json!(SAMPLE_RATES[ix]), cx), maker.clone(), cx))
            .child(step_option("loud", "Loudness", LOUDNESS[loud_ix].to_string(), LOUDNESS.len(), loud_ix, |m, ix, cx| m.set_project_option("Loudness", json!(LOUDNESS[ix]), cx), maker.clone(), cx))
            .child(self.switch_row("race", "Make race music louder", "The game's race mix is quiet, so race songs are made a little louder.", self.option_flag("RaceBoost", true, cx), cx, |this, v, cx| {
                let _ = this.maker.update(cx, |m, cx| m.set_project_option("RaceBoost", json!(v), cx));
            }))
            .child(self.switch_row("boost", "Raise the music volume in the game", "Adds your own copy of revo_kart.brsar with louder music slots when you add the pack to the game.", self.setting_flag("BoostMusicSlots", true, cx), cx, |this, v, cx| this.set("BoostMusicSlots", json!(v), cx)))
            .child(self.switch_row("final", "Write stock final lap files (_f)", "", self.option_flag("StockFinalLapFiles", true, cx), cx, |this, v, cx| {
                let _ = this.maker.update(cx, |m, cx| m.set_project_option("StockFinalLapFiles", json!(v), cx));
            }))
            .child(self.switch_row("multi", "Multi-channel stock slots", "Only for CTGP or other mods. Retro Rewind doesn't need it.", self.option_flag("MultiChannelSlots", false, cx), cx, |this, v, cx| {
                let _ = this.maker.update(cx, |m, cx| m.set_project_option("MultiChannelSlots", json!(v), cx));
            }))
            .child(self.switch_row("original", "Original file names for the 32 original tracks", "For CTGP or the original game, like n_Farm_n. Retro Rewind doesn't use them.", self.option_flag("OriginalNames", false, cx), cx, |this, v, cx| {
                let _ = this.maker.update(cx, |m, cx| m.set_project_option("OriginalNames", json!(v), cx));
            }))
            .child(self.switch_row("wii", "Made for a real Wii", "Songs above 44100 Hz are brought down so a Wii can play them. Off keeps them as they are, for Dolphin.", self.option_flag("TargetWii", true, cx), cx, |this, v, cx| {
                let _ = this.maker.update(cx, |m, cx| m.set_project_option("TargetWii", json!(v), cx));
            }))
            .child(ui::section("Downloads", cx))
            .child(ui::stepper("kbps", "Quality", QUALITIES[kbps_ix].0, cx, {
                let me = cx.weak_entity();
                move |_, cx| {
                    let _ = me.update(cx, |this, cx| this.set("DownloadKbps", json!(QUALITIES[ui::wrap(kbps_ix, -1, QUALITIES.len())].1), cx));
                }
            }, {
                let me = cx.weak_entity();
                move |_, cx| {
                    let _ = me.update(cx, |this, cx| this.set("DownloadKbps", json!(QUALITIES[ui::wrap(kbps_ix, 1, QUALITIES.len())].1), cx));
                }
            }))
            .child(ui::field(
                "FFmpeg",
                h_flex()
                    .gap_2()
                    .child(div().flex_1().text_xs().text_color(cx.theme().muted_foreground).child(match &self.installing {
                        Some(news) => news.clone(),
                        None if ffmpeg => "Installed. It converts your songs.".into(),
                        None => "Not installed. It's needed to make packs.".into(),
                    }))
                    .child(
                        Button::new("ffmpeg")
                            .small()
                            .map(|b| if ffmpeg { b.outline() } else { b.primary() })
                            .loading(self.installing.is_some())
                            .label(if ffmpeg { "Reinstall" } else { "Install" })
                            .on_click(cx.listener(|this, _, window, cx| this.install_ffmpeg(window, cx))),
                    ),
                cx,
            ))
            .child(ui::section("Other", cx))
            .child(self.switch_row("updates", "Tell me when a new version is out", "", self.setting_flag("CheckForUpdates", true, cx), cx, |this, v, cx| this.set("CheckForUpdates", json!(v), cx)))
    }
}

/// A row that steps a project setting through a list of choices.
fn step_option(
    id: &'static str,
    label: &'static str,
    value: String,
    list_len: usize,
    at: usize,
    apply: fn(&mut PackMaker, usize, &mut Context<PackMaker>),
    maker: WeakEntity<PackMaker>,
    cx: &App,
) -> impl IntoElement {
    let (down, up) = (maker.clone(), maker);
    ui::stepper(
        id,
        label,
        value,
        cx,
        move |_, cx| {
            let _ = down.update(cx, |m, cx| apply(m, ui::wrap(at, -1, list_len), cx));
        },
        move |_, cx| {
            let _ = up.update(cx, |m, cx| apply(m, ui::wrap(at, 1, list_len), cx));
        },
    )
}
