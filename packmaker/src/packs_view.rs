//! Packs on this PC: ones imported, ones made here, and Wheel Wizard mods, each with a way to delete it.

use gpui_kit::component::{
    ActiveTheme as _, Sizable as _,
    button::{Button, ButtonVariant, ButtonVariants as _},
    h_flex, v_flex,
    WindowExt as _,
};
use gpui_kit::base::Disableable as _;
use gpui_kit::prelude::FluentBuilder as _;
use gpui_kit::*;

use crate::app::PackMaker;
use crate::engine;
use crate::model::InstalledPack;
use crate::ui;

pub struct PacksView {
    maker: WeakEntity<PackMaker>,
    packs: Option<Vec<InstalledPack>>,
    busy: Option<String>,
    message: SharedString,
}

impl PacksView {
    pub fn new(maker: WeakEntity<PackMaker>, window: &mut Window, cx: &mut Context<Self>) -> Self {
        let mut view = Self { maker, packs: None, busy: None, message: "".into() };
        view.refresh(window, cx);
        view
    }

    fn refresh(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let work = cx.background_executor().spawn(async move { engine::run_json::<Vec<InstalledPack>>(&["installed"]) });
        cx.spawn_in(window, async move |this, cx| {
            let packs = work.await;
            let _ = this.update(cx, |this, cx| {
                match packs {
                    Ok(packs) => this.packs = Some(packs),
                    Err(error) => {
                        this.packs = Some(Vec::new());
                        this.message = format!("Couldn't list the packs. {error}").into();
                    }
                }
                cx.notify();
            });
        })
        .detach();
    }

    fn confirm_delete(&mut self, pack: InstalledPack, window: &mut Window, cx: &mut Context<Self>) {
        let me = cx.weak_entity();
        let name = pack.name.clone();
        let songs = pack.songs;
        window.open_alert_dialog(cx, move |alert, _, _| {
            let me = me.clone();
            let pack = pack.clone();
            alert
                .title(SharedString::from(format!("Delete “{name}”?")))
                .description(SharedString::from(format!(
                    "Its {songs} songs go to the Recycle Bin, and tracks that use them go back to their old music. Other files there, like karts, stay."
                )))
                .confirm()
                .ok_text("Delete")
                .ok_variant(ButtonVariant::Danger)
                .on_ok(move |_, window, cx| {
                    let pack = pack.clone();
                    let _ = me.update(cx, |this, cx| this.delete(pack, window, cx));
                    true
                })
        });
    }

    fn delete(&mut self, pack: InstalledPack, window: &mut Window, cx: &mut Context<Self>) {
        self.busy = Some(format!("Deleting {}… a big pack can take a while.", pack.name));
        cx.notify();
        ui::run_job(
            cx.weak_entity(),
            vec!["installed-delete".into(), "--folder".into(), pack.folder.clone()],
            None,
            window,
            cx,
            |_, _, _| {},
            |this, result, window, cx| {
                this.busy = None;
                match result {
                    Ok(done) => {
                        this.message = done.message.clone().unwrap_or_default().into();
                        let removed: Vec<String> = done
                            .extra
                            .get("removed")
                            .and_then(|v| v.as_array())
                            .map(|list| list.iter().filter_map(|s| s.as_str().map(String::from)).collect())
                            .unwrap_or_default();
                        let _ = this.maker.update(cx, |m, cx| m.songs_removed(&removed, cx));
                    }
                    Err(error) => this.message = format!("Couldn't delete all of it. {error}").into(),
                }
                this.refresh(window, cx);
            },
        );
    }
}

impl Render for PacksView {
    fn render(&mut self, _: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let busy = self.busy.is_some();
        let body = match &self.packs {
            None => div().p_4().text_color(cx.theme().muted_foreground).child("Looking for packs…").into_any_element(),
            Some(packs) if packs.is_empty() => div().p_4().text_color(cx.theme().muted_foreground).child("No packs found on this PC.").into_any_element(),
            Some(packs) => v_flex()
                .id("packs")
                .max_h(px(380.))
                .overflow_y_scroll()
                .children(packs.clone().into_iter().enumerate().map(|(ix, pack)| {
                    let target = pack.clone();
                    h_flex()
                        .id(SharedString::from(format!("pack-{}", pack.folder)))
                        .h_10()
                        .px_2()
                        .gap_2()
                        .when(ix % 2 == 1, |el| el.bg(cx.theme().list_even))
                        .child(
                            v_flex()
                                .flex_1()
                                .min_w_0()
                                .child(div().truncate().child(pack.name.clone()))
                                .child(div().text_xs().truncate().text_color(cx.theme().muted_foreground).child(pack.folder.clone())),
                        )
                        .child(div().text_xs().text_color(cx.theme().muted_foreground).child(format!(
                            "{}{} songs",
                            if pack.is_mod { "Wheel Wizard mod  ·  " } else if pack.made_by_app { "Made here  ·  " } else { "" },
                            pack.songs
                        )))
                        .child(Button::new(SharedString::from(format!("del-{}", pack.folder))).xsmall().danger().label("Delete").disabled(busy).on_click(cx.listener(move |this, _, window, cx| this.confirm_delete(target.clone(), window, cx))))
                }))
                .into_any_element(),
        };
        v_flex()
            .w_full()
            .gap_2()
            .child(body)
            .child(div().min_h_5().text_xs().text_color(cx.theme().muted_foreground).child(match &self.busy {
                Some(text) => SharedString::from(text.clone()),
                None => self.message.clone(),
            }))
    }
}
