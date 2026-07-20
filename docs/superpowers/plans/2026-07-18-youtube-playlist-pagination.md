# YouTube Playlist Track-List + Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the YouTube tool's infinite-scroll with real pagination (First/Prev/Next/Last, page indicator, numeric page-size 1–50 default 12) on both the search-results and playlist track-list screens, and show a friendly message when a pasted playlist is private/unavailable.

**Architecture:** `_ytVideos` stays the full master collection (selection, count, sort, download unchanged). Pagination is a *display* layer: a combined CollectionView predicate `TitleMatches(v) && _ytPageSet.Contains(v)` shows only the current page. Two pure helpers (`ClampPageSize`, `TotalPages`) are unit-tested in RunHarness; the UI logic lives in `FileToolsWindow`.

**Tech Stack:** C# / .NET 10 / WPF; `yt-dlp`; `RunHarness` assertion harness.

---

## Testing note
No unit-test framework; pure logic is tested via the `LLAMASHOT_YTMUSIC` block in `tools/RunHarness/Program.cs` (run with `$env:LLAMASHOT_YTMUSIC="1"; dotnet run --project tools\RunHarness -c Release`). UI wiring is verified by a clean `dotnet build` plus a manual smoke test (final task). **The running app locks `Llamashot.dll`; stop it before building** (`Stop-Process -Name Llamashot -Force -ErrorAction SilentlyContinue`).

## Version-bump note
One bump at the end (Task 4): 8.9.27 → **8.9.28**. Intermediate task commits are not individually bumped.

## File Structure
- `Llamashot/Core/FileToolsService.cs` — **Modify.** Add `ClampPageSize`, `TotalPages`.
- `tools/RunHarness/Program.cs` — **Modify.** Extend the `LLAMASHOT_YTMUSIC` assertion block.
- `Llamashot/Views/FileToolsWindow.xaml` — **Modify.** Replace `YtLoadMoreBar` with a pager row; drop `ScrollViewer.ScrollChanged` from both result grids.
- `Llamashot/Views/FileToolsWindow.xaml.cs` — **Modify.** Pagination state + methods + handlers; reroute filter/sort/select-all; integrate into `RunYtFetchAsync` / `ApplyYtScreen`; remove scroll auto-load; friendly private-playlist message.
- `Llamashot/Llamashot.csproj`, `Llamashot/Views/AboutWindow.xaml` — **Modify.** Version bump (Task 4).

---

### Task 1: Pure pagination helpers + harness tests

**Files:** Modify `Llamashot/Core/FileToolsService.cs`; Test `tools/RunHarness/Program.cs`.

- [ ] **Step 1: Write the failing test.** In `tools/RunHarness/Program.cs`, inside the `LLAMASHOT_YTMUSIC` block, immediately before the line `File.WriteAllText(Path.Combine(Dir, "ytmusic.txt"), sb.ToString());`, insert:

```csharp
            // Pagination helpers
            Assert(FileToolsService.ClampPageSize(0) == 1, "clamp 0 -> 1");
            Assert(FileToolsService.ClampPageSize(12) == 12, "clamp 12 -> 12");
            Assert(FileToolsService.ClampPageSize(50) == 50, "clamp 50 -> 50");
            Assert(FileToolsService.ClampPageSize(99) == 50, "clamp 99 -> 50");
            Assert(FileToolsService.ClampPageSize(-5) == 1, "clamp negative -> 1");
            Assert(FileToolsService.TotalPages(0, 12) == 0, "totalpages 0 items -> 0");
            Assert(FileToolsService.TotalPages(12, 12) == 1, "totalpages exact multiple");
            Assert(FileToolsService.TotalPages(13, 12) == 2, "totalpages remainder");
            Assert(FileToolsService.TotalPages(25, 12) == 3, "totalpages 25/12 -> 3");
            Assert(FileToolsService.TotalPages(10, 0) == 0, "totalpages zero size -> 0");
```

- [ ] **Step 2: Run to verify it fails.**

