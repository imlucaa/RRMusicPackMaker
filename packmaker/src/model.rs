//! What the app works on: the track list from the engine, the saved pack, the packs set aside, the settings and
//! the song library.
//!
//! These are the same files the classic window uses, so both apps open the same work. Unknown fields are kept as
//! they are, so saving here never drops something only the classic window knows about.

use std::collections::HashMap;
use std::path::{Path, PathBuf};

use anyhow::{Context as _, Result};
use serde::Deserialize;
use serde_json::{Map, Value, json};

/// One music slot the game can load.
#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Track {
    pub file: String,
    pub category: String,
    #[serde(default)]
    pub platform: String,
    pub name: String,
    #[serde(default)]
    pub variant_of: String,
    #[serde(default)]
    pub can_have_final: bool,
}

impl Track {
    /// The name without its console, since the console has its own column.
    pub fn short_name(&self) -> &str {
        if !self.platform.is_empty() {
            if let Some(rest) = self.name.strip_prefix(&self.platform) {
                return rest.trim_start();
            }
        }
        &self.name
    }
}

#[derive(Clone, Debug, Deserialize)]
pub struct Group {
    pub title: String,
    pub categories: Vec<String>,
}

#[derive(Debug, Deserialize)]
pub struct Catalog {
    pub message: String,
    pub groups: Vec<Group>,
    pub rows: Vec<Track>,
}

/// A song the user imported, downloaded or made.
#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Song {
    pub path: String,
    pub label: String,
    pub kind: String,
    #[serde(default)]
    pub seconds: f64,
    #[serde(default)]
    pub owned: bool,
}

/// A pack found on this PC: imported, made here, or a Wheel Wizard mod.
#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InstalledPack {
    pub name: String,
    pub folder: String,
    pub songs: u32,
    #[serde(default)]
    pub made_by_app: bool,
    #[serde(default)]
    pub is_mod: bool,
}

/// Where the app keeps its files: the same folder as the classic window.
pub fn data_folder() -> PathBuf {
    if let Ok(custom) = std::env::var("RRMUSIC_DATA") {
        if !custom.trim().is_empty() {
            return PathBuf::from(custom);
        }
    }
    dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("RRMusicPackMaker")
}

fn read_json(path: &Path) -> Result<Value> {
    let text = std::fs::read_to_string(path).with_context(|| format!("reading {}", path.display()))?;
    let text = text.trim_start_matches('\u{feff}');
    Ok(serde_json::from_str(text)?)
}

/// Writes next to the file first, so a crash halfway never leaves half a file.
fn write_json(path: &Path, value: &Value) -> Result<()> {
    if let Some(folder) = path.parent() {
        std::fs::create_dir_all(folder)?;
    }
    let temp = path.with_extension("tmp");
    std::fs::write(&temp, serde_json::to_string_pretty(value)?)?;
    std::fs::rename(&temp, path)?;
    Ok(())
}

/// A choice of speed, matching the classic window's list so both read the same saved values.
pub struct SpeedChoice {
    pub text: &'static str,
    pub speed: f64,
    pub keep_pitch: bool,
    pub reverb: bool,
}

const fn speed(text: &'static str, speed: f64, keep_pitch: bool, reverb: bool) -> SpeedChoice {
    SpeedChoice { text, speed, keep_pitch, reverb }
}

pub const SPEEDS: &[SpeedChoice] = &[
    speed("Normal", 1.0, false, false),
    speed("Normal + reverb", 1.0, false, true),
    speed("Slowed 0.9x + reverb", 0.9, false, true),
    speed("Slowed 0.85x + reverb", 0.85, false, true),
    speed("Slowed 0.8x + reverb", 0.8, false, true),
    speed("Slowed 0.75x + reverb", 0.75, false, true),
    speed("Slowed 0.7x + reverb", 0.7, false, true),
    speed("Slowed 0.9x", 0.9, false, false),
    speed("Slowed 0.85x", 0.85, false, false),
    speed("Slowed 0.8x", 0.8, false, false),
    speed("Slowed 0.75x", 0.75, false, false),
    speed("Slowed 0.7x", 0.7, false, false),
    speed("Faster 1.1x, same pitch", 1.1, true, false),
    speed("Faster 1.2x, same pitch", 1.2, true, false),
    speed("Faster 1.25x, same pitch", 1.25, true, false),
    speed("Faster 1.3x, same pitch", 1.3, true, false),
    speed("Faster 1.5x, same pitch", 1.5, true, false),
    speed("Nightcore 1.1x", 1.1, false, false),
    speed("Nightcore 1.2x", 1.2, false, false),
    speed("Nightcore 1.25x", 1.25, false, false),
    speed("Nightcore 1.3x", 1.3, false, false),
    speed("Nightcore 1.5x", 1.5, false, false),
];

