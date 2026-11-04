//! Plays previews. The engine renders each preview to a WAV with the track's volume and speed, so what plays here
//! is what the pack will sound like.

use std::fs::File;
use std::path::Path;
use std::time::Duration;

use anyhow::{Context as _, Result};
use rodio::{Decoder, OutputStream, OutputStreamBuilder, Sink, Source as _};

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum PlayState {
    Stopped,
    Playing,
    Paused,
}

/// What is loaded: shown in the status bar and marked in the track list.
#[derive(Clone, Debug)]
pub struct NowPlaying {
    pub title: String,
    /// The track's file name, or None for a song played from the library.
    pub track: Option<String>,
    pub length: Duration,
}

pub struct Player {
    stream: Option<OutputStream>,
    sink: Option<Sink>,
    now: Option<NowPlaying>,
    volume: f32,
}

impl Player {
    pub fn new() -> Self {
        Self { stream: None, sink: None, now: None, volume: 0.8 }
    }

    fn mixer_sink(&mut self) -> Result<Sink> {
        if self.stream.is_none() {
            let mut stream = OutputStreamBuilder::open_default_stream().context("no audio output found")?;
            stream.log_on_drop(false);
            self.stream = Some(stream);
        }
        Ok(Sink::connect_new(self.stream.as_ref().expect("stream is open").mixer()))
    }

    pub fn play(&mut self, wav: &Path, title: String, track: Option<String>) -> Result<()> {
        self.stop();
        let source = Decoder::try_from(File::open(wav).context("opening the preview")?).context("reading the preview")?;
        let length = source.total_duration().unwrap_or_default();
        let sink = self.mixer_sink()?;
        sink.set_volume(self.volume);
        sink.append(source);
        sink.play();
        self.sink = Some(sink);
        self.now = Some(NowPlaying { title, track, length });
        Ok(())
    }

    pub fn state(&self) -> PlayState {
        match &self.sink {
            None => PlayState::Stopped,
            Some(sink) if sink.empty() => PlayState::Stopped,
            Some(sink) if sink.is_paused() => PlayState::Paused,
            Some(_) => PlayState::Playing,
        }
    }

    /// Clears what was playing once it has finished by itself.
    pub fn tidy(&mut self) {
        if self.sink.as_ref().is_some_and(Sink::empty) {
            self.sink = None;
            self.now = None;
        }
    }

    pub fn now(&self) -> Option<&NowPlaying> {
        self.now.as_ref()
    }

    pub fn toggle(&mut self) {
        if let Some(sink) = &self.sink {
            if sink.is_paused() {
                sink.play();
            } else {
                sink.pause();
            }
        }
    }

    pub fn stop(&mut self) {
        if let Some(sink) = self.sink.take() {
            sink.stop();
        }
        self.now = None;
    }

    pub fn position(&self) -> Duration {
        self.sink.as_ref().map(Sink::get_pos).unwrap_or_default()
    }

    /// Jumps to a point given as a fraction of the song.
    pub fn seek(&mut self, fraction: f32) {
        if let (Some(sink), Some(now)) = (&self.sink, &self.now) {
            let target = now.length.mul_f32(fraction.clamp(0.0, 1.0));
            let _ = sink.try_seek(target);
        }
    }

    pub fn set_volume(&mut self, volume: f32) {
        self.volume = volume.clamp(0.0, 1.0);
        if let Some(sink) = &self.sink {
            sink.set_volume(self.volume);
        }
    }
}

