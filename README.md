# Finish Replay

Finish Replay is a modern cross-platform video recording and instant replay application built for sports timing.

The application focuses on fast, reliable video capture and frame-accurate replay while integrating with professional timing systems such as the ALGE TimY3.

Whether you're timing an athletics race, cycling event, ski competition, or any other sport, Finish Replay automatically captures the action, preserves configurable pre-start and post-finish buffers, and displays official timing events directly on the replay timeline.

## Download (Windows)

Grab the latest build from the [**Releases**](https://github.com/codesource/finishreplay/releases/latest) page:

1. Download `FinishReplay-<version>-win-x64.zip`.
2. Extract it anywhere.
3. Run `FinishReplay.exe`.

It's a self-contained Windows x64 build — **no .NET install required**, and FFmpeg is bundled. The current builds are early **alpha** pre-releases; expect rough edges.

## Features

* Cross-platform (Windows, macOS planned)
* Live camera preview
* Automatic and manual recording
* Configurable pre-record and post-record buffers
* Frame-by-frame replay
* Interactive replay timeline
* Timing event markers
* ALGE TimY3 integration
* Extensible timing provider architecture
* Session metadata stored as JSON
* Designed for future multi-camera synchronization

Finish Replay aims to provide a modern, lightweight alternative to traditional replay software by combining professional timing integration with a simple and intuitive user experience.

## License

Finish Replay is free software licensed under the **GNU General Public License v3.0** (GPLv3) — see [LICENSE](LICENSE).

Copyright © 2026 Matthias Toscanelli / code-source.ch.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

### Third-party components

This project uses **[FFmpeg](https://ffmpeg.org)** for video capture, decoding and encoding. The FFmpeg
libraries are licensed under the **GPLv3** (this build is configured with `--enable-gpl`); their
corresponding source is available from [ffmpeg.org](https://ffmpeg.org/download.html). Timing
integration targets ALGE-TIMING devices (ALGE-TIMING GmbH); their SDK/DLLs are the property of ALGE
and are not distributed with this repository.
