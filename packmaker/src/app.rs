//! The main window, laid out like a classic player: a toolbar with transport and seek, the library on the left,
//! the track list as the playlist in the middle, selection properties on the right, and a status bar.

use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use gpui_kit::component::{
    ActiveTheme as _, IconName, Sizable as _, WindowExt as _,
    button::{Button, ButtonVariants as _},
    input::{Input, InputEvent, InputState},
    menu::{DropdownMenu as _, PopupMenu},
    notification::Notification,
    resizable::{h_resizable, resizable_panel},
    slider::{Slider, SliderEvent, SliderState},
    v_flex, h_flex,
};
use gpui_kit::assets::IconName as Lucide;
use gpui_kit::base::{Disableable as _, Selectable as _};
use gpui_kit::prelude::FluentBuilder as _;
use gpui_kit::*;

use crate::engine;
use crate::model::{self, Catalog, FINAL_SPEEDS, Group, LOOPS, Project, SPEEDS, Song, Track};
use crate::player::{PlayState, Player};

actions!(
    packmaker,
    [
        PlayPause, Stop, PlayNext, PlayPrevious, PlaySelected, RemoveSongs, SelectAll, MoveUp, MoveDown,
        ExtendUp, ExtendDown, FocusSearch, AddSongs, SaveNow, ExportZip, ExportFolder, AddToGame,
        RemoveFromGame, ToggleWii, OpenClassic, Reload, ToggleConsole, Quit
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
        KeyBinding::new("ctrl-f", FocusSearch, None),
        KeyBinding::new("ctrl-o", AddSongs, None),
        KeyBinding::new("ctrl-s", SaveNow, None),
        KeyBinding::new("ctrl-e", ExportZip, None),
        KeyBinding::new("ctrl-.", Stop, None),
        KeyBinding::new("ctrl-right", PlayNext, None),
        KeyBinding::new("ctrl-left", PlayPrevious, None),
        KeyBinding::new("ctrl-`", ToggleConsole, None),
        KeyBinding::new("ctrl-q", Quit, None),
    ]);
}

/// Which list the left pane shows.
#[derive(Clone, Copy, PartialEq, Eq)]
enum LeftPane {
    Library,
    Songs,
}

/// What the playlist is narrowed to by the library pane.
#[derive(Clone, PartialEq, Eq)]
enum Scope {
    All,
    Set,
    Category(String),
}

/// A long job running in the engine.
struct Busy {
    label: String,
    done: u32,
    total: u32,
}

