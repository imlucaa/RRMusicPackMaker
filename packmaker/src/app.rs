//! The main window, laid out like a classic player: menus, transport and seek on top, the library on the left,
//! tabs for the tracks, the songs and the song editor in the middle, the selection's properties on the right, and
//! a status bar with an optional console.

use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::time::Duration;

use gpui_kit::assets::IconName as Lucide;
use gpui_kit::base::{Disableable as _, Selectable as _};
use gpui_kit::component::{
    ActiveTheme as _, Icon, Sizable as _, WindowExt as _,
    button::{Button, ButtonVariants as _},
    h_flex,
    input::{Input, InputEvent, InputState},
    menu::{ContextMenuExt as _, DropdownMenu as _, PopupMenu, PopupMenuItem},
    notification::Notification,
    resizable::{h_resizable, resizable_panel},
    slider::{Slider, SliderEvent, SliderState},
    switch::Switch,
    v_flex,
};
use gpui_kit::prelude::FluentBuilder as _;
use gpui_kit::*;
use serde_json::{Value, json};

use crate::download_view::DownloadView;
use crate::editor_view::EditorView;
use crate::engine;
use crate::loop_view::LoopView;
use crate::model::{self, CROSSFADES, Catalog, FINAL_SPEEDS, Group, LOOPS, NEW_PACK, Packs, Project, SPEEDS, Settings, Song, Track};
use crate::packs_view::PacksView;
use crate::player::{PlayState, Player};
use crate::settings_view::SettingsView;
use crate::theme;
use crate::ui::{self, JobNews};

actions!(
    packmaker,
    [
        PlayPause, Stop, PlayNext, PlayPrevious, PlaySelected, RemoveSongs, SelectAll, SelectEmpty, SelectFilled,
        MoveUp, MoveDown, ExtendUp, ExtendDown, FocusSearch, AddSongs, SaveNow, ExportZip, ExportFolder, AddToGame,
        RemoveFromGame, ToggleWii, OpenClassic, Reload, ToggleConsole, Quit, OpenSettings, ImportFolder, ImportZip,
        ShowInstalled, FindOnline, EditLoop, HearFinal, NewPack, DeletePack, ShowTracks, ShowSongs, ShowEditor,
        ShowExported, OpenReleases, About, Louder, Quieter
    ]
);

const PLAYLIST: &str = "Playlist";

pub fn bind_keys(cx: &mut App) {
    cx.bind_keys([
        KeyBinding::new("space", PlayPause, Some(PLAYLIST)),
        KeyBinding::new("enter", PlaySelected, Some(PLAYLIST)),
        KeyBinding::new("delete", RemoveSongs, Some(PLAYLIST)),
        KeyBinding::new("ctrl-a", SelectAll, Some(PLAYLIST)),
        KeyBinding::new("up", MoveUp, Some(PLAYLIST)),
        KeyBinding::new("down", MoveDown, Some(PLAYLIST)),
        KeyBinding::new("shift-up", ExtendUp, Some(PLAYLIST)),
        KeyBinding::new("shift-down", ExtendDown, Some(PLAYLIST)),
        KeyBinding::new("ctrl-=", Louder, Some(PLAYLIST)),
        KeyBinding::new("ctrl--", Quieter, Some(PLAYLIST)),
        KeyBinding::new("ctrl-l", EditLoop, Some(PLAYLIST)),
        KeyBinding::new("ctrl-f", FocusSearch, None),
        KeyBinding::new("ctrl-o", AddSongs, None),
        KeyBinding::new("ctrl-i", ImportFolder, None),
        KeyBinding::new("ctrl-d", FindOnline, None),
        KeyBinding::new("ctrl-s", SaveNow, None),
        KeyBinding::new("ctrl-e", ExportZip, None),
        KeyBinding::new("ctrl-n", NewPack, None),
        KeyBinding::new("ctrl-,", OpenSettings, None),
        KeyBinding::new("ctrl-1", ShowTracks, None),
        KeyBinding::new("ctrl-2", ShowSongs, None),
        KeyBinding::new("ctrl-3", ShowEditor, None),
        KeyBinding::new("ctrl-.", Stop, None),
        KeyBinding::new("ctrl-right", PlayNext, None),
        KeyBinding::new("ctrl-left", PlayPrevious, None),
        KeyBinding::new("ctrl-`", ToggleConsole, None),
        KeyBinding::new("ctrl-q", Quit, None),
    ]);
}

const RELEASES: &str = "https://github.com/imlucaa/RRMusicPackMaker/releases/latest";

#[derive(Clone, Copy, PartialEq, Eq)]
enum LeftPane {
    Library,
    Songs,
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Center {
    Tracks,
    Songs,
    Editor,
}

/// What the playlist is narrowed to by the library pane.
#[derive(Clone, PartialEq, Eq)]
enum Scope {
    All,
    Set,
    Missing,
    Category(String),
}

struct Busy {
    label: String,
    done: u32,
    total: u32,
}

/// Songs being dragged onto tracks.
#[derive(Clone)]
struct SongDrag {
    paths: Vec<PathBuf>,
    label: SharedString,
}

struct DragChip(SharedString);

impl Render for DragChip {
    fn render(&mut self, _: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        h_flex()
            .gap_1()
            .px_2()
            .py_1()
            .rounded(cx.theme().radius)
            .bg(cx.theme().popover)
            .border_1()
            .border_color(cx.theme().primary)
            .text_xs()
            .child(Icon::new(Lucide::Music).xsmall())
            .child(self.0.clone())
    }
}

pub struct PackMaker {
    tracks: Vec<Track>,
    groups: Vec<Group>,
    project: Project,
    packs: Packs,
    settings: Settings,
    songs: Vec<Song>,
    scope: Scope,
    left: LeftPane,
    center: Center,
    visible: Vec<usize>,
    selected: HashSet<usize>,
    cursor: Option<usize>,
    anchor: Option<usize>,
    song_cursor: Option<usize>,
    songs_visible: Vec<usize>,
    song_selected: HashSet<usize>,
    song_anchor: Option<usize>,
    list_scroll: UniformListScrollHandle,
    songs_scroll: UniformListScrollHandle,
    song_table_scroll: UniformListScrollHandle,
    focus: FocusHandle,
    search: Entity<InputState>,
    pack_name: Entity<InputState>,
    title_input: Entity<InputState>,
    credits_input: Entity<InputState>,
    synced_file: Option<String>,
    syncing: bool,
    seek: Entity<SliderState>,
    volume: Entity<SliderState>,
    editor: Entity<EditorView>,
    player: Player,
    loading_preview: bool,
    status: SharedString,
    log: Vec<SharedString>,
    show_console: bool,
    busy: Option<Busy>,
    save_generation: u64,
    saved: bool,
    missing: HashSet<String>,
    ffmpeg: bool,
    fill_in_order: bool,
    fill_only_empty: bool,
    last_export: Option<PathBuf>,
    checked: bool,
    shown_demo: bool,
    _tick: Task<()>,
    _subscriptions: Vec<Subscription>,
}

impl PackMaker {
    pub fn new(window: &mut Window, cx: &mut Context<Self>) -> Self {
        let project = Project::load();
        let search = cx.new(|cx| InputState::new(window, cx).placeholder("Filter tracks, songs or files"));
        let pack_name = cx.new(|cx| InputState::new(window, cx).default_value(project.pack_name()));
        let title_input = cx.new(|cx| InputState::new(window, cx).placeholder("Song name"));
        let credits_input = cx.new(|cx| InputState::new(window, cx).placeholder("Credits, like the artist"));
        let seek = cx.new(|_| SliderState::new().min(0.).max(1000.).step(1.).default_value(0.));
        let volume = cx.new(|_| SliderState::new().min(0.).max(100.).step(1.).default_value(80.));
        let weak = cx.weak_entity();
        let editor = cx.new(|cx| EditorView::new(weak, window, cx));

        let subscriptions = vec![
            cx.subscribe_in(&search, window, |this, _, event, _, cx| {
                if matches!(event, InputEvent::Change) {
                    this.refilter(cx);
                }
            }),
            cx.subscribe_in(&pack_name, window, |this, state, event, _, cx| match event {
                InputEvent::Change => {
                    let name = state.read(cx).value().to_string();
                    this.project.set_pack_name(&name);
                    this.changed(cx);
                }
                // Two packs never share a name, so a clash gets a number when typing ends.
                InputEvent::Blur | InputEvent::PressEnter { .. } => this.keep_name_unique(cx),
                _ => {}
            }),
            cx.subscribe_in(&title_input, window, |this, state, event, window, cx| {
                // Filling the box when the selection moves also raises a change. Only typing in it counts as an edit.
                let typing = state.read(cx).focus_handle(cx).is_focused(window);
                if matches!(event, InputEvent::Change) && typing && !this.syncing {
                    if let Some(file) = this.synced_file.clone() {
                        let value = state.read(cx).value().to_string();
                        this.project.set_text(&file, "Song", &value);
                        this.changed(cx);
                    }
                }
            }),
            cx.subscribe_in(&credits_input, window, |this, state, event, window, cx| {
                // Filling the box when the selection moves also raises a change. Only typing in it counts as an edit.
                let typing = state.read(cx).focus_handle(cx).is_focused(window);
                if matches!(event, InputEvent::Change) && typing && !this.syncing {
                    if let Some(file) = this.synced_file.clone() {
                        let value = state.read(cx).value().to_string();
                        this.project.set_text(&file, "Credits", &value);
                        this.changed(cx);
                    }
                }
            }),
            cx.subscribe(&seek, |this, _, event: &SliderEvent, cx| {
                if let SliderEvent::Change(value) = event {
                    this.player.seek(value.start() / 1000.);
                    cx.notify();
                }
            }),
            cx.subscribe(&volume, |this, _, event: &SliderEvent, cx| {
                if let SliderEvent::Change(value) = event {
                    this.player.set_volume(value.start() / 100.);
                    cx.notify();
                }
            }),
        ];

        // Moves the seek bar along while something plays, and notices when a song ends.
        let tick = cx.spawn_in(window, async move |this, cx| {
            loop {
                cx.background_executor().timer(Duration::from_millis(200)).await;
                let alive = this.update_in(cx, |this, window, cx| {
                    if this.player.now().is_none() && this.seek.read(cx).value().start() != 0. {
                        this.seek.update(cx, |seek, cx| seek.set_value(0., window, cx));
                        cx.notify();
                    }
                    if this.player.now().is_some() {
                        if this.player.state() == PlayState::Stopped {
                            this.player.tidy();
                            this.seek.update(cx, |seek, cx| seek.set_value(0., window, cx));
                        } else if let Some(now) = this.player.now() {
                            let length = now.length.as_secs_f32().max(0.001);
                            let at = (this.player.position().as_secs_f32() / length * 1000.).clamp(0., 1000.);
                            this.seek.update(cx, |seek, cx| seek.set_value(at, window, cx));
                        }
                        cx.notify();
                    }
                });
                if alive.is_err() {
                    break;
                }
            }
        });

        let mut view = Self {
            tracks: Vec::new(),
            groups: Vec::new(),
            project,
            packs: Packs::load(),
            settings: Settings::load(),
            songs: Vec::new(),
            scope: Scope::All,
            left: LeftPane::Library,
            center: Center::Tracks,
            visible: Vec::new(),
            selected: HashSet::new(),
            cursor: None,
            anchor: None,
            song_cursor: None,
            songs_visible: Vec::new(),
            song_selected: HashSet::new(),
            song_anchor: None,
            list_scroll: UniformListScrollHandle::new(),
            songs_scroll: UniformListScrollHandle::new(),
            song_table_scroll: UniformListScrollHandle::new(),
            focus: cx.focus_handle(),
            search,
            pack_name,
            title_input,
            credits_input,
            synced_file: None,
            syncing: false,
            seek,
            volume,
            editor,
            player: Player::new(),
            loading_preview: false,
            status: "Loading the track list…".into(),
            log: Vec::new(),
            show_console: false,
            busy: None,
            save_generation: 0,
            saved: true,
            missing: HashSet::new(),
            ffmpeg: true,
            fill_in_order: false,
            fill_only_empty: false,
            last_export: None,
            checked: false,
            shown_demo: false,
            _tick: tick,
            _subscriptions: subscriptions,
        };
        view.load(window, cx);
        view.focus.focus(window, cx);
        view
    }

    // ----- Loading -------------------------------------------------------------------------------------------

