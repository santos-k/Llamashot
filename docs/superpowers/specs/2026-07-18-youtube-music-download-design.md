# YouTube Music download — design

**Date:** 2026-07-18
**Status:** Approved (design), updated after baseline testing, pending implementation plan
**Area:** `FileToolsWindow` / `FileToolsService` — existing `youtube_dl` tool

> **Baseline test results (2026-07-18).** Verified `yt-dlp` + `ffmpeg` behavior before implementing.
> Three findings changed the design; each is folded into the sections below and summarized here:
> 1. Plain `music.youtube.com/search?q=` returns artist/album **browse pages**, not songs. The
>    YouTube Music **Songs filter** (`&sp=EgWKAQIIAWoKEAoQCRAFEAMQBA%3D%3D`) fixes it — clean
>    `music.youtube.com/watch?v=` song entries. (`ytmsearch:` prefix does not exist.)
> 2. Downloads currently hit YouTube's **"confirm you're not a bot"** check — this breaks the
>    **existing** tool too (no `player_client`/cookies workaround present). Fix:
>    `--extractor-args "youtube:player_client=web_embedded"` — the only client tested that both
>    passes the challenge and exposes usable formats. Browser-cookie extraction fails on Edge
>    (DPAPI / app-bound encryption, yt-dlp #10927), so cookies are not a reliable path.
> 3. Installed `yt-dlp` was ~4 months stale. Keeping it current matters (the bot check evolves);
>    a stale binary is a likely support issue.
>
> A full end-to-end download was confirmed working with these fixes: music.youtube.com track →
> M4A (AAC) → embedded PNG album art → title / artist / album / date tags auto-populated.

## Goal

Enhance the existing **YouTube Download** file tool (card id `youtube_dl`) so it is first-class
for downloading **music** as well as video. No new tool card is added — the work extends the tool
already present in `FileToolsWindow`.

Three concrete additions:

1. A **YouTube / Music search-source toggle** so users can search and download from
   `music.youtube.com`.
2. **Audio format choice** — M4A (AAC), OPUS, MP3 — replacing today's MP3-only audio mode.
3. **Always-on music metadata + album-art tagging** for audio downloads.

## Background (what already exists)

The tool is fully built and working today:

- Input is a URL **or** a search term, with upload-date/sort filters (`YtSelectView`).
- **Video mode:** quality combo (Best / 720p / 480p / 360p), merged to mp4.
- **Audio mode:** a radio literally labeled "MP3" → `yt-dlp -x --audio-format mp3 --audio-quality 0`,
  plus an optional "Embed thumbnail" checkbox.
- Single videos, playlists, search results, multi-select batch download.
- Backed by `FileToolsService.DownloadSingleVideoAsync(videoUrl, outputDir, quality, audioOnly, embedThumbnail, progress)`
  and `FetchYouTubeVideosAsync(inputUrl, playlistItems)`, shelling out to `yt-dlp`
  (resolved via `FindYtDlpPath()` → WinGet Links dir, else PATH).

Because `yt-dlp` treats `music.youtube.com` URLs the same as `youtube.com`, pasted YT-Music links
already download. The gap this design fills is **search**, **format choice**, and **music tagging**.

## Design

### 1. Search source toggle

On the search screen (`YtSelectView`), add a small segmented toggle **`YouTube | Music`** beside the
search box.

- **YouTube** (default) — unchanged: `https://www.youtube.com/results?search_query=…` plus the
  existing `sp=` upload-date/sort token.
- **Music** — searches route to the YouTube Music **Songs-filtered** URL:
  `https://music.youtube.com/search?q={query}&sp=EgWKAQIIAWoKEAoQCRAFEAMQBA%3D%3D`; pasted
  `music.youtube.com` links pass straight through. The existing upload-date/sort filters are hidden
  in Music mode (they don't apply to YT-Music search).

**Resolved during testing (was a verification item):** the plain `music.youtube.com/search?q=` URL
returns artist/album browse pages (`ie_key=YoutubeTab`, empty title/url/thumbnail), which
`FetchYouTubeVideosAsync` would misclassify as playlists. Appending the Songs filter `sp=` token
above yields clean single-song entries (`ie_key=Youtube`, `music.youtube.com/watch?v=…`, real
titles) that flow through the existing fetch/download path unchanged. Duration/channel come back `NA`
in flat mode (cosmetic — the list still renders and downloads correctly).

### 2. Audio format (UI)

- Relabel the audio radio button `RbYtAudio` from **"MP3"** to **"Audio"**.
- Add an audio-format combo (`CmbYtAudioFormat`) shown only in audio mode, mirroring how
  `YtVideoOptions` shows `CmbYtQuality` in video mode:
  - **M4A (AAC)** — default (index 0). Native YouTube audio, no re-encode.
  - **OPUS** — native YT-Music codec, smallest files.
  - **MP3** — universal compatibility (re-encoded).
- The old "Embed thumbnail" checkbox (`ChkYtEmbedThumb`) is removed from the audio row; album art is
  now always embedded via the tagging step below.

### 3. Service layer — `DownloadSingleVideoAsync`

Add an `audioFormat` parameter with a default so existing callers are source-compatible:

```
DownloadSingleVideoAsync(string videoUrl, string outputDir, string quality,
    bool audioOnly, bool embedThumbnail, IProgress<…>? progress = null,
    string audioFormat = "mp3")
```

- `DownloadMediaAsync` (calls with `audioOnly` from the Link Downloader) keeps working unchanged via
  the `"mp3"` default.
- In audio mode, build args from the format:
  `-x --audio-format {m4a|opus|mp3} --audio-quality 0`
- **Always** append `--embed-metadata --embed-thumbnail` in audio mode. For `music.youtube.com`
  sources yt-dlp auto-populates artist / album / track tags; regular YouTube audio gets title +
  uploader + thumbnail as cover art. The `embedThumbnail` parameter becomes effectively always-on for
  audio and can be retired from the audio path (kept in the signature for caller compatibility, or
  removed if no caller needs it — decided in the plan).
- Video mode is unchanged.

### 3a. Bot-check fix — `player_client=web_embedded` (bug fix, whole tool)

Testing showed downloads currently fail with YouTube's "confirm you're not a bot" challenge, and the
existing tool passes no workaround — so **downloads are broken today**, independent of the music
feature. The fix is a single shared extractor arg applied to **every** `yt-dlp` invocation that
touches YouTube (both `DownloadSingleVideoAsync` and `FetchYouTubeVideosAsync`):

```
--extractor-args "youtube:player_client=web_embedded"
```

This was the only client (of `tv`, `mweb`, `web_embedded`, `ios`, `android_vr`, `web_safari`,
`default`) that both cleared the challenge and exposed usable formats, with no cookies required.
Implementation should centralize this in one constant/helper (e.g. a `YtDlpClientArgs` string) so
the client can be changed in one place when YouTube shifts again. Cookies-from-browser is **not**
used (Edge/Chromium app-bound encryption breaks extraction).

### 3b. yt-dlp freshness

The installed binary was ~4 months old and still needed the client fix, but stale versions are a
recurring failure source. Low-effort mitigation (final choice made in the plan): surface a clear
"update yt-dlp" hint in the existing "yt-dlp Required"/error dialogs, e.g.
`yt-dlp -U` or `winget upgrade yt-dlp.yt-dlp`. Auto-updating silently is out of scope.

### 4. Wiring & polish

- `YtMode_Changed` toggles visibility of the audio-format combo (audio) vs. the quality combo (video),
  replacing the old thumbnail-checkbox toggle.
- `Yt_Download` reads the selected audio format and passes it through; the confirm dialog reflects it
  ("Download 3 song(s) as M4A?" / "… as OPUS?" / "… as MP3?").
- The YT-Music search URL builder is added alongside the existing `youtube.com/results` builder,
  selected by the toggle state.

## Testing

Add `RunHarness` checks (no network — command-construction assertions, matching existing harness
style):

- For each audio format (M4A / OPUS / MP3), the built `yt-dlp` arg string contains the correct
  `--audio-format` value and `--audio-quality 0`.
- Audio-mode args always include `--embed-metadata` and `--embed-thumbnail`.
- Video-mode args are unchanged from today for each quality (apart from the shared client arg).
- Every YouTube-facing arg string (fetch + download, audio + video) includes
  `--extractor-args "youtube:player_client=web_embedded"`.
- The Music toggle produces the Songs-filtered `music.youtube.com/search?q=…&sp=…` URL and the
  YouTube toggle produces the existing `youtube.com/results` URL.

Plus a manual smoke test (already run once during design, to be repeated after wiring): one real M4A
download from a `music.youtube.com` link, confirming artist / album / cover art land in the file's
tags and the bot check is cleared.

## Version

Bump the version in `Llamashot/Llamashot.csproj` and `AboutWindow.xaml` per the standing rule.

## Non-goals

- No new standalone "YouTube Music" tool card (explicitly chosen: enhance the existing tool).
- No FLAC / WAV audio (lossless containers over a lossy source — omitted by decision).
- No changes to video download behavior.
