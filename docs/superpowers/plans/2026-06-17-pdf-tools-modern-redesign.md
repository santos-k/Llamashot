# PDF / File Tools Modern Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reskin the File Tools feature with a modern layout and a soft color palette, driven by a central runtime-swappable theme (light default + dark toggle), without changing any tool behavior.

**Architecture:** Introduce `Themes/LightTheme.xaml`, `Themes/DarkTheme.xaml`, and `Themes/Shared.xaml` with named brush keys + control styles. A `ThemeManager` swaps the active dictionary at runtime and persists the choice in `AppSettings`. `FileToolsWindow.xaml` inline hexes become `{DynamicResource ...}`; imperative brushes in the code-behind read from `Application.Current.Resources[...]` and rebuild on a theme-change event.

**Tech Stack:** C# / .NET 10 WPF, XAML ResourceDictionaries, `DynamicResource`, System.Text.Json (existing `AppSettings`).

**Verification model:** This is a WPF visual change with no unit-test surface. Each task is verified by a successful `dotnet build` and, where it changes visuals, by launching the app and screenshotting. Final task does the full visual sweep across both themes.

**Build command (use throughout):**
`dotnet build D:\Llamashot\Llamashot\Llamashot.csproj -c Debug -v minimal`
Expected: `Build succeeded.` with `0 Error(s)`.

---

## Brush key reference (used by all tasks)

Both theme dictionaries define **identical keys**, different values:

| Key | Role | Light | Dark |
|---|---|---|---|
| `WindowBrush` | window background | `#F5F6FB` | `#14151A` |
| `HeaderBrush` | header bar | `#FFFFFF` | `#1B1C24` |
| `SurfaceBrush` | cards, search box, lists | `#FFFFFF` | `#1E2029` |
| `SurfaceAltBrush` | nested inputs / list rows | `#EEF1F8` | `#262833` |
| `SurfaceHoverBrush` | card/list hover | `#EDF0FA` | `#2A2D38` |
| `BorderSoftBrush` | borders, dividers | `#E2E6F0` | `#313443` |
| `TextPrimaryBrush` | titles / primary text | `#1F2430` | `#E8EAF0` |
| `TextSecondaryBrush` | body / secondary | `#5B6172` | `#A8AEC0` |
| `TextMutedBrush` | hints, indices, captions | `#9AA0B0` | `#6B7080` |
| `AccentBrush` | primary action / links / progress | `#6E8BFF` | `#7C9CFF` |
| `AccentTextBrush` | text/icon on accent fill | `#FFFFFF` | `#0E1018` |
| `SuccessBrush` | completion accent | `#43C59E` | `#5BD0A4` |
| `DangerBrush` | destructive (remove/delete) | `#FF6B6B` | `#FF7A7A` |
| `ScrimBrush` | overlay scrim | `#CCF5F6FB` | `#CC14151A` |

**Hex → key migration map** (apply when converting existing inline values):

| Existing hex(es) | New key |
|---|---|
| `#1A1A1E` | `WindowBrush` |
| `#222` | `HeaderBrush` |
| `#1E1E24`, `#252528` | `SurfaceBrush` |
| `#282830` | `SurfaceHoverBrush` |
| `#333`, `#444` | `BorderSoftBrush` |
| `#EEE` | `TextPrimaryBrush` |
| `#CCC` | `TextSecondaryBrush` |
| `#888`, `#777`, `#666`, `#555` | `TextMutedBrush` |
| `#EF5350` / `#E53935` / `#F44336` on a **primary action button** (`Background=`) | `AccentBrush` (text `AccentTextBrush`) |
| `#EF5350` as **`Foreground`** of an `X`/remove button | `DangerBrush` |
| `#42A5F5` (links, progress fill) | `AccentBrush` |
| `#4CAF50` (success/complete) | `SuccessBrush` |
| `#E61A1A1E` (overlay scrim) | `ScrimBrush` |

---

## Task 1: Theme infrastructure + persistence

**Files:**
- Create: `D:\Llamashot\Llamashot\Themes\LightTheme.xaml`
- Create: `D:\Llamashot\Llamashot\Themes\DarkTheme.xaml`
- Create: `D:\Llamashot\Llamashot\Themes\Shared.xaml`
- Create: `D:\Llamashot\Llamashot\Core\ThemeManager.cs`
- Modify: `D:\Llamashot\Llamashot\Core\AppSettings.cs` (add `FileToolsTheme`)
- Modify: `D:\Llamashot\Llamashot\App.xaml` (merge dictionaries)

