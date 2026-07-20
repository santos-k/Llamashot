# YouTube playlist track-list + pagination — design

**Date:** 2026-07-18
**Status:** Approved (design), pending implementation plan
**Area:** `FileToolsWindow` / `FileToolsService` — existing `youtube_dl` tool
**Follows:** `2026-07-18-youtube-music-download-design.md` (same feature branch `feature/youtube-music-download`)

## Goal

Two enhancements to the YouTube Download tool:

1. **Public playlist/album URL → selectable track list.** Pasting a public YouTube Music playlist,
   album, or regular YouTube playlist URL lists its tracks so the user can multi-select and download
   them (audio or video). This already works today for public playlists; the additions are graceful
   handling of private/unavailable playlists and pagination of the track list.
2. **Real pagination**, replacing the current infinite-scroll on *both* the search-results screen and
   the playlist track-list screen: First / Prev / Next / Last, a page indicator, and a numeric
   page-size input (default 12, customizable up to 50).

## Decisions (from brainstorming)

- **Public playlists only.** No sign-in/cookies. Private personal playlists (which yt-dlp cannot read
  without the user's browser cookies) are out of scope; they surface a clear "private/unavailable"
  message. Cookie-based auth may be a separate future feature.
- **Replace infinite scroll everywhere.** Both result lists use the paged controls.
- **Numeric page-size input**, default 12, min 1, max 50 (clamped).
- **Search total is unknown** → searches show `Page X` with `Last` disabled; playlists (known track
  count) show `Page X of Y` with `Last` enabled.
- **"Select all" acts on the current page** (predictable with paging), labeled to make that clear.

## Background — current behavior

- **Pasted playlist URL** (`RunYtFetchAsync`, `isSearch == false`): one `FetchYouTubeVideosAsync`
  call fetches *all* entries, populates `_ytVideos`, and goes to the `Download` screen with unchecked
  items for multi-select. Works for public playlists today; no pagination.
- **Keyword search** (`isSearch == true`): stays on the `SearchVideos`/`SearchPlaylists` screen;
  `YtLoadMoreAsync` + `Yt_GridScrollChanged` load 12-item batches via `--playlist-items start-end`
  (infinite scroll), capped at `YtMaxResults = 120`.
- Selection/count/download all read the bound collection `_ytVideos` directly
  (`Yt_SelectAllToggle`, `UpdateYtSelectedCount`, `Yt_Download`).
- `FetchYouTubeVideosAsync(target, playlistItems)` already accepts a `"start-end"` range and (from the
  prior feature) always passes the `player_client=web_embedded` extractor arg. Verified: that arg does
  **not** break playlist (`youtube:tab`) extraction — public playlists list correctly with it.

## Design

### Part A — Public playlist/album track list

Largely existing. Changes:

- **Graceful failure:** when a playlist fetch throws or returns zero entries (private, deleted, or
  region-locked — e.g. a personal `music.youtube.com/playlist?list=…` returns HTTP 404), show a
  friendly message in the results header instead of the raw exception text:
  *"This playlist is private or unavailable. Only public playlists and albums can be downloaded."*
- The track list is paginated per Part B (playlist source → known total → full controls).

### Part B — Pagination model

**Keep `_ytVideos` as the full master; window the *display* via the CollectionView filter.** The bound
collection `_ytVideos` continues to hold *every* fetched item — so selection, the selected-count, size
estimate, download, and the download-screen sort keep operating on it unchanged (selection therefore
persists across pages for free). Pagination is layered on as a **display filter**: only the items on
the current page pass, so only they render.

Concretely, a single combined predicate drives the grid's `CollectionView.Filter`:

```
view.Filter = o => o is YtVideoItem v && TitleMatches(v) && _ytPageSet.Contains(v);
```

- `TitleMatches(v)` — the existing download-screen title filter (always true when the box is empty).
- `_ytPageSet` — a `HashSet<YtVideoItem>` of the items on the current page, recomputed by
  `RecomputeYtPage()`: take `_ytVideos.Where(TitleMatches)` in physical (sorted) order, then
  `.Skip((_ytPage - 1) * _ytPageSize).Take(_ytPageSize)`.

This means the title filter and the download-screen sort (which physically reorders `_ytVideos` via
`.Move`) both compose correctly with paging: the page window is taken over the title-filtered, sorted
sequence.

**State** (added; the infinite-scroll auto-load is removed but the fetch bookkeeping fields
`_ytLoadedCount` / `_ytNoMore` / `_ytLoadingMore` are retained and reused for the `Next` fetch):
- `int _ytPage` — current 1-based page (reset to 1 on new fetch, title-filter change, sort change,
  and page-size change).
- `int _ytPageSize` — items per page (default 12; clamped 1–50).
- `int _ytTotalPages` — total pages for a **bounded** source; `0` when unknown (lazy search).
- `HashSet<YtVideoItem> _ytPageSet` — the current page's items.

**Bounded vs. lazy source:** the **Download** screen always holds every item in hand (a pasted playlist
fetches all entries at once; "Go to download" carries the full selection), so it is *bounded* —
`_ytTotalPages = TotalPages(filteredCount, _ytPageSize)`, and `Last` is enabled. The **search** screens
(`SearchVideos` / `SearchPlaylists`) are *lazy* — `_ytTotalPages = 0`, `Last` disabled.

**Fetching:**
- **Playlist/album URL:** fetch all entries once (as today) into `_ytVideos`; `RecomputeYtPage()` shows
  page 1. Bounded → full First/Prev/Next/Last.
- **Keyword search:** lazy. Items already fetched stay in `_ytVideos`. `First`/`Prev` and page-size
  changes just re-window (no fetch). `Next` past the last fetched page fetches the next
  `--playlist-items {loaded+1}-{loaded+_ytPageSize}` range (the retained `YtLoadMoreAsync` logic,
  invoked by the button instead of the scroll handler) and appends, then advances the page. `Next` is
  disabled once a fetch returns fewer than `_ytPageSize` items (`_ytNoMore`) and the last page is
  reached.

**Pagination bar** (a new control row under the results grid; the old `YtLoadMoreBar` and
`Yt_GridScrollChanged` scroll handler are removed):

```
[⏮ First] [◀ Prev]    Page X of Y   (or "Page X" for search)    [Next ▶] [Last ⏭]     Page size [ 12 ]
```

- `First`/`Prev` enabled when `_ytPage > 1`.
- `Next` enabled when a next page exists (bounded: `_ytPage < _ytTotalPages`; search: last fetch was
  a full page).
- `Last` enabled only when `_ytTotalPages > 0` (playlist); disabled for search.
- **Page-size input:** a numeric `TextBox` (or numeric up/down), default 12. On change: clamp to
  1–50 via `ClampPageSize`, reset to page 1, recompute `_ytTotalPages` (if bounded), re-window. No
  refetch for playlists; for search, if page 1 needs more than is cached it fetches to fill.

The bar is shared by the `SearchVideos` and `Download` (playlist) screens, driven by the same state.

### Architecture / components

**Pure helpers on `FileToolsService`** (UI-independent, unit-tested in RunHarness):
- `public static int ClampPageSize(int n)` → `Math.Clamp(n, 1, 50)`.
- `public static int TotalPages(int totalItems, int pageSize)` → `pageSize <= 0 ? 0 : (totalItems + pageSize - 1) / pageSize` (0 when `totalItems == 0`).

Windowing itself is inline `Where(TitleMatches).Skip((page-1)*size).Take(size)` in `RecomputeYtPage()`
— no separate bounds helper is needed. The playlist total needs no separate count call: a pasted
playlist URL already fetches every entry in one `FetchYouTubeVideosAsync` call, so the bounded total
is `_ytVideos.Where(TitleMatches).Count()`.

**UI (`FileToolsWindow`):**
- XAML: a `YtPager` control row (First/Prev/Next/Last buttons + `TxtYtPageIndicator` + a
  `TxtYtPageSize` numeric input), replacing `YtLoadMoreBar`. Shown on the search and download screens.
- A single `ApplyYtPageFilter()` sets the combined title+page predicate on the `_ytVideos` default
  view (replacing the two existing filter-setting spots in `Yt_DownloadFilterChanged` /
  `ResetYtDownloadFilter`, which now route through it).
- `RecomputeYtPage()` rebuilds `_ytPageSet` + `_ytTotalPages`; `RenderYtPager()` updates the indicator
  text and button `IsEnabled`.
- Handlers `Yt_FirstPage` / `Yt_PrevPage` / `Yt_NextPage` / `Yt_LastPage` / `Yt_PageSizeChanged`; the
  scroll handler `Yt_GridScrollChanged` is removed (both grids drop `ScrollViewer.ScrollChanged`).
- `Yt_SelectAllToggle` re-scoped to the current page: iterate `_ytPageSet` instead of all `_ytVideos`
  (label clarified to "Select page"); the selected-count and download continue to read `_ytVideos`.

### Error handling
- Playlist fetch failure / empty → friendly "private or unavailable" header, empty grid, pager hidden.
- Page-size non-numeric or out of range → clamp (blank/invalid falls back to 12).
- Search `Next` fetch failure → keep current page; leave `Next` enabled to retry.

### Testing
- **RunHarness** (`LLAMASHOT_YTMUSIC` block, extended): assert `ClampPageSize` (0→1, 12→12, 99→50),
  `TotalPages` (0/exact-multiple/remainder), and `PageBounds` (page 1, middle page, last partial page,
  out-of-range page). No network.
- **Manual smoke:** paste a public playlist URL → tracks paginate 12/page → change page size to 30 →
  re-windows, selection preserved → select tracks across two pages → download the full selection.
  Separately, a keyword search paginates with `Next`/`Prev` and `Last` disabled.

## Version
Bump `Llamashot.csproj` + `AboutWindow.xaml` (8.9.27 → 8.9.28).

## Non-goals
- No cookie/sign-in auth; private playlists and personal library stay out of scope.
- No change to the download pipeline, audio-format handling, or the YouTube/Music source toggle from
  the prior feature.
- No "select all across every page" — select-all is per current page.