/// The game plays final laps 6% faster.
pub const GAME_FINAL_SPEED: f64 = 1.06;

pub const FINAL_SPEEDS: &[(&str, f64)] = &[
    ("1.06x, like the game", GAME_FINAL_SPEED),
    ("Same speed", 1.0),
    ("1.1x", 1.1),
    ("1.15x", 1.15),
    ("1.2x", 1.2),
    ("1.25x", 1.25),
];

pub const LOOPS: &[(&str, &str)] = &[
    ("Auto", "Auto"),
    ("WholeSong", "Whole song"),
    ("FindLoop", "Find a loop"),
    ("Custom", "My own points"),
    ("NoLoop", "No loop"),
];

pub const CROSSFADES: &[(&str, f64)] = &[("Off", 0.0), ("0.25 s", 0.25), ("0.5 s", 0.5), ("1 s", 1.0), ("2 s", 2.0)];
pub const SAMPLE_RATES: &[i64] = &[32000, 24000, 22050, 16000];
pub const LOUDNESS: &[&str] = &["Even loudness", "Peak only", "Off"];
pub const QUALITIES: &[(&str, i64)] =
    &[("Best", 0), ("256 kbps", 256), ("192 kbps", 192), ("128 kbps", 128), ("96 kbps", 96), ("64 kbps", 64)];
pub const FADES: &[(&str, f64)] =
    &[("None", 0.0), ("0.5 s", 0.5), ("1 s", 1.0), ("2 s", 2.0), ("3 s", 3.0), ("5 s", 5.0), ("8 s", 8.0)];
pub const NEW_PACK: &str = "My Music Pack";

pub const MIN_VOLUME: i64 = -12;
pub const MAX_VOLUME: i64 = 12;

/// The saved pack: `autosave.rrmusic.json`, shared with the classic window.
pub struct Project {
    path: PathBuf,
    value: Value,
    index: HashMap<String, usize>,
}

impl Project {
    pub fn load() -> Self {
        let path = data_folder().join("autosave.rrmusic.json");
        let value = read_json(&path)
            .ok()
            .filter(Value::is_object)
            .unwrap_or_else(|| json!({ "Options": { "PackName": NEW_PACK, "Author": "" }, "Rows": [] }));
        let mut project = Self { path, value: Value::Null, index: HashMap::new() };
        project.replace(value);
        project
    }

    pub fn save(&self) -> Result<()> {
        write_json(&self.path, &self.value)
    }

    /// Replaces the open pack with another one, kept in memory until saved.
    pub fn replace(&mut self, value: Value) {
        self.value = value;
        if !self.value["Rows"].is_array() {
            self.value["Rows"] = json!([]);
        }
        if !self.value["Options"].is_object() {
            self.value["Options"] = json!({});
        }
        self.reindex();
    }

    /// A fresh pack with the same pack settings, like the classic window's New pack.
    pub fn fresh(&self, name: &str) -> Value {
        let mut options = self.value["Options"].clone();
        if let Some(map) = options.as_object_mut() {
            map.insert("PackName".into(), json!(name));
        }
        json!({ "Options": options, "Rows": [] })
    }

    pub fn value(&self) -> &Value {
        &self.value
    }

    /// True when anything is set on any track, so the pack is worth keeping when another opens.
    pub fn has_content(&self) -> bool {
        self.value["Rows"].as_array().is_some_and(|rows| {
            rows.iter().any(|row| {
                ["Source", "Song", "Credits"].iter().any(|f| row[*f].as_str().is_some_and(|v| !v.is_empty()))
            })
        })
    }