Run:
```powershell
Stop-Process -Name Llamashot -Force -ErrorAction SilentlyContinue
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
```
Expected: build FAILS — `error CS0117: 'FileToolsService' does not contain a definition for 'ClampPageSize'` (and `TotalPages`).

- [ ] **Step 3: Implement.** In `Llamashot/Core/FileToolsService.cs`, add directly below the `BuildMusicSearchUrl` method:

```csharp
    /// <summary>Clamps a requested page size to the supported range (1–50).</summary>
    public static int ClampPageSize(int n) => Math.Clamp(n, 1, 50);

    /// <summary>Total pages for a bounded item count. 0 when there are no items or an invalid page size.</summary>
    public static int TotalPages(int totalItems, int pageSize)
        => pageSize <= 0 || totalItems <= 0 ? 0 : (totalItems + pageSize - 1) / pageSize;
```

- [ ] **Step 4: Run to verify it passes.**

Run:
```powershell
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
Get-Content "$env:TEMP\run_harness_out\ytmusic.txt"
```
Expected: every line `PASS`, including the 10 new pagination lines.

- [ ] **Step 5: Commit.**

```powershell
git add Llamashot/Core/FileToolsService.cs tools/RunHarness/Program.cs
git commit -m @'
feat(youtube): pagination helpers (ClampPageSize, TotalPages)

Pure, harness-tested helpers for the YouTube tool pager.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 2: Pagination UI + core logic (atomic — must build green)

This task replaces infinite-scroll with a pager everywhere. XAML event names and code-behind handlers are mutually dependent, so they land together.

**Files:** Modify `Llamashot/Views/FileToolsWindow.xaml` and `Llamashot/Views/FileToolsWindow.xaml.cs`.

- [ ] **Step 1: Add pagination state fields.** In `FileToolsWindow.xaml.cs`, in the YouTube state region (right after `private bool _ytMusicMode;`, ~line 196), add:

```csharp
    // Pagination (replaces infinite scroll). _ytVideos stays the full master; the pager
    // windows the *display* via a CollectionView filter. _ytTotalPages is 0 for lazy search.
    private int _ytPage = 1;
    private int _ytPageSize = 12;
    private int _ytTotalPages;
    private readonly HashSet<YtVideoItem> _ytPageSet = new();