- [ ] **Step 1: Create `Themes/LightTheme.xaml`**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="WindowBrush" Color="#F5F6FB"/>
    <SolidColorBrush x:Key="HeaderBrush" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="SurfaceBrush" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="SurfaceAltBrush" Color="#EEF1F8"/>
    <SolidColorBrush x:Key="SurfaceHoverBrush" Color="#EDF0FA"/>
    <SolidColorBrush x:Key="BorderSoftBrush" Color="#E2E6F0"/>
    <SolidColorBrush x:Key="TextPrimaryBrush" Color="#1F2430"/>
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="#5B6172"/>
    <SolidColorBrush x:Key="TextMutedBrush" Color="#9AA0B0"/>
    <SolidColorBrush x:Key="AccentBrush" Color="#6E8BFF"/>
    <SolidColorBrush x:Key="AccentTextBrush" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="SuccessBrush" Color="#43C59E"/>
    <SolidColorBrush x:Key="DangerBrush" Color="#FF6B6B"/>
    <SolidColorBrush x:Key="ScrimBrush" Color="#CCF5F6FB"/>
</ResourceDictionary>
```

- [ ] **Step 2: Create `Themes/DarkTheme.xaml`** — same keys, dark values

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="WindowBrush" Color="#14151A"/>
    <SolidColorBrush x:Key="HeaderBrush" Color="#1B1C24"/>
    <SolidColorBrush x:Key="SurfaceBrush" Color="#1E2029"/>
    <SolidColorBrush x:Key="SurfaceAltBrush" Color="#262833"/>
    <SolidColorBrush x:Key="SurfaceHoverBrush" Color="#2A2D38"/>
    <SolidColorBrush x:Key="BorderSoftBrush" Color="#313443"/>
    <SolidColorBrush x:Key="TextPrimaryBrush" Color="#E8EAF0"/>
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="#A8AEC0"/>
    <SolidColorBrush x:Key="TextMutedBrush" Color="#6B7080"/>
    <SolidColorBrush x:Key="AccentBrush" Color="#7C9CFF"/>
    <SolidColorBrush x:Key="AccentTextBrush" Color="#0E1018"/>
    <SolidColorBrush x:Key="SuccessBrush" Color="#5BD0A4"/>
    <SolidColorBrush x:Key="DangerBrush" Color="#FF7A7A"/>
    <SolidColorBrush x:Key="ScrimBrush" Color="#CC14151A"/>
</ResourceDictionary>
```

- [ ] **Step 3: Create `Themes/Shared.xaml`** — control styles referencing the keys via DynamicResource. Move the existing window-scoped templates here so all panels share them.

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Pill primary button -->
    <ControlTemplate x:Key="RoundedButton" TargetType="Button">
        <Border x:Name="border" Background="{TemplateBinding Background}" CornerRadius="25"
                Padding="{TemplateBinding Padding}">
            <Border.Effect>
                <DropShadowEffect BlurRadius="10" ShadowDepth="2" Opacity="0.18" Color="#3A4A8A"/>
            </Border.Effect>
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="border" Property="Opacity" Value="0.92"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="border" Property="Opacity" Value="0.82"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <ControlTemplate x:Key="RoundedButton21" TargetType="Button">
        <Border x:Name="border" Background="{TemplateBinding Background}" CornerRadius="21"
                Padding="{TemplateBinding Padding}">
            <Border.Effect>
                <DropShadowEffect BlurRadius="10" ShadowDepth="2" Opacity="0.18" Color="#3A4A8A"/>
            </Border.Effect>
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="border" Property="Opacity" Value="0.92"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <ControlTemplate x:Key="PeToolButton" TargetType="Button">
        <Border Background="{TemplateBinding Background}" CornerRadius="8"
                Padding="{TemplateBinding Padding}">
            <ContentPresenter VerticalAlignment="Center"/>
        </Border>
    </ControlTemplate>

    <Style TargetType="Slider">
        <Setter Property="Height" Value="20"/>
        <Setter Property="IsMoveToPointEnabled" Value="True"/>
    </Style>
