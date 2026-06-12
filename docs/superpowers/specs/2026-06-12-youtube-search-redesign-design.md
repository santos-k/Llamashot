# YouTube Downloader — Search Screen Redesign — Design

Date: 2026-06-12
Status: Approved

## Goal

Redesign the search-results screen of the YouTube Download tool (`PanelYouTubeDl`
in `FileToolsWindow`) to match the provided mockup: a persistent top search bar,
a left **FILTERS** sidebar (Upload date + Sort by), a richer results grid with
channel/views/age on each card, and a bottom action bar (selected count, estimated
size, **Continue → Review and download**). Continue leads to a dedicated review
screen before downloading.

Scope for this pass: **visual match + working basics**. Filters and sort actually
re-query YouTube; estimated size is a labeled approximation. The generic File Tools
window header is kept (no dedicated header / theme toggle / settings gear). HD badge
and accurate per-video size probing are out of scope.

## Current State

- `Views/FileToolsWindow.xaml` (~2890–3322): `PanelYouTubeDl` with `YtSelectView`
  (centered hero input) and `YtConfigView` (compact top search bar `YtTopSearchBar`,
  header, toolbar with in-results search + selected count + "Checked first" +
  List/Grid toggle, `YtVideoList` row view, `YtVideoGrid` card view, and inline
  `YtDownloadControls`).
- `Views/FileToolsWindow.xaml.cs`:
  - Screen state machine `enum YtScreen { Hero, SearchVideos, SearchPlaylists, Download }`,
    fields `_ytScreen` / `_ytBackTarget`, and `ApplyYtScreen()` which flips visibility.
  - `_ytVideos` (`ObservableCollection<YtVideoItem>`) bound by both list and grid via
    the shared default `CollectionView`. Handlers: `RunYtFetchAsync`, `Yt_FetchTop`,
    `Yt_GoToDownload`, `Yt_OpenPlaylist`, `Yt_BackToResults`, `Yt_SelectAll`,
    `Yt_DeselectAll`, `Yt_CheckedFirst`, `Yt_ListView`/`Yt_GridView`,
    `Yt_SearchChanged`, `ApplyYtViewMode`, `UpdateYtSelectedCount`, `Yt_Download`.
  - `YtVideoItem` (INotifyPropertyChanged): `IsSelected`, `Status`, `Progress`,
    `Title`, `Duration`, `VideoUrl`, `Thumbnail`, `IsPlaylist`, `StatusColor`.
- `Core/FileToolsService.cs`: `FetchYouTubeVideosAsync(inputUrl)` returns
  `List<(string title, string duration, string url, string thumbnail, bool isPlaylist)>`
  via `yt-dlp --flat-playlist --print "title|||duration_string|||webpage_url|||thumbnail|||ie_key"`.
  Video search uses `ytsearch20:{query}`; playlist search uses
  `https://www.youtube.com/results?search_query=…&sp=EgIQAw%3D%3D`.
- `Views/ConfirmDialog.cs`: reusable confirm dialog (already used before download).

## Architecture

The redesign restyles the **`SearchVideos` / `SearchPlaylists`** portion of
`YtConfigView` and repurposes the **`Download`** screen as a **Review-and-download**
screen. Both list/grid still bind to the same `_ytVideos` collection, so selection,
status, and progress persist across view/screen switches with no extra code. Filters
and sort change *which query is run*, not how results are rendered.

## Components

### 1. Top search bar (persistent on search screens)
Restyle `YtTopSearchBar`:
- Search input with a leading magnifier glyph and a trailing **clear (✕)** button
  that empties the input.
- Red **Search** button (existing `Yt_FetchTop`).
- Segmented **[Videos | Playlists]** toggle (existing `RbYtModeVideosTop` /
  `RbYtModePlaylistsTop`, restyled as a segmented control).

### 2. Two-column body: FILTERS sidebar + results
Wrap the results area in a 2-column grid. New left sidebar (`YtFiltersPanel`, ~190px):
- **Upload date** radio group `YtUploadDate` (`GroupName`): Any time (default), Today,
  This week, This month, This year.
- **Sort by** dropdown `CmbYtSort`: Relevance (default), Upload date, View count, Rating.
- **Clear filters** button at the bottom → reset to Any time + Relevance and re-query.

The right results header row gets: results title/count (left); **Select All**
checkbox `ChkYtSelectAll`, a sort dropdown mirroring `CmbYtSort`, and the existing
**grid/list** toggle (right). The two sort dropdowns are bound to one
`_ytSort` value; changing either re-queries. "Checked first" is retained (kept in
the header row or folded next to Select All).

### 3. Filters & sort → re-query via `sp` tokens
`RunYtFetchAsync` builds the video-search target from current filter+sort:
- Default (Any time + Relevance) → keep `ytsearch20:{query}`.
- Otherwise → `https://www.youtube.com/results?search_query={q}&sp={token}` where
  `token` comes from a centralized lookup `YtSearchFilters.BuildSpToken(uploadDate, sort)`
  covering the documented combinations (a `static` table in the code-behind or a small
  helper). Playlists keep their existing `EgIIAw…`-style token.
- **Risk:** `sp` tokens are YouTube-internal and may drift; centralizing them in one
  table makes them trivially patchable. If an unknown combination is requested, fall
  back to the closest known token (sort-only, then plain search).

