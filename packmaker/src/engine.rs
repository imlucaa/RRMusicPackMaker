//! Talks to the app's tested C# core, `MusicPackMaker.exe engine ...`, which does the encoding, the Retro Rewind
//! track list and the exports. Commands answer with JSON on standard output.

use std::io::{BufRead, BufReader};
use std::path::PathBuf;
use std::process::{Command, Stdio};

use anyhow::{Context as _, Result, anyhow, bail};
use serde::Deserialize;
use serde::de::DeserializeOwned;

/// One line of news from a long engine command.
#[derive(Clone, Debug, Deserialize)]
pub struct Event {
    pub event: String,
    #[serde(default)]
    pub message: Option<String>,
    #[serde(default)]
    pub done: Option<u32>,
    #[serde(default)]
    pub total: Option<u32>,
    #[serde(default)]
    pub path: Option<String>,
    #[serde(default, rename = "where")]
    pub location: Option<String>,
}

const ENGINE_EXE: &str = "MusicPackMaker.exe";

/// Finds the engine: set by RRMUSIC_ENGINE, shipped beside this app, or the developer build next to the sources.
pub fn engine_path() -> Option<PathBuf> {
    if let Ok(custom) = std::env::var("RRMUSIC_ENGINE") {
        let path = PathBuf::from(custom);
        if path.is_file() {
            return Some(path);
        }
    }
    let here = std::env::current_exe().ok()?.parent()?.to_path_buf();
    if let Some(shipped) = [here.join("engine").join(ENGINE_EXE), here.join(ENGINE_EXE)].into_iter().find(|p| p.is_file()) {
        return Some(shipped);
    }
    // Run from the sources: packmaker/target/<profile>/packmaker.exe, so the repository is three folders up.
    // The newest build wins, since an older one may not have the engine commands yet.
    let repo = here.ancestors().nth(3)?;
    let builds = ["Debug", "Release"]
        .into_iter()
        .map(|profile| repo.join("bin").join(profile).join("net9.0-windows").join("win-x64").join(ENGINE_EXE))
        .chain([repo.join("publish").join(ENGINE_EXE)]);
    builds
        .filter_map(|path| Some((path.metadata().ok()?.modified().ok()?, path)))
        .max_by_key(|(modified, _)| *modified)
        .map(|(_, path)| path)
}

fn command(args: &[&str]) -> Result<Command> {
    let exe = engine_path().ok_or_else(|| anyhow!("The engine, {ENGINE_EXE}, wasn't found next to this app."))?;
    let mut command = Command::new(exe);
    command.arg("engine").args(args).stdout(Stdio::piped()).stderr(Stdio::piped()).stdin(Stdio::null());
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt as _;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    Ok(command)
}

/// Runs a command that answers with one JSON document.
pub fn run_json<T: DeserializeOwned>(args: &[&str]) -> Result<T> {
    let output = command(args)?.output().context("starting the engine")?;
    let text = String::from_utf8_lossy(&output.stdout);
    let line = text.lines().rev().find(|line| line.trim_start().starts_with(['{', '[']));
    match line {
        Some(line) => Ok(serde_json::from_str(line.trim())?),
        None => bail!(
            "The engine gave no answer. {}",
            String::from_utf8_lossy(&output.stderr).trim()
        ),
    }
}

/// Runs a long command, passing each event on as it arrives. Returns the last "done" event, or the error.
pub fn run_events(args: &[&str], mut on_event: impl FnMut(&Event)) -> Result<Event> {
    let mut child = command(args)?.spawn().context("starting the engine")?;
    let stdout = child.stdout.take().expect("stdout is piped");
    let mut last = None;
    for line in BufReader::new(stdout).lines() {
        let line = line?;
        let Ok(event) = serde_json::from_str::<Event>(line.trim()) else {
            continue;
        };
        on_event(&event);
        if event.event == "done" || event.event == "error" {
            last = Some(event);
        }
    }
    child.wait()?;
    match last {
        Some(event) if event.event == "done" => Ok(event),
        Some(event) => bail!(event.message.unwrap_or_else(|| "The engine stopped.".into())),
        None => bail!("The engine stopped without saying why."),
    }
}

/// Writes a WAV of how a track will sound in the pack, or of a plain song, and returns where it is.
pub fn preview(args: &[&str]) -> Result<PathBuf> {
    let event = run_events(args, |_| {})?;
    event.path.map(PathBuf::from).ok_or_else(|| anyhow!("The engine made no preview."))
}
