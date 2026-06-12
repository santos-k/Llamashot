# YouTube Download Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add list/grid view switching, keyword search, checked-only download with a live count and "checked first" reorder, preserved selection/status across view switches, and a download-type confirmation dialog to the YouTube Download tool.

**Architecture:** Both the existing list ListBox and a new grid ListBox bind to the **same** `_ytVideos` ObservableCollection, so per-item selection/status/progress is shared and preserved when toggling views. Search uses a `Filter` on the shared default `ICollectionView`. The confirmation reuses the existing `ConfirmDialog.Show`.

**Tech Stack:** C# / WPF (.NET 10), `FileToolsWindow` (XAML + code-behind), `ConfirmDialog`.

**Note on testing:** This project has no unit-test harness; the UI is verified by building (`dotnet build`) and manual checks. Because XAML event handlers must have matching code-behind methods to compile, Tasks 1–5 are edits and the single build/verify/commit happens in Task 6.

---

## File Structure

- **Modify** `Views/FileToolsWindow.xaml` — `YtConfigView` block (~lines 2945–3069): add a toolbar row (search + view toggle + selected count + "Checked first") and a new `YtVideoGrid` card view alongside the existing `YtVideoList`.
- **Modify** `Views/FileToolsWindow.xaml.cs` — view-mode state + toggle, search filter, selected-count tracking, checked-first reorder, item subscription/cleanup, and confirm-before-download in `Yt_Download`.
- **Modify** `Llamashot.csproj` and `Views/AboutWindow.xaml` — version bump 8.3.7 → 8.3.8.
- No changes to `Core/FileToolsService.cs` or `Views/ConfirmDialog.cs`.

---

### Task 1: Restructure `YtConfigView` XAML — toolbar + grid view

**Files:**
- Modify: `Views/FileToolsWindow.xaml` (replace the `YtConfigView` Grid, currently lines ~2945–3069)

- [ ] **Step 1: Replace the entire `YtConfigView` block**

Find the opening line `<Grid x:Name="YtConfigView" Visibility="Collapsed" Margin="16,8">` and replace everything from that line through its matching `</Grid>` that precedes the `</Grid>` closing `PanelYouTubeDl` (the block currently ending at line ~3069, just before `<!-- ====================== ... Processing Overlay ... -->`). Replace with:

