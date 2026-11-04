//! Finding songs online: search YouTube Music and YouTube, or paste a link, then download at the chosen quality.
//! A downloaded song joins the library, and goes on the selected tracks when there are any.

use gpui_kit::component::{
    ActiveTheme as _, IconName, Sizable as _,
    button::{Button, ButtonVariants as _},
    h_flex,
    input::{Input, InputEvent, InputState},
    v_flex,
};
use gpui_kit::base::Disableable as _;
use gpui_kit::prelude::FluentBuilder as _;
use gpui_kit::*;

use crate::app::PackMaker;
use crate::model::QUALITIES;
use crate::ui::{self, JobNews};

#[derive(Clone)]
struct Found {
    link: String,
    title: String,
    uploader: String,
    duration: String,
    official: bool,
}

pub struct DownloadView {
    maker: WeakEntity<PackMaker>,
    query: Entity<InputState>,
    results: Vec<Found>,
    busy: Option<String>,
    message: SharedString,
    for_tracks: usize,
    _subscriptions: Vec<Subscription>,
}

impl DownloadView {
    pub fn new(maker: WeakEntity<PackMaker>, for_tracks: usize, window: &mut Window, cx: &mut Context<Self>) -> Self {
        let query = cx.new(|cx| InputState::new(window, cx).placeholder("Song name, artist, or a YouTube link"));
        let subscription = cx.subscribe_in(&query, window, |this, _, event, window, cx| {
            if matches!(event, InputEvent::PressEnter { .. }) {
                this.go(window, cx);
            }
        });
        query.update(cx, |input, cx| input.focus(window, cx));
        Self { maker, query, results: Vec::new(), busy: None, message: "".into(), for_tracks, _subscriptions: vec![subscription] }
    }

    fn go(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let text = self.query.read(cx).value().trim().to_string();
        if text.is_empty() || self.busy.is_some() {
            return;
        }
        if text.starts_with("http://") || text.starts_with("https://") {
            self.download(text, window, cx);
            return;
        }
        self.busy = Some("Searching YouTube Music and YouTube…".into());
        self.results.clear();
        cx.notify();
        ui::run_job(
            cx.weak_entity(),
            vec!["search".into(), "--query".into(), text],
            None,
            window,
            cx,
            |this, news, _| {
                if let JobNews::Log(message) = news {
                    this.busy = Some(message);
                }
            },
            |this, result, _, cx| {
                this.busy = None;
                match result {
                    Ok(done) => {
                        this.results = done
                            .extra
                            .get("results")
                            .and_then(|v| v.as_array())
                            .map(|list| {
                                list.iter()
                                    .map(|r| Found {
                                        link: r["link"].as_str().unwrap_or("").into(),
                                        title: r["title"].as_str().unwrap_or("").into(),
                                        uploader: r["uploader"].as_str().unwrap_or("").into(),
                                        duration: r["duration"].as_str().unwrap_or("").into(),
                                        official: r["official"].as_bool().unwrap_or(false),
                                    })
                                    .collect()
                            })
                            .unwrap_or_default();
                        this.message = if this.results.is_empty() { "Nothing found.".into() } else { "".into() };
                    }
                    Err(error) => this.message = format!("Search failed. {error}").into(),
                }
                cx.notify();
            },
        );
    }

    fn download(&mut self, link: String, window: &mut Window, cx: &mut Context<Self>) {
        self.busy = Some("Starting the download…".into());
        cx.notify();
        ui::run_job(
            cx.weak_entity(),
            vec!["download".into(), "--link".into(), link],
            None,
            window,
            cx,
            |this, news, _| match news {
                JobNews::Progress(done, _) => this.busy = Some(format!("Downloading {done}%")),
                JobNews::Log(message) => this.busy = Some(message),
            },
            |this, result, _, cx| {
                this.busy = None;
                match result {
                    Ok(done) => {
                        this.message = done.message.clone().unwrap_or_default().into();
                        if let Some(path) = done.path {
                            let _ = this.maker.update(cx, |m, cx| m.downloaded(path.into(), cx));
                        }
                    }
                    Err(error) => this.message = format!("Download failed. {error}").into(),
                }
                cx.notify();
            },
        );
    }
}

impl Render for DownloadView {
    fn render(&mut self, _: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let kbps = self.maker.read_with(cx, |m, _| m.setting_number("DownloadKbps", 0)).unwrap_or(0);
        let quality = QUALITIES.iter().position(|(_, k)| *k == kbps).unwrap_or(0);
        let busy = self.busy.is_some();
        v_flex()
            .w_full()
            .gap_2()
            .child(
                h_flex()
                    .gap_2()
                    .child(div().flex_1().child(Input::new(&self.query).prefix(gpui_kit::component::Icon::new(IconName::Search).xsmall())))
                    .child(Button::new("go").primary().small().label("Search").loading(busy).on_click(cx.listener(|this, _, window, cx| this.go(window, cx)))),
            )
            .child(ui::stepper("quality", "Quality", QUALITIES[quality].0, cx, {
                let maker = self.maker.clone();
                move |_, cx| {
                    let _ = maker.update(cx, |m, cx| m.set_setting("DownloadKbps", serde_json::json!(QUALITIES[ui::wrap(quality, -1, QUALITIES.len())].1), cx));
                }
            }, {
                let maker = self.maker.clone();
                move |_, cx| {
                    let _ = maker.update(cx, |m, cx| m.set_setting("DownloadKbps", serde_json::json!(QUALITIES[ui::wrap(quality, 1, QUALITIES.len())].1), cx));
                }
            }))
            .child(div().text_xs().text_color(cx.theme().muted_foreground).child(if self.for_tracks > 0 {
                format!("A downloaded song goes on the {} selected {}.", self.for_tracks, if self.for_tracks == 1 { "track" } else { "tracks" })
            } else {
                "Downloaded songs go in your Songs list.".to_string()
            }))
            .child(
                v_flex()
                    .id("results")
                    .max_h(px(360.))
                    .overflow_y_scroll()
                    .children(self.results.clone().into_iter().enumerate().map(|(ix, found)| {
                        let link = found.link.clone();
                        h_flex()
                            .id(SharedString::from(format!("result-{}", found.link)))
                            .h(px(46.))
                            .items_center()
                            .px_2()
                            .gap_2()
                            .when(ix % 2 == 1, |el| el.bg(cx.theme().list_even))
                            .hover(|el| el.bg(cx.theme().list_hover))
                            .child(
                                v_flex()
                                    .flex_1()
                                    .min_w_0()
                                    .child(div().truncate().child(found.title.clone()))
                                    .child(div().text_xs().truncate().text_color(cx.theme().muted_foreground).child(format!(
                                        "{}{}  ·  {}",
                                        if found.official { "Official  ·  " } else { "" },
                                        found.uploader,
                                        found.duration
                                    ))),
                            )
                            .child(Button::new(SharedString::from(format!("dl-{}", found.link))).xsmall().outline().label("Download").disabled(busy).on_click(cx.listener(move |this, _, window, cx| this.download(link.clone(), window, cx))))
                    })),
            )
            .child(div().min_h_5().text_xs().text_color(cx.theme().muted_foreground).child(match &self.busy {
                Some(text) => SharedString::from(text.clone()),
                None => self.message.clone(),
            }))
            .child(div().text_xs().text_color(cx.theme().muted_foreground).child("Only download music you have the right to use."))
    }
}