```

- [ ] **Step 2: Add the pagination core methods.** In `FileToolsWindow.xaml.cs`, add these methods immediately above `Yt_SelectAllToggle` (~line 6790):

```csharp
    // True when the item passes the download-screen title filter (always true when the box is empty).
    private bool YtTitleMatches(YtVideoItem v)
    {
        string q = TxtYtDownloadFilter?.Text?.Trim() ?? "";
        return q.Length == 0 || v.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // Title-filtered pages already loaded into _ytVideos.
    private int YtLoadedPages() => FileToolsService.TotalPages(_ytVideos.Count(YtTitleMatches), _ytPageSize);

    // Recompute the current page's item set and the bounded total-page count.
    private void RecomputeYtPage()
    {
        var filtered = _ytVideos.Where(YtTitleMatches).ToList();
        bool bounded = _ytScreen == YtScreen.Download;
        _ytTotalPages = bounded ? FileToolsService.TotalPages(filtered.Count, _ytPageSize) : 0;

        int maxPage = bounded ? Math.Max(1, _ytTotalPages) : Math.Max(1, YtLoadedPages());
        _ytPage = Math.Clamp(_ytPage, 1, maxPage);

        _ytPageSet.Clear();
        foreach (var v in filtered.Skip((_ytPage - 1) * _ytPageSize).Take(_ytPageSize))
            _ytPageSet.Add(v);
    }

    // The grid shows only the current page: title filter AND page membership.
    private void ApplyYtPageFilter()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos);
        if (view != null)
            view.Filter = o => o is YtVideoItem v && YtTitleMatches(v) && _ytPageSet.Contains(v);
    }

    // Update the pager bar's indicator text and button enabled-states.
    private void RenderYtPager()
    {
        if (YtPager == null) return;
        bool showPager = _ytScreen == YtScreen.SearchVideos || _ytScreen == YtScreen.SearchPlaylists || _ytScreen == YtScreen.Download;
        YtPager.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;
        if (!showPager) return;

        bool bounded = _ytTotalPages > 0;
        TxtYtPageIndicator.Text = bounded ? $"Page {_ytPage} of {_ytTotalPages}" : $"Page {_ytPage}";

        bool hasPrev = _ytPage > 1;
        bool hasNext = bounded ? _ytPage < _ytTotalPages : (_ytPage < YtLoadedPages() || !_ytNoMore);
        BtnYtFirst.IsEnabled = hasPrev;
        BtnYtPrev.IsEnabled = hasPrev;
        BtnYtNext.IsEnabled = hasNext && !_ytLoadingMore;
        BtnYtLast.IsEnabled = bounded && _ytPage < _ytTotalPages;
        if (TxtYtPageSize != null && !TxtYtPageSize.IsKeyboardFocused) TxtYtPageSize.Text = _ytPageSize.ToString();
    }

    // Recompute page, re-apply the filter, refresh the view, redraw the pager.
    private void RefreshYtPagination()
    {
        RecomputeYtPage();
        ApplyYtPageFilter();
        System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos)?.Refresh();
        RenderYtPager();
    }

    // Lazy search: fetch the next page-sized batch and append to _ytVideos (no auto-advance).
    private async Task YtFetchNextAsync()
    {
        if (_ytLoadingMore || _ytNoMore) return;
        if (!YtOnSearchScreen() || string.IsNullOrWhiteSpace(_ytSearchQuery)) return;
        if (_ytLoadedCount >= YtMaxResults) { _ytNoMore = true; return; }

        _ytLoadingMore = true;
        RenderYtPager(); // disables Next while loading
        try
        {
            int start = _ytLoadedCount + 1;
            int end = _ytLoadedCount + _ytPageSize;
            string target = BuildSearchTarget(_ytSearchQuery, end);
            var more = await FileToolsService.FetchYouTubeVideosAsync(target, $"{start}-{end}");

            var existing = new HashSet<string>(_ytVideos.Select(v => v.VideoUrl), StringComparer.OrdinalIgnoreCase);
            var fresh = more.Where(m => !existing.Contains(m.url)).ToList();
            PopulateYtItems(fresh);

            _ytLoadedCount += more.Count;
            if (more.Count < _ytPageSize || _ytLoadedCount >= YtMaxResults) _ytNoMore = true;
            _ytSearchCache.Clear();
            _ytSearchCache.AddRange(_ytVideos);
        }
        catch { /* keep what we have */ }
        finally { _ytLoadingMore = false; }
    }

    // Pager navigation.
    private void Yt_FirstPage(object sender, RoutedEventArgs e) { _ytPage = 1; RefreshYtPagination(); }

    private void Yt_PrevPage(object sender, RoutedEventArgs e)
    {
        if (_ytPage > 1) { _ytPage--; RefreshYtPagination(); }
    }

    private void Yt_LastPage(object sender, RoutedEventArgs e)
    {
        if (_ytTotalPages > 0) { _ytPage = _ytTotalPages; RefreshYtPagination(); }
    }

    private async void Yt_NextPage(object sender, RoutedEventArgs e)
    {
        // Lazy search: at the last loaded page with more possibly available → fetch the next batch.
        if (_ytTotalPages == 0 && _ytPage >= YtLoadedPages() && !_ytNoMore)
            await YtFetchNextAsync();

        int lastPage = _ytTotalPages > 0 ? _ytTotalPages : YtLoadedPages();
        if (_ytPage < lastPage) { _ytPage++; RefreshYtPagination(); }
        else RenderYtPager();
    }

    // Page-size box changed: clamp to 1–50, reset to page 1, re-window.
    private void Yt_PageSizeChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtYtPageSize == null) return;
        string t = TxtYtPageSize.Text.Trim();
        if (!int.TryParse(t, out int n)) return; // wait for a valid number
        int clamped = FileToolsService.ClampPageSize(n);
        _ytPageSize = clamped;
        _ytPage = 1;
        RefreshYtPagination();
    }

    // Normalize the box to the clamped value when focus leaves (e.g. "99" -> "50", "" -> "12").
    private void Yt_PageSizeLostFocus(object sender, RoutedEventArgs e)
    {
        if (TxtYtPageSize == null) return;
        if (!int.TryParse(TxtYtPageSize.Text.Trim(), out int n)) n = _ytPageSize;
        _ytPageSize = FileToolsService.ClampPageSize(n);
        TxtYtPageSize.Text = _ytPageSize.ToString();
    }