</ResourceDictionary>
```

NOTE: the two `DataTemplate`s (`FileListItemTemplate`, `ImageFileListItemTemplate`) stay in `FileToolsWindow.xaml` resources (Task 2 retints them) because they are window-specific.

- [ ] **Step 4: Add theme setting to `AppSettings.cs`**

Add after the `QuickPreview` block (around line 95):

```csharp
    // File Tools appearance
    public string FileToolsTheme { get; set; } = "Light"; // "Light" | "Dark"
```

- [ ] **Step 5: Create `Core/ThemeManager.cs`**

```csharp
using System.Windows;

namespace Llamashot.Core;

public static class ThemeManager
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static string Current { get; private set; } = Light;

    /// <summary>Raised after the active theme dictionary has been swapped.</summary>
    public static event Action? ThemeChanged;

    private static ResourceDictionary? _active;

    public static void Initialize(string theme)
    {
        Apply(string.IsNullOrWhiteSpace(theme) ? Light : theme, persist: false, notify: false);
    }

    public static void Toggle()
    {
        Apply(Current == Light ? Dark : Light);
    }

    public static void Apply(string theme, bool persist = true, bool notify = true)
    {
        theme = theme == Dark ? Dark : Light;
        var uri = new Uri($"/Themes/{theme}Theme.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_active != null) merged.Remove(_active);
        // Insert theme colors at the front so Shared.xaml (which references them) resolves.
        merged.Insert(0, dict);
        _active = dict;
        Current = theme;

        if (persist)
        {
            AppSettings.Instance.FileToolsTheme = theme;
            AppSettings.Save();
        }
        if (notify) ThemeChanged?.Invoke();
    }
}
```

- [ ] **Step 6: Wire dictionaries in `App.xaml`**

Replace the `<Application.Resources>` block so it merges Light (default) + Shared, keeping the existing `ToolbarButton` style:

```xml
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/Themes/LightTheme.xaml"/>
                <ResourceDictionary Source="/Themes/Shared.xaml"/>
            </ResourceDictionary.MergedDictionaries>

            <!-- Global button style for toolbar buttons -->
            <Style x:Key="ToolbarButton" TargetType="Button">
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="BorderBrush" Value="Transparent" />
                <Setter Property="BorderThickness" Value="0" />
                <Setter Property="Cursor" Value="Hand" />
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="Button">
                            <Border Background="{TemplateBinding Background}"
                                    BorderBrush="{TemplateBinding BorderBrush}"
                                    BorderThickness="{TemplateBinding BorderThickness}"
                                    CornerRadius="3"
                                    Padding="{TemplateBinding Padding}">
                                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter Property="Background" Value="#40FFFFFF" />
                                </Trigger>
                                <Trigger Property="IsPressed" Value="True">
                                    <Setter Property="Background" Value="#60FFFFFF" />
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ResourceDictionary>
    </Application.Resources>
```

- [ ] **Step 7: Initialize the theme at startup**

In `App.xaml.cs`, find where `AppSettings.Load()` is called during startup and add immediately after it:

```csharp
ThemeManager.Initialize(AppSettings.Instance.FileToolsTheme);
```

(If `AppSettings.Load()` is not already called at startup, add `AppSettings.Load();` first, then the `ThemeManager.Initialize` line. Verify by searching `App.xaml.cs` for `AppSettings.Load`.)

- [ ] **Step 8: Build**

Run: `dotnet build D:\Llamashot\Llamashot\Llamashot.csproj -c Debug -v minimal`
Expected: `Build succeeded.` `0 Error(s)`. (The csproj auto-includes new `.xaml`/`.cs` files under the project dir; if the new ResourceDictionaries are not picked up, ensure they build as `Page`/`Resource` — default SDK behavior includes `.xaml` as `Page`.)

- [ ] **Step 9: Commit**

```bash
git add Llamashot/Themes Llamashot/Core/ThemeManager.cs Llamashot/Core/AppSettings.cs Llamashot/App.xaml Llamashot/App.xaml.cs
git commit -m "feat(filetools): add central light/dark theme infrastructure"
```

---

## Task 2: Window resources, header, and home screen

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\FileToolsWindow.xaml:1-166`

- [ ] **Step 1: Remove the moved templates from `<Window.Resources>`**

Delete the `RoundedButton`, `RoundedButton21`, `PeToolButton` control templates and the `Slider` style from `FileToolsWindow.xaml:14-61` (they now live in `Shared.xaml`, already merged app-wide). KEEP the two `DataTemplate`s (`FileListItemTemplate`, `ImageFileListItemTemplate`).

- [ ] **Step 2: Retint the kept DataTemplates** (`:64-107`)