### 4. Card data (extend the fetch)
Extend the `--print` template to
`"%(title)s|||%(duration_string)s|||%(webpage_url)s|||%(thumbnail)s|||%(ie_key)s|||%(channel)s|||%(view_count)s|||%(upload_date)s"`
and parse the two/three new fields (same single yt-dlp call). Update the return tuple
to include `channel`, `views`, `age`. `FetchYouTubeVideosAsync` and `PopulateYtItems`
extend accordingly.

`YtVideoItem` gains:
- `Channel` (string)
- `Views` (string, formatted e.g. "12M views" from `view_count`; empty if NA)
- `Age` (string, relative from `upload_date` when present; **empty when YouTube does
  not supply it** — flat search frequently omits it, which is acceptable)

`view_count` formatting: ≥1e6 → "{n/1e6:0.#}M views", ≥1e3 → "{n/1e3:0.#}K views",
else "{n} views". `upload_date` (YYYYMMDD) → coarse relative age ("2 years ago",
"3 months ago", "5 days ago"); omit if unparseable.

Card layout (grid): thumbnail with duration badge (top-right) + selection checkbox
(top-left); 2-line title; channel; "{Views} · {Age}" (with the separator/age dropped
when age is empty); per-video status/progress retained. A **`…` overflow menu**
(top-right of the text block or over the thumbnail) with **Open on YouTube** and
**Copy link**. No HD badge. The list (row) view gains channel + views inline but is
otherwise unchanged.

### 5. Bottom action bar
New bar pinned at the bottom of the search screen:
- **Selected — N Videos** (red number), bound to the live selected count
  (`UpdateYtSelectedCount` extended to also format this label).
- **Estimated size — ~X** — sum of `durationMinutes × MB_PER_MIN[defaultQuality]`
  over selected items, formatted MB/GB, prefixed "~". `MB_PER_MIN` heuristic:
  Best/720p ≈ 15, 480p ≈ 8, 360p ≈ 5, MP3 ≈ 1. The search screen uses the 720p
  assumption; the review screen recomputes from the chosen format/quality.
- Red **Continue → Review and download** button, disabled until ≥1 selected;
  navigates to the review screen (replaces today's "Go to download →").

### 6. Review-and-download screen
Repurpose the `Download` `YtScreen` state:
- A compact list of the **selected** videos only (thumbnail + title + duration),
  built from `_ytVideos.Where(IsSelected)`.
- Existing **Video/MP3 + quality (+ Embed thumbnail)** controls (`RbYtVideo`/`RbYtAudio`,
  `CmbYtQuality`, `ChkYtEmbedThumb`) and recomputed estimated size for the chosen format.
- **Back** → return to results (selection preserved). **Download** → existing confirm
  dialog + existing `Yt_Download` mechanics, unchanged.

## Data Flow

1. User types a query, picks Videos/Playlists, sets optional Upload-date/Sort, hits
   Search → `RunYtFetchAsync` builds the target (ytsearch or results+`sp`) →
   `FetchYouTubeVideosAsync` → `PopulateYtItems` fills `_ytVideos` (now with channel/
   views/age) → `ApplyYtScreen` shows the redesigned search screen.
2. Selecting cards updates the bottom bar (count + est. size) live via each item's
   `PropertyChanged`.
3. Continue → review screen (selected subset) → choose format/quality → Download →
   confirm → `Yt_Download`.

## Files Touched

- `Views/FileToolsWindow.xaml` — restructure the search portion of `YtConfigView`
  (top bar, FILTERS sidebar, results header, card template, bottom bar) and add the
  review-screen selected-list; restyle the segmented Videos/Playlists toggle.
- `Views/FileToolsWindow.xaml.cs` — filter/sort state (`_ytUploadDate`, `_ytSort`),
  `sp`-token build + re-query, Continue/Review navigation via the `YtScreen` machine,
  selected-count + estimated-size labels, `…` menu handlers (Open on YouTube / Copy
  link), bind new card fields.
- `Core/FileToolsService.cs` — add `channel` + `view_count` (+ `upload_date`) to the
  fetch print/parse; extend the return tuple.
- `YtVideoItem` — add `Channel`, `Views`, `Age`.

No changes to download mechanics, quality options, `yt-dlp` download invocation, or
`ConfirmDialog`.

## Testing

No unit-test harness exists for this WPF UI; verification is manual:

1. Search "bandar mama" (Videos) → grid matches the mockup: top bar, FILTERS sidebar,
   cards with channel + views, bottom bar with count + est. size.
2. Toggle checkboxes / Select All → "Selected N Videos" and "~size" update live.
3. Set Upload date = This year and Sort = View count → results re-query and change;
   Clear filters resets to Any time + Relevance and re-queries.
4. Switch Grid ⇄ List → selection/status/progress persist; channel + views show in both.
5. `…` menu → Open on YouTube opens the browser; Copy link copies the URL.
6. Continue (≥1 selected) → review screen lists exactly the selected videos; size
   recomputes when toggling Video/MP3 and quality.
7. Download from review → confirm dialog shows the correct type; download proceeds.
8. Playlists tab still works (results + Open playlist → results → review).
9. Age is shown when yt-dlp returns `upload_date`, and cleanly absent when it doesn't.

## Out of Scope

- Dedicated mockup header (Back/centered title/moon theme/gear settings).
- Light/dark theme toggle.
- HD badge; accurate per-video size probing.
- Persisting filter/sort/view-mode preferences across sessions.
