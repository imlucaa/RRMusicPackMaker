//! The loop editor: the song's waveform with draggable loop markers, the loop finder's suggestions, and a way to
//! hear the jump before keeping it.

use std::cell::Cell;
use std::rc::Rc;

use gpui_kit::component::{
    ActiveTheme as _, Sizable as _,
    button::{Button, ButtonVariants as _},
    h_flex, v_flex,
};
use gpui_kit::base::Disableable as _;
use gpui_kit::prelude::FluentBuilder as _;
use gpui_kit::*;

use crate::app::PackMaker;
use crate::model::{self, CROSSFADES};
use crate::ui;

#[derive(Clone, Copy, PartialEq)]
enum Marker {
    Start,
    End,
}

struct Found {
    start: f64,
    end: f64,
    score: f64,
}

pub struct LoopView {
    maker: WeakEntity<PackMaker>,
    file: String,
    song: String,
    peaks: Vec<(f32, f32)>,
    seconds: f64,
    start: f64,
    end: f64,
    crossfade: usize,
    found: Vec<Found>,
    loading: Option<&'static str>,
    message: SharedString,
    bounds: Rc<Cell<Bounds<Pixels>>>,
    dragging: Option<Marker>,
}

impl LoopView {
    /// The song and its saved loop come in as arguments: the main window is mid-update when this is made.
    pub fn new(
        maker: WeakEntity<PackMaker>,
        file: String,
        (song, points, crossfade): (String, (Option<f64>, Option<f64>), usize),
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> Self {
        let mut view = Self {
            maker,
            file,
            song,
            peaks: Vec::new(),
            seconds: 0.0,
            start: points.0.unwrap_or(0.0),
            end: points.1.unwrap_or(0.0),
            crossfade,
            found: Vec::new(),
            loading: None,
            message: "".into(),
            bounds: Rc::default(),
            dragging: None,
        };
        view.load(false, window, cx);
        view
    }

    /// Reads the waveform, and with `find` also asks the loop finder for seamless loops.
    fn load(&mut self, find: bool, window: &mut Window, cx: &mut Context<Self>) {
        self.loading = Some(if find { "Looking for seamless loops…" } else { "Reading the song…" });
        cx.notify();
        let mut args = vec!["waveform".to_string(), "--song".into(), self.song.clone(), "--points".into(), "900".into()];
        if find {
            args.push("--find".into());
        }
        ui::run_job(cx.weak_entity(), args, None, window, cx, |_, _, _| {}, move |this, result, _, cx| {
            this.loading = None;
            match result {
                Ok(done) => {
                    let extra = &done.extra;
                    this.seconds = extra.get("seconds").and_then(|v| v.as_f64()).unwrap_or(0.0);
                    this.peaks = extra
                        .get("peaks")
                        .and_then(|v| v.as_array())
                        .map(|list| {
                            list.iter()
                                .map(|p| (p[0].as_f64().unwrap_or(0.0) as f32, p[1].as_f64().unwrap_or(0.0) as f32))
                                .collect()
                        })
                        .unwrap_or_default();
                    if this.end <= 0.0 || this.end > this.seconds {
                        let saved_start = extra.get("loopStart").and_then(|v| v.as_f64());
                        let saved_end = extra.get("loopEnd").and_then(|v| v.as_f64());
                        this.start = saved_start.unwrap_or(0.0);
                        this.end = saved_end.unwrap_or(this.seconds);
                    }
                    if find {
                        this.found = extra
                            .get("found")
                            .and_then(|v| v.as_array())
                            .map(|list| {
                                list.iter()
                                    .map(|f| Found {
                                        start: f["start"].as_f64().unwrap_or(0.0),
                                        end: f["end"].as_f64().unwrap_or(0.0),
                                        score: f["score"].as_f64().unwrap_or(0.0),
                                    })
                                    .collect()
                            })
                            .unwrap_or_default();
                        this.message = if this.found.is_empty() {
                            "No seamless loop found. Try a crossfade, or set the points by hand.".into()
                        } else {
                            format!("Found {} loops. Pick one to try it.", this.found.len()).into()
                        };
                    }
                }
                Err(error) => this.message = format!("Couldn't read the song. {error}").into(),
            }
            cx.notify();
        });
    }

    fn hear_seam(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let args = vec![
            "loop-seam".to_string(),
            "--song".into(),
            self.song.clone(),
            "--start".into(),
            format!("{:.6}", self.start),
            "--end".into(),
            format!("{:.6}", self.end),
        ];
        self.loading = Some("Getting the jump ready…");
        cx.notify();
        ui::run_job(cx.weak_entity(), args, None, window, cx, |_, _, _| {}, |this, result, window, cx| {
            this.loading = None;
            match result.and_then(|done| done.path.ok_or_else(|| anyhow::anyhow!("no preview"))) {
                Ok(path) => {
                    this.message = "Playing the 3 seconds before the jump, then the 3 after it.".into();
                    let _ = this.maker.update(cx, |m, cx| m.play_file(path.into(), "Loop jump".into(), window, cx));
                }
                Err(error) => this.message = format!("Couldn't make the preview. {error}").into(),
            }
            cx.notify();
        });
    }

    fn keep(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let (file, start, end, crossfade) = (self.file.clone(), self.start, self.end, self.crossfade);
        let _ = self.maker.update(cx, |m, cx| m.keep_loop(&file, start, end, crossfade, cx));
        gpui_kit::component::WindowExt::close_dialog(window, cx);
    }

    fn time_at(&self, x: Pixels) -> f64 {
        let bounds = self.bounds.get();
        let width = f32::from(bounds.size.width).max(1.0);
        let fraction = (f32::from(x - bounds.origin.x) / width).clamp(0.0, 1.0);
        fraction as f64 * self.seconds
    }

    fn nudge(&mut self, marker: Marker, by: f64, cx: &mut Context<Self>) {
        match marker {
            Marker::Start => self.start = (self.start + by).clamp(0.0, (self.end - 0.05).max(0.0)),
            Marker::End => self.end = (self.end + by).clamp(self.start + 0.05, self.seconds),
        }
        cx.notify();
    }

    fn render_wave(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let peaks = self.peaks.clone();
        let seconds = self.seconds.max(0.001);
        let (start, end) = ((self.start / seconds) as f32, (self.end / seconds) as f32);
        let theme = cx.theme();
        let (wave, region, accent, line) = (theme.muted_foreground, theme.primary.opacity(0.10), theme.primary, theme.border);
        let store = self.bounds.clone();

        div()
            .id("wave")
            .relative()
            .h_40()
            .w_full()
            .rounded(theme.radius)
            .bg(theme.list_even)
            .border_1()
            .border_color(line)
            .cursor_ew_resize()
            .child(
                canvas(
                    move |bounds, _, _| store.set(bounds),
                    move |bounds, _, window, _| {
                        let width = f32::from(bounds.size.width);
                        let height = f32::from(bounds.size.height);
                        let mid = f32::from(bounds.origin.y) + height / 2.0;
                        // The looped part is shaded, so what repeats in the game stands out.
                        window.paint_quad(fill(
                            Bounds::new(
                                point(bounds.origin.x + px(start * width), bounds.origin.y),
                                size(px(((end - start) * width).max(1.0)), bounds.size.height),
                            ),
                            region,
                        ));
                        let count = peaks.len().max(1) as f32;
                        let bar = (width / count).max(1.0);
                        for (ix, (low, high)) in peaks.iter().enumerate() {
                            let x = f32::from(bounds.origin.x) + ix as f32 * width / count;
                            let top = mid - high.max(0.0) * height * 0.46;
                            let bottom = mid - low.min(0.0) * height * 0.46;
                            let at = ix as f32 / count;
                            let color = if at >= start && at <= end { accent.opacity(0.85) } else { wave };
                            window.paint_quad(fill(
                                Bounds::new(point(px(x), px(top)), size(px(bar.max(1.0)), px((bottom - top).max(1.0)))),
                                color,
                            ));
                        }
                        for marker in [start, end] {
                            window.paint_quad(fill(
                                Bounds::new(point(bounds.origin.x + px(marker * width - 1.0), bounds.origin.y), size(px(2.0), bounds.size.height)),
                                accent,
                            ));
                        }
                    },
                )
                .size_full(),
            )
            .on_mouse_down(MouseButton::Left, cx.listener(|this, event: &MouseDownEvent, _, cx| {
                let at = this.time_at(event.position.x);
                // Grabs whichever marker is nearer to the pointer.
                let marker = if (at - this.start).abs() <= (at - this.end).abs() { Marker::Start } else { Marker::End };
                this.dragging = Some(marker);
                this.place(marker, at, cx);
            }))
            .on_mouse_move(cx.listener(|this, event: &MouseMoveEvent, _, cx| {
                if let Some(marker) = this.dragging {
                    if event.pressed_button == Some(MouseButton::Left) {
                        let at = this.time_at(event.position.x);
                        this.place(marker, at, cx);
                    } else {
                        this.dragging = None;
                    }
                }
            }))
            .on_mouse_up(MouseButton::Left, cx.listener(|this, _: &MouseUpEvent, _, _| this.dragging = None))
    }

    fn place(&mut self, marker: Marker, at: f64, cx: &mut Context<Self>) {
        match marker {
            Marker::Start => self.start = at.min(self.end - 0.05).max(0.0),
            Marker::End => self.end = at.max(self.start + 0.05).min(self.seconds),
        }
        cx.notify();
    }

    fn marker_row(&self, marker: Marker, cx: &mut Context<Self>) -> impl IntoElement {
        let (label, value, id) = match marker {
            Marker::Start => ("Loop start", self.start, "start"),
            Marker::End => ("Loop end", self.end, "end"),
        };
        let nudge = |amount: f64, text: &'static str, cx: &mut Context<Self>| {
            Button::new(SharedString::from(format!("{id}{text}")))
                .xsmall()
                .ghost()
                .label(text)
                .on_click(cx.listener(move |this, _, _, cx| this.nudge(marker, amount, cx)))
        };
        h_flex()
            .gap_1()
            .child(div().w_20().text_xs().text_color(cx.theme().muted_foreground).child(label))
            .child(nudge(-1.0, "−1 s", cx))
            .child(nudge(-0.01, "−0.01", cx))
            .child(div().w_24().text_center().font_family(cx.theme().mono_font_family.clone()).child(model::precise_time(value)[..9.min(model::precise_time(value).len())].to_string()))
            .child(nudge(0.01, "+0.01", cx))
            .child(nudge(1.0, "+1 s", cx))
    }
}