    /// Asks the engine for the track list and the library, off the UI thread.
    fn load(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        self.busy = Some(Busy { label: "Loading tracks".into(), done: 0, total: 0 });
        let work = cx.background_executor().spawn(async move {
            let catalog = engine::run_json::<Catalog>(&["catalog"]);
            let songs = engine::run_json::<Vec<Song>>(&["songs-full"]);
            (catalog, songs)
        });
        cx.spawn_in(window, async move |this, cx| {
            let (catalog, songs) = work.await;
            let _ = this.update_in(cx, |this, window, cx| {
                this.busy = None;
                match catalog {
                    Ok(catalog) => {
                        this.say(catalog.message.clone(), cx);
                        this.tracks = catalog.rows;
                        this.groups = catalog.groups;
                    }
                    Err(error) => {
                        this.say(format!("Couldn't load the track list. {error}"), cx);
                        window.push_notification(Notification::error(format!("{error}")).title("Track list"), cx);
                    }
                }
                match songs {
                    Ok(songs) => this.songs = songs,
                    Err(error) => this.say(format!("Couldn't load your songs. {error}"), cx),
                }
                this.scan_missing();
                this.refilter(cx);
                this.open_for_screenshot(window, cx);
                if !this.checked {
                    this.checked = true;
                    this.run_checks(window, cx);
                }
            });
        })
        .detach();
    }

    /// For taking pictures of each screen without clicking: RRMUSIC_SELECT picks a track, RRMUSIC_OPEN opens a screen.
    fn open_for_screenshot(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        if self.shown_demo {
            return;
        }
        self.shown_demo = true;
        if let Ok(file) = std::env::var("RRMUSIC_SELECT") {
            let files: Vec<&str> = file.split(',').collect();
            if let Some(first) = self.tracks.iter().position(|t| files.contains(&t.file.as_str())) {
                for (ix, track) in self.tracks.iter().enumerate() {
                    if files.contains(&track.file.as_str()) {
                        self.selected.insert(ix);
                    }
                }
                self.cursor = Some(first);
            }
        }
        match std::env::var("RRMUSIC_OPEN").unwrap_or_default().as_str() {
            "settings" => self.open_settings("Settings", window, cx),
            "loop" => self.open_loop_editor(window, cx),
            "download" => self.open_download(window, cx),
            "packs" => self.open_installed(window, cx),
            "about" => self.about(window, cx),
            "editor" => self.center = Center::Editor,
            "songs" => self.center = Center::Songs,
            "console" => self.show_console = true,
            _ => {}
        }
    }

    /// Setup problems, FFmpeg, and new versions, said once at start. The very first start opens Settings.
    fn run_checks(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let work = cx.background_executor().spawn(async move { engine::run_json::<Value>(&["check"]) });
        cx.spawn_in(window, async move |this, cx| {
            let Ok(check) = work.await else {
                return;
            };
            let _ = this.update_in(cx, |this, window, cx| {
                this.ffmpeg = check["ffmpeg"].as_bool().unwrap_or(true);
                if !this.ffmpeg {
                    window.push_notification(
                        Notification::warning("Making packs needs FFmpeg. Install it from Settings (Ctrl+,).").title("FFmpeg"),
                        cx,
                    );
                }
                for issue in check["issues"].as_array().into_iter().flatten().filter_map(Value::as_str) {
                    this.say(issue.to_string(), cx);
                    window.push_notification(Notification::warning(issue.to_string()).title("Retro Rewind"), cx);
                }
                if let Some(newer) = check["newer"].as_str() {
                    window.push_notification(
                        Notification::info(format!("Version {newer} is out. Help > Releases page.")).title("Update"),
                        cx,
                    );
                }
                if !this.settings.flag("SetupDone", false) {
                    this.settings.set("SetupDone", json!(true));
                    let _ = this.settings.save();
                    this.open_settings("Welcome. Check these, then close this window.", window, cx);
                }
            });
        })
        .detach();
    }

    fn reload(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        self.project = Project::load();
        self.packs = Packs::load();
        self.settings = Settings::load();
        self.show_name(window, cx);
        self.selected.clear();
        self.cursor = None;
        self.restyle(cx);
        self.load(window, cx);
    }

    fn show_name(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let name = self.project.pack_name();
        self.pack_name.update(cx, |input, cx| input.set_value(name, window, cx));
    }

    fn scan_missing(&mut self) {
        self.missing = self
            .tracks
            .iter()
            .filter(|t| self.project.is_assigned(&t.file) && !Path::new(self.project.source(&t.file)).exists())
            .map(|t| t.file.clone())
            .collect();
    }

    pub fn songs_changed(&mut self, cx: &mut Context<Self>) {
        let work = cx.background_executor().spawn(async move { engine::run_json::<Vec<Song>>(&["songs-full"]) });
        cx.spawn(async move |this, cx| {
            if let Ok(songs) = work.await {
                let _ = this.update(cx, |this, cx| {
                    this.songs = songs;
                    this.song_selected.clear();
                    this.refilter(cx);
                });
            }
        })
        .detach();
    }

    /// Songs that were deleted come off every track that used them.
    pub fn songs_removed(&mut self, removed: &[String], cx: &mut Context<Self>) {
        let gone: HashSet<String> = removed.iter().map(|s| s.to_ascii_lowercase()).collect();
        let files: Vec<String> = self
            .tracks
            .iter()
            .filter(|t| gone.contains(&self.project.source(&t.file).to_ascii_lowercase()))
            .map(|t| t.file.clone())
            .collect();
        for file in &files {
            self.project.clear(file);
        }
        if !files.is_empty() {
            self.say(format!("{} tracks went back to their old music.", files.len()), cx);
            self.changed(cx);
        }
        self.songs_changed(cx);
    }

    // ----- Messages, settings and saving ---------------------------------------------------------------------------

    pub fn say(&mut self, message: impl Into<SharedString>, cx: &mut Context<Self>) {
        let message = message.into();
        if self.log.last() != Some(&message) {
            self.log.push(message.clone());
            if self.log.len() > 500 {
                self.log.drain(..100);
            }
        }
        self.status = message;
        cx.notify();
    }

    /// Saves a moment after the last change, so a burst of edits writes once.
    fn changed(&mut self, cx: &mut Context<Self>) {
        self.saved = false;
        self.save_generation += 1;
        let generation = self.save_generation;
        cx.spawn(async move |this, cx| {
            cx.background_executor().timer(Duration::from_millis(700)).await;
            let _ = this.update(cx, |this, cx| {
                if this.save_generation == generation {
                    this.save(cx);
                }
            });
        })
        .detach();
        cx.notify();
    }

    fn save(&mut self, cx: &mut Context<Self>) -> bool {
        match self.project.save() {
            Ok(()) => {
                self.saved = true;
                cx.notify();
                true
            }
            Err(error) => {
                self.say(format!("Couldn't save. {error}"), cx);
                false
            }
        }
    }

    pub fn settings_snapshot(&self) -> HashMap<String, String> {
        ["OutputFolder", "GameFile", "AccentColor", "BackgroundColor", "BackgroundImage", "GpuiTheme"]
            .iter()
            .map(|key| (key.to_string(), self.settings.text(key)))
            .collect()
    }

    pub fn setting_number(&self, key: &str, fallback: i64) -> i64 {
        self.settings.number(key, fallback)
    }

    pub fn setting_flag(&self, key: &str, fallback: bool) -> bool {
        self.settings.flag(key, fallback)
    }

    pub fn set_setting(&mut self, key: &str, value: Value, cx: &mut Context<Self>) {
        self.settings.set(key, value);
        if let Err(error) = self.settings.save() {
            self.say(format!("Couldn't save the settings. {error}"), cx);
        }
        cx.notify();
    }

    pub fn project_option(&self, key: &str) -> String {
        self.project.option_text(key, "")
    }

    pub fn project_text(&self, key: &str, fallback: &str) -> String {
        self.project.option_text(key, fallback)
    }

    pub fn project_number(&self, key: &str, fallback: i64) -> i64 {
        self.project.option_number(key, fallback)
    }

    pub fn project_flag(&self, key: &str, fallback: bool) -> bool {
        self.project.option_flag(key, fallback)
    }

    pub fn set_project_option(&mut self, key: &str, value: Value, cx: &mut Context<Self>) {
        self.project.set_option(key, value);
        self.changed(cx);
    }

    pub fn ffmpeg_ready(&self) -> bool {
        self.ffmpeg
    }

    pub fn set_rr_folder(&mut self, folder: String, window: &mut Window, cx: &mut Context<Self>) {
        if folder == self.project.option_text("RetroRewindFolder", "") {
            return;
        }
        self.project.set_option("RetroRewindFolder", json!(folder));
        self.save(cx);
        self.say("Reading the tracks from the new Retro Rewind folder…", cx);
        self.load(window, cx);
    }

    /// Puts a changed look on screen straight away.
    pub fn restyle(&mut self, cx: &mut Context<Self>) {
        theme::apply(&self.settings, cx);
        cx.refresh_windows();
    }

    // ----- Packs (several, like the classic window's pack dropdown) ------------------------------------------------

    fn keep_name_unique(&mut self, cx: &mut Context<Self>) {
        let name = self.project.pack_name();
        if self.packs.contains(&name) {
            let unique = self.packs.unique(&name, "");
            self.project.set_pack_name(&unique);
            self.say(format!("Another pack is named {name}, so this one is {unique}."), cx);
            self.changed(cx);
        }
    }

    /// Puts the open pack aside (if anything is set on it) and opens another.
    fn open_pack(&mut self, next: Value, window: &mut Window, cx: &mut Context<Self>) {
        if self.project.has_content() {
            let current = self.project.pack_name();
            let next_name = next["Options"]["PackName"].as_str().unwrap_or("").to_string();
            self.packs.put(&current, self.project.value().clone(), &next_name);
        }
        if let Err(error) = self.packs.save() {
            self.say(format!("Couldn't save the pack list. {error}"), cx);
        }
        self.project.replace(next);
        self.save(cx);
        self.show_name(window, cx);
        self.selected.clear();
        self.cursor = None;
        self.scan_missing();
        self.refilter(cx);
    }

    fn switch_pack(&mut self, name: String, window: &mut Window, cx: &mut Context<Self>) {
        let Some(next) = self.packs.take(&name) else {
            return;
        };
        self.open_pack(next, window, cx);
        self.say(format!("Opened {name}."), cx);
    }

    fn new_pack(&mut self, name: Option<String>, window: &mut Window, cx: &mut Context<Self>) {
        let wanted = name.unwrap_or_else(|| "New pack".into());
        let unique = self.packs.unique(&wanted, &self.project.pack_name());
        let fresh = self.project.fresh(&unique);
        self.open_pack(fresh, window, cx);
        self.say(format!("Started {unique}."), cx);
    }

    fn delete_pack(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let name = self.project.pack_name();
        let set = self.tracks.iter().filter(|t| self.project.is_assigned(&t.file)).count();
        let entity = cx.weak_entity();
        window.open_alert_dialog(cx, move |alert, _, _| {
            let entity = entity.clone();
            alert
                .title(SharedString::from(format!("Delete the pack “{name}”?")))
                .description(SharedString::from(format!(
                    "Its {set} tracks and settings are removed. The song files stay on your PC, and packs you exported aren't touched."
                )))
                .confirm()
                .ok_text("Delete")
                .ok_variant(gpui_kit::component::button::ButtonVariant::Danger)
                .on_ok(move |_, window, cx| {
                    let _ = entity.update(cx, |this, cx| {
                        let deleted = this.project.pack_name();
                        let next = this.packs.names().into_iter().next().and_then(|n| this.packs.take(&n));
                        let next = next.unwrap_or_else(|| this.project.fresh(NEW_PACK));
                        let _ = this.packs.save();
                        this.project.replace(next);
                        this.save(cx);
                        this.show_name(window, cx);
                        this.selected.clear();
                        this.scan_missing();
                        this.refilter(cx);
                        this.say(format!("Deleted {deleted}."), cx);
                    });
                    true
                })
        });
    }

    // ----- Filtering and selection -----------------------------------------------------------------------------

    fn refilter(&mut self, cx: &mut Context<Self>) {
        let query = self.search.read(cx).value().to_lowercase();
        let words: Vec<&str> = query.split_whitespace().collect();
        let project = &self.project;
        self.visible = self
            .tracks
            .iter()
            .enumerate()
            .filter(|(_, track)| match &self.scope {
                Scope::All => true,
                Scope::Set => project.is_assigned(&track.file),
                Scope::Missing => self.missing.contains(&track.file),
                Scope::Category(category) => &track.category == category,
            })
            .filter(|(_, track)| {
                words.is_empty() || {
                    let haystack =
                        format!("{} {} {}", track.name, track.file, project.song_title(&track.file)).to_lowercase();
                    words.iter().all(|word| haystack.contains(word))
                }
            })
            .map(|(ix, _)| ix)
            .collect();
        self.songs_visible = self
            .songs
            .iter()
            .enumerate()
            .filter(|(_, song)| {
                words.is_empty() || {
                    let haystack = song.label.to_lowercase();
                    words.iter().all(|word| haystack.contains(word))
                }
            })
            .map(|(ix, _)| ix)
            .collect();
        cx.notify();
    }

