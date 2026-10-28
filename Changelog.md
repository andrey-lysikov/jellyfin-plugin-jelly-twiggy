# Changelog

## 0.2.6.1

- Active streams: "Continue here" is now "Continue in browser"; next to it a drop-down moves the playback to another of your Swiftly devices
- Item page: the "Play on…" button has no stray ring and its icon matches the other buttons

## 0.2.6.0

- Statistics: play methods and popular titles moved to the left column, users to the right
- Dashboard library counts: "619 · 4K" instead of "619 [4K]", plus HDR and DV counters
- Dashboard: "Customize dashboard" is now a pencil icon in the header next to active streams; save, reset and cancel are header icons with tooltips while editing
- Dashboard: "Recent plays" header links to the activity page, like the other blocks
- Continue watching with Swiftly clients (user setting, off by default): "Continue here" in the active streams panel takes over your playback from another device at the same position and tracks; "Play on…" sends a video from its page, or the playing one from the player, to another device

## 0.2.5.0

- Stream cards: new layout — poster with title ("Series · S04E01", film with the year in brackets), badges, device and progress on the right, transcoding details across the bottom; device and IP on one line

## 0.2.4.0

- Dashboard: even larger CPU and RAM charts and disk speeds
- Stream cards: device and client are no longer cut with an ellipsis; the IP address never breaks in the middle
- Fix: dark corners behind posters with a large or no corner radius
- Item page: a small gap before the text of audio and subtitle selects; no arrow next to the video badges

## 0.2.3.0

- Library alphabet picker is back to the standard one: Jellyfin filters letters by sort name, so the plugin no longer changes it
- Item page: audio and subtitle selects are standard again (frame and arrow as in Jellyfin); the video track stays as badges with the arrow right after them

## 0.2.2.0

- Active streams: the icon ring spins smoothly around its centre; the triangle stays still
- Stream cards keep device, client and IP address on one line; long names are shortened instead
- Fix: dropdown arrows of audio and subtitle selects no longer overlap the text
- Dashboard: resolution badges show the count first and the type in brackets, e.g. 619 [4K]
- Fix: statistics period tabs did not switch the data after returning to the dashboard
- Fix: library alphabet picker shows the letters of your titles in the default (modern) layout too
- Fix: users hidden from the login screen are shown again in the "Watched" row
- Smaller watched circle on cards

## 0.2.1.0

- Bugfix
- Security fix

## 0.2.0.0

- Watched circle in the top-right corner of cards: empty ring, number of unwatched episodes, or green with a check; replaces the native indicators
- The "More" menu opens from its button like a dropdown instead of centred on it
- Playback statistics: All time period, users on the left, playback method and top 10 most played on the right
- Recent plays as a separate dashboard block
- Library counts: 4K / 1080p / 720p / SD badges in the Movies, Episodes and Music videos cards
- Server block: CPU and RAM load charts and disk read/write speed (Linux servers)
- User option to sync all settings of the current user: Jellyfin settings normally kept only in the browser are stored on the server and restored after clearing the cache or on another device; when turned on with settings already saved, it asks which side to keep
- Library alphabet picker shows the letters that actually start titles in the current view in any language (Cyrillic, Turkish, Chinese…), following its sort and filters
- Audio badge shows the default audio track, the one the player picks
- Active streams icon: the open ring spins while something plays
- User options: hide the Google Cast button, corner radius of posters and cards, and of buttons, fields and dialogs (square, as now, large)
- Admin options under "Page tweaks": item page, player, animations and card styling can each be turned off
- Item page: the dropdown arrow of audio, subtitle and video selects sits right after the text
- Series and season badges come from the first scanned episode, so audio formats like DTS no longer go missing
- Fix: video track text showing between its badges on the item page

## 0.1.8.0

- Rotten Tomatoes score above the rating on posters
- Video badges in the order resolution, HDR / DV, codec, and smaller
- Watched, favorite and "More" buttons as badges on cards with badges, instead of the hover buttons hidden under them; the favorite one follows "Hide favorites"
- Fix: video track text showing next to its badges on the item page

## 0.1.7.0

- Dashboard: smaller blocks fill the space under their neighbours, show or hide blocks with a switch
- Quality badge split into resolution, codec and HDR / DV badges
- Item page: video track shown as the same badges, audio, subtitle and version selects without the box
- No "played" circle on watched items
- A play counts as watched by Jellyfin's "Max resume percentage" instead of a separate plugin setting
- Plugin settings page with the Jelly Twiggy logo
- Profile menu entry renamed to "Advanced settings (Jelly Twiggy)"

## 0.1.6.0

- Dashboard: move and resize blocks right on the page, block names match the page, no flicker on load
- New stream card: larger poster on its own colours, user in the corner, full device and address, tech details as tags
- Active streams: new icon, plain list without a title, opens only when something plays
- Rating badge on seasons (from the series), centred star
- Age ratings shown as plain "6+", "18+"
- Plugin settings page in English, German and Russian
- Monochrome icon in the user menu and the admin menu
- Fix: empty "Watched" row, active streams button spacing, transcode reason text

## 0.1.5.0

- Option to hide favorites everywhere: ♥ buttons, Favorites tabs and filters
- App icon
- Dashboard: block widths 1/3, 1/2, 2/3 or full row, no empty gaps
- Active streams button sits next to the user menu and turns yellow while something plays
- Settings: one "Player behavior" section
- Poster badges with depth and colors per resolution and audio format, as in Jellyfin-Enhanced
- Player: no separate volume popup; with the controls hidden, a volume change shows only the volume bar
- Fix: "Watched" row alignment

## 0.1.0.0

- Poster badges: quality, first audio track and rating
- Auto-skip intro and outro (Intro Skipper segments)
- Auto-pause, auto-resume and auto Picture-in-Picture on tab switch
- Long press for 2x speed
- Active streams button
- Dashboard: show, hide and reorder blocks, devices in the active streams style, playback statistics
- "Watched" row with user avatars on item pages
- Own playback log with Jellyfin play count fallback
- Localization (English, German, Russian)