    fn reindex(&mut self) {
        self.index = self.value["Rows"]
            .as_array()
            .map(|rows| {
                rows.iter()
                    .enumerate()
                    .filter_map(|(ix, row)| row["File"].as_str().map(|file| (file.to_ascii_lowercase(), ix)))
                    .collect()
            })
            .unwrap_or_default();
    }

    pub fn pack_name(&self) -> String {
        self.option_text("PackName", NEW_PACK)
    }

    pub fn set_pack_name(&mut self, name: &str) {
        self.set_option("PackName", json!(name));
    }

    pub fn for_wii(&self) -> bool {
        self.option_flag("TargetWii", true)
    }

    pub fn set_for_wii(&mut self, wii: bool) {
        self.set_option("TargetWii", json!(wii));
    }

    pub fn option_text(&self, key: &str, fallback: &str) -> String {
        self.value["Options"][key].as_str().unwrap_or(fallback).to_string()
    }

    pub fn option_number(&self, key: &str, fallback: i64) -> i64 {
        self.value["Options"][key].as_i64().unwrap_or(fallback)
    }

    pub fn option_flag(&self, key: &str, fallback: bool) -> bool {
        self.value["Options"][key].as_bool().unwrap_or(fallback)
    }

    pub fn set_option(&mut self, key: &str, value: Value) {
        if let Some(options) = self.value["Options"].as_object_mut() {
            options.insert(key.into(), value);
        }
    }

    fn row(&self, file: &str) -> Option<&Map<String, Value>> {
        let ix = *self.index.get(&file.to_ascii_lowercase())?;
        self.value["Rows"][ix].as_object()
    }

    fn row_mut(&mut self, file: &str) -> &mut Map<String, Value> {
        let key = file.to_ascii_lowercase();
        let ix = match self.index.get(&key) {
            Some(ix) => *ix,
            None => {
                let rows = self.value["Rows"].as_array_mut().expect("rows are an array");
                rows.push(json!({
                    "File": file, "Source": "", "FinalSource": "", "Song": "", "Credits": "",
                    "Loop": "Auto", "LoopStart": "", "LoopEnd": "", "Crossfade": 0, "VolumeDb": 0,
                    "Speed": 1, "KeepPitch": false, "Reverb": false, "KeepFile": false,
                    "FinalSpeed": GAME_FINAL_SPEED, "FinalVolumeDb": null, "FinalSongSpeed": 0,
                    "FinalSongKeepPitch": false, "FinalSongReverb": false
                }));
                let ix = rows.len() - 1;
                self.index.insert(key, ix);
                ix
            }
        };
        self.value["Rows"][ix].as_object_mut().expect("rows are objects")
    }

    fn text(&self, file: &str, field: &str) -> &str {
        self.row(file).and_then(|row| row.get(field)).and_then(Value::as_str).unwrap_or("")
    }

    fn number(&self, file: &str, field: &str, fallback: f64) -> f64 {
        self.row(file).and_then(|row| row.get(field)).and_then(Value::as_f64).unwrap_or(fallback)
    }

    fn flag(&self, file: &str, field: &str) -> bool {
        self.row(file).and_then(|row| row.get(field)).and_then(Value::as_bool).unwrap_or(false)
    }

    pub fn source(&self, file: &str) -> &str {
        self.text(file, "Source")
    }

    pub fn is_assigned(&self, file: &str) -> bool {
        !self.source(file).trim().is_empty()
    }

    /// The song's title, or its file name when it has none.
    pub fn song_title(&self, file: &str) -> String {
        let title = self.text(file, "Song");
        if !title.is_empty() {
            return title.to_string();
        }
        Path::new(self.source(file))
            .file_stem()
            .map(|stem| stem.to_string_lossy().into_owned())
            .unwrap_or_default()
    }

    pub fn credits(&self, file: &str) -> String {
        self.text(file, "Credits").to_string()
    }

    pub fn set_text(&mut self, file: &str, field: &str, value: &str) {
        self.row_mut(file).insert(field.into(), json!(value));
    }

    pub fn volume(&self, file: &str) -> i64 {
        self.number(file, "VolumeDb", 0.0) as i64
    }