In both templates replace: index/size `Foreground="#666"` → `{DynamicResource TextMutedBrush}`; name `#CCC` → `{DynamicResource TextSecondaryBrush}`; extra `#888` → `{DynamicResource TextMutedBrush}`; the `X` button `Background="#333"` → `{DynamicResource SurfaceAltBrush}`, `Foreground="#EF5350"` → `{DynamicResource DangerBrush}`, `BorderBrush="#444"` → `{DynamicResource BorderSoftBrush}`.

- [ ] **Step 3: Window background**

`FileToolsWindow.xaml:10` — change `Background="#1A1A1E"` → `Background="{DynamicResource WindowBrush}"`.

- [ ] **Step 4: Header bar with theme toggle** (`:119-127`)

Replace the header `Border`+`Grid` with:

```xml
        <Border Grid.Row="0" Background="{DynamicResource HeaderBrush}" Padding="16,10"
                BorderBrush="{DynamicResource BorderSoftBrush}" BorderThickness="0,0,0,1">
            <Grid>
                <Button x:Name="BtnBack" Content="&#x2190; Back" HorizontalAlignment="Left" Visibility="Collapsed"
                        Click="GoBack_Click" Background="Transparent" Foreground="{DynamicResource TextSecondaryBrush}"
                        BorderThickness="0" FontSize="13" Cursor="Hand" Padding="8,4"/>
                <TextBlock x:Name="TxtToolTitle" Text="File Tools" FontSize="20" FontWeight="SemiBold"
                           Foreground="{DynamicResource TextPrimaryBrush}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                <Button x:Name="BtnThemeToggle" HorizontalAlignment="Right" Content="&#x1F319;"
                        ToolTip="Toggle light / dark theme" Click="ThemeToggle_Click"
                        Background="Transparent" BorderThickness="0" FontSize="18" Cursor="Hand" Padding="8,2"
                        Foreground="{DynamicResource TextSecondaryBrush}"/>
            </Grid>
        </Border>
```

- [ ] **Step 5: Home search + sort bar** (`:140-161`)

Replace inline colors: search `TextBox` `Background="#1E1E24"` → `{DynamicResource SurfaceBrush}`, `Foreground="#EEE"` → `{DynamicResource TextPrimaryBrush}`, `BorderBrush="#333"` → `{DynamicResource BorderSoftBrush}`. Placeholder `Foreground="#555"` → `{DynamicResource TextMutedBrush}`. (The ComboBox uses default system styling; leave as-is.)

- [ ] **Step 6: Add a category filter chip row**

Insert a chip row between the search grid (`:140-161`) and the card `ScrollViewer` (`:163`). Add a new auto-height row to `HomePanel.RowDefinitions` (it currently has Auto + *; make it Auto + Auto + *, and set the `ScrollViewer` to `Grid.Row="2"`):

```xml
            <!-- Category filter chips -->
            <ItemsControl x:Name="CategoryChips" Grid.Row="1" Margin="24,12,24,0">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
            </ItemsControl>
```

The chips are populated in code-behind (Task 5) so they can drive the existing filter logic. Categories: `All`, `PDF Tools`, `Image Tools`, `Office Tools`, `Video & Audio`, `Download`.

- [ ] **Step 7: Build**

Run the build command. Expected: `Build succeeded.` `0 Error(s)`.
(`ThemeToggle_Click` is referenced but not yet defined — add a temporary stub in code-behind to keep it compiling, OR implement it now in Task 5. To keep this task buildable, add this stub to `FileToolsWindow.xaml.cs` now:)

```csharp
    private void ThemeToggle_Click(object sender, RoutedEventArgs e) => Core.ThemeManager.Toggle();
```

- [ ] **Step 8: Commit**

```bash
git add Llamashot/Views/FileToolsWindow.xaml Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m "feat(filetools): theme home header, search, and add category chips"
```

---

## Task 3: Convert the 21 tool panels to DynamicResource