```

- [ ] **Step 3: Re-scope Select-All to the current page.** Replace the whole `Yt_SelectAllToggle` method (~line 6790) with:

```csharp
    // Header "Select page" tri-state checkbox: check/uncheck the current page's (non-playlist) items.
    private void Yt_SelectAllToggle(object sender, RoutedEventArgs e)
    {
        bool check = ChkYtSelectAll.IsChecked == true;
        foreach (var v in _ytPageSet)
            if (!v.IsPlaylist) v.IsSelected = check;
        UpdateYtSelectedCount();
    }
```

- [ ] **Step 4: Route the download title-filter through pagination.** Replace `Yt_DownloadFilterChanged` (~line 6651) with:

```csharp
    // Download page: filter the loaded videos by title (does not re-query YouTube). Re-paginates.
    private void Yt_DownloadFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtYtDownloadFilterPh != null)
            TxtYtDownloadFilterPh.Visibility =
                string.IsNullOrEmpty(TxtYtDownloadFilter.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ytPage = 1;
        RefreshYtPagination();
    }
```

And replace the body of `ResetYtDownloadFilter` (~line 6643) — keep clearing the textbox, but drive the view through pagination:

```csharp
    private void ResetYtDownloadFilter()
    {
        if (TxtYtDownloadFilter != null) TxtYtDownloadFilter.Text = "";
        if (TxtYtDownloadFilterPh != null) TxtYtDownloadFilterPh.Visibility = Visibility.Visible;
        _ytPage = 1;
        RefreshYtPagination();
    }
```

- [ ] **Step 5: Re-paginate after the download-screen sort and the checked-first reorder.** At the END of `ApplyYtDownloadSort` (~line 6707, after the `for` loop that `.Move`s items), add:

```csharp
        _ytPage = 1;
        RefreshYtPagination();
```

At the END of `Yt_CheckedFirst` (~line 6787, after its `for` loop), add:

```csharp
        RefreshYtPagination();
```

- [ ] **Step 6: Use the page size for the initial search fetch.** In `RunYtFetchAsync` (~lines 6026–6034), the search-init builds a `1-{YtBatchSize}` batch. Replace both `YtBatchSize` uses there so the first fetch fills one page:

Change:
```csharp
            target = BuildPlaylistSearchTarget(url);
            batchItems = $"1-{YtBatchSize}";
```
to:
```csharp
            target = BuildPlaylistSearchTarget(url);
            batchItems = $"1-{_ytPageSize}";
```
and change:
```csharp
            target = BuildVideoSearchTarget(url, YtBatchSize);
            batchItems = $"1-{YtBatchSize}";
```
to:
```csharp
            target = BuildVideoSearchTarget(url, _ytPageSize);
            batchItems = $"1-{_ytPageSize}";
```

- [ ] **Step 7: Reset pagination + set fetch bounds on each new fetch.** In `RunYtFetchAsync`, replace the block (~lines 6106–6110):

```csharp
            if (isSearch)
            {
                _ytLoadedCount = videos.Count;
                _ytNoMore = videos.Count < YtBatchSize;
            }