    pub fn speed_index(&self, file: &str) -> usize {
        let speed = self.number(file, "Speed", 1.0);
        let keep = self.flag(file, "KeepPitch") && speed > 1.0;
        let reverb = self.flag(file, "Reverb");
        SPEEDS
            .iter()
            .position(|c| (c.speed - speed).abs() < 1e-6 && c.keep_pitch == keep && c.reverb == reverb)
            .unwrap_or(0)
    }

    pub fn loop_index(&self, file: &str) -> usize {
        let mode = self.text(file, "Loop");
        LOOPS.iter().position(|(key, _)| *key == mode).unwrap_or(0)
    }

    pub fn final_speed_index(&self, file: &str) -> usize {
        let speed = self.number(file, "FinalSpeed", GAME_FINAL_SPEED);
        FINAL_SPEEDS.iter().position(|(_, s)| (s - speed).abs() < 1e-6).unwrap_or(0)
    }

    pub fn crossfade_index(&self, file: &str) -> usize {
        let seconds = self.number(file, "Crossfade", 0.0);
        CROSSFADES.iter().position(|(_, s)| (s - seconds).abs() < 1e-6).unwrap_or(0)
    }

    pub fn final_source(&self, file: &str) -> &str {
        self.text(file, "FinalSource")
    }

    /// The final lap song's own volume, or None when it follows the main song.
    pub fn final_volume(&self, file: &str) -> Option<i64> {
        self.row(file).and_then(|row| row.get("FinalVolumeDb")).and_then(Value::as_i64)
    }

    /// The final lap song's own speed choice, or None when it follows the main song.
    pub fn final_song_speed(&self, file: &str) -> Option<usize> {
        let speed = self.number(file, "FinalSongSpeed", 0.0);
        if speed <= 0.0 {
            return None;
        }
        let keep = self.flag(file, "FinalSongKeepPitch") && speed > 1.0;
        let reverb = self.flag(file, "FinalSongReverb");
        SPEEDS
            .iter()
            .position(|c| (c.speed - speed).abs() < 1e-6 && c.keep_pitch == keep && c.reverb == reverb)
            .or(Some(0))
    }

    /// Own loop points in seconds, as the classic window writes them ("m:ss.ffffff").
    pub fn loop_points(&self, file: &str) -> (Option<f64>, Option<f64>) {
        (parse_time(self.text(file, "LoopStart")), parse_time(self.text(file, "LoopEnd")))
    }

    pub fn assign(&mut self, file: &str, song: &Path) {
        let title = song.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
        let row = self.row_mut(file);
        row.insert("Source".into(), json!(song.to_string_lossy()));
        row.insert("Song".into(), json!(title));
        row.insert("KeepFile".into(), json!(false));
        row.insert("LoopStart".into(), json!(""));
        row.insert("LoopEnd".into(), json!(""));
        if row.get("Loop").and_then(Value::as_str) == Some("Custom") {
            row.insert("Loop".into(), json!("Auto"));
        }
    }

    pub fn clear(&mut self, file: &str) {
        if self.row(file).is_none() {
            return;
        }
        let row = self.row_mut(file);
        for field in ["Source", "Song", "FinalSource"] {
            row.insert(field.into(), json!(""));
        }
        row.insert("KeepFile".into(), json!(false));
    }

    pub fn set_volume(&mut self, file: &str, decibels: i64) {
        self.row_mut(file).insert("VolumeDb".into(), json!(decibels.clamp(MIN_VOLUME, MAX_VOLUME)));
    }

    pub fn set_speed(&mut self, file: &str, choice: usize) {
        let choice = &SPEEDS[choice.min(SPEEDS.len() - 1)];
        let row = self.row_mut(file);
        row.insert("Speed".into(), json!(choice.speed));
        row.insert("KeepPitch".into(), json!(choice.keep_pitch));
        row.insert("Reverb".into(), json!(choice.reverb));
    }

    pub fn set_loop(&mut self, file: &str, choice: usize) {
        let (key, _) = LOOPS[choice.min(LOOPS.len() - 1)];
        self.row_mut(file).insert("Loop".into(), json!(key));
    }

    pub fn set_final_speed(&mut self, file: &str, choice: usize) {
        let (_, speed) = FINAL_SPEEDS[choice.min(FINAL_SPEEDS.len() - 1)];
        self.row_mut(file).insert("FinalSpeed".into(), json!(speed));
    }

