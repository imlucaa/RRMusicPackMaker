//! Small pieces shared by every screen: a value stepper, section titles, label-and-value rows, and a way to run an
//! engine command in the background while the window stays responsive.

use std::sync::{Arc, Mutex};
use std::time::Duration;

use gpui_kit::component::{
    ActiveTheme as _, IconName, Sizable as _,
    button::{Button, ButtonVariants as _},
    h_flex, v_flex,
};
use gpui_kit::*;

use crate::engine::{self, Event};

/// A label, a value, and buttons either side to step through the choices. Like a compact foobar preference row.
pub fn stepper(
    id: impl Into<SharedString>,
    label: impl Into<SharedString>,
    value: impl Into<SharedString>,
    cx: &App,
    down: impl Fn(&mut Window, &mut App) + 'static,
    up: impl Fn(&mut Window, &mut App) + 'static,
) -> impl IntoElement {
    let id: SharedString = id.into();
    h_flex()
        .h_7()
        .gap_2()
        .child(div().w_24().flex_shrink_0().text_xs().text_color(cx.theme().muted_foreground).child(label.into()))
        .child(
            Button::new(SharedString::from(format!("{id}-down")))
                .ghost()
                .xsmall()
                .icon(IconName::ChevronLeft)
                .on_click(move |_, window, cx| down(window, cx)),
        )
        .child(div().flex_1().min_w_0().text_center().truncate().child(value.into()))
        .child(
            Button::new(SharedString::from(format!("{id}-up")))
                .ghost()
                .xsmall()
                .icon(IconName::ChevronRight)
                .on_click(move |_, window, cx| up(window, cx)),
        )
}

/// Steps an index through a list, wrapping at either end.
pub fn wrap(index: usize, step: isize, count: usize) -> usize {
    (index as isize + step).rem_euclid(count.max(1) as isize) as usize
}

pub fn section(title: impl Into<SharedString>, cx: &App) -> Div {
    div()
        .pt_4()
        .pb_1()
        .mb_1()
        .text_xs()
        .text_color(cx.theme().muted_foreground)
        .border_b_1()
        .border_color(cx.theme().border)
        .child(title.into())
}

pub fn field(label: impl Into<SharedString>, value: impl IntoElement, cx: &App) -> Div {
    h_flex()
        .min_h_7()
        .items_start()
        .gap_2()
        .child(div().w_24().flex_shrink_0().text_xs().text_color(cx.theme().muted_foreground).child(label.into()))
        .child(div().flex_1().min_w_0().child(value))
}

/// What a background engine job reports back as it runs.
pub enum JobNews {
    Progress(u32, u32),
    Log(String),
}

/// Runs an engine command away from the window, passing progress back every so often, then hands over the result.
pub fn run_job<T: 'static>(
    entity: WeakEntity<T>,
    args: Vec<String>,
    spec: Option<serde_json::Value>,
    window: &mut Window,
    cx: &mut App,
    news: impl Fn(&mut T, JobNews, &mut Context<T>) + 'static,
    done: impl FnOnce(&mut T, anyhow::Result<Event>, &mut Window, &mut Context<T>) + 'static,
) {
    let events: Arc<Mutex<Vec<Event>>> = Arc::default();
    let sink = events.clone();
    let job = cx.background_executor().spawn(async move {
        let args: Vec<&str> = args.iter().map(String::as_str).collect();
        let push = |event: &Event| sink.lock().unwrap().push(event.clone());
        match spec {
            Some(spec) => engine::run_with_spec(&args, &spec, push),
            None => engine::run_events(&args, push),
        }
    });
    window
        .spawn(cx, async move |cx| {
            let mut job = std::pin::pin!(job);
            let result = loop {
                let timer = cx.background_executor().timer(Duration::from_millis(150));
                match first(&mut job, timer).await {
                    Some(result) => break result,
                    None => deliver(&entity, &events, &news, cx),
                }
            };
            deliver(&entity, &events, &news, cx);
            let _ = entity.update_in(cx, |this, window, cx| done(this, result, window, cx));
        })
        .detach();
}

fn deliver<T: 'static>(
    entity: &WeakEntity<T>,
    events: &Arc<Mutex<Vec<Event>>>,
    news: &impl Fn(&mut T, JobNews, &mut Context<T>),
    cx: &mut AsyncWindowContext,
) {
    let drained: Vec<Event> = events.lock().unwrap().drain(..).collect();
    if drained.is_empty() {
        return;
    }
    let _ = entity.update(cx, |this, cx| {
        for event in drained {
            match event.event.as_str() {
                "progress" => news(this, JobNews::Progress(event.done.unwrap_or(0), event.total.unwrap_or(0)), cx),
                "log" => {
                    if let Some(message) = event.message {
                        news(this, JobNews::Log(message), cx);
                    }
                }
                _ => {}
            }
        }
        cx.notify();
    });
}

/// Waits for whichever finishes first: the job, or the timer. Returns the job's result if it won.
async fn first<T: 'static>(job: &mut std::pin::Pin<&mut Task<T>>, timer: Task<()>) -> Option<T> {
    use std::future::Future;
    use std::task::Poll;
    let mut timer = std::pin::pin!(timer);
    std::future::poll_fn(|ctx| {
        if let Poll::Ready(value) = job.as_mut().poll(ctx) {
            return Poll::Ready(Some(value));
        }
        if timer.as_mut().poll(ctx).is_ready() {
            return Poll::Ready(None);
        }
        Poll::Pending
    })
    .await
}

/// Picks files with the system dialog and hands them over, on the window's thread.
pub fn pick_paths<T: 'static>(
    entity: WeakEntity<T>,
    options: PathPromptOptions,
    window: &mut Window,
    cx: &mut App,
    then: impl FnOnce(&mut T, Vec<std::path::PathBuf>, &mut Window, &mut Context<T>) + 'static,
) {
    let picked = cx.prompt_for_paths(options);
    window
        .spawn(cx, async move |cx| {
            let Ok(Ok(Some(paths))) = picked.await else {
                return;
            };
            let _ = entity.update_in(cx, |this, window, cx| then(this, paths, window, cx));
        })
        .detach();
}

pub fn files(prompt: &str, multiple: bool) -> PathPromptOptions {
    PathPromptOptions { files: true, directories: false, multiple, prompt: Some(prompt.to_string().into()) }
}

pub fn folder(prompt: &str) -> PathPromptOptions {
    PathPromptOptions { files: false, directories: true, multiple: false, prompt: Some(prompt.to_string().into()) }
}

/// A label with a long value under it, like a note or a file path, so the text can wrap without running over the next row.
pub fn long_field(label: impl Into<SharedString>, value: impl Into<SharedString>, cx: &App) -> Div {
    v_flex()
        .gap_0p5()
        .py_1()
        .child(div().text_xs().text_color(cx.theme().muted_foreground).child(label.into()))
        .child(div().w_full().text_sm().child(value.into()))
}