/// A song being dragged from the library onto tracks.
#[derive(Clone)]
struct SongDrag {
    path: PathBuf,
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

use gpui_kit::component::Icon;

pub struct PackMaker {
    tracks: Vec<Track>,
    groups: Vec<Group>,
    project: Project,
    songs: Vec<Song>,
    scope: Scope,
    left: LeftPane,
    visible: Vec<usize>,
    selected: HashSet<usize>,
    cursor: Option<usize>,
    anchor: Option<usize>,
    song_cursor: Option<usize>,
    list_scroll: UniformListScrollHandle,
    songs_scroll: UniformListScrollHandle,
    focus: FocusHandle,
    search: Entity<InputState>,
    pack_name: Entity<InputState>,
    seek: Entity<SliderState>,
    volume: Entity<SliderState>,
    player: Player,
    loading_preview: bool,
    status: SharedString,
    log: Vec<SharedString>,
    show_console: bool,
    busy: Option<Busy>,
    save_generation: u64,
    saved: bool,
    _tick: Task<()>,
    _subscriptions: Vec<Subscription>,
}

impl PackMaker {
    pub fn new(window: &mut Window, cx: &mut Context<Self>) -> Self {
        let project = Project::load();
        let search = cx.new(|cx| InputState::new(window, cx).placeholder("Filter tracks, songs or files"));
        let pack_name = cx.new(|cx| InputState::new(window, cx).default_value(project.pack_name()));
        let seek = cx.new(|_| SliderState::new().min(0.).max(1000.).step(1.).default_value(0.));
        let volume = cx.new(|_| SliderState::new().min(0.).max(100.).step(1.).default_value(80.));

        let subscriptions = vec![
            cx.subscribe_in(&search, window, |this, _, event, _, cx| {
                if matches!(event, InputEvent::Change) {
                    this.refilter(cx);
                }
            }),
            cx.subscribe_in(&pack_name, window, |this, state, event, _, cx| {
                if matches!(event, InputEvent::Change) {
                    let name = state.read(cx).value().to_string();
                    this.project.set_pack_name(&name);
                    this.changed(cx);
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
                    // Stopped by hand or finished: the bar goes back to the start.
                    if this.player.now().is_none() && this.seek.read(cx).value().start() != 0. {
                        this.seek.update(cx, |seek, cx| seek.set_value(0., window, cx));
                        cx.notify();
                    }
                    if this.player.now().is_some() {
                        let state = this.player.state();
                        if state == PlayState::Stopped {
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
            songs: Vec::new(),
            scope: Scope::All,
            left: LeftPane::Library,
            visible: Vec::new(),
            selected: HashSet::new(),
            cursor: None,
            anchor: None,
            song_cursor: None,
            list_scroll: UniformListScrollHandle::new(),
            songs_scroll: UniformListScrollHandle::new(),
            focus: cx.focus_handle(),
            search,
            pack_name,
            seek,
            volume,
            player: Player::new(),
            loading_preview: false,
            status: "Loading the track list…".into(),
            log: Vec::new(),
            show_console: false,
            busy: None,
            save_generation: 0,
            saved: true,
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
            let songs = engine::run_json::<Vec<Song>>(&["songs"]);
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
                this.refilter(cx);
            });
        })
        .detach();
    }

    fn reload(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        self.project = Project::load();
        let name = self.project.pack_name();
        self.pack_name.update(cx, |input, cx| input.set_value(name, window, cx));
        self.load(window, cx);
    }

    // ----- Messages and saving ---------------------------------------------------------------------------------

    fn say(&mut self, message: impl Into<SharedString>, cx: &mut Context<Self>) {
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
                Scope::Category(category) => &track.category == category,
            })
            .filter(|(_, track)| {
                if words.is_empty() {
                    return true;
                }
                let haystack =
                    format!("{} {} {}", track.name, track.file, project.song_title(&track.file)).to_lowercase();
                words.iter().all(|word| haystack.contains(word))
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
        if extend {
            self.click_row(track, Modifiers { shift: true, ..Default::default() }, cx);
        } else {
            self.click_row(track, Modifiers::default(), cx);
        }
        self.list_scroll.scroll_to_item(next, ScrollStrategy::Center);
    }

    // ----- Editing ---------------------------------------------------------------------------------------------

    /// Hands songs to tracks one after another, starting again when the songs run out.
    fn assign(&mut self, files: Vec<String>, songs: Vec<PathBuf>, cx: &mut Context<Self>) {
        if files.is_empty() || songs.is_empty() {
            return;
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
        self.changed(cx);
        self.refresh_songs(cx);
    }

    fn track_name(&self, file: &str) -> String {
        self.tracks.iter().find(|t| t.file == file).map(|t| t.name.clone()).unwrap_or_else(|| file.into())
    }

    fn refresh_songs(&mut self, cx: &mut Context<Self>) {
        let work = cx.background_executor().spawn(async move { engine::run_json::<Vec<Song>>(&["songs"]) });
        cx.spawn(async move |this, cx| {
            if let Ok(songs) = work.await {
                let _ = this.update(cx, |this, cx| {
                    this.songs = songs;
                    cx.notify();
                });
            }
        })
        .detach();
    }

    fn edit_selected(&mut self, edit: impl Fn(&mut Project, &str), cx: &mut Context<Self>) {
        for file in self.selected_files() {
            edit(&mut self.project, &file);
        }
        self.changed(cx);
    }

    fn choose_songs(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let files = self.selected_files();
        let picked = cx.prompt_for_paths(PathPromptOptions {
            files: true,
            directories: false,
            multiple: true,
            prompt: Some("Add songs".into()),
        });
        cx.spawn_in(window, async move |this, cx| {
            let Ok(Ok(Some(paths))) = picked.await else {
                return;
            };
            let songs = model::audio_files(&paths);
            let _ = this.update_in(cx, |this, _, cx| {
                if files.is_empty() {
                    let _ = model::add_to_library(&songs);
                    this.say(format!("Added {} songs to your library.", songs.len()), cx);
                    this.refresh_songs(cx);
                    this.left = LeftPane::Songs;
                } else {
                    this.assign(files, songs, cx);
                }
            });
        })
        .detach();
    }

    fn remove_songs(&mut self, cx: &mut Context<Self>) {
        let files: Vec<String> =
            self.selected_files().into_iter().filter(|f| self.project.is_assigned(f)).collect();
        if files.is_empty() {
            return;
        }
        for file in &files {
            self.project.clear(file);
        }
        self.say(format!("Took the song off {} {}.", files.len(), if files.len() == 1 { "track" } else { "tracks" }), cx);
        self.changed(cx);
    }

    // ----- Playback --------------------------------------------------------------------------------------------

    fn play_track(&mut self, file: String, window: &mut Window, cx: &mut Context<Self>) {
        if !self.project.is_assigned(&file) {
            self.say(format!("{} has no song yet. Drop one on it, or press Ctrl+O.", self.track_name(&file)), cx);
            return;
        }
        // The engine reads the saved pack, so what plays has the latest volume and speed.
        self.save(cx);
        let title = format!("{} — {}", self.project.song_title(&file), self.track_name(&file));
        let args = vec!["preview".to_string(), "--track".into(), file.clone()];
        self.start_preview(args, title, Some(file), window, cx);
    }

    fn play_song(&mut self, song: Song, window: &mut Window, cx: &mut Context<Self>) {
        let args = vec!["preview".to_string(), "--song".into(), song.path.clone()];
        self.start_preview(args, song.label.clone(), None, window, cx);
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
        if !self.save(cx) {
            return;
        }
        let name = self.project.pack_name();
        let downloads = dirs::download_dir().unwrap_or_else(|| PathBuf::from("."));
        let target: Task<Option<PathBuf>> = match mode {
            "zip" => {
                let picked = cx.prompt_for_new_path(&downloads, Some(&format!("{name}.zip")));
                cx.background_executor().spawn(async move { picked.await.ok()?.ok()? })
            }
            "folder" => {
                let picked = cx.prompt_for_paths(PathPromptOptions {
                    files: false,
                    directories: true,
                    multiple: false,
                    prompt: Some("Copy songs here".into()),
                });
                cx.background_executor()
                    .spawn(async move { picked.await.ok()?.ok()?.and_then(|p| p.into_iter().next()) })
            }
            _ => Task::ready(Some(PathBuf::new())),
        };
        let wii = self.project.for_wii();
        cx.spawn_in(window, async move |this, cx| {
            let Some(target) = target.await else {
                return;
            };
            let events: Arc<Mutex<Vec<engine::Event>>> = Arc::default();
            let sink = events.clone();
            let mut args = vec!["export".to_string(), "--mode".into(), mode.into()];
            if mode != "game" {
                args.push("--target".into());
                args.push(target.to_string_lossy().into_owned());
            }
            args.push(if wii { "--wii".into() } else { "--dolphin".into() });
            let _ = this.update(cx, |this, cx| {
                this.busy = Some(Busy { label: "Making the pack".into(), done: 0, total: 0 });
                this.say(format!("Making {name}…"), cx);
            });
            let job = cx.background_executor().spawn(async move {
                let args: Vec<&str> = args.iter().map(String::as_str).collect();
                engine::run_events(&args, |event| sink.lock().unwrap().push(event.clone()))
            });
            // Shows the engine's progress while it works.
            let poll_events = events.clone();
            let mut job = std::pin::pin!(job);
            let result = loop {
                let timer = cx.background_executor().timer(Duration::from_millis(150));
                match futures_lite_select(&mut job, timer).await {
                    Some(result) => break result,
                    None => {
                        let drained: Vec<engine::Event> = poll_events.lock().unwrap().drain(..).collect();
                        let _ = this.update(cx, |this, cx| this.take_events(drained, cx));
                    }
                }
            };
            let drained: Vec<engine::Event> = events.lock().unwrap().drain(..).collect();
            let _ = this.update_in(cx, |this, window, cx| {
                this.take_events(drained, cx);
                this.busy = None;
                match result {
                    Ok(done) => {
                        let message = done.message.clone().unwrap_or_else(|| "Done.".into());
                        let place = done.location.clone().unwrap_or_default();
                        this.say(format!("{message} {place}"), cx);
                        window.push_notification(Notification::success(message).title("Pack made"), cx);
                    }
                    Err(error) => {
                        this.say(format!("Couldn't make the pack. {error}"), cx);
                        window.push_notification(Notification::error(format!("{error}")).title("Export"), cx);
                    }
                }
            });
        })
        .detach();
    }

    fn take_events(&mut self, events: Vec<engine::Event>, cx: &mut Context<Self>) {
        for event in events {
            match event.event.as_str() {
                "progress" => {
                    if let Some(busy) = &mut self.busy {
                        busy.done = event.done.unwrap_or(0);
                        busy.total = event.total.unwrap_or(0);
                    }
                }
                "log" => {
                    if let Some(message) = event.message {
                        self.say(message, cx);
                    }
                }
                _ => {}
            }
        }
        cx.notify();
    }

    fn remove_from_game(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let name = self.project.pack_name();
        let entity = cx.entity();
        window.open_alert_dialog(cx, move |alert, _, _| {
            let entity = entity.clone();
            alert
                .title(SharedString::from(format!("Take “{name}” out of the game?")))
                .description("Retro Rewind's own songs go back in the strm folder. Your pack and songs stay.")
                .confirm()
                .ok_text("Take out")
                .on_ok(move |_, window, cx| {
                    entity.update(cx, |this, cx| this.run_remove(window, cx));
                    true
                })
        });
    }

    fn run_remove(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let work = cx.background_executor().spawn(async move { engine::run_events(&["remove"], |_| {}) });
        cx.spawn_in(window, async move |this, cx| {
            let result = work.await;
            let _ = this.update(cx, |this, cx| match result {
                Ok(done) => this.say(done.message.unwrap_or_default(), cx),
                Err(error) => this.say(format!("Couldn't take it out. {error}"), cx),
            });
        })
        .detach();
    }

    fn open_classic(&mut self, cx: &mut Context<Self>) {
        self.save(cx);
        match engine::engine_path() {
            Some(exe) => {
                let _ = std::process::Command::new(exe).spawn();
                self.say("Opened the classic window. Use Reload here after changing things there.", cx);
            }
            None => self.say("The classic app wasn't found next to this one.", cx),
        }
    }
}

/// Waits for whichever finishes first: the job, or the timer. Returns the job's result if it won.
async fn futures_lite_select<T: 'static>(
    job: &mut std::pin::Pin<&mut Task<T>>,
    timer: Task<()>,
) -> Option<T> {
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

// ----- Rendering ---------------------------------------------------------------------------------------------------

impl PackMaker {
    fn render_toolbar(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
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
                    .menu("Save", Box::new(SaveNow))
                    .menu("Reload from disk", Box::new(Reload))
                    .separator()
                    .menu("Save as zip…", Box::new(ExportZip))
                    .menu("Copy songs to a folder…", Box::new(ExportFolder))
                    .separator()
                    .menu("Open the classic window", Box::new(OpenClassic))
                    .separator()
                    .menu("Exit", Box::new(Quit))
            }))
            .child(menu("menu-playback", "Playback").dropdown_menu(|m: PopupMenu, _, _| {
                m.menu("Play or pause", Box::new(PlayPause))
                    .menu("Stop", Box::new(Stop))
                    .menu("Next track with a song", Box::new(PlayNext))
                    .menu("Previous track with a song", Box::new(PlayPrevious))
            }))
            .child(menu("menu-pack", "Pack").dropdown_menu(move |m: PopupMenu, _, _| {
                m.menu_with_check("Made for a real Wii", wii, Box::new(ToggleWii))
                    .separator()
                    .menu("Add to the game", Box::new(AddToGame))
                    .menu("Take out of the game…", Box::new(RemoveFromGame))
            }))
            .child(menu("menu-view", "View").dropdown_menu(|m: PopupMenu, _, _| {
                m.menu("Console", Box::new(ToggleConsole)).menu("Find", Box::new(FocusSearch))
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
            .child(div().w_48().child(Input::new(&self.pack_name).small()))
            .child(
                Button::new("export")
                    .primary()
                    .small()
                    .icon(Lucide::PackageOpen)
                    .label("Export")
                    .loading(self.busy.is_some())
                    .dropdown_menu_with_anchor(Anchor::TopRight, |m: PopupMenu, _, _| {
                        m.menu("Save as zip…", Box::new(ExportZip))
                            .menu("Copy songs to a folder…", Box::new(ExportFolder))
                            .menu("Add to the game", Box::new(AddToGame))
                    }),
            )
    }

    fn render_left(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let tab = |id: &'static str, label: &'static str, pane: LeftPane, current: LeftPane| {
            Button::new(id)
                .ghost()
                .xsmall()
                .label(label)
                .selected(pane == current)
        };
        let current = self.left;
        v_flex()
            .size_full()
            .bg(cx.theme().sidebar)
            .child(
                h_flex()
                    .h_8()
                    .px_2()
                    .gap_1()
                    .border_b_1()
                    .border_color(cx.theme().border)
                    .child(tab("tab-library", "Library", LeftPane::Library, current).on_click(cx.listener(
                        |this, _, _, cx| {
                            this.left = LeftPane::Library;
                            cx.notify();
                        },
                    )))
                    .child(tab("tab-songs", "Songs", LeftPane::Songs, current).on_click(cx.listener(
                        |this, _, _, cx| {
                            this.left = LeftPane::Songs;
                            cx.notify();
                        },
                    ))),
            )
            .child(match self.left {
                LeftPane::Library => self.render_library(cx).into_any_element(),
                LeftPane::Songs => self.render_songs(cx).into_any_element(),
            })
    }

    fn render_library(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let mut counts: std::collections::HashMap<&str, (usize, usize)> = std::collections::HashMap::new();
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
        let row = |id: SharedString, label: String, count: String, selected: bool| {
            h_flex()
                .id(id)
                .h_6()
                .px_3()
                .gap_2()
                .rounded(theme.radius)
                .mx_1()
                .when(selected, |el| el.bg(theme.list_active).text_color(theme.foreground))
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
        for group in self.groups.clone() {
            list = list.child(
                div()
                    .px_4()
                    .pt_3()
                    .pb_1()
                    .text_xs()
                    .text_color(cx.theme().muted_foreground)
                    .child(group.title.clone()),
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
        self.refilter(cx);
        self.list_scroll.scroll_to_item(0, ScrollStrategy::Top);
    }

    fn render_songs(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let count = self.songs.len();
        let body = if count == 0 {
            v_flex()
                .flex_1()
                .p_4()
                .gap_2()
                .text_color(cx.theme().muted_foreground)
                .child("No songs yet.")
                .child("Add some, or drop files straight onto tracks.")
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
                            let drag = SongDrag { path: PathBuf::from(&song.path), label: song.label.clone().into() };
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
                                .child(div().text_xs().text_color(theme.muted_foreground).child(song.kind.clone()))
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
                    .child(
                        Button::new("songs-add").small().outline().icon(Lucide::Plus).label("Add songs")
                            .on_click(cx.listener(|this, _, window, cx| {
                                this.selected.clear();
                                this.choose_songs(window, cx);
                            })),
                    )
                    .child(
                        Button::new("songs-use")
                            .small()
                            .label("Use for selected")
                            .disabled(self.song_cursor.is_none() || self.selected.is_empty())
                            .on_click(cx.listener(|this, _, _, cx| {
                                if let Some(song) = this.song_cursor.and_then(|ix| this.songs.get(ix)).cloned() {
                                    let files = this.selected_files();
                                    this.assign(files, vec![PathBuf::from(song.path)], cx);
                                }
                            })),
                    ),
            )
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
                .flex_1()
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
            .flex_1()
            .into_any_element()
        };

        v_flex()
            .size_full()
            .child(
                h_flex()
                    .h_8()
                    .flex_shrink_0()
                    .px_2()
                    .gap_2()
                    .border_b_1()
                    .border_color(cx.theme().border)
                    .child(
                        div().flex_1().child(
                            Input::new(&self.search)
                                .small()
                                .cleanable(true)
                                .prefix(Icon::new(Lucide::Search).xsmall()),
                        ),
                    )
                    .child(
                        div()
                            .text_xs()
                            .text_color(cx.theme().muted_foreground)
                            .child(format!("{count} shown")),
                    ),
            )
            .child(header)
            .child(list)
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
        let song = if assigned { self.project.song_title(&file) } else { String::new() };
        let volume = self.project.volume(&file);
        let speed = SPEEDS[self.project.speed_index(&file)].text;
        let looping = LOOPS[self.project.loop_index(&file)].1;
        let muted = theme.muted_foreground;
        let text = if playing { theme.primary } else { theme.foreground };

        let drop_file = file.clone();
        let drop_song_file = file.clone();
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
                this.assign(files, vec![drag.path.clone()], cx);
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
                    .child(if assigned { song } else { "—".into() }),
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
            .child(
                div()
                    .w_20()
                    .truncate()
                    .text_xs()
                    .text_color(muted)
                    .font_family(theme.mono_font_family.clone())
                    .child(file),
            )
    }

    /// Dropping on a selected track fills the whole selection; dropping elsewhere fills just that track.
    fn drop_targets(&self, file: &str) -> Vec<String> {
        let selected = self.selected_files();
        if selected.len() > 1 && selected.iter().any(|f| f == file) {
            selected
        } else {
            vec![file.to_string()]
        }
    }

    fn stepper(
        &self,
        id: &'static str,
        label: &'static str,
        value: String,
        step: impl Fn(&mut Self, isize, &mut Context<Self>) + Clone + 'static,
        cx: &mut Context<Self>,
    ) -> impl IntoElement {
        let back = step.clone();
        h_flex()
            .h_7()
            .gap_2()
            .child(div().w_24().text_xs().text_color(cx.theme().muted_foreground).child(label))
            .child(
                Button::new(SharedString::from(format!("{id}-down")))
                    .ghost()
                    .xsmall()
                    .icon(IconName::ChevronLeft)
                    .on_click(cx.listener(move |this, _, _, cx| back(this, -1, cx))),
            )
            .child(div().flex_1().text_center().truncate().child(value))
            .child(
                Button::new(SharedString::from(format!("{id}-up")))
                    .ghost()
                    .xsmall()
                    .icon(IconName::ChevronRight)
                    .on_click(cx.listener(move |this, _, _, cx| step(this, 1, cx))),
            )
    }

    fn render_properties(&mut self, cx: &mut Context<Self>) -> impl IntoElement {
        let files = self.selected_files();
        let theme = cx.theme();
        let section = |title: &'static str| {
            div()
                .pt_4()
                .pb_1()
                .text_xs()
                .text_color(theme.muted_foreground)
                .border_b_1()
                .border_color(theme.border)
                .mb_1()
                .child(title)
        };
        let field = |label: &'static str, value: String| {
            h_flex()
                .min_h_6()
                .gap_2()
                .items_start()
                .child(div().w_24().flex_shrink_0().text_xs().text_color(theme.muted_foreground).pt_0p5().child(label))
                .child(div().flex_1().min_w_0().text_sm().child(value))
        };

        let mut panel = v_flex().id("properties").size_full().overflow_y_scroll().p_3().gap_0p5();
        if files.is_empty() {
            return panel
                .text_color(theme.muted_foreground)
                .child("Select a track to see and change its song.")
                .child(div().pt_2().text_xs().child("Tip: drop song files on a track, or on a selection to fill it."));
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

        if many {
            panel = panel.child(div().text_base().font_weight(FontWeight::SEMIBOLD).child(format!("{} tracks selected", files.len())));
            let set = files.iter().filter(|f| self.project.is_assigned(f)).count();
            panel = panel.child(div().text_xs().text_color(theme.muted_foreground).child(format!("{set} have a song")));
        } else {
            let track = self.tracks.iter().find(|t| t.file == first).cloned();
            if let Some(track) = track {
                panel = panel
                    .child(div().text_base().font_weight(FontWeight::SEMIBOLD).child(track.name.clone()))
                    .child(section("Track"))
                    .child(field("Category", track.category.clone()))
                    .child(field("File", format!("{}.brstm", track.file)))
                    .when(!track.variant_of.is_empty(), |el| el.child(field("Version of", track.variant_of.clone())))
                    .when(!track.note.is_empty(), |el| el.child(field("Note", track.note.clone())));
                panel = panel.child(section("Song"));
                if self.project.is_assigned(&first) {
                    panel = panel
                        .child(field("Title", self.project.song_title(&first)))
                        .child(field("From", self.project.source(&first).to_string()));
                } else {
                    panel = panel.child(div().text_color(theme.muted_foreground).child("No song yet. The game's own music plays."));
                }
            }
        }

        let any_assigned = files.iter().any(|f| self.project.is_assigned(f));
        let play_file = first.clone();
        panel = panel.child(
            h_flex()
                .pt_2()
                .gap_2()
                .flex_wrap()
                .child(
                    Button::new("prop-choose").small().primary()
                        .label(if many { "Fill with songs…" } else { "Choose song…" })
                        .on_click(cx.listener(|this, _, window, cx| this.choose_songs(window, cx))),
                )
                .when(!many && any_assigned, |el| {
                    el.child(
                        Button::new("prop-play").small().outline().icon(Lucide::Play).label("Play")
                            .on_click(cx.listener(move |this, _, window, cx| this.play_track(play_file.clone(), window, cx))),
                    )
                })
                .when(any_assigned, |el| {
                    el.child(
                        Button::new("prop-remove").small().ghost().label("Remove song")
                            .on_click(cx.listener(|this, _, _, cx| this.remove_songs(cx))),
                    )
                }),
        );

        let can_final = files.iter().any(|f| self.tracks.iter().any(|t| &t.file == f && t.can_have_final));
        panel = panel
            .child(section("Sound"))
            .child(self.stepper("vol", "Volume", volume, |this, step, cx| {
                this.edit_selected(move |p, f| p.set_volume(f, p.volume(f) + step as i64), cx)
            }, cx))
            .child(self.stepper("speed", "Speed", speed, |this, step, cx| {
                this.edit_selected(move |p, f| {
                    let n = SPEEDS.len() as isize;
                    p.set_speed(f, ((p.speed_index(f) as isize + step).rem_euclid(n)) as usize)
                }, cx)
            }, cx))
            .child(self.stepper("loop", "Loop", looping, |this, step, cx| {
                this.edit_selected(move |p, f| {
                    let n = LOOPS.len() as isize;
                    p.set_loop(f, ((p.loop_index(f) as isize + step).rem_euclid(n)) as usize)
                }, cx)
            }, cx))
            .when(can_final, |el| {
                el.child(self.stepper("final", "Final lap", final_speed, |this, step, cx| {
                    this.edit_selected(move |p, f| {
                        let n = FINAL_SPEEDS.len() as isize;
                        p.set_final_speed(f, ((p.final_speed_index(f) as isize + step).rem_euclid(n)) as usize)
                    }, cx)
                }, cx))
            });
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
                let text = if busy.total > 0 {
                    format!("{} {}/{}", busy.label, busy.done, busy.total)
                } else {
                    format!("{}…", busy.label)
                };
                el.child(div().text_color(theme.primary).child(text))
            })
            .child(div().child(format!("{set}/{} tracks set", self.tracks.len())))
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
        let toolbar = self.render_toolbar(window, cx).into_any_element();
        let left = self.render_left(cx).into_any_element();
        let playlist = self.render_playlist(cx).into_any_element();
        let properties = self.render_properties(cx).into_any_element();
        let status = self.render_status(cx).into_any_element();
        let console = self.show_console.then(|| self.render_console(cx).into_any_element());

        v_flex()
            .id("packmaker")
            .key_context(PLAYLIST)
            .track_focus(&self.focus)
            .size_full()
            .bg(cx.theme().background)
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
            .on_action(cx.listener(|this, _: &RemoveSongs, _, cx| this.remove_songs(cx)))
            .on_action(cx.listener(|this, _: &SelectAll, _, cx| {
                this.selected = this.visible.iter().copied().collect();
                cx.notify();
            }))
            .on_action(cx.listener(|this, _: &MoveUp, _, cx| this.move_cursor(-1, false, cx)))
            .on_action(cx.listener(|this, _: &MoveDown, _, cx| this.move_cursor(1, false, cx)))
            .on_action(cx.listener(|this, _: &ExtendUp, _, cx| this.move_cursor(-1, true, cx)))
            .on_action(cx.listener(|this, _: &ExtendDown, _, cx| this.move_cursor(1, true, cx)))
            .on_action(cx.listener(|this, _: &FocusSearch, window, cx| {
                this.search.update(cx, |input, cx| input.focus(window, cx));
            }))
            .on_action(cx.listener(|this, _: &AddSongs, window, cx| this.choose_songs(window, cx)))
            .on_action(cx.listener(|this, _: &SaveNow, _, cx| {
                if this.save(cx) {
                    this.say("Saved.", cx);
                }
            }))
            .on_action(cx.listener(|this, _: &ExportZip, window, cx| this.export("zip", window, cx)))
            .on_action(cx.listener(|this, _: &ExportFolder, window, cx| this.export("folder", window, cx)))
            .on_action(cx.listener(|this, _: &AddToGame, window, cx| this.export("game", window, cx)))
            .on_action(cx.listener(|this, _: &RemoveFromGame, window, cx| this.remove_from_game(window, cx)))
            .on_action(cx.listener(|this, _: &ToggleWii, _, cx| {
                let wii = !this.project.for_wii();
                this.project.set_for_wii(wii);
                this.say(if wii { "Packs are made for a real Wii." } else { "Packs are made for Dolphin." }, cx);
                this.changed(cx);
            }))
            .on_action(cx.listener(|this, _: &OpenClassic, _, cx| this.open_classic(cx)))
            .on_action(cx.listener(|this, _: &Reload, window, cx| this.reload(window, cx)))
            .on_action(cx.listener(|this, _: &ToggleConsole, _, cx| {
                this.show_console = !this.show_console;
                cx.notify();
            }))
            .on_action(cx.listener(|this, _: &Quit, _, cx| {
                this.save(cx);
                cx.quit();
            }))
            .child(toolbar)
            .child(
                div().flex_1().min_h_0().child(
                    h_resizable("body")
                        .child(resizable_panel().size(px(230.)).size_range(px(170.)..px(380.)).child(left))
                        .child(resizable_panel().child(playlist))
                        .child(
                            resizable_panel()
                                .size(px(320.))
                                .size_range(px(260.)..px(480.))
                                .child(div().size_full().bg(cx.theme().sidebar).child(properties)),
                        ),
                ),
            )
            .children(console)
            .child(status)
    }
}