    pub fn set_crossfade(&mut self, file: &str, choice: usize) {
        let (_, seconds) = CROSSFADES[choice.min(CROSSFADES.len() - 1)];
        self.row_mut(file).insert("Crossfade".into(), json!(seconds));
    }

    pub fn set_final_source(&mut self, file: &str, song: Option<&Path>) {
        let value = song.map(|p| p.to_string_lossy().into_owned()).unwrap_or_default();
        self.row_mut(file).insert("FinalSource".into(), json!(value));
    }

    pub fn set_final_volume(&mut self, file: &str, decibels: Option<i64>) {
        let value = decibels.map(|d| json!(d.clamp(MIN_VOLUME, MAX_VOLUME))).unwrap_or(Value::Null);
        self.row_mut(file).insert("FinalVolumeDb".into(), value);
    }

    pub fn set_final_song_speed(&mut self, file: &str, choice: Option<usize>) {
        let (speed, keep, reverb) = match choice {
            None => (0.0, false, false),
            Some(choice) => {
                let c = &SPEEDS[choice.min(SPEEDS.len() - 1)];
                (c.speed, c.keep_pitch, c.reverb)
            }
        };
        let row = self.row_mut(file);
        row.insert("FinalSongSpeed".into(), json!(speed));
        row.insert("FinalSongKeepPitch".into(), json!(keep));
        row.insert("FinalSongReverb".into(), json!(reverb));
    }

    pub fn set_loop_points(&mut self, file: &str, start: f64, end: f64) {
        let row = self.row_mut(file);
        row.insert("Loop".into(), json!("Custom"));
        row.insert("LoopStart".into(), json!(precise_time(start)));
        row.insert("LoopEnd".into(), json!(precise_time(end)));
    }
}

/// Packs set aside: `packs.json`, the same list the classic window's pack dropdown uses.
pub struct Packs {
    path: PathBuf,
    map: Map<String, Value>,
}

impl Packs {
    pub fn load() -> Self {
        let path = data_folder().join("packs.json");
        let map = read_json(&path).ok().and_then(|v| v.as_object().cloned()).unwrap_or_default();
        Self { path, map }
    }

    pub fn names(&self) -> Vec<String> {
        let mut names: Vec<String> = self.map.keys().cloned().collect();
        names.sort_by(|a, b| natural_cmp(a, b));
        names
    }

    pub fn contains(&self, name: &str) -> bool {
        self.map.keys().any(|key| key.eq_ignore_ascii_case(name))
    }

    pub fn take(&mut self, name: &str) -> Option<Value> {
        let key = self.map.keys().find(|key| key.eq_ignore_ascii_case(name))?.clone();
        self.map.remove(&key)
    }

    /// Puts a pack aside under a name no other pack has, so nothing is ever written over.
    pub fn put(&mut self, wanted: &str, mut project: Value, avoid: &str) -> String {
        let mut name = wanted.to_string();
        let mut number = 2;
        while self.contains(&name) || name.eq_ignore_ascii_case(avoid) {
            name = format!("{wanted} {number}");
            number += 1;
        }
        if let Some(options) = project["Options"].as_object_mut() {
            options.insert("PackName".into(), json!(name));
        }
        self.map.insert(name.clone(), project);
        name
    }

    /// A name for a new pack that no pack has yet.
    pub fn unique(&self, wanted: &str, open: &str) -> String {
        let mut name = wanted.to_string();
        let mut number = 2;
        while self.contains(&name) || name.eq_ignore_ascii_case(open) {
            name = format!("{wanted} {number}");
            number += 1;
        }
        name
    }

    pub fn save(&self) -> Result<()> {
        write_json(&self.path, &Value::Object(self.map.clone()))
    }
}

/// The app's settings: `settings.json`, shared with the classic window. Unknown fields are kept.
pub struct Settings {
    path: PathBuf,
    value: Value,
}

impl Settings {
    pub fn load() -> Self {
        let path = data_folder().join("settings.json");
        let value = read_json(&path).ok().filter(Value::is_object).unwrap_or_else(|| json!({}));
        Self { path, value }
    }

    pub fn save(&self) -> Result<()> {
        write_json(&self.path, &self.value)
    }

    pub fn text(&self, key: &str) -> String {
        self.value[key].as_str().unwrap_or("").to_string()
    }