The tool panels span `FileToolsWindow.xaml:169-2933`. Convert inline hexes to `{DynamicResource ...}` using the migration map. Work in batches by panel to keep the diff reviewable and the app buildable.

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\FileToolsWindow.xaml:169-2933`

- [ ] **Step 1: PDF panels** (`PanelMergePdf`…`PanelInsertPages`, ~`:169-753`)

For each panel apply the migration map to every inline color:
- container/section `Background` `#1E1E24`/`#252528` → `SurfaceBrush`; `#1A1A1E` → `WindowBrush`.
- borders/dividers `#333`/`#444` → `BorderSoftBrush`.
- text: `#EEE`→`TextPrimaryBrush`, `#CCC`→`TextSecondaryBrush`, `#888`/`#777`/`#666`/`#555`→`TextMutedBrush`.
- primary action buttons (`Background="#EF5350"`/`#E53935`/`#F44336"` with `Foreground="White"`) → `Background="{DynamicResource AccentBrush}"`, `Foreground="{DynamicResource AccentTextBrush}"`.
- progress/link blues `#42A5F5` → `AccentBrush`.
- any green `#4CAF50` → `SuccessBrush`.

- [ ] **Step 2: Build** — Run build command. Expected `Build succeeded.`

- [ ] **Step 3: Image panels** (`PanelCompressImage`…`PanelImageEditor`, ~`:754-1454`)

Apply the same migration map. The Image Editor panel has its own toolbar buttons using `PeToolButton`; retint their `Background`/`Foreground` per the map (tool buttons → `SurfaceAltBrush` bg, `TextSecondaryBrush` fg; active state stays accent — map any existing active-highlight hex to `AccentBrush`).

- [ ] **Step 4: Build** — Expected `Build succeeded.`

- [ ] **Step 5: Office + Video/Audio + YouTube panels** (`PanelCompressOffice`…`PanelYouTubeDl`, ~`:1455-2933`)

Apply the same migration map across these panels (largest section; the YouTube panel has multiple sub-screens — retint each).

- [ ] **Step 6: Build** — Expected `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add Llamashot/Views/FileToolsWindow.xaml
git commit -m "feat(filetools): theme all 21 tool panels via dynamic resources"
```

---

## Task 4: Shared overlays (Processing + Complete)

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\FileToolsWindow.xaml:3064-3118` (post-edit line numbers will shift; locate by `x:Name="ProcessingOverlay"` and `x:Name="CompleteOverlay"`)

- [ ] **Step 1: ProcessingOverlay** — scrim `Background="#E61A1A1E"` → `{DynamicResource ScrimBrush}`; inner card bg → `SurfaceBrush`; progress bar `Foreground`/fill `#42A5F5` → `AccentBrush`; text tiers per map; spinner color → `AccentBrush`.

- [ ] **Step 2: CompleteOverlay** — scrim → `ScrimBrush`; card bg → `SurfaceBrush`; the green check `#4CAF50` → `SuccessBrush`; `TxtCompleteMsg` → `TextPrimaryBrush`; the Open/Folder/Start-Over buttons: primary → `AccentBrush`/`AccentTextBrush`, secondary/neutral → `SurfaceAltBrush`/`TextSecondaryBrush`.

- [ ] **Step 3: Build** — Expected `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Llamashot/Views/FileToolsWindow.xaml
git commit -m "feat(filetools): theme processing and complete overlays"
```

---

## Task 5: Imperative code-behind (cards, chips, dynamic brushes, live toggle)

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\FileToolsWindow.xaml.cs` (`CreateCard` `:240-315`, `RebuildCardPanel` `:317-...`, constructor, `ResetToolStates` `:586-701`)

- [ ] **Step 1: Add a resource-brush helper**

Add a private helper to read a themed brush from app resources:

```csharp
    private static SolidColorBrush ThemeBrush(string key)
        => (SolidColorBrush)System.Windows.Application.Current.Resources[key];
```

- [ ] **Step 2: Make `CreateCard` use theme brushes**

In `CreateCard` (`:240-315`) replace the hard-coded card colors:
- card `Background` `#1E1E24` → `ThemeBrush("SurfaceBrush")`.
- title `Foreground = Brushes.White` → `ThemeBrush("TextPrimaryBrush")`.
- desc `#777` → `ThemeBrush("TextSecondaryBrush")`.
- chevron `#555` → `ThemeBrush("TextMutedBrush")`.
- hover handler: bg `#282830` → `ThemeBrush("SurfaceHoverBrush")`; mouse-leave bg `#1E1E24` → `ThemeBrush("SurfaceBrush")`.
- Keep the per-tool `accentColor` logic for the icon tile/border (accent comes from `ToolDefs`, softened in Task 6).

- [ ] **Step 3: Theme the category headers in `RebuildCardPanel`**

In `RebuildCardPanel` (`:346-351`) the category header `Foreground` `#666` → `ThemeBrush("TextMutedBrush")`.

