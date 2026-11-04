//! What the app works on: the track list from the engine, the saved pack, and the song library.
//!
//! The pack and the library are the same files the classic window uses, so both apps open the same work. Unknown
//! fields are kept as they are, so saving here never drops something only the classic window knows about.

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
    pub note: String,
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

pub const SPEEDS: &[SpeedChoice] = &[
    SpeedChoice { text: "Normal", speed: 1.0, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Normal + reverb", speed: 1.0, keep_pitch: false, reverb: true },
    SpeedChoice { text: "Slowed 0.9x + reverb", speed: 0.9, keep_pitch: false, reverb: true },
    SpeedChoice { text: "Slowed 0.85x + reverb", speed: 0.85, keep_pitch: false, reverb: true },
    SpeedChoice { text: "Slowed 0.8x + reverb", speed: 0.8, keep_pitch: false, reverb: true },
    SpeedChoice { text: "Slowed 0.75x + reverb", speed: 0.75, keep_pitch: false, reverb: true },
    SpeedChoice { text: "Slowed 0.7x + reverb", speed: 0.7, keep_pitch: false, reverb: true },
    SpeedChoice { text: "Slowed 0.9x", speed: 0.9, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Slowed 0.85x", speed: 0.85, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Slowed 0.8x", speed: 0.8, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Slowed 0.75x", speed: 0.75, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Slowed 0.7x", speed: 0.7, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Faster 1.1x, same pitch", speed: 1.1, keep_pitch: true, reverb: false },
    SpeedChoice { text: "Faster 1.2x, same pitch", speed: 1.2, keep_pitch: true, reverb: false },
    SpeedChoice { text: "Faster 1.25x, same pitch", speed: 1.25, keep_pitch: true, reverb: false },
    SpeedChoice { text: "Faster 1.3x, same pitch", speed: 1.3, keep_pitch: true, reverb: false },
    SpeedChoice { text: "Faster 1.5x, same pitch", speed: 1.5, keep_pitch: true, reverb: false },
    SpeedChoice { text: "Nightcore 1.1x", speed: 1.1, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Nightcore 1.2x", speed: 1.2, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Nightcore 1.25x", speed: 1.25, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Nightcore 1.3x", speed: 1.3, keep_pitch: false, reverb: false },
    SpeedChoice { text: "Nightcore 1.5x", speed: 1.5, keep_pitch: false, reverb: false },
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
        let value = read_json(&path).ok().filter(Value::is_object).unwrap_or_else(|| {
            json!({ "Options": { "PackName": "My Music Pack", "Author": "" }, "Rows": [] })
        });
        let mut project = Self { path, value, index: HashMap::new() };
        if !project.value["Rows"].is_array() {
            project.value["Rows"] = json!([]);
        }
        project.reindex();
        project
    }

    pub fn save(&self) -> Result<()> {
        write_json(&self.path, &self.value)
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
        self.value["Options"]["PackName"].as_str().unwrap_or("My Music Pack").to_string()
    }

    pub fn set_pack_name(&mut self, name: &str) {
        if let Some(options) = self.value["Options"].as_object_mut() {
            options.insert("PackName".into(), json!(name));
        }
    }

    pub fn for_wii(&self) -> bool {
        self.value["Options"]["TargetWii"].as_bool().unwrap_or(true)
    }

    pub fn set_for_wii(&mut self, wii: bool) {
        if let Some(options) = self.value["Options"].as_object_mut() {
            options.insert("TargetWii".into(), json!(wii));
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

    pub fn assign(&mut self, file: &str, song: &Path) {
        let title = song.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
        let row = self.row_mut(file);
        row.insert("Source".into(), json!(song.to_string_lossy()));
        row.insert("Song".into(), json!(title));
        row.insert("KeepFile".into(), json!(false));
    }

    pub fn clear(&mut self, file: &str) {
        if self.row(file).is_none() {
            return;
        }
        let row = self.row_mut(file);
        row.insert("Source".into(), json!(""));
        row.insert("Song".into(), json!(""));
        row.insert("FinalSource".into(), json!(""));
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
    fn speed_choices_round_trip() {
        let dir = std::env::temp_dir().join(format!("pm-test-{}", std::process::id()));
        // SAFETY: tests in this module run single-threaded for this variable.
        unsafe { std::env::set_var("RRMUSIC_DATA", &dir) };
        let mut project = Project::load();
        project.assign("6_N", Path::new("C:/music/Song A.mp3"));
        project.set_speed("6_N", 14);
        project.set_volume("6_N", 40);
        project.set_loop("6_N", 4);
        project.set_final_speed("6_N", 5);
        project.save().unwrap();
        let again = Project::load();
        assert!(again.is_assigned("6_N"));
        assert_eq!(again.song_title("6_N"), "Song A");
        assert_eq!(again.speed_index("6_N"), 14);
        assert_eq!(again.volume("6_N"), MAX_VOLUME);
        assert_eq!(again.loop_index("6_N"), 4);
        assert_eq!(again.final_speed_index("6_N"), 5);
        let _ = std::fs::remove_dir_all(dir);
    }
}