    fn selected_files(&self) -> Vec<String> {
        let mut rows: Vec<usize> = self.selected.iter().copied().collect();
        rows.sort_by_key(|ix| self.visible.iter().position(|v| v == ix).unwrap_or(usize::MAX));
        rows.into_iter().map(|ix| self.tracks[ix].file.clone()).collect()
    }

    fn position_of(&self, track: usize) -> Option<usize> {
        self.visible.iter().position(|ix| *ix == track)
    }

    fn click_row(&mut self, track: usize, modifiers: Modifiers, cx: &mut Context<Self>) {
        if modifiers.shift {
            if let (Some(anchor), Some(to)) = (self.anchor.and_then(|a| self.position_of(a)), self.position_of(track)) {
                let (from, to) = if anchor <= to { (anchor, to) } else { (to, anchor) };
                if !modifiers.control {
                    self.selected.clear();
                }
                self.selected.extend(self.visible[from..=to].iter().copied());
            }
        } else if modifiers.control {
            if !self.selected.remove(&track) {
                self.selected.insert(track);
            }
            self.anchor = Some(track);
        } else {
            self.selected.clear();
            self.selected.insert(track);
            self.anchor = Some(track);
        }
        self.cursor = Some(track);
        cx.notify();
    }

    fn select_where(&mut self, test: impl Fn(&Project, &Track) -> bool, cx: &mut Context<Self>) {
        self.selected = self.visible.iter().copied().filter(|ix| test(&self.project, &self.tracks[*ix])).collect();
        self.cursor = self.visible.iter().copied().find(|ix| self.selected.contains(ix));
        self.say(format!("Selected {} tracks.", self.selected.len()), cx);
    }

    fn move_cursor(&mut self, step: isize, extend: bool, cx: &mut Context<Self>) {
        if self.visible.is_empty() {
            return;
        }
        let at = self.cursor.and_then(|c| self.position_of(c));
        let next = match at {
            Some(at) => (at as isize + step).clamp(0, self.visible.len() as isize - 1) as usize,
            None => 0,
        };
        let track = self.visible[next];
        let modifiers = Modifiers { shift: extend, ..Default::default() };
        self.click_row(track, modifiers, cx);
        self.list_scroll.scroll_to_item(next, ScrollStrategy::Center);
    }

    fn click_song(&mut self, song: usize, modifiers: Modifiers, cx: &mut Context<Self>) {
        let position = |ix: usize, list: &Vec<usize>| list.iter().position(|v| *v == ix);
        if modifiers.shift {
            if let (Some(a), Some(b)) =
                (self.song_anchor.and_then(|a| position(a, &self.songs_visible)), position(song, &self.songs_visible))
            {
                let (from, to) = if a <= b { (a, b) } else { (b, a) };
                self.song_selected = self.songs_visible[from..=to].iter().copied().collect();
            }
        } else if modifiers.control {
            if !self.song_selected.remove(&song) {
                self.song_selected.insert(song);
            }
            self.song_anchor = Some(song);
        } else {
            self.song_selected = HashSet::from([song]);
            self.song_anchor = Some(song);
        }
        cx.notify();
    }

    fn selected_songs(&self) -> Vec<Song> {
        self.songs_visible.iter().filter(|ix| self.song_selected.contains(ix)).map(|ix| self.songs[*ix].clone()).collect()
    }

    // ----- Editing tracks ----------------------------------------------------------------------------------------

    /// Hands songs to tracks one after another, starting again when the songs run out.
    fn assign(&mut self, files: Vec<String>, mut songs: Vec<PathBuf>, cx: &mut Context<Self>) {
        let files: Vec<String> = if self.fill_only_empty && files.len() > 1 {
            files.into_iter().filter(|f| !self.project.is_assigned(f)).collect()
        } else {
            files
        };
        if files.is_empty() || songs.is_empty() {
            if files.is_empty() {
                self.say("Every selected track already has a song.", cx);
            }
            return;
        }
        if files.len() > 1 && !self.fill_in_order {
            shuffle(&mut songs);
        } else if files.len() > 1 {
            songs.sort_by(|a, b| model::natural_cmp(&a.file_name().unwrap_or_default().to_string_lossy(), &b.file_name().unwrap_or_default().to_string_lossy()));
        }
        let _ = model::add_to_library(&songs);
        for (ix, file) in files.iter().enumerate() {
            self.project.assign(file, &songs[ix % songs.len()]);
        }
        let message = if files.len() == 1 {
            format!("{} now plays {}.", self.track_name(&files[0]), self.project.song_title(&files[0]))
        } else {
            format!("Gave {} tracks a song from {}.", files.len(), songs.len())
        };
        self.say(message, cx);
        self.scan_missing();
        self.changed(cx);
        self.songs_changed(cx);
    }

    fn track_name(&self, file: &str) -> String {
        self.tracks.iter().find(|t| t.file == file).map(|t| t.name.clone()).unwrap_or_else(|| file.into())
    }

    fn edit_selected(&mut self, edit: impl Fn(&mut Project, &str), cx: &mut Context<Self>) {
        for file in self.selected_files() {
            edit(&mut self.project, &file);
        }
        self.changed(cx);
    }

    fn choose_songs(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let files = self.selected_files();
        ui::pick_paths(cx.weak_entity(), ui::files("Add songs", true), window, cx, move |this, paths, _, cx| {
            let songs = model::audio_files(&paths);
            if files.is_empty() {
                let _ = model::add_to_library(&songs);
                this.say(format!("Added {} songs to your library.", songs.len()), cx);
                this.songs_changed(cx);
                this.left = LeftPane::Songs;
            } else {
                this.assign(files, songs, cx);
            }
        });
    }

    /// Picks songs and gives each to the track whose name is in its file name.
    fn match_names(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let tracks = self.selected_files();
        ui::pick_paths(cx.weak_entity(), ui::files("Songs named after their tracks", true), window, cx, move |this, paths, window, cx| {
            let songs = model::audio_files(&paths);
            this.match_paths(songs, tracks, window, cx);
        });
    }

    fn match_paths(&mut self, songs: Vec<PathBuf>, tracks: Vec<String>, window: &mut Window, cx: &mut Context<Self>) {
        if songs.is_empty() {
            return;
        }
        let spec = json!({ "files": songs, "tracks": tracks });
        let work = cx.background_executor().spawn(async move { engine::json_with_spec::<Value>(&["match"], &spec) });
        cx.spawn_in(window, async move |this, cx| {
            let result = work.await;
            let _ = this.update(cx, |this, cx| match result {
                Ok(found) => {
                    let mut count = 0;
                    for item in found["matches"].as_array().into_iter().flatten() {
                        if let (Some(file), Some(source)) = (item["file"].as_str(), item["source"].as_str()) {
                            this.project.assign(file, Path::new(source));
                            count += 1;
                        }
                    }
                    for name in found["unmatched"].as_array().into_iter().flatten().filter_map(Value::as_str) {
                        this.say(format!("No single track matches {name}. Drag it onto a track instead."), cx);
                    }
                    let _ = model::add_to_library(&songs);
                    this.say(format!("Matched {count} of {} songs by file name.", songs.len()), cx);
                    this.scan_missing();
                    this.changed(cx);
                    this.songs_changed(cx);
                }
                Err(error) => this.say(format!("Couldn't match them. {error}"), cx),
            });
        })
        .detach();
    }

    fn remove_songs(&mut self, cx: &mut Context<Self>) {
        let files: Vec<String> = self.selected_files().into_iter().filter(|f| self.project.is_assigned(f)).collect();
        if files.is_empty() {
            return;
        }
        for file in &files {
            self.project.clear(file);
        }
        self.say(format!("Took the song off {} {}.", files.len(), if files.len() == 1 { "track" } else { "tracks" }), cx);
        self.scan_missing();
        self.changed(cx);
    }

    fn choose_final(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let Some(file) = self.selected_files().into_iter().next() else {
            return;
        };
        ui::pick_paths(cx.weak_entity(), ui::files("Song for the final lap", false), window, cx, move |this, paths, _, cx| {
            if let Some(song) = model::audio_files(&paths).into_iter().next() {
                this.project.set_final_source(&file, Some(&song));
                let _ = model::add_to_library(&[song]);
                this.say("The final lap now has its own song.", cx);
                this.changed(cx);
            }
        });
    }

    pub fn loop_info(&self, file: &str) -> (String, (Option<f64>, Option<f64>), usize) {
        (self.project.source(file).to_string(), self.project.loop_points(file), self.project.crossfade_index(file))
    }

    pub fn keep_loop(&mut self, file: &str, start: f64, end: f64, crossfade: usize, cx: &mut Context<Self>) {
        self.project.set_loop_points(file, start, end);
        self.project.set_crossfade(file, crossfade);
        self.say(format!("{} loops from {} to {}.", self.track_name(file), model::format_time(start), model::format_time(end)), cx);
        self.changed(cx);
    }

    /// A downloaded song joins the library, and goes on the selected tracks when there are any.
    pub fn downloaded(&mut self, path: PathBuf, cx: &mut Context<Self>) {
        let files = self.selected_files();
        if files.is_empty() {
            let name = path.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
            self.say(format!("Downloaded {name}. It's in your songs."), cx);
            self.songs_changed(cx);
        } else {
            self.assign(files, vec![path], cx);
        }
    }

    // ----- Dialogs ---------------------------------------------------------------------------------------------

    fn open_settings(&mut self, title: &'static str, window: &mut Window, cx: &mut Context<Self>) {
        let weak = cx.weak_entity();
        let (snapshot, rr, author) = (self.settings_snapshot(), self.project_option("RetroRewindFolder"), self.project_option("Author"));
        let view = cx.new(|cx| SettingsView::new(weak, snapshot, rr, author, window, cx));
        window.open_dialog(cx, move |dialog, _, _| dialog.title(title).w(px(620.)).child(view.clone()));
    }

    fn open_loop_editor(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let Some(file) = self.selected_files().into_iter().next() else {
            self.say("Select a track with a song to edit its loop.", cx);
            return;
        };
        if !self.project.is_assigned(&file) {
            self.say(format!("{} has no song yet.", self.track_name(&file)), cx);
            return;
        }
        let title = format!("Loop for {}", self.track_name(&file));
        let weak = cx.weak_entity();
        let info = self.loop_info(&file);
        let view = cx.new(|cx| LoopView::new(weak, file, info, window, cx));
        window.open_dialog(cx, move |dialog, _, _| dialog.title(SharedString::from(title.clone())).w(px(820.)).child(view.clone()));
    }

    fn open_download(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let weak = cx.weak_entity();
        let count = self.selected.len();
        let view = cx.new(|cx| DownloadView::new(weak, count, window, cx));
        window.open_dialog(cx, move |dialog, _, _| dialog.title("Find a song online").w(px(680.)).child(view.clone()));
    }

    fn open_installed(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let weak = cx.weak_entity();
        let view = cx.new(|cx| PacksView::new(weak, window, cx));
        window.open_dialog(cx, move |dialog, _, _| dialog.title("Packs on this PC").w(px(680.)).child(view.clone()));
    }

    fn about(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        window.open_dialog(cx, |dialog, _, cx| {
            dialog.title("Pack Maker").w(px(420.)).child(
                v_flex()
                    .gap_2()
                    .child(format!("Version {}", env!("CARGO_PKG_VERSION")))
                    .child(div().text_color(cx.theme().muted_foreground).child(
                        "Music packs for Retro Rewind. Built with GPUI, on the same tested core as the classic window.",
                    )),
            )
        });
    }

    // ----- Import -----------------------------------------------------------------------------------------------

    fn import(&mut self, zip: bool, window: &mut Window, cx: &mut Context<Self>) {
        let options = if zip { ui::files("Import a pack zip", false) } else { ui::folder("Import a pack folder") };
        ui::pick_paths(cx.weak_entity(), options, window, cx, |this, paths, window, cx| {
            if let Some(path) = paths.into_iter().next() {
                this.ask_import(path, window, cx);
            }
        });
    }