```
with:
```csharp
            _ytPage = 1;
            _ytLoadedCount = videos.Count;
            // Search is lazy (more may exist); a pasted playlist/URL is fully in hand (bounded).
            _ytNoMore = !isSearch || videos.Count < _ytPageSize;
```

The `ResetYtDownloadFilter()` already called near the end of the try block (~line 6138) now performs the first `RefreshYtPagination()`.

- [ ] **Step 8: Re-paginate after "Go to download".** In `Yt_GoToDownload` (~line 6251), after `ResetYtDownloadFilter();` at the end of the method, the reset already refreshes pagination — but page must reset and the screen is now `Download` (bounded). Add right before its final `ResetYtDownloadFilter();` call:

```csharp
        _ytPage = 1;
```

- [ ] **Step 9: Show the pager from `ApplyYtScreen`.** In `ApplyYtScreen` (~line 6205), after the existing visibility assignments and before `UpdateYtGoDownloadEnabled();`, add:

```csharp
        RenderYtPager();
```

- [ ] **Step 10: Remove infinite-scroll.** Delete the `Yt_GridScrollChanged` method (~lines 6919–6926) and the now-unused `YtLoadMoreAsync` method (~lines 6881–6917) entirely. (Its logic moved to `YtFetchNextAsync`.)

- [ ] **Step 11: XAML — drop the scroll handler from both grids.** In `Llamashot/Views/FileToolsWindow.xaml`, remove the attribute `ScrollViewer.ScrollChanged="Yt_GridScrollChanged"` from both elements that carry it (near lines 2769 and 2877). Remove only that attribute; leave the rest of each opening tag intact.

- [ ] **Step 12: XAML — replace the load-more bar with the pager.** In `Llamashot/Views/FileToolsWindow.xaml`, replace the entire `<Border x:Name="YtLoadMoreBar" …> … </Border>` element (~lines 3024–3032) with:

```xml
                <!-- Pagination bar: First / Prev / indicator / Next / Last + page-size input -->
                <Border x:Name="YtPager" Grid.Row="2" Grid.Column="1" Visibility="Collapsed"
                        VerticalAlignment="Bottom" HorizontalAlignment="Stretch" Height="48"
                        Background="{DynamicResource ScrimBrush}" BorderBrush="{DynamicResource BorderSoftBrush}" BorderThickness="0,1,0,0">
                    <Grid Margin="16,0">
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center">
                            <Button x:Name="BtnYtFirst" Content="⏮ First" Click="Yt_FirstPage" Cursor="Hand"
                                    Height="30" Padding="10,0" Margin="0,0,6,0" Template="{StaticResource RoundedButton}"
                                    Background="{DynamicResource SurfaceBrush}" Foreground="{DynamicResource TextPrimaryBrush}"/>
                            <Button x:Name="BtnYtPrev" Content="◀ Prev" Click="Yt_PrevPage" Cursor="Hand"
                                    Height="30" Padding="10,0" Margin="0,0,12,0" Template="{StaticResource RoundedButton}"
                                    Background="{DynamicResource SurfaceBrush}" Foreground="{DynamicResource TextPrimaryBrush}"/>
                            <TextBlock x:Name="TxtYtPageIndicator" Text="Page 1" VerticalAlignment="Center" MinWidth="90"
                                       TextAlignment="Center" Foreground="{DynamicResource TextSecondaryBrush}" FontSize="13"/>
                            <Button x:Name="BtnYtNext" Content="Next ▶" Click="Yt_NextPage" Cursor="Hand"
                                    Height="30" Padding="10,0" Margin="12,0,6,0" Template="{StaticResource RoundedButton}"
                                    Background="{DynamicResource SurfaceBrush}" Foreground="{DynamicResource TextPrimaryBrush}"/>
                            <Button x:Name="BtnYtLast" Content="Last ⏭" Click="Yt_LastPage" Cursor="Hand"
                                    Height="30" Padding="10,0" Template="{StaticResource RoundedButton}"
                                    Background="{DynamicResource SurfaceBrush}" Foreground="{DynamicResource TextPrimaryBrush}"/>
                        </StackPanel>
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
                            <TextBlock Text="Per page" VerticalAlignment="Center" Foreground="{DynamicResource TextMutedBrush}" FontSize="12" Margin="0,0,8,0"/>
                            <TextBox x:Name="TxtYtPageSize" Text="12" Width="52" Height="30" VerticalContentAlignment="Center"
                                     TextAlignment="Center" FontSize="13" MaxLength="2"
                                     TextChanged="Yt_PageSizeChanged" LostFocus="Yt_PageSizeLostFocus"/>
                        </StackPanel>
                    </Grid>
                </Border>
