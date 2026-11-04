//! The look. A handful of presets in the spirit of foobar2000's skins, plus the user's own accent color, background
//! color, background picture and size, saved in the same settings file the classic window uses.
//! This is the only place raw colors live; views read semantic tokens from `cx.theme()`.

use gpui_kit::component::{Theme, ThemeMode};
use gpui_kit::{App, Hsla, Rgba, hsla, px};

use crate::model::Settings;

/// One preset: a few base colors, from which every token is worked out.
pub struct Preset {
    pub id: &'static str,
    pub name: &'static str,
    dark: bool,
    background: (f32, f32, f32),
    foreground: (f32, f32, f32),
    accent: (f32, f32, f32),
}

const fn preset(
    id: &'static str,
    name: &'static str,
    dark: bool,
    background: (f32, f32, f32),
    foreground: (f32, f32, f32),
    accent: (f32, f32, f32),
) -> Preset {
    Preset { id, name, dark, background, foreground, accent }
}

/// Hue in degrees, saturation and lightness from 0 to 1.
pub const PRESETS: &[Preset] = &[
    preset("amber", "Amber", true, (220., 0.08, 0.075), (220., 0.10, 0.88), (30., 0.78, 0.58)),
    preset("graphite", "Graphite", true, (0., 0.0, 0.06), (0., 0.0, 0.90), (0., 0.0, 0.92)),
    preset("midnight", "Midnight", true, (225., 0.30, 0.09), (220., 0.20, 0.88), (195., 0.80, 0.55)),
    preset("oxide", "Oxide", true, (0., 0.05, 0.06), (20., 0.08, 0.88), (355., 0.72, 0.56)),
    preset("moss", "Moss", true, (150., 0.10, 0.07), (140., 0.08, 0.86), (95., 0.55, 0.52)),
    preset("violet", "Violet", true, (265., 0.18, 0.08), (260., 0.15, 0.88), (275., 0.75, 0.68)),
    preset("classic", "Classic light", false, (0., 0.0, 0.97), (220., 0.12, 0.14), (212., 0.80, 0.46)),
    preset("paper", "Paper", false, (40., 0.30, 0.95), (30., 0.15, 0.16), (18., 0.70, 0.48)),
];

pub const SIZES: &[i64] = &[75, 90, 100, 110, 125, 150, 175, 200];

fn hsl((h, s, l): (f32, f32, f32)) -> Hsla {
    hsla(h / 360.0, s, l, 1.0)
}

fn shift(color: Hsla, by: f32) -> Hsla {
    Hsla { l: (color.l + by).clamp(0.0, 1.0), ..color }
}

/// Reads "#RRGGBB" as a color.
pub fn parse_hex(text: &str) -> Option<Hsla> {
    let hex = text.trim().trim_start_matches('#');
    if hex.len() != 6 {
        return None;
    }
    let value = u32::from_str_radix(hex, 16).ok()?;
    let rgba = Rgba {
        r: ((value >> 16) & 0xff) as f32 / 255.0,
        g: ((value >> 8) & 0xff) as f32 / 255.0,
        b: (value & 0xff) as f32 / 255.0,
        a: 1.0,
    };
    Some(rgba.into())
}

/// A preset's accent, for its swatch in Settings.
pub fn swatch(preset: &Preset) -> (Hsla, Hsla) {
    (hsl(preset.background), hsl(preset.accent))
}

pub fn preset_by_id(id: &str) -> &'static Preset {
    PRESETS.iter().find(|p| p.id == id).unwrap_or(&PRESETS[0])
}

/// The app size from the settings, as a scale where 1 is 100%.
pub fn scale(settings: &Settings) -> f32 {
    settings.number("UiScale", 100).clamp(75, 200) as f32 / 100.0
}

pub fn apply(settings: &Settings, cx: &mut App) {
    let preset = preset_by_id(&settings.text("GpuiTheme"));
    let custom_back = parse_hex(&settings.text("BackgroundColor"));
    let background = custom_back.unwrap_or_else(|| hsl(preset.background));
    let accent = parse_hex(&settings.text("AccentColor")).unwrap_or_else(|| hsl(preset.accent));
    // A custom background decides light or dark, so text always stays readable on it.
    let dark = custom_back.map_or(preset.dark, |back| back.l < 0.5);
    let foreground = match custom_back {
        None => hsl(preset.foreground),
        Some(_) if dark => hsla(0., 0., 0.9, 1.),
        Some(_) => hsla(0., 0., 0.12, 1.),
    };
    let picture = !settings.text("BackgroundImage").is_empty();
    let size = scale(settings);

    Theme::change(if dark { ThemeMode::Dark } else { ThemeMode::Light }, None, cx);
    Theme::update(cx, |theme| {
        let step = if dark { 1.0 } else { -1.0 };
        let panel = shift(background, 0.02 * step);
        let raised = shift(background, 0.045 * step);
        let line = shift(background, 0.10 * step);
        let hover = shift(background, 0.065 * step);
        // With a picture behind the window, surfaces let some of it through.
        let see = |color: Hsla| if picture { color.opacity(0.82) } else { color };
        let accent_text = if accent.l > 0.6 { hsla(0., 0., 0.08, 1.) } else { hsla(0., 0., 0.98, 1.) };

        theme.font_size = px(13. * size);
        theme.radius = px(4.);
        theme.radius_lg = px(6.);

        theme.background = see(background);
        theme.foreground = foreground;
        theme.muted = raised;
        theme.muted_foreground = foreground.opacity(0.58);
        theme.border = line;
        theme.input = line;

        theme.primary = accent;
        theme.primary_hover = shift(accent, 0.05);
        theme.primary_active = shift(accent, -0.06);
        theme.primary_foreground = accent_text;
        theme.button_primary = accent;
        theme.button_primary_hover = shift(accent, 0.05);
        theme.button_primary_active = shift(accent, -0.06);
        theme.button_primary_foreground = accent_text;
        theme.ring = accent.opacity(0.7);
        theme.caret = accent;
        theme.selection = accent.opacity(0.28);
        theme.slider_bar = accent;
        theme.slider_thumb = if dark { hsla(0., 0., 0.92, 1.) } else { hsla(0., 0., 1., 1.) };
        theme.progress_bar = accent;

        theme.sidebar = see(panel);
        theme.sidebar_foreground = foreground;
        theme.sidebar_border = line;
        theme.sidebar_accent = raised;
        theme.sidebar_accent_foreground = foreground;

        theme.colors.list = see(background);
        theme.list_even = see(shift(background, 0.012 * step));
        theme.list_hover = see(hover);
        theme.list_active = accent.opacity(0.18);
        theme.list_active_border = accent.opacity(0.55);
        theme.list_head = see(panel);

        theme.title_bar = see(panel);
        theme.title_bar_border = line;
        theme.status_bar = see(panel);
        theme.status_bar_border = line;
        theme.popover = raised;
        theme.drop_target = accent.opacity(0.22);
        theme.drag_border = accent;
    });
}
