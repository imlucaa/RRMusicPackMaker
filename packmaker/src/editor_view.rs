//! The song editor: parts of songs played one after another, which can be trimmed, split, moved, blended, sped up
//! and faded, then saved as a new song. The parts are kept in `editor.json`, the same file the classic window uses.

use std::path::PathBuf;

use gpui_kit::component::{
    ActiveTheme as _, Sizable as _,
    button::{Button, ButtonVariants as _},
    h_flex,
    input::{Input, InputState},
    v_flex,
};
use gpui_kit::base::Disableable as _;
use gpui_kit::prelude::FluentBuilder as _;
use gpui_kit::*;
use serde_json::{Value, json};

use crate::app::PackMaker;
use crate::model::{self, FADES, SPEEDS};
use crate::ui;

/// The editor works at this rate; part positions are counted in its samples.
const RATE: f64 = 44100.0;
const BLENDS: &[(&str, f64)] = &[("Off", 0.0), ("0.1 s", 0.1), ("0.5 s", 0.5), ("1 s", 1.0), ("2 s", 2.0), ("4 s", 4.0)];

#[derive(Clone)]
struct Part {
    source: String,
    start: i64,
    end: i64,
    /// The whole song's length, so a trim can't run past it.
    total: i64,
}

impl Part {
    fn name(&self) -> String {
        std::path::Path::new(&self.source).file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default()
    }

    fn seconds(&self) -> f64 {
        (self.end - self.start) as f64 / RATE
    }
}

pub struct EditorView {
    maker: WeakEntity<PackMaker>,
    parts: Vec<Part>,
    selected: Option<usize>,
    name: Entity<InputState>,
    blend: usize,
    speed: usize,
    fade_in: usize,
    fade_out: usize,
    busy: Option<String>,
    message: SharedString,
}

fn state_path() -> PathBuf {
    model::data_folder().join("editor.json")
}

impl EditorView {
    /// True while the song name box has the keyboard.
    pub fn typing(&self, window: &Window, cx: &App) -> bool {
        self.name.read(cx).focus_handle(cx).is_focused(window)
    }

    pub fn new(maker: WeakEntity<PackMaker>, window: &mut Window, cx: &mut Context<Self>) -> Self {
        let saved: Value = std::fs::read_to_string(state_path())
            .ok()
            .and_then(|text| serde_json::from_str(text.trim_start_matches('\u{feff}')).ok())
            .unwrap_or(Value::Null);
        let parts = saved["Clips"]
            .as_array()
            .map(|clips| {
                clips
                    .iter()
                    .filter_map(|c| {
                        let source = c["Source"].as_str()?.to_string();
                        let end = c["End"].as_i64()?;
                        std::path::Path::new(&source).exists().then(|| Part { source, start: c["Start"].as_i64().unwrap_or(0), end, total: end })
                    })
                    .collect()
            })
            .unwrap_or_default();
        let index = |key: &str, len: usize| (saved[key].as_i64().unwrap_or(0).max(0) as usize).min(len - 1);
        let name_text = saved["Name"].as_str().unwrap_or("").to_string();
        let name = cx.new(|cx| InputState::new(window, cx).placeholder("Name of the new song").default_value(name_text));
        Self {
            maker,
            parts,
            selected: None,
            name,
            blend: index("Blend", BLENDS.len()),
            speed: index("Speed", SPEEDS.len()),
            fade_in: index("FadeIn", FADES.len()),
            fade_out: index("FadeOut", FADES.len()),
            busy: None,
            message: "".into(),
        }
    }

    /// Remembers the parts, in the classic window's format, so either app can carry on with the edit.
    fn remember(&self, cx: &App) {
        let state = json!({
            "Clips": self.parts.iter().map(|p| json!({ "Source": p.source, "Start": p.start, "End": p.end })).collect::<Vec<_>>(),
            "Name": self.name.read(cx).value().to_string(),
            "Blend": self.blend,
            "Speed": self.speed,
            "FadeIn": self.fade_in,
            "FadeOut": self.fade_out,
        });
        let _ = std::fs::write(state_path(), serde_json::to_string_pretty(&state).unwrap_or_default());
    }

    fn changed(&mut self, cx: &mut Context<Self>) {
        self.remember(cx);
        cx.notify();
    }