    /// When a pack is already being worked on, asks where the new one's songs should go.
    fn ask_import(&mut self, path: PathBuf, window: &mut Window, cx: &mut Context<Self>) {
        if !self.project.has_content() {
            self.run_import(path, false, false, window, cx);
            return;
        }
        let open = self.project.pack_name();
        let entity = cx.weak_entity();
        let name = path.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
        window.open_dialog(cx, move |dialog, _, cx| {
            let (a, b, c) = (entity.clone(), entity.clone(), entity.clone());
            let (p1, p2, p3) = (path.clone(), path.clone(), path.clone());
            dialog
                .title(SharedString::from(format!("Import “{name}”")))
                .w(px(560.))
                .child(div().text_color(cx.theme().muted_foreground).child(format!("“{open}” is open with songs set.")))
                .footer(
                    h_flex()
                        .gap_2()
                        .justify_end()
                        .child(Button::new("imp-keep").small().ghost().label("Add, keep my songs").on_click(move |_, window, cx| {
                            window.close_dialog(cx);
                            let _ = a.update(cx, |this, cx| this.run_import(p1.clone(), false, true, window, cx));
                        }))
                        .child(Button::new("imp-replace").small().outline().label("Add, replace songs").on_click(move |_, window, cx| {
                            window.close_dialog(cx);
                            let _ = b.update(cx, |this, cx| this.run_import(p2.clone(), false, false, window, cx));
                        }))
                        .child(Button::new("imp-own").small().primary().label("Open as its own pack").on_click(move |_, window, cx| {
                            window.close_dialog(cx);
                            let _ = c.update(cx, |this, cx| this.run_import(p3.clone(), true, false, window, cx));
                        })),
                )
        });
    }

    fn run_import(&mut self, path: PathBuf, own_pack: bool, keep: bool, window: &mut Window, cx: &mut Context<Self>) {
        if own_pack {
            let name = path.file_stem().map(|s| s.to_string_lossy().into_owned());
            self.new_pack(name, window, cx);
        }
        // The engine imports into the saved pack, so what's on screen is saved first.
        if !self.save(cx) {
            return;
        }
        let mut args = vec!["import".to_string(), "--path".into(), path.to_string_lossy().into_owned()];
        if keep {
            args.push("--keep".into());
        }
        self.busy = Some(Busy { label: "Importing".into(), done: 0, total: 0 });
        self.say(format!("Importing {}…", path.display()), cx);
        ui::run_job(cx.weak_entity(), args, None, window, cx, Self::job_news, |this, result, window, cx| {
            this.busy = None;
            match result {
                Ok(done) => {
                    let message = done.message.unwrap_or_default();
                    this.say(message.clone(), cx);
                    window.push_notification(Notification::success(message).title("Imported"), cx);
                    this.project = Project::load();
                    this.scope = Scope::Set;
                    this.center = Center::Tracks;
                    this.load(window, cx);
                }
                Err(error) => {
                    this.say(format!("Couldn't import it. {error}"), cx);
                    window.push_notification(Notification::error(format!("{error}")).title("Import"), cx);
                }
            }
        });
    }

    fn job_news(&mut self, news: JobNews, cx: &mut Context<Self>) {
        match news {
            JobNews::Progress(done, total) => {
                if let Some(busy) = &mut self.busy {
                    busy.done = done;
                    busy.total = total;
                }
            }
            JobNews::Log(message) => self.say(message, cx),
        }
    }

    // ----- Songs tab actions ---------------------------------------------------------------------------------------

    fn use_songs_on_tracks(&mut self, cx: &mut Context<Self>) {
        let songs: Vec<PathBuf> = self.selected_songs().into_iter().map(|s| PathBuf::from(s.path)).collect();
        let files = self.selected_files();
        if files.is_empty() {
            self.say("Select tracks on the Tracks tab first, then use the songs on them.", cx);
            return;
        }
        self.assign(files, songs, cx);
    }

    fn take_songs_off_tracks(&mut self, cx: &mut Context<Self>) {
        let chosen: HashSet<String> = self.selected_songs().into_iter().map(|s| s.path.to_ascii_lowercase()).collect();
        let files: Vec<String> = self
            .tracks
            .iter()
            .filter(|t| chosen.contains(&self.project.source(&t.file).to_ascii_lowercase()))
            .map(|t| t.file.clone())
            .collect();
        for file in &files {
            self.project.clear(file);
        }
        self.say(format!("Took them off {} tracks.", files.len()), cx);
        self.changed(cx);
    }

    fn delete_songs(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let songs = self.selected_songs();
        if songs.is_empty() {
            return;
        }
        let entity = cx.weak_entity();
        let count = songs.len();
        let owned = songs.iter().filter(|s| s.owned).count();
        window.open_alert_dialog(cx, move |alert, _, _| {
            let entity = entity.clone();
            let songs = songs.clone();
            alert
                .title(SharedString::from(format!("Delete {count} {}?", if count == 1 { "song" } else { "songs" })))
                .description(SharedString::from(format!(
                    "{owned} were downloaded, made or imported here and go to the Recycle Bin. Your own files only leave the list. Tracks using them go back to their old music."
                )))
                .confirm()
                .ok_text("Delete")
                .ok_variant(gpui_kit::component::button::ButtonVariant::Danger)
                .on_ok(move |_, window, cx| {
                    let spec = json!({ "files": songs.iter().map(|s| s.path.clone()).collect::<Vec<_>>() });
                    let _ = entity.update(cx, |this, cx| {
                        this.say("Deleting…", cx);
                        ui::run_job(cx.weak_entity(), vec!["songs-delete".into()], Some(spec), window, cx, Self::job_news, |this, result, _, cx| {
                            let removed: Vec<String> = match &result {
                                Ok(done) => done
                                    .extra
                                    .get("removed")
                                    .and_then(|v| v.as_array())
                                    .map(|l| l.iter().filter_map(|s| s.as_str().map(String::from)).collect())
                                    .unwrap_or_default(),
                                Err(_) => Vec::new(),
                            };
                            match result {
                                Ok(done) => this.say(done.message.unwrap_or_default(), cx),
                                Err(error) => this.say(format!("Couldn't delete all of them. {error}"), cx),
                            }
                            this.songs_removed(&removed, cx);
                        });
                    });
                    true
                })
        });
    }

    fn send_to_editor(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let songs: Vec<PathBuf> = self.selected_songs().into_iter().map(|s| PathBuf::from(s.path)).collect();
        if songs.is_empty() {
            return;
        }
        self.editor.update(cx, |editor, cx| editor.add_songs(songs, window, cx));
        self.center = Center::Editor;
        cx.notify();
    }

    // ----- Playback --------------------------------------------------------------------------------------------

    fn play_track(&mut self, file: String, window: &mut Window, cx: &mut Context<Self>) {
        self.play_track_lap(file, false, window, cx);
    }

    fn play_track_lap(&mut self, file: String, final_lap: bool, window: &mut Window, cx: &mut Context<Self>) {
        if !self.project.is_assigned(&file) {
            self.say(format!("{} has no song yet. Drop one on it, or press Ctrl+O.", self.track_name(&file)), cx);
            return;
        }
        // The engine reads the saved pack, so what plays has the latest volume and speed.
        self.save(cx);
        let lap = if final_lap { " (final lap)" } else { "" };
        let title = format!("{} — {}{lap}", self.project.song_title(&file), self.track_name(&file));
        let mut args = vec!["preview".to_string(), "--track".into(), file.clone()];
        if final_lap {
            args.push("--final".into());
        }
        self.start_preview(args, title, Some(file), window, cx);
    }

    fn play_song(&mut self, song: Song, window: &mut Window, cx: &mut Context<Self>) {
        let args = vec!["preview".to_string(), "--song".into(), song.path.clone()];
        self.start_preview(args, song.label.clone(), None, window, cx);
    }

    /// Plays a WAV the engine already made, like a loop's jump or the editor's song.
    pub fn play_file(&mut self, wav: PathBuf, title: String, window: &mut Window, cx: &mut Context<Self>) {
        match self.player.play(&wav, title.clone(), None) {
            Ok(()) => self.say(format!("Playing {title}"), cx),
            Err(error) => {
                self.say(format!("Couldn't play it. {error}"), cx);
                window.push_notification(Notification::error(format!("{error}")).title("Playback"), cx);
            }
        }
    }

    fn start_preview(&mut self, args: Vec<String>, title: String, track: Option<String>, window: &mut Window, cx: &mut Context<Self>) {
        self.loading_preview = true;
        self.status = format!("Getting {title} ready…").into();
        cx.notify();
        let work = cx.background_executor().spawn(async move {
            let args: Vec<&str> = args.iter().map(String::as_str).collect();
            engine::preview(&args)
        });
        cx.spawn_in(window, async move |this, cx| {
            let wav = work.await;
            let _ = this.update_in(cx, |this, window, cx| {
                this.loading_preview = false;
                match wav.and_then(|wav| this.player.play(&wav, title.clone(), track)) {
                    Ok(()) => this.say(format!("Playing {title}"), cx),
                    Err(error) => {
                        this.say(format!("Couldn't play it. {error}"), cx);
                        window.push_notification(Notification::error(format!("{error}")).title("Playback"), cx);
                    }
                }
            });
        })
        .detach();
    }

    fn play_pause(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        if self.player.state() == PlayState::Stopped {
            if let Some(track) = self.cursor {
                let file = self.tracks[track].file.clone();
                self.play_track(file, window, cx);
            }
        } else {
            self.player.toggle();
            cx.notify();
        }
    }

    /// The next or previous track with a song, in list order, from what's playing or selected.
    fn play_step(&mut self, step: isize, window: &mut Window, cx: &mut Context<Self>) {
        let from_file = self.player.now().and_then(|now| now.track.clone());
        let start = from_file
            .and_then(|file| self.visible.iter().position(|ix| self.tracks[*ix].file == file))
            .or_else(|| self.cursor.and_then(|c| self.position_of(c)));
        let count = self.visible.len() as isize;
        if count == 0 {
            return;
        }
        let mut at = start.map(|s| s as isize).unwrap_or(if step > 0 { -1 } else { count });
        for _ in 0..count {
            at += step;
            if at < 0 || at >= count {
                break;
            }
            let track = self.visible[at as usize];
            if self.project.is_assigned(&self.tracks[track].file) {
                self.click_row(track, Modifiers::default(), cx);
                self.list_scroll.scroll_to_item(at as usize, ScrollStrategy::Center);
                let file = self.tracks[track].file.clone();
                self.play_track(file, window, cx);
                return;
            }
        }
        self.say("No more tracks with songs that way.", cx);
    }

    // ----- Export ----------------------------------------------------------------------------------------------

    fn export(&mut self, mode: &'static str, window: &mut Window, cx: &mut Context<Self>) {
        if self.busy.is_some() {
            return;
        }
        if !self.tracks.iter().any(|t| self.project.is_assigned(&t.file)) {
            self.say("Add a song to at least one track first.", cx);
            return;
        }
        if !self.ffmpeg {
            window.push_notification(Notification::warning("Making packs needs FFmpeg. Install it from Settings.").title("FFmpeg"), cx);
            return;
        }
        if !self.save(cx) {
            return;
        }
        let name = self.project.pack_name();
        let start_in = PathBuf::from(self.settings.text("ExportFolder"));
        let start_in = if start_in.is_dir() { start_in } else { dirs::download_dir().unwrap_or_else(|| PathBuf::from(".")) };
        let wii = self.project.for_wii();
        let entity = cx.weak_entity();
        let target: Task<Option<PathBuf>> = match mode {
            "zip" => {
                let picked = cx.prompt_for_new_path(&start_in, Some(&format!("{name}.zip")));
                cx.background_executor().spawn(async move { picked.await.ok()?.ok()? })
            }
            "folder" => {
                let picked = cx.prompt_for_paths(ui::folder("Copy songs here"));
                cx.background_executor().spawn(async move { picked.await.ok()?.ok()?.and_then(|p| p.into_iter().next()) })
            }
            _ => Task::ready(Some(PathBuf::new())),
        };
        window
            .spawn(cx, async move |cx| {
                let Some(target) = target.await else {
                    return;
                };
                let _ = entity.update_in(cx, |this, window, cx| {
                    let mut args = vec!["export".to_string(), "--mode".into(), mode.into()];
                    if mode != "game" {
                        args.push("--target".into());
                        args.push(target.to_string_lossy().into_owned());
                        let folder = if mode == "zip" { target.parent().map(Path::to_path_buf) } else { Some(target.clone()) };
                        if let Some(folder) = folder {
                            this.settings.set("ExportFolder", json!(folder.to_string_lossy()));
                            let _ = this.settings.save();
                        }
                    }
                    args.push(if wii { "--wii".into() } else { "--dolphin".into() });
                    this.busy = Some(Busy { label: "Making the pack".into(), done: 0, total: 0 });
                    this.say(format!("Making {name}…"), cx);
                    ui::run_job(cx.weak_entity(), args, None, window, cx, Self::job_news, move |this, result, window, cx| {
                        this.busy = None;
                        match result {
                            Ok(done) => {
                                let message = done.message.clone().unwrap_or_else(|| "Done.".into());
                                let place = done.location.clone().unwrap_or_default();
                                this.last_export = (!place.is_empty()).then(|| PathBuf::from(&place));
                                this.say(format!("{message} {place}"), cx);
                                window.push_notification(Notification::success(message).title("Pack made"), cx);
                            }
                            Err(error) => {
                                this.say(format!("Couldn't make the pack. {error}"), cx);
                                window.push_notification(Notification::error(format!("{error}")).title("Export"), cx);
                            }
                        }
                    });
                });
            })
            .detach();
    }

