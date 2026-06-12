# YouTube Download Enhancements — Design

Date: 2026-06-12
Status: Approved

## Goal

Enhance the existing YouTube Download tool (`PanelYouTubeDl` in `FileToolsWindow`)
with five features:

1. List/Grid view switch (default Grid).
2. Keyword search to filter videos in a playlist / multi-video result.
3. Download only checked videos, with a live selected-count and a "Checked first"
   reorder action.
4. Preserve selection and download status when switching between List and Grid.
5. Confirmation dialog on Download that confirms the chosen type (Video vs MP3).

## Current State

- `Views/FileToolsWindow.xaml` lines ~2890–3070: `PanelYouTubeDl` with
  `YtSelectView` (URL input) and `YtConfigView` (header + `YtVideoList` ListBox +
  download controls: `RbYtVideo` / `RbYtAudio`, `CmbYtQuality`, `ChkYtEmbedThumb`,
  `BtnYtDownload`, `BtnYtStop`).
- `Views/FileToolsWindow.xaml.cs`: `_ytVideos` (`ObservableCollection<YtVideoItem>`),
  handlers `Yt_Fetch`, `Yt_Download`, `Yt_Stop`, `Yt_SelectAll`, `Yt_DeselectAll`,
  `YtMode_Changed`.
- `YtVideoItem` (INotifyPropertyChanged) already has `IsSelected`, `Status`,
  `Progress`, `Title`, `Duration`, `VideoUrl`, `Thumbnail`, `StatusColor`.
- `Views/ConfirmDialog.cs` exposes a reusable
  `ConfirmDialog.Show(owner, title, message, confirmText, cancelText) -> bool`.

## Architecture

Both views bind to the **same `_ytVideos` collection**. Each `YtVideoItem` carries
its own selection/status/progress as observable state, so switching views only flips
`Visibility` of two controls — it never rebuilds the lists. This makes feature 4
(preserve selection + status) automatic.

## Components

### 1. List/Grid toggle (default Grid)
- Keep `YtVideoList` (row ListBox) as the **List** view.
- Add `YtVideoGrid` — a `ListBox` whose `ItemsPanel` is a `WrapPanel`. Card template:
  thumbnail 160×90 with a checkbox overlaid top-left, a status badge, title (up to 2
  lines, ellipsis), duration, and the per-video progress bar.
- Toolbar segmented toggle (List | Grid). `_ytViewMode` field; toggling swaps
  `Visibility`. Grid is shown by default after Fetch.

### 2. Keyword search (filter, hide non-matching)
- `TxtYtSearch` textbox in the toolbar, visible only when more than one video.
- On `TextChanged`, set a `Filter` predicate on
  `CollectionViewSource.GetDefaultView(_ytVideos)`: keep items whose `Title` contains
  the query (case-insensitive). Both ListBoxes share the default view, so both filter
  together. Hidden items retain `IsSelected`.

### 3. Download-only-checked + count + "Checked first"
- Live **"X of Y selected"** label in the toolbar. Updated whenever any item's
  `IsSelected` changes — subscribe to each item's `PropertyChanged` when added in
  `Yt_Fetch`, and recompute on `Yt_SelectAll` / `Yt_DeselectAll`.
- `Yt_Download` already filters to `IsSelected`; keep the "select at least one" guard.
- **"Checked first"** button stably reorders `_ytVideos` so ticked items move to the
  top (checked items in original order, then unchecked in original order).

### 4. Preserve selection + status across switch
- Inherent to the shared-collection architecture; no extra code. Verified manually.

### 5. Confirmation dialog on Download
- In `Yt_Download`, after the checked>0 guard and before the folder picker:
  build a message — `"Download {n} video(s) as MP3 audio?"` when `RbYtAudio` is
  checked, else `"Download {n} video(s) as Video — {quality}?"` — and call
  `ConfirmDialog.Show(owner, "Confirm Download", message, "Download", "Cancel")`.
  Abort if it returns false; otherwise continue with the existing folder-picker flow.

## Files Touched

- `Views/FileToolsWindow.xaml` — toolbar (search + List/Grid toggle + selected count +
  "Checked first") and the new `YtVideoGrid` card view inside `YtConfigView`.
- `Views/FileToolsWindow.xaml.cs` — view-mode toggle, search filter, selected-count
  tracking, checked-first reorder, confirm-before-download.
- No changes to `Core/FileToolsService.cs` or `Views/ConfirmDialog.cs`.

## Testing

No unit-test harness exists for this WPF UI; verification is manual:

1. Fetch a playlist (multiple videos) and a single video.
2. Switch List ⇄ Grid — selection, status, and progress persist; Grid is default.
3. Type a keyword — non-matching titles hide; hidden items keep their checkbox state.
4. Toggle checkboxes — the "X of Y selected" count updates live.
5. "Checked first" moves ticked videos to the top.
6. Download with 0 checked → blocked with a message.
7. Download in Video and in MP3 mode → confirmation dialog shows the correct type;
   Cancel aborts, Download proceeds to the folder picker.

## Out of Scope

- No changes to download mechanics, quality options, or `yt-dlp` invocation.
- No persistence of view-mode preference across sessions.
