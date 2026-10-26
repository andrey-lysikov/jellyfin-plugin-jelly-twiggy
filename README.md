# Jelly Twiggy

Jelly Twiggy extends the Jellyfin web client with poster badges, playback options, active streams, a customizable dashboard and watch history.

[![Downloads](https://img.shields.io/github/downloads/andrey-lysikov/jellyfin-plugin-jelly-twiggy/total)](https://github.com/andrey-lysikov/jellyfin-plugin-jelly-twiggy/releases/latest)
[![Release](https://img.shields.io/github/v/release/andrey-lysikov/jellyfin-plugin-jelly-twiggy)](https://github.com/andrey-lysikov/jellyfin-plugin-jelly-twiggy/releases/latest)
[![Jellyfin](https://img.shields.io/badge/jellyfin-12.x-blue.svg?style=flat)](https://jellyfin.org)

## Features

- Poster badges: quality (`4K HDR (HEVC)`, `1080p (x264)`, `4K DV (AV1)`), first audio track (`DTS 5.1`) and rating
- Auto-skip intro and outro (segments from Intro Skipper)
- Auto-pause, auto-resume and auto Picture-in-Picture on tab switch
- Long press / hold for 2x speed
- Active streams button with progress, device and transcoding details
- Dashboard: show, hide and reorder blocks, devices in the active streams style
- Playback statistics for 24 hours / 7 days / month: users, most played, recent plays, direct play vs. transcode
- "Watched" row on item pages: user avatars with *"Watched N times, last on &lt;date&gt;"* or *"12 of 20 episodes"*
- Per-user settings stored on the server, all off by default
- Localization (English, German, Russian)

*Works in the web client and apps that wrap it (Jellyfin Media Player, Android). Native clients are not affected.*

## Install

Add the repository in **Dashboard → Plugins → Repositories**:

    https://raw.githubusercontent.com/andrey-lysikov/jellyfin-plugin-jelly-twiggy/main/manifest.json

Install **Jelly Twiggy** from the catalog and restart the server. User options are in **Settings → Jelly Twiggy**, admin options in **Dashboard → Plugins → Jelly Twiggy**.

## Tech

C# / .NET 10 for Jellyfin 12.x, requires [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation). Data is kept in `<jellyfin data>/jellytwiggy/`. Releases are built by GitHub Actions from `vX.Y.Z.W` tags.

Based on: [Jellyfin-Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced), [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation), [Intro Skipper](https://github.com/intro-skipper/intro-skipper)

License: [GPL-3.0](LICENSE)