    fn remove_from_game(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let name = self.project.pack_name();
        let entity = cx.weak_entity();
        window.open_alert_dialog(cx, move |alert, _, _| {
            let entity = entity.clone();
            alert
                .title(SharedString::from(format!("Take “{name}” out of the game?")))
                .description("Retro Rewind's own songs go back in the strm folder. Your pack and songs stay.")
                .confirm()
                .ok_text("Take out")
                .on_ok(move |_, window, cx| {
                    let _ = entity.update(cx, |_, cx| {
                        ui::run_job(cx.weak_entity(), vec!["remove".into()], None, window, cx, Self::job_news, |this, result, _, cx| match result {
                            Ok(done) => this.say(done.message.unwrap_or_default(), cx),
                            Err(error) => this.say(format!("Couldn't take it out. {error}"), cx),
                        });
                    });
                    true
                })
        });
    }

    fn open_classic(&mut self, cx: &mut Context<Self>) {
        self.save(cx);
        match engine::engine_path() {
            Some(exe) => {
                let _ = std::process::Command::new(exe).spawn();
                self.say("Opened the classic window. Use File > Reload here after changing things there.", cx);
            }
            None => self.say("The classic app wasn't found next to this one.", cx),
        }
    }
}

/// Mixes songs for a shuffled fill, so neighboring tracks don't get the same order every time.
fn shuffle(songs: &mut [PathBuf]) {
    let mut seed = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_nanos() as u64).unwrap_or(7) | 1;
    for i in (1..songs.len()).rev() {
        seed ^= seed << 13;
        seed ^= seed >> 7;
        seed ^= seed << 17;
        songs.swap(i, (seed % (i as u64 + 1)) as usize);
    }
}

// ----- Rendering ---------------------------------------------------------------------------------------------------

impl PackMaker {
    fn render_toolbar(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let state = self.player.state();
        let playing = state == PlayState::Playing;
        let time = match self.player.now() {
            Some(now) => format!(
                "{} / {}",
                model::format_time(self.player.position().as_secs_f64()),
                model::format_time(now.length.as_secs_f64())
            ),
            None => "0:00 / 0:00".into(),
        };
        let wii = self.project.for_wii();
        let packs = self.packs.names();
        let entity = cx.weak_entity();
        let menu = |id: &'static str, label: &'static str| Button::new(id).ghost().xsmall().label(label);

        h_flex()
            .id("toolbar")
            .h_10()
            .px_2()
            .gap_1()
            .flex_shrink_0()
            .bg(cx.theme().title_bar)
            .border_b_1()
            .border_color(cx.theme().title_bar_border)
            .child(menu("menu-file", "File").dropdown_menu(|m: PopupMenu, _, _| {
                m.menu("Add songs…", Box::new(AddSongs))
                    .menu("Import a pack folder…", Box::new(ImportFolder))
                    .menu("Import a pack zip…", Box::new(ImportZip))
                    .menu("Packs on this PC…", Box::new(ShowInstalled))
                    .separator()
                    .menu("Save", Box::new(SaveNow))
                    .menu("Reload from disk", Box::new(Reload))
                    .separator()
                    .menu("Save as zip…", Box::new(ExportZip))
                    .menu("Copy songs to a folder…", Box::new(ExportFolder))
                    .menu("Show the exported pack", Box::new(ShowExported))
                    .separator()
                    .menu("Settings…", Box::new(OpenSettings))
                    .menu("Open the classic window", Box::new(OpenClassic))
                    .separator()
                    .menu("Exit", Box::new(Quit))
            }))
            .child(menu("menu-edit", "Edit").dropdown_menu(|m: PopupMenu, _, _| {
                m.menu("Select all", Box::new(SelectAll))
                    .menu("Select tracks without a song", Box::new(SelectEmpty))
                    .menu("Select tracks with a song", Box::new(SelectFilled))
                    .separator()
                    .menu("Edit loop…", Box::new(EditLoop))
                    .menu("Louder (1 dB)", Box::new(Louder))
                    .menu("Quieter (1 dB)", Box::new(Quieter))
                    .menu("Remove songs", Box::new(RemoveSongs))
                    .separator()
                    .menu("Find a song online…", Box::new(FindOnline))
                    .menu("Filter", Box::new(FocusSearch))
            }))
            .child(menu("menu-playback", "Playback").dropdown_menu(|m: PopupMenu, _, _| {
                m.menu("Play or pause", Box::new(PlayPause))
                    .menu("Stop", Box::new(Stop))
                    .menu("Next track with a song", Box::new(PlayNext))
                    .menu("Previous track with a song", Box::new(PlayPrevious))
                    .menu("Hear the final lap", Box::new(HearFinal))
            }))
            .child(menu("menu-pack", "Pack").dropdown_menu(move |mut m: PopupMenu, _, _| {
                for name in &packs {
                    let (name, entity) = (name.clone(), entity.clone());
                    m = m.item(PopupMenuItem::new(format!("Open {name}")).on_click(move |_, window, cx| {
                        let _ = entity.update(cx, |this, cx| this.switch_pack(name.clone(), window, cx));
                    }));
                }
                if !packs.is_empty() {
                    m = m.separator();
                }
                m.menu("New pack", Box::new(NewPack))
                    .menu("Delete this pack…", Box::new(DeletePack))
                    .separator()
                    .menu_with_check("Made for a real Wii", wii, Box::new(ToggleWii))
                    .menu("Add to the game", Box::new(AddToGame))
                    .menu("Take out of the game…", Box::new(RemoveFromGame))
            }))
            .child(menu("menu-view", "View").dropdown_menu(|m: PopupMenu, _, _| {
                m.menu("Tracks", Box::new(ShowTracks))
                    .menu("Songs", Box::new(ShowSongs))
                    .menu("Song editor", Box::new(ShowEditor))
                    .separator()
                    .menu("Console", Box::new(ToggleConsole))
                    .menu("Settings and themes…", Box::new(OpenSettings))
            }))
            .child(menu("menu-help", "Help").dropdown_menu(|m: PopupMenu, _, _| {
                m.menu("Releases page", Box::new(OpenReleases)).menu("About", Box::new(About))
            }))
            .child(div().w_px().h_5().mx_1().bg(cx.theme().border))
            .child(
                Button::new("prev").ghost().small().icon(Lucide::SkipBack).tooltip("Previous (Ctrl+Left)")
                    .on_click(cx.listener(|this, _, window, cx| this.play_step(-1, window, cx))),
            )
            .child(
                Button::new("play")
                    .ghost()
                    .small()
                    .loading(self.loading_preview)
                    .icon(if playing { Lucide::Pause } else { Lucide::Play })
                    .tooltip(if playing { "Pause (Space)" } else { "Play (Space)" })
                    .on_click(cx.listener(|this, _, window, cx| this.play_pause(window, cx))),
            )
            .child(
                Button::new("stop").ghost().small().icon(Lucide::Square).tooltip("Stop (Ctrl+.)")
                    .disabled(state == PlayState::Stopped)
                    .on_click(cx.listener(|this, _, _, cx| {
                        this.player.stop();
                        cx.notify();
                    })),
            )
            .child(
                Button::new("next").ghost().small().icon(Lucide::SkipForward).tooltip("Next (Ctrl+Right)")
                    .on_click(cx.listener(|this, _, window, cx| this.play_step(1, window, cx))),
            )
            .child(
                h_flex()
                    .flex_1()
                    .min_w_32()
                    .mx_2()
                    .gap_2()
                    .child(div().flex_1().child(Slider::new(&self.seek).disabled(state == PlayState::Stopped)))
                    .child(
                        div()
                            .text_xs()
                            .text_color(cx.theme().muted_foreground)
                            .font_family(cx.theme().mono_font_family.clone())
                            .child(time),
                    ),
            )
            .child(Icon::new(Lucide::Volume2).small().text_color(cx.theme().muted_foreground))
            .child(div().w_24().child(Slider::new(&self.volume)))
            .child(div().w_px().h_5().mx_1().bg(cx.theme().border))
            .child(div().w(px(176.)).child(Input::new(&self.pack_name).small()))
            .child(
                Button::new("settings").ghost().small().icon(Lucide::Settings).tooltip("Settings and themes (Ctrl+,)")
                    .on_click(cx.listener(|this, _, window, cx| this.open_settings("Settings", window, cx))),
            )
            .child(
                Button::new("export")
                    .primary()
                    .small()
                    .icon(Lucide::PackageOpen)
                    .label("Export")
                    .loading(self.busy.is_some())
                    .dropdown_menu_with_anchor(Anchor::TopRight, move |m: PopupMenu, _, _| {
                        m.menu("Save as zip…", Box::new(ExportZip))
                            .menu("Copy songs to a folder…", Box::new(ExportFolder))
                            .menu("Add to the game", Box::new(AddToGame))
                            .separator()
                            .menu_with_check("Made for a real Wii", wii, Box::new(ToggleWii))
                    }),
            )
    }

    fn render_left(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let current = self.left;
        let tab = |id: &'static str, label: &'static str, pane: LeftPane| {
            Button::new(id).ghost().xsmall().label(label).selected(pane == current)
        };
        v_flex()
            .size_full()
            .bg(cx.theme().sidebar)
            .child(
                h_flex()
                    .h_8()
                    .px_2()
                    .gap_1()
                    .flex_shrink_0()
                    .border_b_1()
                    .border_color(cx.theme().border)
                    .child(tab("tab-library", "Library", LeftPane::Library).on_click(cx.listener(|this, _, _, cx| {
                        this.left = LeftPane::Library;
                        cx.notify();
                    })))
                    .child(tab("tab-songs", "Songs", LeftPane::Songs).on_click(cx.listener(|this, _, _, cx| {
                        this.left = LeftPane::Songs;
                        cx.notify();
                    }))),
            )
            .child(match self.left {
                LeftPane::Library => self.render_library(cx).into_any_element(),
                LeftPane::Songs => self.render_song_list(cx).into_any_element(),
            })
    }

    fn render_library(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let mut counts: HashMap<&str, (usize, usize)> = HashMap::new();
        let mut set_total = 0;
        for track in &self.tracks {
            let entry = counts.entry(track.category.as_str()).or_default();
            entry.0 += 1;
            if self.project.is_assigned(&track.file) {
                entry.1 += 1;
                set_total += 1;
            }
        }
        let theme = cx.theme();
        let danger = theme.danger;
        let row = |id: SharedString, label: String, count: String, selected: bool| {
            h_flex()
                .id(id)
                .h_6()
                .px_3()
                .gap_2()
                .rounded(theme.radius)
                .mx_1()
                .when(selected, |el| el.bg(theme.list_active))
                .when(!selected, |el| el.hover(|el| el.bg(theme.list_hover)))
                .child(div().flex_1().truncate().child(label))
                .child(div().text_xs().text_color(theme.muted_foreground).child(count))
        };

        let mut list = v_flex().py_1().gap_px().id("library").overflow_y_scroll().flex_1();
        list = list
            .child(
                row("scope-all".into(), "All tracks".into(), self.tracks.len().to_string(), self.scope == Scope::All)
                    .on_click(cx.listener(|this, _, _, cx| this.set_scope(Scope::All, cx))),
            )
            .child(
                row("scope-set".into(), "Songs I set".into(), set_total.to_string(), self.scope == Scope::Set)
                    .on_click(cx.listener(|this, _, _, cx| this.set_scope(Scope::Set, cx))),
            );
        if !self.missing.is_empty() {
            list = list.child(
                row("scope-missing".into(), "Missing song files".into(), self.missing.len().to_string(), self.scope == Scope::Missing)
                    .text_color(danger)
                    .on_click(cx.listener(|this, _, _, cx| this.set_scope(Scope::Missing, cx))),
            );
        }
        for group in self.groups.clone() {
            list = list.child(
                div().px_4().pt_3().pb_1().text_xs().text_color(cx.theme().muted_foreground).child(group.title.clone()),
            );
            for category in group.categories {
                let (total, set) = counts.get(category.as_str()).copied().unwrap_or_default();
                let selected = self.scope == Scope::Category(category.clone());
                let scope = Scope::Category(category.clone());
                let count = if set > 0 { format!("{set}/{total}") } else { total.to_string() };
                list = list.child(
                    row(format!("scope-{category}").into(), category.clone(), count, selected)
                        .on_click(cx.listener(move |this, _, _, cx| this.set_scope(scope.clone(), cx))),
                );
            }
        }
        list
    }