```xml
            <Grid x:Name="YtConfigView" Visibility="Collapsed" Margin="16,8">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>   <!-- Header: title/detail + select all/deselect -->
                    <RowDefinition Height="Auto"/>   <!-- Toolbar: search + count + checked-first + view toggle -->
                    <RowDefinition Height="*"/>       <!-- Video views (list + grid) -->
                    <RowDefinition Height="Auto"/>   <!-- Download controls -->
                </Grid.RowDefinitions>

                <!-- Header -->
                <Grid Margin="0,0,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <StackPanel>
                        <TextBlock x:Name="TxtYtTitle" Text="" Foreground="#EEE" FontSize="16" FontWeight="SemiBold"
                                   TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxHeight="44"/>
                        <TextBlock x:Name="TxtYtDetail" Text="" Foreground="#888" FontSize="12" Margin="0,2,0,0"/>
                    </StackPanel>
                    <Button Grid.Column="1" Content="Select All" Width="80" Height="28" FontSize="11"
                            Background="#333" Foreground="#CCC" BorderBrush="#444" Cursor="Hand"
                            Click="Yt_SelectAll" Margin="8,0,4,0"/>
                    <Button Grid.Column="2" Content="Deselect All" Width="85" Height="28" FontSize="11"
                            Background="#333" Foreground="#CCC" BorderBrush="#444" Cursor="Hand"
                            Click="Yt_DeselectAll"/>
                </Grid>

                <!-- Toolbar -->
                <Grid x:Name="YtToolbar" Grid.Row="1" Margin="0,0,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>      <!-- search -->
                        <ColumnDefinition Width="Auto"/>   <!-- count -->
                        <ColumnDefinition Width="Auto"/>   <!-- checked first -->
                        <ColumnDefinition Width="Auto"/>   <!-- view toggle -->
                    </Grid.ColumnDefinitions>

                    <!-- Search box with watermark -->
                    <Grid x:Name="YtSearchBox" Grid.Column="0" HorizontalAlignment="Left" Width="280">
                        <TextBox x:Name="TxtYtSearch" Height="30" Padding="10,4" FontSize="12"
                                 Background="#252528" Foreground="#EEE" BorderBrush="#444" BorderThickness="1"
                                 VerticalContentAlignment="Center" TextChanged="Yt_SearchChanged"/>
                        <TextBlock x:Name="TxtYtSearchPlaceholder" Text="Search videos…" Foreground="#666"
                                   FontSize="12" Margin="12,0,0,0" VerticalAlignment="Center"
                                   IsHitTestVisible="False"/>
                    </Grid>

                    <TextBlock x:Name="TxtYtSelectedCount" Grid.Column="1" Text="0 of 0 selected"
                               Foreground="#888" FontSize="12" VerticalAlignment="Center" Margin="12,0"/>

                    <Button x:Name="BtnYtCheckedFirst" Grid.Column="2" Content="Checked first" Height="28"
                            FontSize="11" Background="#333" Foreground="#CCC" BorderBrush="#444" Cursor="Hand"
                            Padding="10,0" Margin="0,0,8,0" Click="Yt_CheckedFirst"/>

                    <StackPanel Grid.Column="3" Orientation="Horizontal">
                        <Button x:Name="BtnYtListView" Content="List" Width="50" Height="28" FontSize="11"
                                Background="#333" Foreground="#CCC" BorderBrush="#444" Cursor="Hand"
                                Click="Yt_ListView"/>
                        <Button x:Name="BtnYtGridView" Content="Grid" Width="50" Height="28" FontSize="11"
                                Background="#FF0000" Foreground="White" BorderBrush="#444" Cursor="Hand"
                                Click="Yt_GridView"/>
                    </StackPanel>
                </Grid>

                <!-- Video list (row) view -->
                <ListBox x:Name="YtVideoList" Grid.Row="2" Visibility="Collapsed"
                         Background="#1A1A1E" BorderBrush="#333" BorderThickness="1"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                         ScrollViewer.VerticalScrollBarVisibility="Auto">
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                            <Setter Property="Padding" Value="0"/>
                            <Setter Property="Background" Value="Transparent"/>
                            <Setter Property="BorderBrush" Value="#252528"/>
                            <Setter Property="BorderThickness" Value="0,0,0,1"/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="8,6">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" VerticalAlignment="Center"
                                          Cursor="Hand" Margin="0,0,8,0"/>
                                <Image Grid.Column="1" Source="{Binding Thumbnail}" Width="120" Height="68"
                                       Stretch="UniformToFill" RenderOptions.BitmapScalingMode="HighQuality"
                                       Margin="0,0,10,0">
                                    <Image.Clip>
                                        <RectangleGeometry Rect="0,0,120,68" RadiusX="4" RadiusY="4"/>
                                    </Image.Clip>
                                </Image>
                                <StackPanel Grid.Column="2" VerticalAlignment="Center">
                                    <TextBlock Text="{Binding Title}" Foreground="#CCC" FontSize="13"
                                               TextTrimming="CharacterEllipsis" TextWrapping="NoWrap"/>
                                    <TextBlock Text="{Binding Duration}" Foreground="#666" FontSize="11" Margin="0,2,0,0"/>
                                    <ProgressBar Value="{Binding Progress}" Minimum="0" Maximum="100" Height="3"
                                                 Foreground="#42A5F5" Background="#333" BorderThickness="0"
                                                 Margin="0,4,0,0"/>
                                </StackPanel>
                                <TextBlock Grid.Column="3" Text="{Binding Status}" Foreground="{Binding StatusColor}"
                                           FontSize="11" FontWeight="SemiBold" VerticalAlignment="Center" Margin="10,0,0,0"/>
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <!-- Video grid (card) view -->
                <ListBox x:Name="YtVideoGrid" Grid.Row="2"
                         Background="#1A1A1E" BorderBrush="#333" BorderThickness="1"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                         ScrollViewer.VerticalScrollBarVisibility="Auto">
                    <ListBox.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel Orientation="Horizontal"/>
                        </ItemsPanelTemplate>
                    </ListBox.ItemsPanel>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="Padding" Value="0"/>
                            <Setter Property="Margin" Value="6"/>
                            <Setter Property="Background" Value="Transparent"/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border Width="180" Background="#222226" CornerRadius="6">
                                <StackPanel>
                                    <Grid>
                                        <Image Source="{Binding Thumbnail}" Width="180" Height="101"
                                               Stretch="UniformToFill" RenderOptions.BitmapScalingMode="HighQuality">
                                            <Image.Clip>
                                                <RectangleGeometry Rect="0,0,180,101" RadiusX="6" RadiusY="6"/>
                                            </Image.Clip>
                                        </Image>
                                        <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}"
                                                  HorizontalAlignment="Left" VerticalAlignment="Top" Margin="6"
                                                  Cursor="Hand"/>
                                        <Border HorizontalAlignment="Right" VerticalAlignment="Top" Margin="6"
                                                Background="#CC000000" CornerRadius="3" Padding="5,2">
                                            <TextBlock Text="{Binding Duration}" Foreground="#EEE" FontSize="10"/>
                                        </Border>
                                        <Border HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="6"
                                                Background="#CC000000" CornerRadius="3" Padding="5,2">
                                            <TextBlock Text="{Binding Status}" Foreground="{Binding StatusColor}"
                                                       FontSize="10" FontWeight="SemiBold"/>
                                        </Border>
                                    </Grid>
                                    <TextBlock Text="{Binding Title}" Foreground="#CCC" FontSize="12"
                                               Margin="8,6,8,4" TextWrapping="Wrap"
                                               TextTrimming="CharacterEllipsis" MaxHeight="32"/>
                                    <ProgressBar Value="{Binding Progress}" Minimum="0" Maximum="100" Height="3"
                                                 Foreground="#42A5F5" Background="#333" BorderThickness="0"
                                                 Margin="8,0,8,8"/>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <!-- Download controls -->
                <Grid Grid.Row="3" Margin="0,12,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <RadioButton x:Name="RbYtVideo" Content="Video" Foreground="#CCC" FontSize="12"
                                     IsChecked="True" GroupName="YtMode" Margin="0,0,12,0" Cursor="Hand"
                                     Checked="YtMode_Changed"/>
                        <RadioButton x:Name="RbYtAudio" Content="MP3" Foreground="#CCC" FontSize="12"
                                     GroupName="YtMode" Cursor="Hand" Checked="YtMode_Changed"/>
                    </StackPanel>

                    <StackPanel x:Name="YtVideoOptions" Grid.Column="1" Orientation="Horizontal" Margin="16,0,0,0">
                        <ComboBox x:Name="CmbYtQuality" Width="80" Height="28" FontSize="11" SelectedIndex="0">
                            <ComboBoxItem Content="Best"/>
                            <ComboBoxItem Content="720p"/>
                            <ComboBoxItem Content="480p"/>
                            <ComboBoxItem Content="360p"/>
                        </ComboBox>
                    </StackPanel>

                    <CheckBox x:Name="ChkYtEmbedThumb" Grid.Column="1" Content="Embed thumbnail"
                              Foreground="#CCC" FontSize="11" IsChecked="True" Cursor="Hand"
                              Visibility="Collapsed" VerticalAlignment="Center" Margin="16,0,0,0"/>

                    <TextBlock x:Name="TxtYtOverallProgress" Grid.Column="2" Text="" Foreground="#888" FontSize="12"
                               VerticalAlignment="Center" HorizontalAlignment="Center"/>

                    <Button x:Name="BtnYtStop" Grid.Column="3" Content="Stop" Width="70" Height="38"
                            FontSize="13" Background="#EF5350" Foreground="White" BorderThickness="0"
                            Cursor="Hand" Click="Yt_Stop" Visibility="Collapsed" Margin="0,0,8,0"
                            Template="{StaticResource RoundedButton21}"/>

                    <Button x:Name="BtnYtDownload" Grid.Column="4" Content="Download &#x2192;" Width="140" Height="42"
                            FontSize="14" FontWeight="SemiBold" Foreground="White" Background="#FF0000"
                            Cursor="Hand" Click="Yt_Download"
                            Template="{StaticResource RoundedButton}"/>
                </Grid>
            </Grid>
```