impl Render for LoopView {
    fn render(&mut self, _: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let found: Vec<(usize, f64, f64, f64)> = self.found.iter().enumerate().map(|(i, f)| (i, f.start, f.end, f.score)).collect();
        let crossfade = self.crossfade;
        v_flex()
            .w_full()
            .gap_2()
            .child(div().text_xs().text_color(cx.theme().muted_foreground).truncate().child(self.song.clone()))
            .child(self.render_wave(cx))
            .child(
                h_flex()
                    .justify_between()
                    .text_xs()
                    .text_color(cx.theme().muted_foreground)
                    .child("Drag in the waveform to move the nearer marker.")
                    .child(format!(
                        "Loop {} long · song {}",
                        model::format_time(self.end - self.start),
                        model::format_time(self.seconds)
                    )),
            )
            .child(self.marker_row(Marker::Start, cx))
            .child(self.marker_row(Marker::End, cx))
            .child(ui::stepper(
                "crossfade",
                "Smooth the jump",
                CROSSFADES[crossfade].0,
                cx,
                {
                    let me = cx.weak_entity();
                    move |_, cx| {
                        let _ = me.update(cx, |this, cx| {
                            this.crossfade = ui::wrap(this.crossfade, -1, CROSSFADES.len());
                            cx.notify();
                        });
                    }
                },
                {
                    let me = cx.weak_entity();
                    move |_, cx| {
                        let _ = me.update(cx, |this, cx| {
                            this.crossfade = ui::wrap(this.crossfade, 1, CROSSFADES.len());
                            cx.notify();
                        });
                    }
                },
            ))
            .when(!found.is_empty(), |el| {
                el.child(ui::section("Seamless loops the finder suggests", cx)).children(found.into_iter().map(|(ix, start, end, score)| {
                    h_flex()
                        .id(("found", ix))
                        .h_6()
                        .px_2()
                        .gap_2()
                        .rounded(cx.theme().radius)
                        .hover(|el| el.bg(cx.theme().list_hover))
                        .child(div().flex_1().child(format!("{} to {}", model::format_time(start), model::format_time(end))))
                        .child(div().text_xs().text_color(cx.theme().muted_foreground).child(format!("{:.0}% match", score * 100.0)))
                        .on_click(cx.listener(move |this, _, _, cx| {
                            this.start = start;
                            this.end = end;
                            cx.notify();
                        }))
                }))
            })
            .child(div().min_h_5().text_xs().text_color(cx.theme().muted_foreground).child(match self.loading {
                Some(text) => SharedString::from(text),
                None => self.message.clone(),
            }))
            .child(
                h_flex()
                    .gap_2()
                    .child(Button::new("find").small().outline().label("Find loops").loading(self.loading.is_some()).on_click(cx.listener(|this, _, window, cx| this.load(true, window, cx))))
                    .child(Button::new("whole").small().ghost().label("Whole song").on_click(cx.listener(|this, _, _, cx| {
                        this.start = 0.0;
                        this.end = this.seconds;
                        cx.notify();
                    })))
                    .child(Button::new("seam").small().outline().label("Hear the jump").on_click(cx.listener(|this, _, window, cx| this.hear_seam(window, cx))))
                    .child(div().flex_1())
                    .child(Button::new("keep").small().primary().label("Use this loop").disabled(self.seconds <= 0.0).on_click(cx.listener(|this, _, window, cx| this.keep(window, cx)))),
            )
    }
}