```

- [ ] **Step 13: Verify the whole solution builds.**

Run:
```powershell
Stop-Process -Name Llamashot -Force -ErrorAction SilentlyContinue
dotnet build Llamashot/Llamashot.csproj -c Release 2>&1 | Select-String -Pattern "error CS"
```
Expected: NO output. If a `CS` error names a missing symbol (`YtLoadMoreBar`, `Yt_GridScrollChanged`, `YtLoadMoreAsync`, `YtBatchSize`), fix the corresponding leftover reference. Note: `YtBatchSize` may still be referenced elsewhere — if the build flags it as unused or still used, leave the const in place; only its two search-init uses were replaced. Grep to confirm no code still calls `Yt_GridScrollChanged`, `YtLoadMoreAsync`, or `YtLoadMoreBar`.

- [ ] **Step 14: Commit.**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m @'
feat(youtube): paginate results — First/Prev/Next/Last + page size (replaces infinite scroll)

_ytVideos stays the master; a combined title+page CollectionView filter windows
the display. Page size 1–50 (default 12). Search is lazy (Next fetches, Last
disabled); playlist/download is bounded (Page X of Y). Select-all scoped to the
current page. Removes the scroll auto-loader and load-more bar.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 3: Friendly message for private / unavailable playlists

**Files:** Modify `Llamashot/Views/FileToolsWindow.xaml.cs` (`RunYtFetchAsync`).

- [ ] **Step 1: Detect an empty non-search fetch and message it.** In `RunYtFetchAsync`, inside the `try` block right after `var videos = await FileToolsService.FetchYouTubeVideosAsync(target, batchItems);` (~line 6081), add:

```csharp
            // A pasted playlist/album URL that yields nothing is almost always private, deleted,
            // or region-locked (yt-dlp returns HTTP 404 for private YouTube Music playlists).
            if (!isSearch && videos.Count == 0)
            {
                if (fromHero)
                {
                    YtSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    YtSpinner.Visibility = Visibility.Collapsed;
                }
                else { ProcessingOverlay.Visibility = Visibility.Collapsed; ProcessingOverlay.Opacity = 1; }
                ConfirmDialog.Alert(this, "Playlist Unavailable",
                    "This playlist is private or unavailable.\n\nOnly public playlists and albums can be downloaded.");
                return;
            }
```

- [ ] **Step 2: Also convert the fetch exception (404) into the same friendly text for playlist URLs.** In the `catch (Exception ex)` block of `RunYtFetchAsync` (~line 6141), replace:

```csharp
            TxtYtTitle.Text = "Failed to fetch";
            TxtYtDetail.Text = ex.Message;
```
with:
```csharp
            TxtYtTitle.Text = "Failed to fetch";
            TxtYtDetail.Text = !isSearch
                ? "This playlist is private or unavailable. Only public playlists and albums can be downloaded."
                : ex.Message;