Note: `YtVideoList` now starts `Visibility="Collapsed"` and is `Grid.Row="2"`; `YtVideoGrid` is visible by default (grid is the default view). Download controls moved from `Grid.Row="2"` to `Grid.Row="3"`.

---

### Task 2: Code-behind — view-mode state, grid ItemsSource, toggle handlers

**Files:**
- Modify: `Views/FileToolsWindow.xaml.cs`

- [ ] **Step 1: Add the view-mode field**

Find (near line 165):

```csharp
    private readonly ObservableCollection<YtVideoItem> _ytVideos = new();
    private CancellationTokenSource? _ytCancelSource;
    private string? _ytUrl;
```

Replace with:

```csharp
    private readonly ObservableCollection<YtVideoItem> _ytVideos = new();
    private CancellationTokenSource? _ytCancelSource;
    private string? _ytUrl;
    private bool _ytGridView = true; // grid is the default view
```

- [ ] **Step 2: Bind the grid to the same collection**

Find (near line 223):

```csharp
        YtVideoList.ItemsSource = _ytVideos;
```

Replace with:

```csharp
        YtVideoList.ItemsSource = _ytVideos;
        YtVideoGrid.ItemsSource = _ytVideos;
```

- [ ] **Step 3: Add the toggle handlers**

Add these methods next to the other `Yt_*` handlers (e.g. after `Yt_DeselectAll` near line 6367):