    pub fn number(&self, key: &str, fallback: i64) -> i64 {
        self.value[key].as_i64().unwrap_or(fallback)
    }

    pub fn flag(&self, key: &str, fallback: bool) -> bool {
        self.value[key].as_bool().unwrap_or(fallback)
    }

    pub fn set(&mut self, key: &str, value: Value) {
        if let Some(map) = self.value.as_object_mut() {
            map.insert(key.into(), value);
        }
    }
}

/// Reads "m:ss.fff", "ss.fff" or "12.5s" as seconds.
pub fn parse_time(text: &str) -> Option<f64> {
    let text = text.trim().trim_end_matches('s');
    if text.is_empty() {
        return None;
    }
    match text.split_once(':') {
        Some((minutes, seconds)) => Some(minutes.trim().parse::<f64>().ok()? * 60.0 + seconds.trim().parse::<f64>().ok()?),
        None => text.parse().ok(),
    }
}

/// Writes seconds the way the classic window does, exact enough to keep a loop on the right sample.
pub fn precise_time(seconds: f64) -> String {
    let minutes = (seconds / 60.0).floor();
    format!("{}:{:09.6}", minutes as i64, seconds - minutes * 60.0)
}

/// Puts songs at the top of the library list the classic window shows, keeping its saved edits.
pub fn add_to_library(paths: &[PathBuf]) -> Result<()> {
    let path = data_folder().join("songs.json");
    let mut value = read_json(&path).unwrap_or_else(|_| json!({ "Songs": [], "Edits": {} }));
    if !value["Songs"].is_array() {
        value["Songs"] = json!([]);
    }
    let songs = value["Songs"].as_array_mut().expect("songs are an array");
    for song in paths.iter().rev() {
        let text = song.to_string_lossy().to_string();
        if !songs.iter().any(|s| s.as_str().is_some_and(|s| s.eq_ignore_ascii_case(&text))) {
            songs.insert(0, json!(text));
        }
    }
    write_json(&path, &value)
}

/// Writes a small JSON file for an engine command, so long lists of paths stay off the command line.
pub fn write_spec(value: &Value) -> Result<PathBuf> {
    let stamp = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or_default();
    let path = std::env::temp_dir().join(format!("packmaker-spec-{}-{stamp}.json", std::process::id()));
    std::fs::write(&path, serde_json::to_string(value)?)?;
    Ok(path)
}

const AUDIO: &[&str] = &["wav", "mp3", "flac", "ogg", "m4a", "aac", "opus", "wma", "brstm", "webm", "mp4"];

pub fn is_audio(path: &Path) -> bool {
    path.extension()
        .and_then(|ext| ext.to_str())
        .is_some_and(|ext| AUDIO.contains(&ext.to_ascii_lowercase().as_str()))
}

/// Songs inside dropped folders too, sorted so 2 comes before 10.
pub fn audio_files(paths: &[PathBuf]) -> Vec<PathBuf> {
    let mut found = Vec::new();
    for path in paths {
        if path.is_dir() {
            let mut inner: Vec<PathBuf> = std::fs::read_dir(path)
                .map(|entries| entries.filter_map(|e| e.ok().map(|e| e.path())).collect())
                .unwrap_or_default();
            inner.sort_by(|a, b| natural_cmp(&a.to_string_lossy(), &b.to_string_lossy()));
            found.extend(audio_files(&inner));
        } else if is_audio(path) {
            found.push(path.clone());
        }
    }
    found
}

/// Compares names the way a person would, so 2 comes before 10.
pub fn natural_cmp(a: &str, b: &str) -> std::cmp::Ordering {
    let (mut a, mut b) = (a.chars().peekable(), b.chars().peekable());
    loop {
        match (a.peek(), b.peek()) {
            (None, None) => return std::cmp::Ordering::Equal,
            (None, _) => return std::cmp::Ordering::Less,
            (_, None) => return std::cmp::Ordering::Greater,
            (Some(x), Some(y)) if x.is_ascii_digit() && y.is_ascii_digit() => {
                let take = |it: &mut std::iter::Peekable<std::str::Chars>| {
                    let mut digits = String::new();
                    while let Some(c) = it.peek().filter(|c| c.is_ascii_digit()) {
                        digits.push(*c);
                        it.next();
                    }
                    digits.trim_start_matches('0').to_string()
                };
                let (x, y) = (take(&mut a), take(&mut b));
                let order = x.len().cmp(&y.len()).then_with(|| x.cmp(&y));
                if order != std::cmp::Ordering::Equal {
                    return order;
                }
            }
            (Some(x), Some(y)) => {
                let order = x.to_ascii_lowercase().cmp(&y.to_ascii_lowercase());
                if order != std::cmp::Ordering::Equal {
                    return order;
                }
                a.next();
                b.next();
            }
        }
    }
}