    fn set_scope(&mut self, scope: Scope, cx: &mut Context<Self>) {
        self.scope = scope;
        self.center = Center::Tracks;
        self.refilter(cx);
        self.list_scroll.scroll_to_item(0, ScrollStrategy::Top);
    }

    /// The small song list on the left, for dragging songs onto tracks.
    fn render_song_list(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let count = self.songs.len();
        let body = if count == 0 {
            v_flex()
                .flex_1()
                .p_4()
                .gap_2()
                .text_color(cx.theme().muted_foreground)
                .child("No songs yet.")
                .child("Add some, find them online, or drop files straight onto tracks.")
                .into_any_element()
        } else {
            uniform_list(
                "songs",
                count,
                cx.processor(|this, range: std::ops::Range<usize>, _window, cx| {
                    range
                        .map(|ix| {
                            let song = this.songs[ix].clone();
                            let selected = this.song_cursor == Some(ix);
                            let theme = cx.theme();
                            let drag = SongDrag { paths: vec![PathBuf::from(&song.path)], label: song.label.clone().into() };
                            let play = song.clone();
                            h_flex()
                                .id(SharedString::from(format!("song-{}", song.path)))
                                .w_full()
                                .h_6()
                                .px_3()
                                .gap_2()
                                .when(ix % 2 == 1, |el| el.bg(theme.list_even))
                                .when(selected, |el| el.bg(theme.list_active))
                                .hover(|el| el.bg(theme.list_hover))
                                .child(Icon::new(Lucide::Music).xsmall().text_color(theme.muted_foreground))
                                .child(div().flex_1().truncate().child(song.label.clone()))
                                .child(div().text_xs().text_color(theme.muted_foreground).child(model::format_time(song.seconds)))
                                .on_drag(drag, |drag: &SongDrag, _, _, cx| cx.new(|_| DragChip(drag.label.clone())))
                                .on_click(cx.listener(move |this, event: &ClickEvent, window, cx| {
                                    this.song_cursor = Some(ix);
                                    if event.click_count() >= 2 {
                                        this.play_song(play.clone(), window, cx);
                                    }
                                    cx.notify();
                                }))
                        })
                        .collect::<Vec<_>>()
                }),
            )
            .track_scroll(&self.songs_scroll)
            .flex_1()
            .into_any_element()
        };
        v_flex()
            .flex_1()
            .child(body)
            .child(
                h_flex()
                    .p_2()
                    .gap_2()
                    .border_t_1()
                    .border_color(cx.theme().border)
                    .child(Button::new("songs-add").small().outline().icon(Lucide::Plus).label("Add").on_click(cx.listener(|this, _, window, cx| {
                        this.selected.clear();
                        this.choose_songs(window, cx);
                    })))
                    .child(Button::new("songs-find").small().outline().icon(Lucide::Search).label("Find online").on_click(cx.listener(|this, _, window, cx| this.open_download(window, cx)))),
            )
    }

    fn render_center(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let current = self.center;
        let tab = |id: &'static str, label: &'static str, which: Center| {
            Button::new(id).ghost().xsmall().label(label).selected(which == current)
        };
        let count = match self.center {
            Center::Tracks => format!("{} shown", self.visible.len()),
            Center::Songs => format!("{} songs", self.songs_visible.len()),
            Center::Editor => String::new(),
        };
        let body = match self.center {
            Center::Tracks => self.render_playlist(cx).into_any_element(),
            Center::Songs => self.render_song_table(cx).into_any_element(),
            Center::Editor => self.editor.clone().into_any_element(),
        };
        v_flex()
            .size_full()
            .child(
                h_flex()
                    .h_8()
                    .flex_shrink_0()
                    .px_2()
                    .gap_1()
                    .border_b_1()
                    .border_color(cx.theme().border)
                    .child(tab("center-tracks", "Tracks", Center::Tracks).on_click(cx.listener(|this, _, _, cx| {
                        this.center = Center::Tracks;
                        cx.notify();
                    })))
                    .child(tab("center-songs", "Songs", Center::Songs).on_click(cx.listener(|this, _, _, cx| {
                        this.center = Center::Songs;
                        cx.notify();
                    })))
                    .child(tab("center-editor", "Editor", Center::Editor).on_click(cx.listener(|this, _, _, cx| {
                        this.center = Center::Editor;
                        cx.notify();
                    })))
                    .child(div().w_px().h_4().mx_1().bg(cx.theme().border))
                    .when(self.center != Center::Editor, |el| {
                        el.child(
                            div().flex_1().child(
                                Input::new(&self.search).small().cleanable(true).prefix(Icon::new(Lucide::Search).xsmall()),
                            ),
                        )
                        .child(div().text_xs().text_color(cx.theme().muted_foreground).child(count))
                    }),
            )
            .child(div().flex_1().min_h_0().child(body))
    }

    fn render_playlist(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = cx.theme();
        let header = h_flex()
            .h_7()
            .flex_shrink_0()
            .px_2()
            .gap_2()
            .bg(theme.list_head)
            .border_b_1()
            .border_color(theme.border)
            .text_xs()
            .text_color(theme.muted_foreground)
            .child(div().w_4())
            .child(div().w_12().child("Console"))
            .child(div().flex_1().min_w_32().child("Track"))
            .child(div().flex_1().min_w_32().child("Song"))
            .child(div().w_16().text_right().child("Volume"))
            .child(div().w_32().child("Speed"))
            .child(div().w_20().child("Loop"))
            .child(div().w_20().child("File"));

        let count = self.visible.len();
        let list = if count == 0 {
            v_flex()
                .size_full()
                .items_center()
                .justify_center()
                .gap_1()
                .text_color(theme.muted_foreground)
                .child(if self.tracks.is_empty() { "Loading tracks…" } else { "No tracks match." })
                .into_any_element()
        } else {
            uniform_list(
                "playlist",
                count,
                cx.processor(|this, range: std::ops::Range<usize>, _window, cx| {
                    range.map(|pos| this.render_row(pos, cx).into_any_element()).collect::<Vec<_>>()
                }),
            )
            .track_scroll(&self.list_scroll)
            .size_full()
            .into_any_element()
        };

        v_flex().id("playlist-area").size_full().child(header).child(
            div()
                .id("playlist-drop")
                .flex_1()
                .min_h_0()
                .drag_over::<ExternalPaths>(|style, _, _, cx| style.bg(cx.theme().drop_target.opacity(0.4)))
                // Dropping on the list but not on a track matches songs to tracks by their file names.
                .on_drop(cx.listener(|this, paths: &ExternalPaths, window, cx| {
                    let songs = model::audio_files(paths.paths());
                    this.match_paths(songs, Vec::new(), window, cx);
                }))
                .child(list),
        )
    }