```csharp
    private void Yt_ListView(object sender, RoutedEventArgs e)
    {
        _ytGridView = false;
        ApplyYtViewMode();
    }

    private void Yt_GridView(object sender, RoutedEventArgs e)
    {
        _ytGridView = true;
        ApplyYtViewMode();
    }

    private void ApplyYtViewMode()
    {
        if (YtVideoList == null || YtVideoGrid == null) return;

        YtVideoList.Visibility = _ytGridView ? Visibility.Collapsed : Visibility.Visible;
        YtVideoGrid.Visibility = _ytGridView ? Visibility.Visible : Visibility.Collapsed;

        var active = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0000"));
        var inactive = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333"));
        var activeFg = Brushes.White;
        var inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC"));

        BtnYtGridView.Background = _ytGridView ? active : inactive;
        BtnYtListView.Background = _ytGridView ? inactive : active;
        BtnYtGridView.Foreground = _ytGridView ? activeFg : inactiveFg;
        BtnYtListView.Foreground = _ytGridView ? inactiveFg : activeFg;
    }
```

---

### Task 3: Code-behind — keyword search filter

**Files:**
- Modify: `Views/FileToolsWindow.xaml.cs`

- [ ] **Step 1: Add the search handler**

Add next to the other `Yt_*` handlers:

```csharp
    private void Yt_SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtYtSearchPlaceholder != null)
            TxtYtSearchPlaceholder.Visibility =
                string.IsNullOrEmpty(TxtYtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos);
        if (view == null) return;

        string q = TxtYtSearch.Text.Trim();
        view.Filter = string.IsNullOrEmpty(q)
            ? null
            : o => o is YtVideoItem v && v.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
```

Both `YtVideoList` and `YtVideoGrid` bind to `_ytVideos`, so they share this default view and filter together.

---

### Task 4: Code-behind — selected count, checked-first, subscriptions, fetch/reset wiring

