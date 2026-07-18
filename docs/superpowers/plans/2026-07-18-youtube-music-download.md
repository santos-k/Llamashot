# YouTube Music Download Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance the existing `youtube_dl` file tool so it downloads music as well as video — adding a YouTube/Music search toggle, audio-format choice (M4A/OPUS/MP3), always-on music metadata + album-art tagging, and a fix for YouTube's bot-check that currently breaks all downloads.

**Architecture:** The yt-dlp argument strings and the YouTube Music search URL are extracted into pure, testable `public static` methods on `FileToolsService` (Core). `DownloadSingleVideoAsync` and `FetchYouTubeVideosAsync` are refactored to use them and to include a shared `player_client=web_embedded` extractor arg. The `FileToolsWindow` UI gains an audio-format combo, a relabeled "Audio" mode, and a YouTube/Music source toggle that routes searches through the new music-URL builder.

**Tech Stack:** C# / .NET 10 / WPF; `yt-dlp` + `ffmpeg` (shelled out); `RunHarness` (the project's WPF-hosted assertion harness) as the test mechanism.

---

## Testing note

This project has **no unit-test framework**. Its automated tests live in `tools/RunHarness/Program.cs` as env-var-gated blocks that call into `Llamashot.Core` and assert, writing results to `%TEMP%\run_harness_out\`. Pure-logic tasks (1, 3) are done TDD-style against that harness. UI-wiring tasks (4, 5, 6) are verified by successful compilation plus the manual smoke test in Task 7 — WPF event handlers are not unit-testable here without a heavy window harness.

**Harness run command (PowerShell), used throughout:**
```powershell
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
```
Result file: `%TEMP%\run_harness_out\ytmusic.txt` (each line prefixed `PASS`/`FAIL`). Any `FAIL` also throws, which lands in `%TEMP%\run_harness_out\err.txt`.

## Version-bump note

The standing rule is "bump version on every commit." This feature is delivered as one logical unit across several TDD commits; bumping on all of them would burn ~7 versions. **Interpretation used here:** intermediate commits are sub-steps of one feature and are *not* individually version-bumped; the single bump 8.9.26 → **8.9.27** happens in the final task (Task 7). If you prefer a bump per commit, adjust as you go.

## File Structure

- `Llamashot/Core/FileToolsService.cs` — **Modify.** Add `YtDlpClientArgs` const, `YtMusicSongsFilter` const, `BuildAudioDownloadArgs`, `BuildVideoDownloadArgs`, `BuildMusicSearchUrl` (all `public static`). Refactor `DownloadSingleVideoAsync` (new `audioFormat` param, drop `embedThumbnail`) and `FetchYouTubeVideosAsync` (inject client args). Update `DownloadMediaAsync`.
- `Llamashot/Views/FileToolsWindow.xaml` — **Modify.** Relabel audio radio to "Audio", replace the embed-thumbnail checkbox with an audio-format combo, add the YouTube/Music source toggle on the hero.
- `Llamashot/Views/FileToolsWindow.xaml.cs` — **Modify.** `_ytMusicMode` state, `YtSource_Changed` handler, music-aware `BuildVideoSearchTarget`, updated `YtMode_Changed` and `Yt_Download`, updated yt-dlp error dialog.
- `tools/RunHarness/Program.cs` — **Modify.** Add the `LLAMASHOT_YTMUSIC` assertion block.
- `Llamashot/Llamashot.csproj`, `Llamashot/Views/AboutWindow.xaml` — **Modify.** Version bump (Task 7).

---

### Task 1: Service — shared client arg + audio/video arg builders (bot-check fix)

**Files:**
- Modify: `Llamashot/Core/FileToolsService.cs` (add methods near the existing yt-dlp helpers, ~line 2069)
- Test: `tools/RunHarness/Program.cs` (new env block in `Run()`, after the `LLAMASHOT_VIDEDIT` block ~line 84)

- [ ] **Step 1: Write the failing test** — add this block inside `Run()` in `tools/RunHarness/Program.cs`, immediately after the `LLAMASHOT_VIDEDIT` block:

```csharp
        // yt-dlp argument-construction assertions (no network). Verifies the audio/video arg
        // builders, the shared bot-check client arg, and (Task 3) the YouTube Music search URL.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_YTMUSIC") == "1")
        {
            var sb = new System.Text.StringBuilder();
            int fails = 0;
            void Assert(bool cond, string label)
            {
                sb.AppendLine((cond ? "PASS " : "FAIL ") + label);
                if (!cond) fails++;
            }

            string audioM4a = FileToolsService.BuildAudioDownloadArgs(@"C:\out", "m4a", "https://music.youtube.com/watch?v=X");
            Assert(audioM4a.Contains("--audio-format m4a"), "audio m4a format");
            Assert(audioM4a.Contains("--audio-quality 0"), "audio quality 0");
            Assert(audioM4a.Contains("--embed-metadata"), "audio embed-metadata");
            Assert(audioM4a.Contains("--embed-thumbnail"), "audio embed-thumbnail");
            Assert(audioM4a.Contains("player_client=web_embedded"), "audio client arg");

            string audioOpus = FileToolsService.BuildAudioDownloadArgs(@"C:\out", "opus", "u");
            Assert(audioOpus.Contains("--audio-format opus"), "audio opus format");
            string audioMp3 = FileToolsService.BuildAudioDownloadArgs(@"C:\out", "mp3", "u");
            Assert(audioMp3.Contains("--audio-format mp3"), "audio mp3 format");
            string audioBad = FileToolsService.BuildAudioDownloadArgs(@"C:\out", "flac", "u");
            Assert(audioBad.Contains("--audio-format mp3"), "audio unknown->mp3 fallback");

            string vidBest = FileToolsService.BuildVideoDownloadArgs(@"C:\out", "best", "u");
            Assert(vidBest.Contains("--merge-output-format mp4"), "video mp4 merge");
            Assert(vidBest.Contains("player_client=web_embedded"), "video client arg");
            Assert(!vidBest.Contains("--audio-format"), "video has no audio-format");
            string vid720 = FileToolsService.BuildVideoDownloadArgs(@"C:\out", "720p", "u");
            Assert(vid720.Contains("height<=720"), "video 720p height cap");

            // (Task 3 appends YouTube Music URL assertions here.)

            File.WriteAllText(Path.Combine(Dir, "ytmusic.txt"), sb.ToString());
            if (fails > 0) throw new Exception($"YTMusic arg tests: {fails} failure(s)\n{sb}");
            return;
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```powershell
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
```
Expected: **build FAILS** — `error CS0117: 'FileToolsService' does not contain a definition for 'BuildAudioDownloadArgs'` (and `BuildVideoDownloadArgs`). A compile failure is the "red" state here.

- [ ] **Step 3: Write minimal implementation** — add to `Llamashot/Core/FileToolsService.cs` just above `IsYtDlpAvailable()` (~line 2069):

```csharp
    /// <summary>
    /// Shared yt-dlp extractor args for every YouTube call (fetch + download). YouTube gates
    /// downloads behind a "confirm you're not a bot" challenge; the web_embedded player client is
    /// the one client that clears it without cookies. If YouTube shifts again, change it here only.
    /// </summary>
    public const string YtDlpClientArgs = "--extractor-args \"youtube:player_client=web_embedded\"";

    /// <summary>Builds the yt-dlp argument string for an audio-only ("music") download:
    /// extracts audio to the chosen format at best quality and always embeds metadata + album art.</summary>
    public static string BuildAudioDownloadArgs(string outputDir, string audioFormat, string videoUrl)
    {
        string fmt = audioFormat.ToLowerInvariant() switch
        {
            "m4a" => "m4a",
            "opus" => "opus",
            _ => "mp3",
        };
        string outTemplate = Path.Combine(outputDir, "%(title)s.%(ext)s");
        return $"{YtDlpClientArgs} -x --audio-format {fmt} --audio-quality 0 " +
               $"--embed-metadata --embed-thumbnail " +
               $"-o \"{outTemplate}\" --newline \"{videoUrl}\"";
    }

    /// <summary>Builds the yt-dlp argument string for a video download at the given quality, merged to mp4.</summary>
    public static string BuildVideoDownloadArgs(string outputDir, string quality, string videoUrl)
    {
        string formatArg = quality.ToLowerInvariant() switch
        {
            "720p" => "-f \"bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/best[height<=720]/best\"",
            "480p" => "-f \"bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/best[height<=480]/best\"",
            "360p" => "-f \"bestvideo[height<=360][ext=mp4]+bestaudio[ext=m4a]/best[height<=360]/best\"",
            _ => "-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\"",
        };
        string outTemplate = Path.Combine(outputDir, "%(title)s.%(ext)s");
        return $"{YtDlpClientArgs} {formatArg} --merge-output-format mp4 " +
               $"-o \"{outTemplate}\" --newline \"{videoUrl}\"";
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```powershell
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
```
Then:
```powershell
Get-Content "$env:TEMP\run_harness_out\ytmusic.txt"
```
Expected: every line starts `PASS`; no `err.txt` thrown.

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Core/FileToolsService.cs tools/RunHarness/Program.cs
git commit -m @'
feat(youtube): testable yt-dlp arg builders + bot-check client fix

Add BuildAudioDownloadArgs / BuildVideoDownloadArgs and a shared
player_client=web_embedded extractor arg that clears YouTube's bot check.
Covered by a new LLAMASHOT_YTMUSIC harness block.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 2: Service — use the builders in `DownloadSingleVideoAsync` + inject client arg into `FetchYouTubeVideosAsync`

**Files:**
- Modify: `Llamashot/Core/FileToolsService.cs:2209-2232` (`DownloadSingleVideoAsync` signature + args block), `:2361-2363` (`DownloadMediaAsync`), `:2106-2107` and `:2147-2148` (the two `FetchYouTubeVideosAsync` `ProcessStartInfo` arg strings)

- [ ] **Step 1: Change the signature and args block of `DownloadSingleVideoAsync`.** Replace the current signature line and the entire `string args; if (audioOnly) {…} else {…}` block (lines 2209–2232) with:

```csharp
    public static async Task<string?> DownloadSingleVideoAsync(string videoUrl, string outputDir, string quality, bool audioOnly, string audioFormat = "mp3", IProgress<(int percent, string status)>? progress = null)
    {
        Directory.CreateDirectory(outputDir);
        string? outputFile = null;

        await Task.Run(() =>
        {
            string args = audioOnly
                ? BuildAudioDownloadArgs(outputDir, audioFormat, videoUrl)
                : BuildVideoDownloadArgs(outputDir, quality, videoUrl);
```

Leave everything from `var psi = new ProcessStartInfo(FindYtDlpPath(), args)` (old line 2234) onward unchanged.

- [ ] **Step 2: Update `DownloadMediaAsync`** (was line 2361-2363) to match the new signature:

```csharp
    public static Task<string?> DownloadMediaAsync(string url, string outputDir, bool audioOnly,
        IProgress<(int percent, string status)>? progress = null)
        => DownloadSingleVideoAsync(url, outputDir, "best", audioOnly, "mp3", progress);
```

- [ ] **Step 3: Inject the client arg into both `FetchYouTubeVideosAsync` process calls.**

At line ~2106, change:
```csharp
            var psi = new ProcessStartInfo(ytdlp,
                $"--flat-playlist {itemsArg}--print \"{printFmt}\" --no-warnings \"{inputUrl}\"")
```
to:
```csharp
            var psi = new ProcessStartInfo(ytdlp,
                $"{YtDlpClientArgs} --flat-playlist {itemsArg}--print \"{printFmt}\" --no-warnings \"{inputUrl}\"")
```

At line ~2147, change:
```csharp
                var psi2 = new ProcessStartInfo(ytdlp,
                    $"--print \"{printFmt}\" --no-download --no-warnings \"{inputUrl}\"")
```
to:
```csharp
                var psi2 = new ProcessStartInfo(ytdlp,
                    $"{YtDlpClientArgs} --print \"{printFmt}\" --no-download --no-warnings \"{inputUrl}\"")
```

- [ ] **Step 4: Verify it compiles** (the call site in `FileToolsWindow.Yt_Download` still uses the old 6-arg form and will now break — that is expected and fixed in Task 4; to keep this task self-checking, verify only the Core project builds):

Run:
```powershell
dotnet build Llamashot/Llamashot.csproj -c Release 2>&1 | Select-String -Pattern "error CS" -Context 0,0
```
Expected: exactly one error, at `FileToolsWindow.xaml.cs` in `Yt_Download`, `DownloadSingleVideoAsync … cannot convert / no overload`. No errors inside `FileToolsService.cs`. (If Core itself errors, fix before moving on.)

- [ ] **Step 5: Commit** (the tree is momentarily not-building at the UI call site; that is acceptable for this intermediate commit and is resolved in Task 4):

```powershell
git add Llamashot/Core/FileToolsService.cs
git commit -m @'
refactor(youtube): route downloads through arg builders; add audioFormat param

DownloadSingleVideoAsync gains audioFormat and drops the now-unused
embedThumbnail flag (album art is always embedded). FetchYouTubeVideosAsync
now passes the shared client arg. UI call site updated in a later task.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 3: Service — YouTube Music search URL (Songs filter)

**Files:**
- Modify: `Llamashot/Core/FileToolsService.cs` (add near `YtDlpClientArgs`)
- Test: `tools/RunHarness/Program.cs` (extend the `LLAMASHOT_YTMUSIC` block)

- [ ] **Step 1: Write the failing test** — in `tools/RunHarness/Program.cs`, replace the placeholder line `// (Task 3 appends YouTube Music URL assertions here.)` with:

```csharp
            string musicUrl = FileToolsService.BuildMusicSearchUrl("lofi beats");
            Assert(musicUrl.StartsWith("https://music.youtube.com/search?q="), "music url base");
            Assert(musicUrl.Contains("lofi%20beats"), "music url query encoded");
            Assert(musicUrl.Contains("sp=EgWKAQIIAWoKEAoQCRAFEAMQBA%3D%3D"), "music songs filter token");
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```powershell
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
```
Expected: **build FAILS** — `error CS0117: 'FileToolsService' does not contain a definition for 'BuildMusicSearchUrl'`.

- [ ] **Step 3: Write minimal implementation** — add to `Llamashot/Core/FileToolsService.cs` directly below the `YtDlpClientArgs` constant:

```csharp
    /// <summary>YouTube Music "Songs" search-filter token. Without it, a music.youtube.com search
    /// returns artist/album browse pages (ie_key=YoutubeTab, no titles); with it, yt-dlp returns
    /// single-song entries (ie_key=Youtube, music.youtube.com/watch?v=…) the fetch path handles.</summary>
    public const string YtMusicSongsFilter = "EgWKAQIIAWoKEAoQCRAFEAMQBA%3D%3D";

    /// <summary>Builds a YouTube Music songs-search URL for a keyword query.</summary>
    public static string BuildMusicSearchUrl(string query)
        => $"https://music.youtube.com/search?q={Uri.EscapeDataString(query)}&sp={YtMusicSongsFilter}";
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```powershell
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
Get-Content "$env:TEMP\run_harness_out\ytmusic.txt"
```
Expected: all lines `PASS` (including the three new `music …` lines).

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Core/FileToolsService.cs tools/RunHarness/Program.cs
git commit -m @'
feat(youtube): YouTube Music songs-search URL builder

BuildMusicSearchUrl appends the YT-Music Songs filter so searches return
songs, not artist/album browse pages. Covered by the harness block.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 4: UI — audio-format combo, "Audio" relabel, and download wiring

**Files:**
- Modify: `Llamashot/Views/FileToolsWindow.xaml:3067-3088` (mode radios + options row)
- Modify: `Llamashot/Views/FileToolsWindow.xaml.cs:6402-6409` (`YtMode_Changed`), `:6411-6475` (`Yt_Download`)

- [ ] **Step 1: XAML — relabel the audio radio and replace the thumbnail checkbox with a format combo.** In `FileToolsWindow.xaml`:

Change the `RbYtAudio` radio (line ~3071) `Content="MP3"` to `Content="Audio"`:
```xml
                        <RadioButton x:Name="RbYtAudio" Content="Audio" Foreground="{DynamicResource TextSecondaryBrush}" FontSize="12"
                                     GroupName="YtMode" Cursor="Hand" Checked="YtMode_Changed"/>
```

Replace the entire `<CheckBox x:Name="ChkYtEmbedThumb" … />` element (lines ~3085-3087) with:
```xml
                    <StackPanel x:Name="YtAudioOptions" Grid.Column="1" Orientation="Horizontal" Margin="16,0,0,0" Visibility="Collapsed">
                        <TextBlock Text="Format" Foreground="{DynamicResource TextMutedBrush}" FontSize="11" VerticalAlignment="Center" Margin="0,0,8,0"/>
                        <ComboBox x:Name="CmbYtAudioFormat" Width="90" Height="28" FontSize="11" SelectedIndex="0">
                            <ComboBoxItem Content="M4A"/>
                            <ComboBoxItem Content="OPUS"/>
                            <ComboBoxItem Content="MP3"/>
                        </ComboBox>
                    </StackPanel>
```

- [ ] **Step 2: Update `YtMode_Changed`** (line ~6402) to toggle the audio-format panel instead of the removed checkbox:

```csharp
    private void YtMode_Changed(object sender, RoutedEventArgs e)
    {
        if (YtVideoOptions == null || YtAudioOptions == null) return;
        bool isVideo = RbYtVideo?.IsChecked == true;
        YtVideoOptions.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        YtAudioOptions.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
        UpdateYtSelectedCount();
    }
```

- [ ] **Step 3: Update `Yt_Download`.** Replace the confirm-message block (lines ~6416-6420):

```csharp
        bool confirmAudio = RbYtAudio.IsChecked == true;
        string confirmQuality = (CmbYtQuality.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Best";
        string confirmFormat = (CmbYtAudioFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "M4A";
        string confirmMsg = confirmAudio
            ? $"Download {selected.Count} song(s) as {confirmFormat}?"
            : $"Download {selected.Count} video(s) as Video — {confirmQuality}?";
```

Replace the settings-read block (lines ~6436-6438):

```csharp
        bool isAudio = RbYtAudio.IsChecked == true;
        string quality = (CmbYtQuality.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "best";
        string audioFormat = (CmbYtAudioFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "m4a";
```

Replace the download call (lines ~6464-6465):

```csharp
                    await FileToolsService.DownloadSingleVideoAsync(
                        video.VideoUrl, folderDlg.SelectedPath, quality, isAudio, audioFormat, progress);
```

- [ ] **Step 4: Verify the whole solution builds**

Run:
```powershell
dotnet build Llamashot/Llamashot.csproj -c Release 2>&1 | Select-String -Pattern "error CS"
```
Expected: no output (zero errors). The Task 2 call-site break is now resolved.

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m @'
feat(youtube): audio-format choice (M4A/OPUS/MP3) in the download UI

Relabel the audio mode to "Audio", add a format combo replacing the
embed-thumbnail checkbox (art is always embedded now), and pass the chosen
format through to the downloader.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 5: UI — YouTube/Music search source toggle

**Files:**
- Modify: `Llamashot/Views/FileToolsWindow.xaml:2544-2553` (add source toggle after the Videos/Playlists toggle)
- Modify: `Llamashot/Views/FileToolsWindow.xaml.cs` — add `_ytMusicMode` field (state region ~line 195), add `YtSource_Changed` handler, make `BuildVideoSearchTarget` music-aware (~line 6835)

- [ ] **Step 1: XAML — add the source toggle.** In `FileToolsWindow.xaml`, immediately after the closing `</Border>` of the Videos/Playlists toggle (line ~2553), insert:

```xml
                    <!-- Source: YouTube / Music. Music routes searches to YouTube Music songs. -->
                    <Border HorizontalAlignment="Center" Background="{DynamicResource SurfaceBrush}" CornerRadius="8" BorderBrush="{DynamicResource BorderSoftBrush}"
                            BorderThickness="1" Padding="3" Margin="0,10,0,0">
                        <StackPanel Orientation="Horizontal">
                            <RadioButton x:Name="RbYtSourceYouTube" Style="{StaticResource YtSegment}" Content="YouTube"
                                         GroupName="YtSource" IsChecked="True" Checked="YtSource_Changed"/>
                            <RadioButton x:Name="RbYtSourceMusic" Style="{StaticResource YtSegment}" Content="Music"
                                         GroupName="YtSource" Checked="YtSource_Changed"/>
                        </StackPanel>
                    </Border>
```

- [ ] **Step 2: Add the state field.** In `FileToolsWindow.xaml.cs`, in the YouTube state region (after line ~195, near `_ytDownloadSortSyncing`):

```csharp
    private bool _ytMusicMode;   // true when the YouTube/Music source toggle is on Music
```

- [ ] **Step 3: Add the handler.** Add near the other Yt handlers (e.g. after `Yt_SelectAllToggle`, ~line 6789):

```csharp
    // Source toggle (YouTube / Music). Music routes keyword searches to YouTube Music songs;
    // playlist search doesn't apply there, so that toggle is hidden while Music is active.
    private void YtSource_Changed(object sender, RoutedEventArgs e)
    {
        _ytMusicMode = RbYtSourceMusic?.IsChecked == true;
        if (RbYtModePlaylists != null)
            RbYtModePlaylists.Visibility = _ytMusicMode ? Visibility.Collapsed : Visibility.Visible;
        if (_ytMusicMode && RbYtModeVideos != null) RbYtModeVideos.IsChecked = true;
    }
```

- [ ] **Step 4: Make `BuildVideoSearchTarget` music-aware** (line ~6835). Replace the method with:

```csharp
    // Builds a video-search target for the first `end` results. In Music mode, routes to the
    // YouTube Music songs search; otherwise ytsearch (or the results page + sp= token when a
    // filter/sort is active).
    private string BuildVideoSearchTarget(string query, int end)
    {
        if (_ytMusicMode)
            return FileToolsService.BuildMusicSearchUrl(query);
        string sp = BuildYtSp(_ytUploadDate, _ytSort);
        return string.IsNullOrEmpty(sp)
            ? $"ytsearch{end}:{query}"
            : $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp={Uri.EscapeDataString(sp)}";
    }
```

(No other call-site changes: `RunYtFetchAsync`, `YtLoadMoreAsync`, and `YtReSearchAsync` all go through this method for keyword video searches, so Music mode is picked up on the initial fetch, paging, and re-search. Paging still works because `--playlist-items` is applied by `FetchYouTubeVideosAsync` on top of the music URL.)

- [ ] **Step 5: Verify build**

Run:
```powershell
dotnet build Llamashot/Llamashot.csproj -c Release 2>&1 | Select-String -Pattern "error CS"
```
Expected: no output (zero errors).

- [ ] **Step 6: Commit**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m @'
feat(youtube): YouTube/Music search source toggle

Add a YouTube|Music toggle on the search hero; Music routes keyword
searches through the YT-Music songs URL and hides the playlist toggle.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 6: UI — yt-dlp freshness hint in the error dialog

**Files:**
- Modify: `Llamashot/Views/FileToolsWindow.xaml.cs:6010-6014` (the "yt-dlp Required" alert in `RunYtFetchAsync`)

- [ ] **Step 1: Expand the alert text** to include install *and* update guidance (a stale yt-dlp is a common cause of the bot-check failure). Replace lines ~6010-6014:

```csharp
        if (!FileToolsService.IsYtDlpAvailable())
        {
            ConfirmDialog.Alert(this, "yt-dlp Required",
                "yt-dlp is required.\n\n" +
                "Install or update it:\n" +
                "  winget install yt-dlp.yt-dlp\n" +
                "  yt-dlp -U\n\n" +
                "An outdated yt-dlp can fail with a “confirm you’re not a bot” error — update it if downloads stop working.");
            return;
        }
```

- [ ] **Step 2: Verify build**

Run:
```powershell
dotnet build Llamashot/Llamashot.csproj -c Release 2>&1 | Select-String -Pattern "error CS"
```
Expected: no output (zero errors).

- [ ] **Step 3: Commit**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m @'
feat(youtube): add update guidance to the yt-dlp error dialog

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 7: Version bump, full harness pass, and manual smoke

**Files:**
- Modify: `Llamashot/Llamashot.csproj:19-21`, `Llamashot/Views/AboutWindow.xaml:26`

- [ ] **Step 1: Bump version** 8.9.26 → 8.9.27.

In `Llamashot/Llamashot.csproj` (lines 19-21):
```xml
    <Version>8.9.27</Version>
    <AssemblyVersion>8.9.27</AssemblyVersion>
    <FileVersion>8.9.27</FileVersion>
```

In `Llamashot/Views/AboutWindow.xaml` (line 26):
```xml
                <TextBlock Text="Version 8.9.27" Foreground="{DynamicResource TextMutedBrush}"
```

- [ ] **Step 2: Full build + harness assertions**

Run:
```powershell
dotnet build Llamashot/Llamashot.csproj -c Release 2>&1 | Select-String -Pattern "error CS"
$env:LLAMASHOT_YTMUSIC = "1"; dotnet run --project tools\RunHarness -c Release; Remove-Item Env:\LLAMASHOT_YTMUSIC
Get-Content "$env:TEMP\run_harness_out\ytmusic.txt"
```
Expected: no build errors; every `ytmusic.txt` line `PASS`.

- [ ] **Step 3: Manual smoke test (real network).** Run the app, open **File Tools → YouTube Download**, then verify each:
  1. Toggle **Music**, search a song name → results list populates (songs, not empty rows).
  2. Go to download, choose **Audio → M4A**, download one track to a temp folder.
  3. Confirm the file plays and — via right-click ▸ Properties ▸ Details, or `ffprobe` — carries **title / artist / album** tags and embedded **album art**.
  4. Toggle **YouTube**, confirm normal video search + a **Video → 720p** download still works (regression check).

Record the outcome (pass/fail per step) in the commit message or PR body. If step 2 hits the bot check, confirm `yt-dlp --version` is recent (`yt-dlp -U`); the `web_embedded` client is already applied by the code.

- [ ] **Step 4: Commit**

```powershell
git add Llamashot/Llamashot.csproj Llamashot/Views/AboutWindow.xaml
git commit -m @'
chore: bump version to 8.9.27 — YouTube Music download

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

## Self-review (completed)

**Spec coverage:**
- §1 Search toggle → Task 3 (URL builder) + Task 5 (toggle UI + routing). ✓
- §2 Audio format (M4A/OPUS/MP3) → Task 1 (arg builder) + Task 4 (combo + wiring). ✓
- §3 Always-on metadata + album art → Task 1 (`--embed-metadata --embed-thumbnail` always in audio args). ✓
- §3a `web_embedded` bot-check fix on fetch + download → Task 1 (const) + Task 2 (both fetch calls + download). ✓
- §3b yt-dlp freshness hint → Task 6. ✓
- Testing (arg-string + music-URL assertions) → Tasks 1 & 3 harness block; manual smoke → Task 7. ✓
- Version bump → Task 7. ✓

**Type/name consistency:** `BuildAudioDownloadArgs`, `BuildVideoDownloadArgs`, `BuildMusicSearchUrl`, `YtDlpClientArgs`, `YtMusicSongsFilter`, `_ytMusicMode`, `YtSource_Changed`, `YtAudioOptions`, `CmbYtAudioFormat` — used identically across tasks. `DownloadSingleVideoAsync` new signature `(videoUrl, outputDir, quality, audioOnly, audioFormat="mp3", progress=null)` is matched by both call sites (Task 2 `DownloadMediaAsync`, Task 4 `Yt_Download`). ✓

**Placeholder scan:** no TBD/TODO; every code step shows complete code. The one intentional cross-task marker (`// (Task 3 appends …)`) is a real line replaced in Task 3, not a plan placeholder. ✓