    fn render_row(&mut self, pos: usize, cx: &mut Context<Self>) -> impl IntoElement {
        let track_ix = self.visible[pos];
        let track = &self.tracks[track_ix];
        let file = track.file.clone();
        let theme = cx.theme();
        let selected = self.selected.contains(&track_ix);
        let focused = self.cursor == Some(track_ix);
        let playing = self.player.now().and_then(|n| n.track.as_deref()) == Some(file.as_str());
        let assigned = self.project.is_assigned(&file);
        let missing = self.missing.contains(&file);
        let song = if assigned { self.project.song_title(&file) } else { String::new() };
        let volume = self.project.volume(&file);
        let speed = SPEEDS[self.project.speed_index(&file)].text;
        let looping = LOOPS[self.project.loop_index(&file)].1;
        let muted = theme.muted_foreground;
        let text = if playing { theme.primary } else { theme.foreground };
        let entity = cx.weak_entity();

        let drop_file = file.clone();
        let drop_song_file = file.clone();
        let menu_file = file.clone();
        h_flex()
            .id(SharedString::from(format!("row-{file}")))
            .w_full()
            .h_6()
            .px_2()
            .gap_2()
            .text_color(text)
            .border_l_2()
            .border_color(if focused { theme.primary } else { gpui_kit::transparent_black() })
            .when(pos % 2 == 1, |el| el.bg(theme.list_even))
            .when(selected, |el| el.bg(theme.list_active))
            .hover(|el| el.bg(theme.list_hover))
            .drag_over::<ExternalPaths>(|style, _, _, cx| style.bg(cx.theme().drop_target))
            .drag_over::<SongDrag>(|style, _, _, cx| style.bg(cx.theme().drop_target))
            .on_drop(cx.listener(move |this, paths: &ExternalPaths, _, cx| {
                let songs = model::audio_files(paths.paths());
                let files = this.drop_targets(&drop_file);
                this.assign(files, songs, cx);
            }))
            .on_drop(cx.listener(move |this, drag: &SongDrag, _, cx| {
                let files = this.drop_targets(&drop_song_file);
                this.assign(files, drag.paths.clone(), cx);
            }))
            .on_mouse_down(MouseButton::Right, cx.listener(move |this, _: &MouseDownEvent, _, cx| {
                // A right click on a track outside the selection selects just that track, like a file manager.
                if !this.selected.contains(&track_ix) {
                    this.click_row(track_ix, Modifiers::default(), cx);
                }
            }))
            .on_click(cx.listener(move |this, event: &ClickEvent, window, cx| {
                this.focus.focus(window, cx);
                if event.click_count() >= 2 {
                    let file = this.tracks[track_ix].file.clone();
                    this.play_track(file, window, cx);
                } else {
                    this.click_row(track_ix, event.modifiers(), cx);
                }
            }))
            .child(
                div().w_4().flex_shrink_0().child(if playing {
                    Icon::new(Lucide::Play).xsmall().text_color(theme.primary).into_any_element()
                } else if missing {
                    Icon::new(Lucide::TriangleAlert).xsmall().text_color(theme.danger).into_any_element()
                } else if assigned {
                    div().size_1p5().rounded_full().bg(muted).into_any_element()
                } else {
                    div().into_any_element()
                }),
            )
            .child(div().w_12().flex_shrink_0().text_xs().text_color(muted).child(track.platform.clone()))
            .child(div().flex_1().min_w_32().truncate().child(track.short_name().to_string()))
            .child(
                div()
                    .flex_1()
                    .min_w_32()
                    .truncate()
                    .when(!assigned, |el| el.text_color(muted))
                    .when(missing, |el| el.text_color(theme.danger))
                    .child(if missing { format!("{song} (file missing)") } else if assigned { song } else { "—".into() }),
            )
            .child(
                div()
                    .w_16()
                    .text_right()
                    .text_xs()
                    .text_color(if volume == 0 { muted } else { text })
                    .child(if volume == 0 { "0 dB".to_string() } else { format!("{volume:+} dB") }),
            )
            .child(div().w_32().truncate().text_xs().text_color(if speed == "Normal" { muted } else { text }).child(speed))
            .child(div().w_20().truncate().text_xs().text_color(muted).child(looping))
            .child(div().w_20().truncate().text_xs().text_color(muted).font_family(theme.mono_font_family.clone()).child(file))
            .context_menu(move |menu, _, _| {
                let item = |label: &'static str, run: fn(&mut PackMaker, String, &mut Window, &mut Context<PackMaker>)| {
                    let (entity, file) = (entity.clone(), menu_file.clone());
                    PopupMenuItem::new(label).on_click(move |_, window, cx| {
                        let _ = entity.update(cx, |this, cx| run(this, file.clone(), window, cx));
                    })
                };
                menu.item(item("Play", |this, file, window, cx| this.play_track(file, window, cx)))
                    .item(item("Hear the final lap", |this, file, window, cx| this.play_track_lap(file, true, window, cx)))
                    .separator()
                    .item(item("Choose song…", |this, _, window, cx| this.choose_songs(window, cx)))
                    .item(item("Find a song online…", |this, _, window, cx| this.open_download(window, cx)))
                    .item(item("Edit loop…", |this, _, window, cx| this.open_loop_editor(window, cx)))
                    .separator()
                    .item(item("Louder (1 dB)", |this, _, _, cx| this.edit_selected(|p, f| p.set_volume(f, p.volume(f) + 1), cx)))
                    .item(item("Quieter (1 dB)", |this, _, _, cx| this.edit_selected(|p, f| p.set_volume(f, p.volume(f) - 1), cx)))
                    .separator()
                    .item(item("Remove song", |this, _, _, cx| this.remove_songs(cx)))
            })
    }

    /// Dropping on a selected track fills the whole selection; dropping elsewhere fills just that track.
    fn drop_targets(&self, file: &str) -> Vec<String> {
        let selected = self.selected_files();
        if selected.len() > 1 && selected.iter().any(|f| f == file) { selected } else { vec![file.to_string()] }
    }

    fn render_song_table(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = cx.theme();
        let mut uses: HashMap<String, usize> = HashMap::new();
        for track in &self.tracks {
            if self.project.is_assigned(&track.file) {
                *uses.entry(self.project.source(&track.file).to_ascii_lowercase()).or_default() += 1;
            }
        }
        let picked = self.song_selected.len();
        let toolbar = h_flex()
            .p_2()
            .gap_2()
            .flex_wrap()
            .flex_shrink_0()
            .border_b_1()
            .border_color(theme.border)
            .child(Button::new("st-play").small().outline().icon(Lucide::Play).label("Play").disabled(picked != 1).on_click(cx.listener(|this, _, window, cx| {
                if let Some(song) = this.selected_songs().into_iter().next() {
                    this.play_song(song, window, cx);
                }
            })))
            .child(Button::new("st-use").small().outline().label(format!("Use on {} selected tracks", self.selected.len())).disabled(picked == 0 || self.selected.is_empty()).on_click(cx.listener(|this, _, _, cx| this.use_songs_on_tracks(cx))))
            .child(Button::new("st-off").small().outline().label("Take off tracks").disabled(picked == 0).on_click(cx.listener(|this, _, _, cx| this.take_songs_off_tracks(cx))))
            .child(Button::new("st-edit").small().outline().label("Open in editor").disabled(picked == 0).on_click(cx.listener(|this, _, window, cx| this.send_to_editor(window, cx))))
            .child(Button::new("st-delete").small().danger().label("Delete…").disabled(picked == 0).on_click(cx.listener(|this, _, window, cx| this.delete_songs(window, cx))))
            .child(div().flex_1())
            .child(Button::new("st-add").small().ghost().icon(Lucide::Plus).label("Add").on_click(cx.listener(|this, _, window, cx| {
                this.selected.clear();
                this.choose_songs(window, cx);
            })))
            .child(Button::new("st-find").small().ghost().icon(Lucide::Search).label("Find online").on_click(cx.listener(|this, _, window, cx| this.open_download(window, cx))))
            .child(Button::new("st-packs").small().ghost().label("Packs on this PC").on_click(cx.listener(|this, _, window, cx| this.open_installed(window, cx))));

        let header = h_flex()
            .h_7()
            .px_2()
            .gap_2()
            .flex_shrink_0()
            .bg(theme.list_head)
            .border_b_1()
            .border_color(theme.border)
            .text_xs()
            .text_color(theme.muted_foreground)
            .child(div().flex_1().min_w_32().child("Song"))
            .child(div().w_20().child("Kind"))
            .child(div().w(px(56.)).text_right().child("Length"))
            .child(div().w_20().text_right().child("On tracks"))
            .child(div().flex_1().min_w_32().child("File"));

        let count = self.songs_visible.len();
        let list = if count == 0 {
            v_flex().size_full().items_center().justify_center().text_color(theme.muted_foreground).child("No songs yet. Add some, or find them online.").into_any_element()
        } else {
            uniform_list(
                "song-table",
                count,
                cx.processor(move |this, range: std::ops::Range<usize>, _, cx| {
                    range
                        .map(|pos| {
                            let ix = this.songs_visible[pos];
                            let song = this.songs[ix].clone();
                            let theme = cx.theme();
                            let selected = this.song_selected.contains(&ix);
                            let used = uses.get(&song.path.to_ascii_lowercase()).copied().unwrap_or(0);
                            let drag_paths: Vec<PathBuf> = if selected {
                                this.selected_songs().into_iter().map(|s| PathBuf::from(s.path)).collect()
                            } else {
                                vec![PathBuf::from(&song.path)]
                            };
                            let drag = SongDrag { label: format!("{} songs", drag_paths.len()).into(), paths: drag_paths };
                            let play = song.clone();
                            h_flex()
                                .id(SharedString::from(format!("st-{}", song.path)))
                                .w_full()
                                .h_6()
                                .px_2()
                                .gap_2()
                                .when(pos % 2 == 1, |el| el.bg(theme.list_even))
                                .when(selected, |el| el.bg(theme.list_active))
                                .hover(|el| el.bg(theme.list_hover))
                                .child(div().flex_1().min_w_32().truncate().child(song.label.clone()))
                                .child(div().w_20().text_xs().text_color(theme.muted_foreground).child(song.kind.clone()))
                                .child(div().w(px(56.)).text_right().text_xs().text_color(theme.muted_foreground).child(if song.seconds > 0.0 { model::format_time(song.seconds) } else { String::new() }))
                                .child(div().w_20().text_right().text_xs().text_color(if used > 0 { theme.foreground } else { theme.muted_foreground }).child(if used > 0 { used.to_string() } else { "—".into() }))
                                .child(div().flex_1().min_w_32().truncate().text_xs().text_color(theme.muted_foreground).child(song.path.clone()))
                                .on_drag(drag, |drag: &SongDrag, _, _, cx| cx.new(|_| DragChip(drag.label.clone())))
                                .on_click(cx.listener(move |this, event: &ClickEvent, window, cx| {
                                    if event.click_count() >= 2 {
                                        this.play_song(play.clone(), window, cx);
                                    } else {
                                        this.click_song(ix, event.modifiers(), cx);
                                    }
                                }))
                        })
                        .collect::<Vec<_>>()
                }),
            )
            .track_scroll(&self.song_table_scroll)
            .size_full()
            .into_any_element()
        };
        v_flex().size_full().child(toolbar).child(header).child(div().flex_1().min_h_0().child(list))
    }

    /// Keeps the name and credits boxes showing the track that's selected.
    fn sync_inputs(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let files = self.selected_files();
        let wanted = (files.len() == 1).then(|| files[0].clone());
        if wanted == self.synced_file {
            return;
        }
        self.syncing = true;
        let (title, credits) = match &wanted {
            Some(file) => (self.project.song_title(file), self.project.credits(file)),
            None => (String::new(), String::new()),
        };
        self.title_input.update(cx, |input, cx| input.set_value(title, window, cx));
        self.credits_input.update(cx, |input, cx| input.set_value(credits, window, cx));
        self.synced_file = wanted;
        self.syncing = false;
    }

    fn step_row(
        &self,
        id: &'static str,
        label: &'static str,
        value: String,
        edit: fn(&mut Project, &str, isize),
        cx: &mut Context<Self>,
    ) -> impl IntoElement {
        let (a, b) = (cx.weak_entity(), cx.weak_entity());
        ui::stepper(
            id,
            label,
            value,
            cx,
            move |_, cx| {
                let _ = a.update(cx, |this, cx| this.edit_selected(|p, f| edit(p, f, -1), cx));
            },
            move |_, cx| {
                let _ = b.update(cx, |this, cx| this.edit_selected(|p, f| edit(p, f, 1), cx));
            },
        )
    }

    fn render_properties(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let files = self.selected_files();
        let mut panel = v_flex().id("properties").size_full().overflow_y_scroll().p_3().gap_0p5();
        if files.is_empty() {
            return panel
                .text_color(cx.theme().muted_foreground)
                .child("Select a track to see and change its song.")
                .child(div().pt_2().text_xs().child("Drop song files on a track, or on a selection to fill it. Drop them on the list to match them by name. Right-click a track for more."));
        }

        let first = files[0].clone();
        let many = files.len() > 1;
        let same = |value: &dyn Fn(&str) -> String| {
            let v = value(&first);
            if files.iter().all(|f| value(f) == v) { v } else { "Mixed".into() }
        };
        let volume = same(&|f| {
            let v = self.project.volume(f);
            if v == 0 { "0 dB".into() } else { format!("{v:+} dB") }
        });
        let speed = same(&|f| SPEEDS[self.project.speed_index(f)].text.into());
        let looping = same(&|f| LOOPS[self.project.loop_index(f)].1.into());
        let final_speed = same(&|f| FINAL_SPEEDS[self.project.final_speed_index(f)].0.into());
        let crossfade = same(&|f| CROSSFADES[self.project.crossfade_index(f)].0.into());
        let any_assigned = files.iter().any(|f| self.project.is_assigned(f));
        let can_final = files.iter().any(|f| self.tracks.iter().any(|t| &t.file == f && t.can_have_final));

        if many {
            let set = files.iter().filter(|f| self.project.is_assigned(f)).count();
            panel = panel
                .child(div().text_base().font_weight(FontWeight::SEMIBOLD).child(format!("{} tracks selected", files.len())))
                .child(div().text_xs().text_color(cx.theme().muted_foreground).child(format!("{set} have a song")))
                .child(ui::section("Give them songs", cx))
                .child(
                    h_flex()
                        .gap_2()
                        .flex_wrap()
                        .child(Button::new("fill").small().primary().label("Fill with songs…").on_click(cx.listener(|this, _, window, cx| this.choose_songs(window, cx))))
                        .child(Button::new("match").small().outline().label("Match song names…").on_click(cx.listener(|this, _, window, cx| this.match_names(window, cx))))
                        .child(Button::new("find").small().outline().label("Find online…").on_click(cx.listener(|this, _, window, cx| this.open_download(window, cx)))),
                )
                .child(
                    v_flex()
                        .pt_2()
                        .gap_1()
                        .child(Switch::new("in-order").label("In order, by file name (off shuffles)").checked(self.fill_in_order).on_change(cx.listener(|this, v: &bool, _, cx| {
                            this.fill_in_order = *v;
                            cx.notify();
                        })))
                        .child(Switch::new("only-empty").label("Only tracks without a song").checked(self.fill_only_empty).on_change(cx.listener(|this, v: &bool, _, cx| {
                            this.fill_only_empty = *v;
                            cx.notify();
                        }))),
                );
        } else if let Some(track) = self.tracks.iter().find(|t| t.file == first).cloned() {
            panel = panel
                .child(div().text_base().font_weight(FontWeight::SEMIBOLD).child(track.name.clone()))
                .child(div().text_xs().text_color(cx.theme().muted_foreground).child(if track.variant_of.is_empty() {
                    format!("{}  ·  {}.brstm", track.category, track.file)
                } else {
                    format!("{}  ·  {}.brstm  ·  version of {}", track.category, track.file, track.variant_of)
                }))
                .child(ui::section("Song", cx));
            if self.project.is_assigned(&first) {
                panel = panel
                    .child(ui::field("Name", Input::new(&self.title_input).small(), cx))
                    .child(ui::field("Credits", Input::new(&self.credits_input).small(), cx));
            } else {
                panel = panel.child(div().text_color(cx.theme().muted_foreground).child("No song yet. The game's own music plays."));
            }
            let play_file = first.clone();
            panel = panel.child(
                h_flex()
                    .pt_2()
                    .gap_2()
                    .flex_wrap()
                    .child(Button::new("prop-choose").small().primary().label("Choose song…").on_click(cx.listener(|this, _, window, cx| this.choose_songs(window, cx))))
                    .when(any_assigned, |el| {
                        el.child(Button::new("prop-play").small().outline().icon(Lucide::Play).label("Play").on_click(cx.listener(move |this, _, window, cx| this.play_track(play_file.clone(), window, cx))))
                            .child(Button::new("prop-loop").small().outline().label("Edit loop…").on_click(cx.listener(|this, _, window, cx| this.open_loop_editor(window, cx))))
                    }),
            );
        }

        if any_assigned || many {
            panel = panel
                .child(ui::section("Sound", cx))
                .child(self.step_row("vol", "Volume", volume, |p, f, s| p.set_volume(f, p.volume(f) + s as i64), cx))
                .child(self.step_row("speed", "Speed", speed, |p, f, s| p.set_speed(f, ui::wrap(p.speed_index(f), s, SPEEDS.len())), cx))
                .child(self.step_row("loop", "Loop", looping, |p, f, s| p.set_loop(f, ui::wrap(p.loop_index(f), s, LOOPS.len())), cx))
                .child(self.step_row("fade", "Smooth the jump", crossfade, |p, f, s| p.set_crossfade(f, ui::wrap(p.crossfade_index(f), s, CROSSFADES.len())), cx));
        }

        if can_final && (any_assigned || many) {
            panel = panel
                .child(ui::section("Final lap", cx))
                .child(self.step_row("final", "Speed up", final_speed, |p, f, s| p.set_final_speed(f, ui::wrap(p.final_speed_index(f), s, FINAL_SPEEDS.len())), cx));
            if !many {
                let final_source = self.project.final_source(&first).to_string();
                let hear = first.clone();
                panel = panel
                    .when(!final_source.is_empty(), |el| {
                        el.child(ui::field(
                            "Own song",
                            div().truncate().child(Path::new(&final_source).file_stem().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()),
                            cx,
                        ))
                    })
                    .when(!final_source.is_empty(), |el| {
                        let volume = match self.project.final_volume(&first) {
                            None => "Same as the song".to_string(),
                            Some(v) if v == 0 => "0 dB".into(),
                            Some(v) => format!("{v:+} dB"),
                        };
                        let speed = match self.project.final_song_speed(&first) {
                            None => "Same as the song",
                            Some(ix) => SPEEDS[ix].text,
                        };
                        el.child(self.step_row("fvol", "Its volume", volume, |p, f, s| {
                            let next = match p.final_volume(f) {
                                None => Some(0),
                                Some(v) => {
                                    let n = v + s as i64;
                                    (model::MIN_VOLUME..=model::MAX_VOLUME).contains(&n).then_some(n)
                                }
                            };
                            p.set_final_volume(f, next);
                        }, cx))
                        .child(self.step_row("fspeed", "Its speed", speed.to_string(), |p, f, s| {
                            let next = match p.final_song_speed(f) {
                                None => if s > 0 { Some(0) } else { Some(SPEEDS.len() - 1) },
                                Some(ix) => {
                                    let n = ix as isize + s;
                                    (n >= 0 && (n as usize) < SPEEDS.len()).then_some(n as usize)
                                }
                            };
                            p.set_final_song_speed(f, next);
                        }, cx))
                    })
                    .child(
                        h_flex()
                            .pt_1()
                            .gap_2()
                            .flex_wrap()
                            .child(Button::new("final-choose").small().outline().label(if final_source.is_empty() { "Choose a song…" } else { "Change…" }).on_click(cx.listener(|this, _, window, cx| this.choose_final(window, cx))))
                            .when(!final_source.is_empty(), |el| {
                                let file = first.clone();
                                el.child(Button::new("final-remove").small().ghost().label("Remove").on_click(cx.listener(move |this, _, _, cx| {
                                    this.project.set_final_source(&file, None);
                                    this.changed(cx);
                                })))
                            })
                            .child(Button::new("final-hear").small().outline().icon(Lucide::Play).label("Hear the final lap").on_click(cx.listener(move |this, _, window, cx| this.play_track_lap(hear.clone(), true, window, cx)))),
                    );
            }
        }

        panel
    }

    fn render_status(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = cx.theme();
        let set = self.tracks.iter().filter(|t| self.project.is_assigned(&t.file)).count();
        let playing = match (self.player.now(), self.player.state()) {
            (Some(now), PlayState::Playing) => format!("Playing · {}", now.title),
            (Some(now), PlayState::Paused) => format!("Paused · {}", now.title),
            _ => "Stopped".into(),
        };
        h_flex()
            .id("status")
            .h_6()
            .flex_shrink_0()
            .px_3()
            .gap_3()
            .text_xs()
            .bg(theme.status_bar)
            .border_t_1()
            .border_color(theme.status_bar_border)
            .text_color(theme.muted_foreground)
            .child(div().max_w_80().truncate().child(playing))
            .child(div().w_px().h_3().bg(theme.border))
            .child(div().flex_1().truncate().child(self.status.clone()))
            .when_some(self.busy.as_ref(), |el, busy| {
                let text = if busy.total > 0 { format!("{} {}/{}", busy.label, busy.done, busy.total) } else { format!("{}…", busy.label) };
                el.child(div().text_color(theme.primary).child(text))
            })
            .child(div().child(format!("{set}/{} tracks set", self.tracks.len())))
            .when(!self.missing.is_empty(), |el| el.child(div().text_color(theme.danger).child(format!("{} missing", self.missing.len()))))
            .child(div().child(if self.project.for_wii() { "Wii" } else { "Dolphin" }))
            .child(div().child(if self.saved { "Saved" } else { "Saving…" }))
    }

    fn render_console(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = cx.theme();
        let lines: Vec<SharedString> = self.log.iter().rev().take(200).rev().cloned().collect();
        v_flex()
            .id("console")
            .h_32()
            .flex_shrink_0()
            .px_3()
            .py_1()
            .overflow_y_scroll()
            .bg(theme.background)
            .border_t_1()
            .border_color(theme.border)
            .text_xs()
            .font_family(theme.mono_font_family.clone())
            .text_color(theme.muted_foreground)
            .children(lines.into_iter().map(|line| div().child(line)))
    }
}

