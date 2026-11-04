//! The look: a dark, quiet player theme with one warm accent, in the spirit of foobar2000's dark mode.
//! This is the only place raw colors live; the views read semantic tokens from `cx.theme()`.

use gpui_kit::component::{Theme, ThemeMode};
use gpui_kit::{App, Hsla, hsla, px};

fn hsl(h: f32, s: f32, l: f32) -> Hsla {
    hsla(h / 360.0, s, l, 1.0)
}

pub fn apply(cx: &mut App) {
    Theme::change(ThemeMode::Dark, None, cx);
    Theme::update(cx, |theme| {
        // Dense like a classic player: smaller type, tight corners.
        theme.font_size = px(13.);
        theme.radius = px(4.);
        theme.radius_lg = px(6.);

        let amber = hsl(30.0, 0.78, 0.58);
        let ink = hsl(220.0, 0.08, 0.075);
        let panel = hsl(220.0, 0.08, 0.095);
        let raised = hsl(220.0, 0.08, 0.12);
        let line = hsl(220.0, 0.07, 0.17);

        theme.background = ink;
        theme.foreground = hsl(220.0, 0.10, 0.88);
        theme.muted = raised;
        theme.muted_foreground = hsl(220.0, 0.07, 0.56);
        theme.border = line;
        theme.input = line;

        theme.primary = amber;
        theme.primary_hover = hsl(30.0, 0.82, 0.63);
        theme.primary_active = hsl(30.0, 0.70, 0.52);
        theme.primary_foreground = hsl(30.0, 0.45, 0.08);
        theme.button_primary = amber;
        theme.button_primary_hover = theme.primary_hover;
        theme.button_primary_active = theme.primary_active;
        theme.button_primary_foreground = theme.primary_foreground;
        theme.ring = amber.opacity(0.7);
        theme.caret = amber;
        theme.selection = amber.opacity(0.28);
        theme.slider_bar = amber;
        theme.slider_thumb = hsl(30.0, 0.30, 0.92);
        theme.progress_bar = amber;

        theme.sidebar = panel;
        theme.sidebar_foreground = theme.foreground;
        theme.sidebar_border = line;
        theme.sidebar_accent = raised;
        theme.sidebar_accent_foreground = theme.foreground;

        theme.colors.list = ink;
        theme.list_even = hsl(220.0, 0.08, 0.088);
        theme.list_hover = hsl(220.0, 0.08, 0.14);
        theme.list_active = amber.opacity(0.16);
        theme.list_active_border = amber.opacity(0.55);
        theme.list_head = panel;

        theme.title_bar = panel;
        theme.title_bar_border = line;
        theme.status_bar = panel;
        theme.status_bar_border = line;
        theme.popover = raised;
        theme.drop_target = amber.opacity(0.22);
        theme.drag_border = amber;
    });
}