    /// Adds whole songs to the end, reading how long each is first.
    pub fn add_songs(&mut self, songs: Vec<PathBuf>, window: &mut Window, cx: &mut Context<Self>) {
        for song in songs {
            let source = crate::model::keep_copy(&song).to_string_lossy().into_owned();
            self.busy = Some(format!("Reading {}…", song.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()));
            cx.notify();
            ui::run_job(cx.weak_entity(), vec!["length".into(), "--song".into(), source.clone()], None, window, cx, |_, _, _| {}, move |this, result, _, cx| {
                this.busy = None;
                match result {
                    Ok(done) => {
                        let samples = done.extra.get("samples").and_then(|v| v.as_i64()).unwrap_or(0);
                        if samples > 0 {
                            this.parts.push(Part { source: source.clone(), start: 0, end: samples, total: samples });
                            this.selected = Some(this.parts.len() - 1);
                        }
                    }
                    Err(error) => this.message = format!("Couldn't read that song. {error}").into(),
                }
                this.changed(cx);
            });
        }
    }

    fn with_selected(&mut self, cx: &mut Context<Self>, edit: impl FnOnce(&mut Vec<Part>, usize) -> Option<usize>) {
        if let Some(ix) = self.selected.filter(|ix| *ix < self.parts.len()) {
            self.selected = edit(&mut self.parts, ix);
            self.changed(cx);
        }
    }

    fn trim(&mut self, at_start: bool, seconds: f64, cx: &mut Context<Self>) {
        let samples = (seconds * RATE) as i64;
        self.with_selected(cx, |parts, ix| {
            let part = &mut parts[ix];
            let shortest = (RATE / 100.0) as i64;
            if at_start {
                part.start = (part.start + samples).clamp(0, part.end - shortest);
            } else {
                part.end = (part.end + samples).clamp(part.start + shortest, part.total.max(part.end));
            }
            Some(ix)
        });
    }

    fn spec(&self, cx: &App) -> Value {
        json!({
            "clips": self.parts.iter().map(|p| json!({ "source": p.source, "start": p.start, "end": p.end })).collect::<Vec<_>>(),
            "name": self.name.read(cx).value().to_string(),
            "blend": (BLENDS[self.blend].1 * RATE) as i64,
            "speed": self.speed,
            "fadeIn": FADES[self.fade_in].1,
            "fadeOut": FADES[self.fade_out].1,
        })
    }

    fn render_edit(&mut self, save: bool, window: &mut Window, cx: &mut Context<Self>) {
        if self.parts.is_empty() || self.busy.is_some() {
            return;
        }
        self.remember(cx);
        self.busy = Some(if save { "Saving the song…" } else { "Getting it ready to play…" }.into());
        cx.notify();
        let mut args = vec!["render-edit".to_string()];
        if save {
            args.push("--save".into());
        }
        let spec = self.spec(cx);
        ui::run_job(cx.weak_entity(), args, Some(spec), window, cx, |_, _, _| {}, move |this, result, window, cx| {
            this.busy = None;
            match result {
                Ok(done) if save => {
                    this.message = done.message.unwrap_or_default().into();
                    let _ = this.maker.update(cx, |m, cx| m.songs_changed(cx));
                }
                Ok(done) => {
                    if let Some(path) = done.path {
                        let _ = this.maker.update(cx, |m, cx| m.play_file(path.into(), "Editor".into(), window, cx));
                    }
                }
                Err(error) => this.message = format!("Couldn't make it. {error}").into(),
            }
            cx.notify();
        });
    }

    fn render_timeline(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = cx.theme();
        let total: f64 = self.parts.iter().map(Part::seconds).sum::<f64>().max(0.001);
        h_flex()
            .id("timeline")
            .h_16()
            .w_full()
            .gap_px()
            .rounded(theme.radius)
            .bg(theme.list_even)
            .border_1()
            .border_color(theme.border)
            .p_1()
            .children(self.parts.iter().enumerate().map(|(ix, part)| {
                let selected = self.selected == Some(ix);
                v_flex()
                    .id(("part", ix))
                    .h_full()
                    .flex_basis(px(0.))
                    .flex_grow((part.seconds() / total) as f32)
                    .min_w_6()
                    .px_1()
                    .justify_center()
                    .rounded(theme.radius)
                    .bg(if selected { theme.primary.opacity(0.35) } else { theme.primary.opacity(0.14) })
                    .border_1()
                    .border_color(if selected { theme.primary } else { theme.border })
                    .text_xs()
                    .overflow_hidden()
                    .child(div().truncate().child(part.name()))
                    .child(div().truncate().text_color(theme.muted_foreground).child(model::format_time(part.seconds())))
                    .on_click(cx.listener(move |this, _, _, cx| {
                        this.selected = Some(ix);
                        cx.notify();
                    }))
            }))
    }
}