```

- [ ] **Step 3: Verify build.**

Run:
```powershell
Stop-Process -Name Llamashot -Force -ErrorAction SilentlyContinue
dotnet build Llamashot/Llamashot.csproj -c Release 2>&1 | Select-String -Pattern "error CS"
```
Expected: no output.

- [ ] **Step 4: Commit.**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m @'
feat(youtube): friendly message for private/unavailable playlists

A pasted playlist that returns no entries (or 404s) now shows a clear
"private or unavailable — only public playlists" message instead of a raw error.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 4: Version bump, build, harness, manual smoke

**Files:** Modify `Llamashot/Llamashot.csproj`, `Llamashot/Views/AboutWindow.xaml`.

- [ ] **Step 1: Bump version** 8.9.27 → 8.9.28.

In `Llamashot/Llamashot.csproj` (lines 19–21):
```xml
    <Version>8.9.28</Version>
    <AssemblyVersion>8.9.28</AssemblyVersion>
    <FileVersion>8.9.28</FileVersion>
```
In `Llamashot/Views/AboutWindow.xaml` (~line 26):
```xml
                <TextBlock Text="Version 8.9.28" Foreground="{DynamicResource TextMutedBrush}"
```

- [ ] **Step 2: Full build + harness.**

Run:
```powershell
Stop-Process -Name Llamashot -Force -ErrorAction SilentlyContinue
dotnet build Llamashot/Llamashot.csproj -c Release 2>&1 | Select-String -Pattern "error CS"
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
Get-Content "$env:TEMP\run_harness_out\ytmusic.txt"
```
Expected: no build errors; every `ytmusic.txt` line `PASS`.

- [ ] **Step 3: Manual smoke.** Launch `Llamashot\bin\Release\net10.0-windows10.0.19041.0\win-x64\Llamashot.exe`, open **File Tools → YouTube Downloader**, and verify:
  1. **Search** a term → results show 12 → `Next` advances (fetches more), `Prev`/`First` go back, `Last` is disabled, indicator reads `Page N`.
  2. Change **Per page** to 30 → grid re-windows to 30/page; typing 99 then leaving the box normalizes to 50.
  3. Paste a **public** playlist/album URL (e.g. `https://www.youtube.com/playlist?list=PLbpi6ZahtOH6Blw3RGYpWkSByi_T7Rygb`) → tracks list with `Page X of Y`, `Last` enabled; select tracks on page 1, go to page 2, select more, download the full selection (audio M4A).
  4. Paste a **private** playlist URL → the "private or unavailable" message appears (no raw error).

Record pass/fail per item in the commit body.

- [ ] **Step 4: Commit.**

```powershell
git add Llamashot/Llamashot.csproj Llamashot/Views/AboutWindow.xaml
git commit -m @'
chore: bump version to 8.9.28 — YouTube pagination + playlist track list

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

## Self-review (completed)

**Spec coverage:**
- Part A (public playlist track list, already works) → verified existing; graceful private handling → Task 3. ✓
- Part B pagination (First/Prev/Next/Last, indicator, page size 1–50 default 12, replace scroll, both screens) → Task 2. ✓
- Selection persists across pages (master `_ytVideos` retained) → Task 2 design. ✓
- Search lazy / Last disabled; playlist bounded / Page X of Y → Task 2 (`RecomputeYtPage`/`RenderYtPager`). ✓
- Select-all = current page → Task 2 Step 3. ✓
- Filter/sort compose with paging → Task 2 Steps 4–5. ✓
- Pure helpers + tests → Task 1. ✓
- Version bump → Task 4. ✓

**Placeholder scan:** none — every step has concrete code/commands.

**Type/name consistency:** `_ytPage`, `_ytPageSize`, `_ytTotalPages`, `_ytPageSet`, `YtTitleMatches`, `YtLoadedPages`, `RecomputeYtPage`, `ApplyYtPageFilter`, `RenderYtPager`, `RefreshYtPagination`, `YtFetchNextAsync`, and handlers `Yt_FirstPage/PrevPage/NextPage/LastPage/PageSizeChanged/PageSizeLostFocus` are used identically across XAML and code-behind. Pager control names `YtPager`, `BtnYtFirst/Prev/Next/Last`, `TxtYtPageIndicator`, `TxtYtPageSize` match between Step 12 (XAML) and Step 2 (code-behind). `ClampPageSize`/`TotalPages` match Task 1. ✓