- [ ] **Step 4: Theme brushes in `ResetToolStates` and any other code-behind hexes**

Search `FileToolsWindow.xaml.cs` for `ConvertFromString("#` and `Brushes.White`/`Brushes.Black` used for UI chrome; map each per the migration table using `ThemeBrush(...)`. (Leave brushes that are genuinely content-related, e.g. image-editor canvas drawing colors, unchanged — only retint UI chrome.)

- [ ] **Step 5: Populate category chips + wire filtering**

Add a field for the active category and build the chips in the constructor (after `CreateToolCards()`):

```csharp
    private string _activeCategory = "All";

    private void BuildCategoryChips()
    {
        CategoryChips.Items.Clear();
        string[] cats = { "All", "PDF Tools", "Image Tools", "Office Tools", "Video & Audio", "Download" };
        foreach (var cat in cats)
        {
            var chip = new Border
            {
                Background = ThemeBrush(cat == _activeCategory ? "AccentBrush" : "SurfaceAltBrush"),
                CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand, Tag = cat
            };
            chip.Child = new TextBlock
            {
                Text = cat, FontSize = 12, FontWeight = FontWeights.Medium,
                Foreground = ThemeBrush(cat == _activeCategory ? "AccentTextBrush" : "TextSecondaryBrush")
            };
            chip.MouseLeftButtonDown += (s, _) =>
            {
                _activeCategory = (string)((Border)s).Tag;
                RebuildCardPanel();
                BuildCategoryChips();
            };
            CategoryChips.Items.Add(chip);
        }
    }
```

In `RebuildCardPanel`, after the search filter, add the category filter:

```csharp
        if (_activeCategory != "All")
            tools = tools.Where(t => t.category == _activeCategory);
```

Call `BuildCategoryChips()` in the constructor after `CreateToolCards()`.

- [ ] **Step 6: Live toggle — rebuild on theme change**

In the constructor, subscribe to theme changes and set the toggle glyph; unsubscribe on close:

```csharp
        Core.ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateThemeToggleGlyph();
        Closed += (s, e) => Core.ThemeManager.ThemeChanged -= OnThemeChanged;
```

Add the handlers (replace the temporary stub from Task 2 Step 7):

```csharp
    private void ThemeToggle_Click(object sender, RoutedEventArgs e) => Core.ThemeManager.Toggle();

    private void OnThemeChanged()
    {
        UpdateThemeToggleGlyph();
        RebuildCardPanel();   // cards use ThemeBrush() snapshots, so rebuild to repaint
        BuildCategoryChips(); // chips likewise
    }

    private void UpdateThemeToggleGlyph()
        => BtnThemeToggle.Content = Core.ThemeManager.Current == Core.ThemeManager.Dark ? "☀" : "ἱ9";
```

NOTE: `ἱ9` (🌙) is outside the BMP; in C# use the literal `"\U0001F319"` for the moon and `"☀"` (☀) for the sun. Use:

```csharp
        => BtnThemeToggle.Content = Core.ThemeManager.Current == Core.ThemeManager.Dark ? "☀" : "\U0001F319";
```

- [ ] **Step 7: Build** — Expected `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m "feat(filetools): theme-aware cards, category chips, live theme toggle"
```

---