impl Render for PackMaker {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        window.set_rem_size(px(16. * theme::scale(&self.settings)));
        self.sync_inputs(window, cx);
        let toolbar = self.render_toolbar(cx).into_any_element();
        let left = self.render_left(cx).into_any_element();
        let center = self.render_center(cx).into_any_element();
        let properties = self.render_properties(cx).into_any_element();
        let status = self.render_status(cx).into_any_element();
        let console = self.show_console.then(|| self.render_console(cx).into_any_element());
        let picture = self.settings.text("BackgroundImage");
        let picture = (!picture.is_empty() && Path::new(&picture).exists()).then(|| PathBuf::from(picture));

        div()
            .size_full()
            .relative()
            .bg(cx.theme().background)
            .when_some(picture, |el, picture| el.child(img(picture).absolute().size_full().object_fit(ObjectFit::Cover)))
            .child(
                v_flex()
                    .id("packmaker")
                    .key_context(PLAYLIST)
                    .track_focus(&self.focus)
                    .absolute()
                    .size_full()
                    .text_color(cx.theme().foreground)
                    .text_sm()
                    .on_action(cx.listener(|this, _: &PlayPause, window, cx| this.play_pause(window, cx)))
                    .on_action(cx.listener(|this, _: &Stop, _, cx| {
                        this.player.stop();
                        cx.notify();
                    }))
                    .on_action(cx.listener(|this, _: &PlayNext, window, cx| this.play_step(1, window, cx)))
                    .on_action(cx.listener(|this, _: &PlayPrevious, window, cx| this.play_step(-1, window, cx)))
                    .on_action(cx.listener(|this, _: &PlaySelected, window, cx| {
                        if let Some(track) = this.cursor {
                            let file = this.tracks[track].file.clone();
                            this.play_track(file, window, cx);
                        }
                    }))
                    .on_action(cx.listener(|this, _: &HearFinal, window, cx| {
                        if let Some(file) = this.selected_files().into_iter().next() {
                            this.play_track_lap(file, true, window, cx);
                        }
                    }))
                    .on_action(cx.listener(|this, _: &RemoveSongs, _, cx| this.remove_songs(cx)))
                    .on_action(cx.listener(|this, _: &SelectAll, _, cx| this.select_where(|_, _| true, cx)))
                    .on_action(cx.listener(|this, _: &SelectEmpty, _, cx| this.select_where(|p, t| !p.is_assigned(&t.file), cx)))
                    .on_action(cx.listener(|this, _: &SelectFilled, _, cx| this.select_where(|p, t| p.is_assigned(&t.file), cx)))
                    .on_action(cx.listener(|this, _: &MoveUp, _, cx| this.move_cursor(-1, false, cx)))
                    .on_action(cx.listener(|this, _: &MoveDown, _, cx| this.move_cursor(1, false, cx)))
                    .on_action(cx.listener(|this, _: &ExtendUp, _, cx| this.move_cursor(-1, true, cx)))
                    .on_action(cx.listener(|this, _: &ExtendDown, _, cx| this.move_cursor(1, true, cx)))
                    .on_action(cx.listener(|this, _: &Louder, _, cx| this.edit_selected(|p, f| p.set_volume(f, p.volume(f) + 1), cx)))
                    .on_action(cx.listener(|this, _: &Quieter, _, cx| this.edit_selected(|p, f| p.set_volume(f, p.volume(f) - 1), cx)))
                    .on_action(cx.listener(|this, _: &EditLoop, window, cx| this.open_loop_editor(window, cx)))
                    .on_action(cx.listener(|this, _: &FocusSearch, window, cx| {
                        this.search.update(cx, |input, cx| input.focus(window, cx));
                    }))
                    .on_action(cx.listener(|this, _: &AddSongs, window, cx| this.choose_songs(window, cx)))
                    .on_action(cx.listener(|this, _: &ImportFolder, window, cx| this.import(false, window, cx)))
                    .on_action(cx.listener(|this, _: &ImportZip, window, cx| this.import(true, window, cx)))
                    .on_action(cx.listener(|this, _: &ShowInstalled, window, cx| this.open_installed(window, cx)))
                    .on_action(cx.listener(|this, _: &FindOnline, window, cx| this.open_download(window, cx)))
                    .on_action(cx.listener(|this, _: &SaveNow, _, cx| {
                        if this.save(cx) {
                            this.say("Saved.", cx);
                        }
                    }))
                    .on_action(cx.listener(|this, _: &ExportZip, window, cx| this.export("zip", window, cx)))
                    .on_action(cx.listener(|this, _: &ExportFolder, window, cx| this.export("folder", window, cx)))
                    .on_action(cx.listener(|this, _: &AddToGame, window, cx| this.export("game", window, cx)))
                    .on_action(cx.listener(|this, _: &ShowExported, _, cx| match this.last_export.clone() {
                        Some(path) => cx.reveal_path(&path),
                        None => this.say("Nothing exported yet in this session.", cx),
                    }))
                    .on_action(cx.listener(|this, _: &RemoveFromGame, window, cx| this.remove_from_game(window, cx)))
                    .on_action(cx.listener(|this, _: &ToggleWii, _, cx| {
                        let wii = !this.project.for_wii();
                        this.project.set_for_wii(wii);
                        this.say(if wii { "Packs are made for a real Wii." } else { "Packs are made for Dolphin." }, cx);
                        this.changed(cx);
                    }))
                    .on_action(cx.listener(|this, _: &NewPack, window, cx| this.new_pack(None, window, cx)))
                    .on_action(cx.listener(|this, _: &DeletePack, window, cx| this.delete_pack(window, cx)))
                    .on_action(cx.listener(|this, _: &OpenSettings, window, cx| this.open_settings("Settings", window, cx)))
                    .on_action(cx.listener(|this, _: &OpenClassic, _, cx| this.open_classic(cx)))
                    .on_action(cx.listener(|this, _: &Reload, window, cx| this.reload(window, cx)))
                    .on_action(cx.listener(|this, _: &ShowTracks, _, cx| {
                        this.center = Center::Tracks;
                        cx.notify();
                    }))
                    .on_action(cx.listener(|this, _: &ShowSongs, _, cx| {
                        this.center = Center::Songs;
                        cx.notify();
                    }))
                    .on_action(cx.listener(|this, _: &ShowEditor, _, cx| {
                        this.center = Center::Editor;
                        cx.notify();
                    }))
                    .on_action(cx.listener(|this, _: &ToggleConsole, _, cx| {
                        this.show_console = !this.show_console;
                        cx.notify();
                    }))
                    .on_action(cx.listener(|_, _: &OpenReleases, _, cx| cx.open_url(RELEASES)))
                    .on_action(cx.listener(|this, _: &About, window, cx| this.about(window, cx)))
                    .on_action(cx.listener(|this, _: &Quit, _, cx| {
                        this.save(cx);
                        cx.quit();
                    }))
                    .child(toolbar)
                    .child(
                        div().flex_1().min_h_0().child(
                            h_resizable("body")
                                .child(resizable_panel().size(px(230.)).size_range(px(170.)..px(380.)).child(left))
                                .child(resizable_panel().child(center))
                                .child(
                                    resizable_panel()
                                        .size(px(340.))
                                        .size_range(px(260.)..px(520.))
                                        .child(div().size_full().bg(cx.theme().sidebar).child(properties)),
                                ),
                        ),
                    )
                    .children(console)
                    .child(status),
            )
    }
}