**Files:**
- Modify: `Views/FileToolsWindow.xaml.cs`

- [ ] **Step 1: Add count + checked-first + item-change handler**

Add next to the other `Yt_*` handlers:

```csharp
    private void YtItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(YtVideoItem.IsSelected))
            UpdateYtSelectedCount();
    }

    private void UpdateYtSelectedCount()
    {
        if (TxtYtSelectedCount == null) return;
        int sel = _ytVideos.Count(v => v.IsSelected);
        TxtYtSelectedCount.Text = $"{sel} of {_ytVideos.Count} selected";
    }

    private void Yt_CheckedFirst(object sender, RoutedEventArgs e)
    {
        var ordered = _ytVideos.Where(v => v.IsSelected)
            .Concat(_ytVideos.Where(v => !v.IsSelected))
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int cur = _ytVideos.IndexOf(ordered[i]);
            if (cur != i) _ytVideos.Move(cur, i);
        }
    }
```

- [ ] **Step 2: Subscribe items + reset toolbar state in `Yt_Fetch`**

In `Yt_Fetch`, find:

```csharp
        _ytUrl = url;
        _ytVideos.Clear();
```

Replace with:

```csharp
        _ytUrl = url;
        foreach (var v in _ytVideos) v.PropertyChanged -= YtItem_PropertyChanged;
        _ytVideos.Clear();
        TxtYtSearch.Text = "";
        System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Filter = null;
        _ytGridView = true;
```

- [ ] **Step 3: Subscribe each new item in `Yt_Fetch`**

In `Yt_Fetch`, find:

```csharp
                _ytVideos.Add(item);
            }
        }
        catch (Exception ex)
```

Replace with:

```csharp
                item.PropertyChanged += YtItem_PropertyChanged;
                _ytVideos.Add(item);
            }

            ApplyYtViewMode();
            UpdateYtSelectedCount();
            YtSearchBox.Visibility = _ytVideos.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            TxtYtSearchPlaceholder.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
```

- [ ] **Step 4: Update count after Select/Deselect All**

Find:

```csharp
    private void Yt_SelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var v in _ytVideos) v.IsSelected = true;
    }
```

Replace with:

```csharp
    private void Yt_SelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var v in _ytVideos) v.IsSelected = true;
        UpdateYtSelectedCount();
    }
```

Find:

```csharp
    private void Yt_DeselectAll(object sender, RoutedEventArgs e)
    {
        foreach (var v in _ytVideos) v.IsSelected = false;
    }
```

Replace with:

```csharp
    private void Yt_DeselectAll(object sender, RoutedEventArgs e)
    {
        foreach (var v in _ytVideos) v.IsSelected = false;
        UpdateYtSelectedCount();
    }
```

- [ ] **Step 5: Unsubscribe + reset in the tool cleanup**

Find (near line 630):

```csharp
        _ytUrl = null;
        _ytVideos.Clear();
        _ytCancelSource?.Cancel();
        _ytCancelSource = null;
```

Replace with:

```csharp
        foreach (var v in _ytVideos) v.PropertyChanged -= YtItem_PropertyChanged;
        _ytUrl = null;
        _ytVideos.Clear();
        System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Filter = null;
        _ytGridView = true;
        _ytCancelSource?.Cancel();
        _ytCancelSource = null;
```

---

### Task 5: Code-behind — confirmation dialog before download

**Files:**
- Modify: `Views/FileToolsWindow.xaml.cs`

- [ ] **Step 1: Insert the confirm gate in `Yt_Download`**

Find:

```csharp
        var selected = _ytVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one video."); return; }

        var folderDlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select download folder" };
```

Replace with:

```csharp
        var selected = _ytVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one video."); return; }

        bool confirmAudio = RbYtAudio.IsChecked == true;
        string confirmQuality = (CmbYtQuality.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Best";
        string confirmMsg = confirmAudio
            ? $"Download {selected.Count} video(s) as MP3 audio?"
            : $"Download {selected.Count} video(s) as Video — {confirmQuality}?";
        if (!Views.ConfirmDialog.Show(this, "Confirm Download", confirmMsg, "Download", "Cancel"))
            return;

        var folderDlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select download folder" };
```