## Task 6: Soften the per-tool accent palette

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\FileToolsWindow.xaml.cs:25-48` (`ToolDefs`)

- [ ] **Step 1: Replace the accent hexes with pastel variants** (keep ids/titles/desc/icon/category identical, only the 4th tuple field changes):

```csharp
        ("merge_pdf",      "Merge PDF",       "Combine multiple PDFs into one",      "#FF8A8A", "⊞", "PDF Tools"),
        ("split_pdf",      "Split PDF",       "Extract pages from PDF",              "#FFB088", "‖", "PDF Tools"),
        ("compress_pdf",   "Compress PDF",    "Reduce PDF file size",                "#FF9B9B", "⬇", "PDF Tools"),
        ("pdf_to_images",  "PDF to Images",   "Convert pages to JPG/PNG",            "#F58F8F", "⧉", "PDF Tools"),
        ("images_to_pdf",  "Images to PDF",   "Combine images into PDF",             "#F48FB1", "⬜", "PDF Tools"),
        ("rotate_pdf",     "Rotate PDF",      "Rotate PDF pages",                    "#FFC777", "↻", "PDF Tools"),
        ("watermark",      "Watermark PDF",   "Add text watermark",                  "#B79CFF", "♦", "PDF Tools"),
        ("page_numbers",   "Page Numbers",    "Add numbers to PDF",                  "#90A0F0", "#",      "PDF Tools"),
        ("extract_pages",  "Extract Pages",   "Pick specific pages from PDF",        "#FFB59B", "⎘", "PDF Tools"),
        ("insert_pages",   "Insert Pages",    "Add new pages to a PDF",              "#C9B0A6", "⊕", "PDF Tools"),
        ("image_editor",   "Image Editor",    "All-in-one image editor",             "#A78BFA", "⬜", "Image Tools"),
        ("compress_image", "Compress Image",  "Reduce image file size",              "#6FD9E6", "⬇", "Image Tools"),
        ("resize_image",   "Resize Image",    "Change dimensions",                   "#6FD0C3", "⤢", "Image Tools"),
        ("crop_image",     "Crop Image",      "Crop to selection",                   "#8FBEF7", "✂", "Image Tools"),
        ("rotate_flip",    "Rotate & Flip",   "Rotate or flip images",               "#C99BDB", "↺", "Image Tools"),
        ("convert_format", "Convert Format",  "Change image format",                 "#F48FB1", "⇄", "Image Tools"),
        ("compress_office","Compress Office",  "Reduce DOCX/XLSX/PPTX",              "#A7B6C2", "≣", "Office Tools"),
        ("video_tools",   "Video Tools",      "Trim, crop, rotate, flip & extract",  "#F58F8F", "▶", "Video & Audio"),
        ("extract_audio", "Extract Audio",    "Extract audio from video",            "#6FD3E6", "♫", "Video & Audio"),
        ("trim_audio",    "Trim Audio",       "Cut start and end of audio",          "#6FD0C3", "✂", "Video & Audio"),
        ("youtube_dl",    "YouTube Download", "Download video or audio from URL",    "#FF9B9B", "▶", "Download"),
```

- [ ] **Step 2: Build** — Expected `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m "feat(filetools): soften per-tool accent palette to pastels"
```

---

## Task 7: Full visual verification + version bump

**Files:**
- Modify: `D:\Llamashot\Llamashot\Llamashot.csproj` (version)
- Modify: `D:\Llamashot\Llamashot\Views\AboutWindow.xaml` (version string)

- [ ] **Step 1: Build the release/debug exe** — `dotnet build ...` Expected `Build succeeded.`

- [ ] **Step 2: Launch and screenshot**

Launch `Llamashot.exe`, open File Tools. Capture screenshots of: home (light), home (dark via toggle), one PDF tool panel (light), the same panel (dark), and the Complete overlay if reachable. Read each screenshot and confirm: soft palette applied, text legible in both themes, no leftover dark `#1A1A1E`/harsh-red chrome in light mode, toggle swaps the entire UI live including an open panel.

- [ ] **Step 3: Verify persistence** — toggle to dark, close the app, relaunch; confirm it reopens in dark. Then check `%AppData%/Llamashot/settings.json` contains `"FileToolsTheme": "Dark"`.

- [ ] **Step 4: Bump version**

In `Llamashot.csproj` increment the `<Version>`/`<AssemblyVersion>`/`<FileVersion>` (per project convention). Update the version string shown in `AboutWindow.xaml` to match.

- [ ] **Step 5: Commit**

```bash
git add Llamashot/Llamashot.csproj Llamashot/Views/AboutWindow.xaml
git commit -m "chore: bump version for File Tools modern redesign"
```

---

## Self-review notes

- **Spec coverage:** theme engine (T1), palette keys (T1), home layout + toggle + chips (T2/T5), 21 panels (T3), overlays (T4), imperative cards/brushes + live toggle (T5), softened tool accents (T6), persistence (T1/T5), verification both themes (T7). All spec sections covered.
- **Type consistency:** `ThemeBrush(string)`, `ThemeManager.Apply/Toggle/Initialize/Current/ThemeChanged`, `BuildCategoryChips()`, `OnThemeChanged()`, `UpdateThemeToggleGlyph()`, `_activeCategory` used consistently across tasks.
- **Known gotcha:** moon emoji must be `"\U0001F319"` (not `ἱ9`) in C#; flagged in T5.
- **Buildability:** `ThemeToggle_Click` stub added in T2 so the XAML compiles before T5 replaces it.