impl Render for EditorView {
    fn render(&mut self, _: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let total: f64 = self.parts.iter().map(Part::seconds).sum();
        let selected = self.selected.and_then(|ix| self.parts.get(ix)).cloned();
        let busy = self.busy.is_some();
        let me = cx.weak_entity();
        let tool = |id: &'static str, label: &'static str| Button::new(id).small().outline().label(label);

        v_flex()
            .size_full()
            .p_3()
            .gap_3()
            .child(
                h_flex()
                    .gap_2()
                    .child(div().text_base().font_weight(FontWeight::SEMIBOLD).child("Song editor"))
                    .child(div().text_xs().text_color(cx.theme().muted_foreground).child(format!("{} parts · {}", self.parts.len(), model::format_time(total))))
                    .child(div().flex_1())
                    .child(Button::new("ed-add").small().icon(gpui_kit::assets::IconName::Plus).label("Add songs").on_click(cx.listener(|_, _, window, cx| {
                        ui::pick_paths(cx.weak_entity(), ui::files("Add to the editor", true), window, cx, |this, paths, window, cx| {
                            this.add_songs(model::audio_files(&paths), window, cx)
                        });
                    })))
                    .child(Button::new("ed-clear").small().ghost().label("Clear").disabled(self.parts.is_empty()).on_click(cx.listener(|this, _, _, cx| {
                        this.parts.clear();
                        this.selected = None;
                        this.changed(cx);
                    }))),
            )
            .child(if self.parts.is_empty() {
                div()
                    .h_16()
                    .flex()
                    .items_center()
                    .justify_center()
                    .rounded(cx.theme().radius)
                    .border_1()
                    .border_dashed()
                    .border_color(cx.theme().border)
                    .text_color(cx.theme().muted_foreground)
                    .child("Add songs, or send them here from the Songs tab.")
                    .into_any_element()
            } else {
                self.render_timeline(cx).into_any_element()
            })
            .child(
                h_flex()
                    .flex_wrap()
                    .gap_2()
                    .child(tool("ed-left", "Move left").disabled(selected.is_none()).on_click(cx.listener(|this, _, _, cx| {
                        this.with_selected(cx, |parts, ix| {
                            if ix == 0 { return Some(ix) }
                            parts.swap(ix, ix - 1);
                            Some(ix - 1)
                        })
                    })))
                    .child(tool("ed-right", "Move right").disabled(selected.is_none()).on_click(cx.listener(|this, _, _, cx| {
                        this.with_selected(cx, |parts, ix| {
                            if ix + 1 >= parts.len() { return Some(ix) }
                            parts.swap(ix, ix + 1);
                            Some(ix + 1)
                        })
                    })))
                    .child(tool("ed-split", "Split in half").disabled(selected.is_none()).on_click(cx.listener(|this, _, _, cx| {
                        this.with_selected(cx, |parts, ix| {
                            let part = parts[ix].clone();
                            let middle = part.start + (part.end - part.start) / 2;
                            parts[ix].end = middle;
                            parts.insert(ix + 1, Part { start: middle, ..part });
                            Some(ix + 1)
                        })
                    })))
                    .child(tool("ed-copy", "Copy part").disabled(selected.is_none()).on_click(cx.listener(|this, _, _, cx| {
                        this.with_selected(cx, |parts, ix| {
                            parts.insert(ix + 1, parts[ix].clone());
                            Some(ix + 1)
                        })
                    })))
                    .child(tool("ed-even", "Same length for all").disabled(self.parts.len() < 2).on_click(cx.listener(|this, _, _, cx| {
                        // Every part plays for as long as the shortest one, like the classic window's merge.
                        if let Some(shortest) = this.parts.iter().map(|p| p.end - p.start).min() {
                            for part in &mut this.parts {
                                part.end = part.start + shortest;
                            }
                            this.changed(cx);
                        }
                    })))
                    .child(Button::new("ed-remove").small().ghost().label("Remove part").disabled(selected.is_none()).on_click(cx.listener(|this, _, _, cx| {
                        this.with_selected(cx, |parts, ix| {
                            parts.remove(ix);
                            (!parts.is_empty()).then(|| ix.min(parts.len() - 1))
                        })
                    }))),
            )
            .when_some(selected, |el, part| {
                let trim = |id: &'static str, label: &'static str, at_start: bool, by: f64, me: WeakEntity<EditorView>| {
                    Button::new(id).xsmall().ghost().label(label).on_click(move |_, _, cx| {
                        let _ = me.update(cx, |this, cx| this.trim(at_start, by, cx));
                    })
                };
                el.child(ui::section(format!("Part: {}", part.name()), cx))
                    .child(ui::field(
                        "Starts at",
                        h_flex()
                            .gap_1()
                            .child(trim("s-1", "−1 s", true, -1.0, me.clone()))
                            .child(trim("s-01", "−0.1", true, -0.1, me.clone()))
                            .child(div().w_20().text_center().child(model::format_time(part.start as f64 / RATE)))
                            .child(trim("s+01", "+0.1", true, 0.1, me.clone()))
                            .child(trim("s+1", "+1 s", true, 1.0, me.clone())),
                        cx,
                    ))
                    .child(ui::field(
                        "Ends at",
                        h_flex()
                            .gap_1()
                            .child(trim("e-1", "−1 s", false, -1.0, me.clone()))
                            .child(trim("e-01", "−0.1", false, -0.1, me.clone()))
                            .child(div().w_20().text_center().child(model::format_time(part.end as f64 / RATE)))
                            .child(trim("e+01", "+0.1", false, 0.1, me.clone()))
                            .child(trim("e+1", "+1 s", false, 1.0, me.clone())),
                        cx,
                    ))
            })
            .child(ui::section("The new song", cx))
            .child(ui::field("Name", Input::new(&self.name).small(), cx))
            .child(step("ed-blend", "Blend parts", BLENDS[self.blend].0, BLENDS.len(), |e| e.blend, |e, i| e.blend = i, me.clone(), cx))
            .child(step("ed-speed", "Speed", SPEEDS[self.speed].text, SPEEDS.len(), |e| e.speed, |e, i| e.speed = i, me.clone(), cx))
            .child(step("ed-in", "Fade in", FADES[self.fade_in].0, FADES.len(), |e| e.fade_in, |e, i| e.fade_in = i, me.clone(), cx))
            .child(step("ed-out", "Fade out", FADES[self.fade_out].0, FADES.len(), |e| e.fade_out, |e, i| e.fade_out = i, me.clone(), cx))
            .child(
                h_flex()
                    .gap_2()
                    .pt_2()
                    .child(Button::new("ed-play").small().outline().icon(gpui_kit::assets::IconName::Play).label("Play it").disabled(self.parts.is_empty() || busy).on_click(cx.listener(|this, _, window, cx| this.render_edit(false, window, cx))))
                    .child(Button::new("ed-save").small().primary().label("Save as a new song").loading(busy).disabled(self.parts.is_empty()).on_click(cx.listener(|this, _, window, cx| this.render_edit(true, window, cx))))
                    .child(div().text_xs().text_color(cx.theme().muted_foreground).child(match &self.busy {
                        Some(text) => SharedString::from(text.clone()),
                        None => self.message.clone(),
                    })),
            )
    }
}

/// A row that steps one of the editor's choices through its list.
fn step(
    id: &'static str,
    label: &'static str,
    value: &'static str,
    len: usize,
    get: fn(&EditorView) -> usize,
    set: fn(&mut EditorView, usize),
    me: WeakEntity<EditorView>,
    cx: &App,
) -> impl IntoElement {
    let (down, up) = (me.clone(), me);
    ui::stepper(
        id,
        label,
        value,
        cx,
        move |_, cx| {
            let _ = down.update(cx, |this, cx| {
                let ix = ui::wrap(get(this), -1, len);
                set(this, ix);
                this.changed(cx);
            });
        },
        move |_, cx| {
            let _ = up.update(cx, |this, cx| {
                let ix = ui::wrap(get(this), 1, len);
                set(this, ix);
                this.changed(cx);
            });
        },
    )
}