pub fn format_time(seconds: f64) -> String {
    let total = seconds.max(0.0) as u64;
    format!("{}:{:02}", total / 60, total % 60)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn natural_order_counts_numbers_as_numbers() {
        let mut names = vec!["10 ten", "2 two", "1 one"];
        names.sort_by(|a, b| natural_cmp(a, b));
        assert_eq!(names, ["1 one", "2 two", "10 ten"]);
    }

    #[test]
    fn times_read_back_as_written() {
        assert_eq!(parse_time("1:05.500000"), Some(65.5));
        assert_eq!(parse_time("12.5s"), Some(12.5));
        assert_eq!(parse_time(""), None);
        assert_eq!(precise_time(65.5), "1:05.500000");
        assert!((parse_time(&precise_time(123.456789)).unwrap() - 123.456789).abs() < 1e-6);
    }

    #[test]
    fn packs_never_share_a_name() {
        let mut packs = Packs { path: std::env::temp_dir().join("unused.json"), map: Map::new() };
        assert_eq!(packs.put("Mine", json!({"Options": {}}), "Other"), "Mine");
        assert_eq!(packs.put("Mine", json!({"Options": {}}), "Other"), "Mine 2");
        assert_eq!(packs.put("Open", json!({"Options": {}}), "Open"), "Open 2");
        assert!(packs.take("mine").is_some());
        assert_eq!(packs.names(), vec!["Mine 2".to_string(), "Open 2".to_string()]);
        assert_eq!(packs.unique("Mine 2", "x"), "Mine 2 2");
    }

    #[test]
    fn every_track_setting_round_trips() {
        let dir = std::env::temp_dir().join(format!("pm-test-{}", std::process::id()));
        // SAFETY: the only test that sets this variable.
        unsafe { std::env::set_var("RRMUSIC_DATA", &dir) };
        let mut project = Project::load();
        project.assign("6_N", Path::new("C:/music/Song A.mp3"));
        project.set_speed("6_N", 14);
        project.set_volume("6_N", 40);
        project.set_loop("6_N", 4);
        project.set_final_speed("6_N", 5);
        project.set_crossfade("6_N", 3);
        project.set_final_source("6_N", Some(Path::new("C:/music/Fast.mp3")));
        project.set_final_volume("6_N", Some(-3));
        project.set_final_song_speed("6_N", Some(2));
        project.set_text("6_N", "Credits", "Someone");
        project.save().unwrap();
        let mut again = Project::load();
        assert!(again.is_assigned("6_N"));
        assert!(again.has_content());
        assert_eq!(again.song_title("6_N"), "Song A");
        assert_eq!(again.speed_index("6_N"), 14);
        assert_eq!(again.volume("6_N"), MAX_VOLUME);
        assert_eq!(again.loop_index("6_N"), 4);
        assert_eq!(again.final_speed_index("6_N"), 5);
        assert_eq!(again.crossfade_index("6_N"), 3);
        assert_eq!(again.final_source("6_N"), "C:/music/Fast.mp3");
        assert_eq!(again.final_volume("6_N"), Some(-3));
        assert_eq!(again.final_song_speed("6_N"), Some(2));
        assert_eq!(again.credits("6_N"), "Someone");
        again.set_loop_points("6_N", 1.5, 61.25);
        assert_eq!(again.loop_points("6_N"), (Some(1.5), Some(61.25)));
        assert_eq!(again.loop_index("6_N"), 3);
        again.set_final_song_speed("6_N", None);
        assert_eq!(again.final_song_speed("6_N"), None);
        let _ = std::fs::remove_dir_all(dir);
    }
}