Note: `ConfirmDialog` lives in the `Llamashot.Views` namespace; `FileToolsWindow` is also in `Llamashot.Views`, so `ConfirmDialog.Show(...)` resolves without the `Views.` prefix — but the prefix is harmless and unambiguous. Use `ConfirmDialog.Show(...)` if the `Views.` prefix fails to resolve.

---

### Task 6: Version bump, build, manual verification, commit

**Files:**
- Modify: `Llamashot.csproj`, `Views/AboutWindow.xaml`

- [ ] **Step 1: Bump version in `Llamashot.csproj`**

Find:

```xml
    <Version>8.3.7</Version>
    <AssemblyVersion>8.3.7</AssemblyVersion>
    <FileVersion>8.3.7</FileVersion>
```

Replace with:

```xml
    <Version>8.3.8</Version>
    <AssemblyVersion>8.3.8</AssemblyVersion>
    <FileVersion>8.3.8</FileVersion>
```

- [ ] **Step 2: Bump version in `Views/AboutWindow.xaml`**

Find:

```xml
                <TextBlock Text="Version 8.3.7" Foreground="#666"
```

Replace with:

```xml
                <TextBlock Text="Version 8.3.8" Foreground="#666"
```

- [ ] **Step 3: Build**

Run: `dotnet build Llamashot.csproj -c Debug`
Expected: `Build succeeded`, 0 errors. (Pre-existing warnings are acceptable.)

- [ ] **Step 4: Manual verification**

Run the app, open File Tools → YouTube Download, then verify:
1. Fetch a playlist URL → results appear in **Grid** view by default; the search box is visible (>1 video).
2. Click **List** → rows shown; click **Grid** → cards shown. Active toggle is highlighted red.
3. Toggle some checkboxes, switch List⇄Grid → **selection persists** in both directions.
4. Start a download, then switch views mid-download → **status/progress persist**.
5. Type a keyword → non-matching titles hide; clear it → all return; a previously-hidden item keeps its checkbox state.
6. Toggle checkboxes → **"X of Y selected"** updates live. Select All / Deselect All update it too.
7. Click **Checked first** → ticked videos move to the top.
8. With 0 checked → Download shows "Select at least one video."
9. With items checked, **Video** mode → Download shows confirm "Download N video(s) as Video — <quality>?"; Cancel aborts (no folder picker), Download proceeds to folder picker.
10. **MP3** mode → confirm reads "Download N video(s) as MP3 audio?".
11. Fetch a **single video** URL → search box hidden; everything else works.

- [ ] **Step 5: Commit** (ask the user first, per their always-confirm-commit rule)

```bash
git add Views/FileToolsWindow.xaml Views/FileToolsWindow.xaml.cs Llamashot.csproj Views/AboutWindow.xaml docs/superpowers/plans/2026-06-12-youtube-download-enhancements.md
git commit -m "feat(youtube): list/grid switch, search, checked-only download, confirm dialog"
```

---

## Self-Review Notes

- **Spec coverage:** view toggle (Task 1–2), search filter (Task 1 XAML + Task 3), checked-only count + checked-first (Task 1 + Task 4), preserved selection/status (shared collection, Tasks 1–2; verified Task 6 steps 3–4), confirm dialog (Task 5). All five features mapped.
- **Type consistency:** `_ytGridView`, `ApplyYtViewMode()`, `UpdateYtSelectedCount()`, `YtItem_PropertyChanged`, and control names (`YtVideoGrid`, `YtSearchBox`, `TxtYtSearch`, `TxtYtSearchPlaceholder`, `TxtYtSelectedCount`, `BtnYtListView`, `BtnYtGridView`, `BtnYtCheckedFirst`) match between XAML and code-behind.
- **No placeholders:** every step contains complete code/commands.
