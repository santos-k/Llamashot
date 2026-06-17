# Snipping Toolbar & Color Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Windows Snipping Tool-style toolbar to the capture overlay with Screenshot/Video/OCR mode switching, Region/Window/Fullscreen capture types, delay timer, and window detection — plus persist the user's color selection across sessions.

**Architecture:** Integrated into the existing `OverlayWindow` as a new initial `ToolbarIdle` state. When PrintScreen is pressed, the screen is captured and dimmed, and a horizontal toolbar appears at top center. The user picks mode/capture type, then interacts with the dimmed screen. No new windows except dropdown popups. Color persistence uses the existing `AppSettings.DefaultColor` property.

**Tech Stack:** .NET 10 / WPF, Win32 interop (WindowFromPoint, GetAncestor, GetWindowRect), System.Text.Json settings

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `Core/AppSettings.cs` | Modify | Change `DefaultColor` default to `#FFFF00` |
| `Views/OverlayWindow.xaml` | Modify | Add SnippingToolbar UI, capture mode popup, delay popup |
| `Views/OverlayWindow.xaml.cs` | Modify | New enums, toolbar state, window detection, color persistence, OCR simplification |
| `Views/RecordingOverlay.xaml.cs` | Modify | Load annotation color from settings |
| `App.xaml.cs` | Modify | Update tray menu "Record Screen" to open overlay with Video pre-selected |

---

### Task 1: Color Persistence

**Files:**
- Modify: `Llamashot/Core/AppSettings.cs:56`
- Modify: `Llamashot/Views/OverlayWindow.xaml.cs:51,78-85,134-141`
- Modify: `Llamashot/Views/RecordingOverlay.xaml.cs:264`

- [ ] **Step 1: Update AppSettings default color**

In `Core/AppSettings.cs`, change line 56:

```csharp
// Before:
public string DefaultColor { get; set; } = "#FF0000";
// After:
public string DefaultColor { get; set; } = "#FFFF00";
```

- [ ] **Step 2: Load persisted color in OverlayWindow constructor**

In `Views/OverlayWindow.xaml.cs`, replace the hardcoded color initialization at line 51 and update the constructor (lines 78-85):

```csharp
private Color _currentColor = Colors.Yellow; // Will be overridden in constructor
```

Add to the constructor after `ThicknessLabel.Text = "3";` (line 84):

```csharp
// Load persisted color
try
{
    var cc = (Color)System.Windows.Media.ColorConverter.ConvertFromString(AppSettings.Instance.DefaultColor);
    _currentColor = cc;
}
catch { /* keep default yellow */ }
```

- [ ] **Step 3: Save color on palette selection**

In `Views/OverlayWindow.xaml.cs`, in the `InitializeColorPalette()` method (line 134-141), add save logic after setting the color:

```csharp
swatch.MouseLeftButtonDown += (s, e) =>
{
    _currentColor = color;
    ColorIndicator.Fill = new SolidColorBrush(color);
    if (_currentTool != null) _currentTool.StrokeColor = color;
    ColorPaletteCanvas.Visibility = Visibility.Collapsed;
    // Persist color choice
    AppSettings.Instance.DefaultColor = color.ToString();
    AppSettings.Save();
    e.Handled = true;
};
```

- [ ] **Step 4: Load persisted color in RecordingOverlay**

In `Views/RecordingOverlay.xaml.cs`, replace line 264:

```csharp
// Before:
tool.StrokeColor = Color.FromRgb(0xFF, 0xFF, 0x00); // yellow
// After:
try
{
    tool.StrokeColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(AppSettings.Instance.DefaultColor);
}
catch
{
    tool.StrokeColor = Color.FromRgb(0xFF, 0xFF, 0x00);
}
```

- [ ] **Step 5: Update ColorIndicator initial fill in XAML**

In `Views/OverlayWindow.xaml`, the `ColorIndicator` Ellipse has `Fill="Yellow"`. This needs to be set dynamically in the constructor instead. After the color load in the constructor, add:

```csharp
ColorIndicator.Fill = new SolidColorBrush(_currentColor);
```

- [ ] **Step 6: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add Llamashot/Core/AppSettings.cs Llamashot/Views/OverlayWindow.xaml Llamashot/Views/OverlayWindow.xaml.cs Llamashot/Views/RecordingOverlay.xaml.cs
git commit -m "feat: persist color selection across sessions"
```

---

### Task 2: Add Enums and State Fields for Snipping Toolbar

**Files:**
- Modify: `Llamashot/Views/OverlayWindow.xaml.cs:20-25,47-52`

- [ ] **Step 1: Add CaptureMode and CaptureType enums**

In `Views/OverlayWindow.xaml.cs`, add after the `Interaction` enum (after line 21):

```csharp
private enum CaptureMode { Screenshot, Video, Ocr }
private enum CaptureType { Region, Window, Fullscreen }
```

- [ ] **Step 2: Add ToolbarIdle and WindowSelecting to Interaction enum**

Update the `Interaction` enum at line 21:

```csharp
private enum Interaction { None, ToolbarIdle, Selecting, WindowSelecting, Resizing, Moving, Drawing, OcrSelecting }
```

- [ ] **Step 3: Add state fields**

Add after `_currentThickness` (line 52):

```csharp
// Snipping toolbar state
private CaptureMode _captureMode = CaptureMode.Screenshot;
private CaptureType _captureType = CaptureType.Region;
private int _delaySeconds = 0;
private IntPtr _highlightedWindow = IntPtr.Zero;
private System.Windows.Shapes.Rectangle? _windowHighlight;
```

- [ ] **Step 4: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build succeeds (new fields/enums unused for now — no warnings expected since they'll be used in subsequent tasks).

- [ ] **Step 5: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml.cs
git commit -m "feat: add snipping toolbar enums and state fields"
```

---

### Task 3: Add Snipping Toolbar XAML

**Files:**
- Modify: `Llamashot/Views/OverlayWindow.xaml:46-80`

- [ ] **Step 1: Add SnippingToolbar and dropdown popups to OverlayWindow.xaml**

In `Views/OverlayWindow.xaml`, add the following **inside `<Grid x:Name="RootGrid">`**, right after the `HandleCanvas` element (after line 77) and **before** the existing `<!-- Toolbars -->` comment (line 79):

```xml
        <!-- Snipping Toolbar (top center, shown before region selection) -->
        <Canvas x:Name="SnippingToolbarCanvas" Visibility="Collapsed">
            <Border x:Name="SnippingToolbar"
                    Background="#FF111111" CornerRadius="8" Padding="4"
                    BorderBrush="#333" BorderThickness="1">
                <Border.Effect>
                    <DropShadowEffect BlurRadius="12" Opacity="0.7" ShadowDepth="3" />
                </Border.Effect>
                <StackPanel Orientation="Horizontal">
                    <!-- Screenshot mode -->
                    <Button x:Name="BtnModeScreenshot" ToolTip="Screenshot (1)" Click="ModeScreenshot_Click" Style="{StaticResource TBtn}">
                        <Canvas Width="16" Height="16">
                            <Rectangle Canvas.Left="1" Canvas.Top="3" Width="14" Height="10" RadiusX="2" RadiusY="2"
                                       Stroke="#64B5F6" StrokeThickness="1.5" />
                            <Ellipse Canvas.Left="5" Canvas.Top="5" Width="6" Height="6"
                                     Stroke="#64B5F6" StrokeThickness="1.5" />
                        </Canvas>
                    </Button>
                    <!-- Video mode -->
                    <Button x:Name="BtnModeVideo" ToolTip="Video (2)" Click="ModeVideo_Click" Style="{StaticResource TBtn}">
                        <Canvas Width="16" Height="16">
                            <Rectangle Canvas.Left="1" Canvas.Top="3" Width="10" Height="10" RadiusX="2" RadiusY="2"
                                       Stroke="#888" StrokeThickness="1.5" />
                            <Path Data="M12,5 L16,3 L16,13 L12,11 Z" Fill="#888" />
                        </Canvas>
                    </Button>
                    <Rectangle Width="1" Fill="#333" Margin="3,5" />
                    <!-- Capture type dropdown -->
                    <Button x:Name="BtnCaptureType" ToolTip="Capture type" Click="CaptureType_Click" Style="{StaticResource TBtn}" Width="46">
                        <StackPanel Orientation="Horizontal">
                            <Canvas Width="16" Height="16" x:Name="CaptureTypeIcon">
                                <Rectangle Canvas.Left="1" Canvas.Top="1" Width="14" Height="12"
                                           Stroke="#CCC" StrokeThickness="1.5" StrokeDashArray="3 1.5" />
                            </Canvas>
                            <Path Data="M0,0 L4,4 L8,0" Stroke="#888" StrokeThickness="1.5" Margin="2,6,0,0" Width="8" Height="5" />
                        </StackPanel>
                    </Button>
                    <Rectangle Width="1" Fill="#333" Margin="3,5" />
                    <!-- OCR mode -->
                    <Button x:Name="BtnModeOcr" ToolTip="Text Extract (3)" Click="ModeOcr_Click" Style="{StaticResource TBtn}">
                        <Canvas Width="16" Height="16">
                            <Ellipse Canvas.Left="1" Canvas.Top="0" Width="10" Height="10"
                                     Stroke="#26C6DA" StrokeThickness="1.5" />
                            <TextBlock Canvas.Left="3.5" Canvas.Top="-1" Text="T" Foreground="#26C6DA"
                                       FontSize="8" FontWeight="Bold" />
                            <Line X1="9.5" Y1="9" X2="14" Y2="13.5" Stroke="#00ACC1" StrokeThickness="2"
                                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
                        </Canvas>
                    </Button>
                    <!-- Delay dropdown -->
                    <Button x:Name="BtnDelay" ToolTip="Delay timer" Click="Delay_Click" Style="{StaticResource TBtn}" Width="50">
                        <StackPanel Orientation="Horizontal">
                            <Canvas Width="14" Height="14">
                                <Ellipse Width="14" Height="14" Stroke="#888" StrokeThickness="1.5" />
                                <Line X1="7" Y1="3" X2="7" Y2="7" Stroke="#888" StrokeThickness="1.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
                                <Line X1="7" Y1="7" X2="10" Y2="9" Stroke="#888" StrokeThickness="1.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
                            </Canvas>
                            <TextBlock x:Name="DelayLabel" Text="" Foreground="#AAA" FontSize="11" Margin="2,0,0,0" VerticalAlignment="Center" />
                            <Path Data="M0,0 L4,4 L8,0" Stroke="#888" StrokeThickness="1.5" Margin="2,6,0,0" Width="8" Height="5" />
                        </StackPanel>
                    </Button>
                    <Rectangle Width="1" Fill="#333" Margin="3,5" />
                    <!-- Close -->
                    <Button x:Name="BtnSnippingClose" ToolTip="Close (Esc)" Click="Close_Click" Style="{StaticResource TBtn}">
                        <Canvas Width="14" Height="14">
                            <Line X1="2" Y1="2" X2="12" Y2="12" Stroke="#EF5350" StrokeThickness="2.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
                            <Line X1="12" Y1="2" X2="2" Y2="12" Stroke="#EF5350" StrokeThickness="2.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
                        </Canvas>
                    </Button>
                </StackPanel>
            </Border>
            <!-- Mode underline indicator -->
            <Border x:Name="ModeIndicator" Height="3" Width="22" CornerRadius="1.5" Background="#2196F3"
                    IsHitTestVisible="False" />
        </Canvas>

        <!-- Capture type dropdown popup -->
        <Canvas x:Name="CaptureTypePopupCanvas" Visibility="Collapsed">
            <Border x:Name="CaptureTypePopup"
                    Background="#FF111111" CornerRadius="6" Padding="4"
                    BorderBrush="#333" BorderThickness="1">
                <Border.Effect>
                    <DropShadowEffect BlurRadius="8" Opacity="0.5" ShadowDepth="2" />
                </Border.Effect>
                <StackPanel>
                    <Button x:Name="BtnTypeRegion" Click="TypeRegion_Click" Style="{StaticResource TBtn}" Width="120" Height="28" HorizontalContentAlignment="Left">
                        <StackPanel Orientation="Horizontal" Margin="4,0">
                            <Canvas Width="16" Height="16">
                                <Rectangle Canvas.Left="1" Canvas.Top="1" Width="14" Height="12"
                                           Stroke="#CCC" StrokeThickness="1.5" StrokeDashArray="3 1.5" />
                            </Canvas>
                            <TextBlock Text="Region" Foreground="#CCC" FontSize="12" Margin="8,0,0,0" VerticalAlignment="Center" />
                        </StackPanel>
                    </Button>
                    <Button x:Name="BtnTypeWindow" Click="TypeWindow_Click" Style="{StaticResource TBtn}" Width="120" Height="28" HorizontalContentAlignment="Left">
                        <StackPanel Orientation="Horizontal" Margin="4,0">
                            <Canvas Width="16" Height="16">
                                <Rectangle Canvas.Left="1" Canvas.Top="1" Width="14" Height="12"
                                           Stroke="#CCC" StrokeThickness="1.5" />
                                <Rectangle Canvas.Left="1" Canvas.Top="1" Width="14" Height="3"
                                           Fill="#555" />
                            </Canvas>
                            <TextBlock Text="Window" Foreground="#CCC" FontSize="12" Margin="8,0,0,0" VerticalAlignment="Center" />
                        </StackPanel>
                    </Button>
                    <Button x:Name="BtnTypeFullscreen" Click="TypeFullscreen_Click" Style="{StaticResource TBtn}" Width="120" Height="28" HorizontalContentAlignment="Left">
                        <StackPanel Orientation="Horizontal" Margin="4,0">
                            <Canvas Width="16" Height="16">
                                <Rectangle Canvas.Left="1" Canvas.Top="1" Width="14" Height="12"
                                           Stroke="#CCC" StrokeThickness="1.5" />
                                <Ellipse Canvas.Left="5" Canvas.Top="4" Width="6" Height="6"
                                         Fill="#CCC" />
                            </Canvas>
                            <TextBlock Text="Full Screen" Foreground="#CCC" FontSize="12" Margin="8,0,0,0" VerticalAlignment="Center" />
                        </StackPanel>
                    </Button>
                </StackPanel>
            </Border>
        </Canvas>

        <!-- Delay dropdown popup -->
        <Canvas x:Name="DelayPopupCanvas" Visibility="Collapsed">
            <Border x:Name="DelayPopup"
                    Background="#FF111111" CornerRadius="6" Padding="4"
                    BorderBrush="#333" BorderThickness="1">
                <Border.Effect>
                    <DropShadowEffect BlurRadius="8" Opacity="0.5" ShadowDepth="2" />
                </Border.Effect>
                <StackPanel x:Name="DelayOptions" />
            </Border>
        </Canvas>
```

- [ ] **Step 2: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build fails — event handlers referenced in XAML don't exist yet. This is expected; they'll be added in the next task.

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml
git commit -m "feat: add snipping toolbar XAML layout"
```

---

### Task 4: Implement Snipping Toolbar Logic

**Files:**
- Modify: `Llamashot/Views/OverlayWindow.xaml.cs`

This is the largest task. It adds all toolbar button handlers, mode/type switching, toolbar positioning, and delay popup initialization.

- [ ] **Step 1: Add toolbar initialization and positioning**

In `Views/OverlayWindow.xaml.cs`, add the following method after `InitializeThicknessPopup()` (after line 191):

```csharp
    private void InitializeDelayPopup()
    {
        foreach (var (label, seconds) in new[] { ("No delay", 0), ("1 second", 1), ("3 seconds", 3), ("5 seconds", 5), ("10 seconds", 10) })
        {
            int val = seconds;
            string text = label;
            var btn = new Button
            {
                Width = 100, Height = 26,
                Content = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12 },
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 0, 0, 0)
            };
            btn.Click += (s, e) =>
            {
                _delaySeconds = val;
                DelayLabel.Text = val > 0 ? $"{val}s" : "";
                DelayPopupCanvas.Visibility = Visibility.Collapsed;
            };
            DelayOptions.Children.Add(btn);
        }
    }

    private void PositionSnippingToolbar()
    {
        SnippingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = SnippingToolbar.DesiredSize.Width;
        var h = SnippingToolbar.DesiredSize.Height;
        double left = (ActualWidth - w) / 2;
        double top = 40;
        Canvas.SetLeft(SnippingToolbar, left);
        Canvas.SetTop(SnippingToolbar, top);

        // Position mode indicator under the active mode button
        UpdateModeIndicator();
    }

    private void UpdateModeIndicator()
    {
        Button activeBtn = _captureMode switch
        {
            CaptureMode.Video => BtnModeVideo,
            CaptureMode.Ocr => BtnModeOcr,
            _ => BtnModeScreenshot
        };
        var color = _captureMode switch
        {
            CaptureMode.Video => Color.FromRgb(0xF4, 0x43, 0x36),
            CaptureMode.Ocr => Color.FromRgb(0x26, 0xC6, 0xDA),
            _ => Color.FromRgb(0x21, 0x96, 0xF3)
        };
        ModeIndicator.Background = new SolidColorBrush(color);

        // Position indicator under the button
        var btnPos = activeBtn.TranslatePoint(new Point(0, 0), RootGrid);
        Canvas.SetLeft(ModeIndicator, btnPos.X + (activeBtn.ActualWidth - 22) / 2);
        Canvas.SetTop(ModeIndicator, btnPos.Y + activeBtn.ActualHeight + 2);

        // Update icon colors
        UpdateModeButtonColors();
    }

    private void UpdateModeButtonColors()
    {
        // Screenshot icon
        foreach (var child in ((Canvas)BtnModeScreenshot.Content).Children)
        {
            var c = _captureMode == CaptureMode.Screenshot ? "#64B5F6" : "#888";
            if (child is System.Windows.Shapes.Rectangle r) { r.Stroke = (Brush)new BrushConverter().ConvertFromString(c)!; }
            if (child is Ellipse el) { el.Stroke = (Brush)new BrushConverter().ConvertFromString(c)!; }
        }
        // Video icon
        foreach (var child in ((Canvas)BtnModeVideo.Content).Children)
        {
            var c = _captureMode == CaptureMode.Video ? "#F44336" : "#888";
            if (child is System.Windows.Shapes.Rectangle r) { r.Stroke = (Brush)new BrushConverter().ConvertFromString(c)!; }
            if (child is System.Windows.Shapes.Path p) { p.Fill = (Brush)new BrushConverter().ConvertFromString(c)!; }
        }
        // OCR icon colors stay fixed (#26C6DA) since it has its own distinct look
    }
```

- [ ] **Step 2: Add mode and capture type button handlers**

Add after the methods above:

```csharp
    // ============ SNIPPING TOOLBAR HANDLERS ============

    private void ModeScreenshot_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = CaptureMode.Screenshot;
        OnModeChanged();
    }

    private void ModeVideo_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = CaptureMode.Video;
        OnModeChanged();
    }

    private void ModeOcr_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = CaptureMode.Ocr;
        OnModeChanged();
    }

    private void OnModeChanged()
    {
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        DelayPopupCanvas.Visibility = Visibility.Collapsed;
        UpdateModeIndicator();

        // If capture type is Fullscreen, execute immediately
        if (_captureType == CaptureType.Fullscreen)
            ExecuteFullscreenCapture();
        else if (_captureType == CaptureType.Window)
            EnterWindowSelecting();
        else
            Cursor = Cursors.Cross;
    }

    private void CaptureType_Click(object sender, RoutedEventArgs e)
    {
        DelayPopupCanvas.Visibility = Visibility.Collapsed;
        if (CaptureTypePopupCanvas.Visibility == Visibility.Visible)
        {
            CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
            return;
        }
        var btnPos = BtnCaptureType.TranslatePoint(new Point(0, 0), RootGrid);
        Canvas.SetLeft(CaptureTypePopup, btnPos.X);
        Canvas.SetTop(CaptureTypePopup, btnPos.Y + BtnCaptureType.ActualHeight + 4);
        CaptureTypePopupCanvas.Visibility = Visibility.Visible;
    }

    private void TypeRegion_Click(object sender, RoutedEventArgs e)
    {
        _captureType = CaptureType.Region;
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        UpdateCaptureTypeIcon();
        ClearWindowHighlight();
        _interaction = Interaction.ToolbarIdle;
        Cursor = Cursors.Cross;
    }

    private void TypeWindow_Click(object sender, RoutedEventArgs e)
    {
        _captureType = CaptureType.Window;
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        UpdateCaptureTypeIcon();
        EnterWindowSelecting();
    }

    private void TypeFullscreen_Click(object sender, RoutedEventArgs e)
    {
        _captureType = CaptureType.Fullscreen;
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        UpdateCaptureTypeIcon();
        ExecuteFullscreenCapture();
    }

    private void UpdateCaptureTypeIcon()
    {
        // Update the icon in the capture type button to reflect current selection
        var canvas = CaptureTypeIcon;
        canvas.Children.Clear();
        switch (_captureType)
        {
            case CaptureType.Region:
                canvas.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = 14, Height = 12, Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC")),
                    StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 1.5 }
                });
                Canvas.SetLeft(canvas.Children[0], 1);
                Canvas.SetTop(canvas.Children[0], 1);
                break;
            case CaptureType.Window:
                var r1 = new System.Windows.Shapes.Rectangle { Width = 14, Height = 12, Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC")), StrokeThickness = 1.5 };
                Canvas.SetLeft(r1, 1); Canvas.SetTop(r1, 1);
                var r2 = new System.Windows.Shapes.Rectangle { Width = 14, Height = 3, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")) };
                Canvas.SetLeft(r2, 1); Canvas.SetTop(r2, 1);
                canvas.Children.Add(r1);
                canvas.Children.Add(r2);
                break;
            case CaptureType.Fullscreen:
                var r3 = new System.Windows.Shapes.Rectangle { Width = 14, Height = 12, Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC")), StrokeThickness = 1.5 };
                Canvas.SetLeft(r3, 1); Canvas.SetTop(r3, 1);
                var el = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC")) };
                Canvas.SetLeft(el, 5); Canvas.SetTop(el, 4);
                canvas.Children.Add(r3);
                canvas.Children.Add(el);
                break;
        }
    }

    private void Delay_Click(object sender, RoutedEventArgs e)
    {
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        if (DelayPopupCanvas.Visibility == Visibility.Visible)
        {
            DelayPopupCanvas.Visibility = Visibility.Collapsed;
            return;
        }
        var btnPos = BtnDelay.TranslatePoint(new Point(0, 0), RootGrid);
        Canvas.SetLeft(DelayPopup, btnPos.X);
        Canvas.SetTop(DelayPopup, btnPos.Y + BtnDelay.ActualHeight + 4);
        DelayPopupCanvas.Visibility = Visibility.Visible;
    }
```

- [ ] **Step 3: Update StartCapture to begin in ToolbarIdle state**

Replace the `StartCapture()` method (lines 87-118):

```csharp
    public void StartCapture(CaptureMode initialMode = CaptureMode.Screenshot)
    {
        _virtualBounds = ScreenCapture.GetVirtualScreenBounds();
        _screenshot = ScreenCapture.CaptureFullScreen();
        ScreenshotImage.Source = _screenshot;

        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;

        UpdateDimming(Rect.Empty);

        _interaction = Interaction.ToolbarIdle;
        _captureMode = initialMode;
        _captureType = CaptureType.Region;
        _hasSelection = false;
        _currentTool = null;
        _currentToolTag = null;
        _highlightedWindow = IntPtr.Zero;
        ClearWindowHighlight();
        SelectionBorder.Visibility = Visibility.Collapsed;
        DimensionBorder.Visibility = Visibility.Collapsed;
        ToolbarCanvas.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Visibility = Visibility.Collapsed;
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
        ThicknessPopupCanvas.Visibility = Visibility.Collapsed;
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        DelayPopupCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Children.Clear();
        _undoStack.Clear();
        _redoStack.Clear();

        // Show snipping toolbar
        SnippingToolbarCanvas.Visibility = Visibility.Visible;
        Cursor = Cursors.Cross;

        Show();
        Activate();
        Focus();

        // Position toolbar after layout
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, PositionSnippingToolbar);
    }
```

- [ ] **Step 4: Call InitializeDelayPopup in constructor**

In the constructor, add after `InitializeThicknessPopup();` (line 82):

```csharp
InitializeDelayPopup();
```

- [ ] **Step 5: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build may fail due to missing `ExecuteFullscreenCapture`, `EnterWindowSelecting`, `ClearWindowHighlight` methods. These will be added in the next tasks. Add stub methods for now:

```csharp
    private void ExecuteFullscreenCapture() { /* Task 6 */ }
    private void EnterWindowSelecting() { /* Task 5 */ }
    private void ClearWindowHighlight()
    {
        if (_windowHighlight != null)
        {
            MainCanvas.Children.Remove(_windowHighlight);
            _windowHighlight = null;
        }
        _highlightedWindow = IntPtr.Zero;
    }
```

Run build again. Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml.cs
git commit -m "feat: implement snipping toolbar button handlers and state management"
```

---

### Task 5: Implement Window Capture Detection

**Files:**
- Modify: `Llamashot/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Implement EnterWindowSelecting**

Replace the stub `EnterWindowSelecting()`:

```csharp
    private void EnterWindowSelecting()
    {
        _interaction = Interaction.WindowSelecting;
        Cursor = Cursors.Arrow;
        ClearWindowHighlight();
    }
```

- [ ] **Step 2: Add window highlight logic to Canvas_MouseMove**

In `Canvas_MouseMove` (line 268), add a new case to the switch statement, before the `case Interaction.None when _hasSelection:` case:

```csharp
            case Interaction.WindowSelecting:
                UpdateWindowHighlight(pos);
                break;
```

- [ ] **Step 3: Implement UpdateWindowHighlight**

Add this method:

```csharp
    private void UpdateWindowHighlight(Point dipPos)
    {
        // Convert DIP position to screen pixels
        double dpi = GetDpiScale();
        var screenPt = new NativeMethods.POINT
        {
            X = (int)((dipPos.X + _virtualBounds.X) * dpi),
            Y = (int)((dipPos.Y + _virtualBounds.Y) * dpi)
        };

        var hwnd = NativeMethods.WindowFromPoint(screenPt);
        if (hwnd == IntPtr.Zero) return;

        // Get the top-level ancestor
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
        if (root != IntPtr.Zero) hwnd = root;

        // Skip our own window
        var ourHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == ourHwnd) return;

        // Skip if same window as already highlighted
        if (hwnd == _highlightedWindow) return;
        _highlightedWindow = hwnd;

        // Get window bounds in pixels
        NativeMethods.GetWindowRect(hwnd, out var rect);

        // Convert pixel bounds to DIPs relative to our overlay
        double x = rect.Left / dpi - _virtualBounds.X;
        double y = rect.Top / dpi - _virtualBounds.Y;
        double w = (rect.Right - rect.Left) / dpi;
        double h = (rect.Bottom - rect.Top) / dpi;

        // Clamp to our overlay bounds
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        w = Math.Min(w, ActualWidth - x);
        h = Math.Min(h, ActualHeight - y);

        if (w < 10 || h < 10) return;

        // Update or create highlight rectangle
        if (_windowHighlight == null)
        {
            _windowHighlight = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                StrokeThickness = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0x20, 0x21, 0x96, 0xF3)),
                IsHitTestVisible = false
            };
            MainCanvas.Children.Add(_windowHighlight);
        }

        Canvas.SetLeft(_windowHighlight, x);
        Canvas.SetTop(_windowHighlight, y);
        _windowHighlight.Width = w;
        _windowHighlight.Height = h;
    }
```

- [ ] **Step 4: Handle click in WindowSelecting mode**

In `Canvas_MouseLeftButtonDown` (line 195), add a check at the very beginning of the method (after `var pos` and the ColorPalette collapse):

```csharp
        // Window capture: click selects the highlighted window
        if (_interaction == Interaction.WindowSelecting && _highlightedWindow != IntPtr.Zero)
        {
            SelectHighlightedWindow();
            e.Handled = true;
            return;
        }
```

- [ ] **Step 5: Implement SelectHighlightedWindow**

```csharp
    private void SelectHighlightedWindow()
    {
        if (_windowHighlight == null) return;

        double x = Canvas.GetLeft(_windowHighlight);
        double y = Canvas.GetTop(_windowHighlight);
        double w = _windowHighlight.Width;
        double h = _windowHighlight.Height;

        ClearWindowHighlight();
        SnippingToolbarCanvas.Visibility = Visibility.Collapsed;

        if (_captureMode == CaptureMode.Video)
        {
            LaunchVideoRecording(new Rect(x, y, w, h));
        }
        else if (_captureMode == CaptureMode.Ocr)
        {
            PerformDirectOcr(new Rect(x, y, w, h));
        }
        else
        {
            // Screenshot mode: set selection and show annotation tools
            _selStart = new Point(x, y);
            _selEnd = new Point(x + w, y + h);
            _selection = new Rect(x, y, w, h);
            _hasSelection = true;
            _interaction = Interaction.None;

            SelectionBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBorder, x);
            Canvas.SetTop(SelectionBorder, y);
            SelectionBorder.Width = w;
            SelectionBorder.Height = h;

            DimensionText.Text = $"{(int)w} x {(int)h}";
            Canvas.SetLeft(DimensionBorder, x);
            Canvas.SetTop(DimensionBorder, Math.Max(0, y - 28));
            DimensionBorder.Visibility = Visibility.Visible;

            UpdateDimming(_selection);
            ShowToolbars();
            ShowResizeHandles();
            Focus();
        }
    }
```

- [ ] **Step 6: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build may fail due to missing `LaunchVideoRecording` and `PerformDirectOcr`. Add stubs:

```csharp
    private void LaunchVideoRecording(Rect dipRegion) { /* Task 6 */ }
    private void PerformDirectOcr(Rect dipRegion) { /* Task 7 */ }
```

Build should succeed.

- [ ] **Step 7: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml.cs
git commit -m "feat: implement window detection and highlight for capture"
```

---

### Task 6: Implement Fullscreen Capture and Video Launch

**Files:**
- Modify: `Llamashot/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Implement ExecuteFullscreenCapture**

Replace the stub:

```csharp
    private void ExecuteFullscreenCapture()
    {
        SnippingToolbarCanvas.Visibility = Visibility.Collapsed;
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        DelayPopupCanvas.Visibility = Visibility.Collapsed;

        if (_delaySeconds > 0)
        {
            ExecuteWithDelay(() => ExecuteFullscreenCapture_Inner());
            return;
        }

        ExecuteFullscreenCapture_Inner();
    }

    private void ExecuteFullscreenCapture_Inner()
    {
        if (_captureMode == CaptureMode.Video)
        {
            LaunchVideoRecording(new Rect(0, 0, ActualWidth, ActualHeight));
        }
        else if (_captureMode == CaptureMode.Ocr)
        {
            PerformDirectOcr(new Rect(0, 0, ActualWidth, ActualHeight));
        }
        else
        {
            // Screenshot fullscreen: set full selection and show annotation tools
            _selStart = new Point(0, 0);
            _selEnd = new Point(ActualWidth, ActualHeight);
            _selection = new Rect(0, 0, ActualWidth, ActualHeight);
            _hasSelection = true;
            _isFullRegion = true;
            _interaction = Interaction.None;

            SelectionBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBorder, 0);
            Canvas.SetTop(SelectionBorder, 0);
            SelectionBorder.Width = ActualWidth;
            SelectionBorder.Height = ActualHeight;

            DimensionText.Text = $"{(int)ActualWidth} x {(int)ActualHeight}";
            Canvas.SetLeft(DimensionBorder, 0);
            Canvas.SetTop(DimensionBorder, 0);
            DimensionBorder.Visibility = Visibility.Visible;

            UpdateDimming(_selection);
            ShowToolbars();
            ShowResizeHandles();
            Focus();
        }
    }
```

- [ ] **Step 2: Implement LaunchVideoRecording**

Replace the stub:

```csharp
    private void LaunchVideoRecording(Rect dipRegion)
    {
        var dpi = GetDpiScale();
        int px = (int)(dipRegion.X * dpi);
        int py = (int)(dipRegion.Y * dpi);
        int pw = (int)(dipRegion.Width * dpi);
        int ph = (int)(dipRegion.Height * dpi);

        double dx = dipRegion.X + Left;
        double dy = dipRegion.Y + Top;

        Hide();

        var overlay = new RecordingOverlay(px, py, pw, ph, dx, dy, dipRegion.Width, dipRegion.Height);
        overlay.Show();
        Close();
    }
```

- [ ] **Step 3: Implement ExecuteWithDelay**

```csharp
    private async void ExecuteWithDelay(Action afterDelay)
    {
        // Hide overlay, show countdown, then re-capture and execute
        Hide();

        for (int i = _delaySeconds; i > 0; i--)
        {
            // Brief visual feedback could use tray balloon or a tiny overlay
            await Task.Delay(1000);
        }

        // Re-capture screen after delay
        _screenshot = ScreenCapture.CaptureFullScreen();
        ScreenshotImage.Source = _screenshot;

        Show();
        Activate();
        Focus();

        afterDelay();
    }
```

- [ ] **Step 4: Update mouse down handling for ToolbarIdle region selection**

In `Canvas_MouseLeftButtonDown`, the existing code at lines 257-265 starts a new selection. We need to add a check: when in `ToolbarIdle` state, the click should start region selection and hide the snipping toolbar. Modify the section starting at `// Start new selection` (line 257):

After the window-selecting check added in Task 5, add before the existing `if (_hasSelection)` block:

```csharp
        // ToolbarIdle: clicking on dimmed area starts region selection
        if (_interaction == Interaction.ToolbarIdle)
        {
            if (_captureType == CaptureType.Region)
            {
                if (_delaySeconds > 0)
                {
                    ExecuteWithDelay(() =>
                    {
                        // After delay, show overlay and let user draw region
                        SnippingToolbarCanvas.Visibility = Visibility.Collapsed;
                        _interaction = Interaction.ToolbarIdle;
                        Cursor = Cursors.Cross;
                    });
                    e.Handled = true;
                    return;
                }

                // Hide toolbar, start selecting
                SnippingToolbarCanvas.Visibility = Visibility.Collapsed;
                CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
                DelayPopupCanvas.Visibility = Visibility.Collapsed;

                if (_captureMode == CaptureMode.Video)
                {
                    // For video: remove dimming so user sees live screen
                    DimmingPath.Data = Geometry.Empty;
                    ScreenshotImage.Source = null;
                }

                _interaction = Interaction.Selecting;
                _selStart = pos;
                _selEnd = pos;
                MainCanvas.CaptureMouse();
                SelectionBorder.Visibility = Visibility.Visible;
                DimensionBorder.Visibility = Visibility.Visible;
                UpdateSelectionVisuals();
                e.Handled = true;
                return;
            }
            // Window and Fullscreen are handled by their button clicks
            e.Handled = true;
            return;
        }
```

- [ ] **Step 5: Update mouse up to handle Video mode after region selection**

In `Canvas_MouseLeftButtonUp` (line 313), modify the `Interaction.Selecting` case to check for video/OCR mode:

```csharp
            case Interaction.Selecting:
                _selEnd = e.GetPosition(MainCanvas);
                UpdateSelectionVisuals();
                if (_selection.Width > 5 && _selection.Height > 5)
                {
                    if (_captureMode == CaptureMode.Video)
                    {
                        LaunchVideoRecording(_selection);
                        return;
                    }
                    else if (_captureMode == CaptureMode.Ocr)
                    {
                        PerformDirectOcr(_selection);
                        return;
                    }
                    _hasSelection = true;
                    ShowToolbars();
                    ShowResizeHandles();
                }
                break;
```

- [ ] **Step 6: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build succeeds (PerformDirectOcr is still a stub from Task 5).

- [ ] **Step 7: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml.cs
git commit -m "feat: implement fullscreen capture, video launch, and delay timer"
```

---

### Task 7: Implement Direct OCR Flow

**Files:**
- Modify: `Llamashot/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Implement PerformDirectOcr**

Replace the stub:

```csharp
    private async void PerformDirectOcr(Rect dipRegion)
    {
        if (_screenshot == null) return;

        double dpi = GetDpiScale();
        int x = Math.Max(0, (int)(dipRegion.X * dpi));
        int y = Math.Max(0, (int)(dipRegion.Y * dpi));
        int w = (int)(dipRegion.Width * dpi);
        int h = (int)(dipRegion.Height * dpi);

        w = Math.Min(w, _screenshot.PixelWidth - x);
        h = Math.Min(h, _screenshot.PixelHeight - y);

        if (w < 5 || h < 5) { Close(); return; }

        var region = new Int32Rect(x, y, w, h);
        var cropped = ScreenCapture.CropBitmap(_screenshot, region);

        try
        {
            var text = await Core.OcrHelper.ExtractTextAsync(cropped);
            if (!string.IsNullOrWhiteSpace(text))
            {
                Clipboard.SetText(text);
            }
        }
        catch { /* silent failure */ }

        Close();
    }
```

- [ ] **Step 2: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml.cs
git commit -m "feat: implement direct OCR extract-copy-close flow"
```

---

### Task 8: Add Toolbar Keyboard Shortcuts

**Files:**
- Modify: `Llamashot/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Add toolbar-phase keyboard handling**

In `Window_KeyDown` (line 1173), add a block right after the TextBox typing check (after line 1184) and before the Space/pan check:

```csharp
        // Snipping toolbar shortcuts (only when toolbar is visible, before selection)
        if (_interaction == Interaction.ToolbarIdle || _interaction == Interaction.WindowSelecting)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D1) { ModeScreenshot_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.D2) { ModeVideo_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.D3) { ModeOcr_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.R) { TypeRegion_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.W) { TypeWindow_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.F) { TypeFullscreen_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            return; // Don't process annotation shortcuts while toolbar is active
        }
```

- [ ] **Step 2: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml.cs
git commit -m "feat: add keyboard shortcuts for snipping toolbar modes"
```

---

### Task 9: Update App.xaml.cs and Tray Menu

**Files:**
- Modify: `Llamashot/App.xaml.cs:200-201,250-257`

- [ ] **Step 1: Update StartRegionCapture to use new StartCapture signature**

The method at line 250-257 stays the same — `StartCapture()` defaults to `CaptureMode.Screenshot`.

- [ ] **Step 2: Update StartRecordCapture to open toolbar with Video mode**

Replace `StartRecordCapture()` (lines 259-276):

```csharp
    private void StartRecordCapture()
    {
        Dispatcher.Invoke(() =>
        {
            var overlay = new OverlayWindow();
            overlay.StartCapture(OverlayWindow.CaptureMode.Video);
        });
    }
```

Wait — `CaptureMode` is a private enum inside OverlayWindow. We need to either make it internal or add a public method. The simplest approach is to add a parameter that takes a string or make the enum internal.

- [ ] **Step 3: Make CaptureMode accessible from App.xaml.cs**

In `OverlayWindow.xaml.cs`, change the enum visibility from private to internal:

```csharp
internal enum CaptureMode { Screenshot, Video, Ocr }
```

And make `StartCapture` parameter use it:

```csharp
public void StartCapture(CaptureMode initialMode = CaptureMode.Screenshot)
```

This is already done in Task 4 Step 3. Just change `private enum CaptureMode` to `internal enum CaptureMode` (or nest it and keep it accessible via `OverlayWindow.CaptureMode`).

Actually, since `CaptureMode` is defined inside `OverlayWindow` class, the caller in App.xaml.cs would use `OverlayWindow.CaptureMode.Video`. But the enum must not be private. Change to:

```csharp
internal enum CaptureMode { Screenshot, Video, Ocr }
```

Then in `App.xaml.cs`, `StartRecordCapture`:

```csharp
    private void StartRecordCapture()
    {
        Dispatcher.Invoke(() =>
        {
            var overlay = new OverlayWindow();
            overlay.StartCapture(OverlayWindow.CaptureMode.Video);
        });
    }
```

- [ ] **Step 4: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml.cs Llamashot/App.xaml.cs
git commit -m "feat: update tray menu to open snipping toolbar with video mode"
```

---

### Task 10: Handle Popup Dismissal and Edge Cases

**Files:**
- Modify: `Llamashot/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Dismiss popups on click outside**

In `Canvas_MouseLeftButtonDown`, at the very top after `var pos = ...`:

```csharp
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        DelayPopupCanvas.Visibility = Visibility.Collapsed;
```

(Note: `ColorPaletteCanvas.Visibility = Visibility.Collapsed;` is already there at line 198.)

- [ ] **Step 2: Ensure snipping toolbar hides on annotation phase**

In `ShowToolbars()` (line 543), add:

```csharp
    private void ShowToolbars()
    {
        SnippingToolbarCanvas.Visibility = Visibility.Collapsed;
        CaptureTypePopupCanvas.Visibility = Visibility.Collapsed;
        DelayPopupCanvas.Visibility = Visibility.Collapsed;
        ToolbarCanvas.Visibility = Visibility.Visible;
        DrawingCanvas.Visibility = Visibility.Visible;
        UpdateToolbarPositions();
        UpdateDrawingCanvasClip();
    }
```

- [ ] **Step 3: Handle mouse move during ToolbarIdle for cursor feedback**

In `Canvas_MouseMove`, add a case for ToolbarIdle (the cursor should stay as crosshair when in Region mode):

```csharp
            case Interaction.ToolbarIdle:
                // Cursor already set based on capture type
                break;
```

- [ ] **Step 4: Handle recording shortcut 'T' in the global keyboard hook**

In `App.xaml.cs`, the recording shortcut hook (line 142-155) already handles 'T' via `0x54 => 'T'` being passed to `HandleShortcut`. Check if it's already there:

Looking at the code at lines 142-153, 'T' (0x54) is NOT in the list. Add it:

```csharp
                char key = vkCode switch
                {
                    0x50 => 'P', // P - Pen
                    0x41 => 'A', // A - Arrow
                    0x52 => 'R', // R - Rectangle
                    0x54 => 'T', // T - Text
                    0x43 => 'C', // C - Clear
                    0x4D => 'M', // M - Mic toggle
                    0x53 => 'S', // S - System audio toggle
                    0x20 => ' ', // Space - Pause/Resume
                    0x51 => 'Q', // Q - Stop
                    _ => '\0'
                };
```

- [ ] **Step 5: Build and verify**

Run: `cd D:/Llamashot/Llamashot && dotnet build`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add Llamashot/Views/OverlayWindow.xaml.cs Llamashot/App.xaml.cs
git commit -m "feat: handle popup dismissal, edge cases, and recording text shortcut"
```

---

### Task 11: Full Build, Manual Test, and Final Commit

**Files:**
- All modified files

- [ ] **Step 1: Kill any running Llamashot instance**

```bash
taskkill //IM Llamashot.exe //F 2>/dev/null; echo "done"
```

- [ ] **Step 2: Clean build**

```bash
cd D:/Llamashot/Llamashot && dotnet build -c Release
```

Expected: Build succeeds with no errors.

- [ ] **Step 3: Run the app**

```bash
cd D:/Llamashot/Llamashot && dotnet run &
```

- [ ] **Step 4: Verify features**

Manual test checklist:
1. Press PrintScreen → snipping toolbar appears at top center over dimmed screen
2. Default mode is Screenshot with blue underline
3. Click on dimmed area and drag → region selection works, annotation toolbar appears
4. Press Escape → overlay closes
5. Press PrintScreen → toolbar → click Video → red underline → draw region → recording starts
6. Press PrintScreen → toolbar → click OCR → draw region → text copied to clipboard → overlay closes
7. Press PrintScreen → toolbar → Capture dropdown → Window → hover highlights windows → click captures
8. Press PrintScreen → toolbar → Capture dropdown → Fullscreen → immediate capture
9. Press PrintScreen → toolbar → Delay dropdown → 3s → click area → 3 second delay → capture
10. Change color in annotation palette → close → reopen → color is remembered
11. Shift+PrintScreen still does direct fullscreen save
12. Ctrl+PrintScreen still does direct fullscreen copy
13. Tray "Record Screen" opens toolbar with Video pre-selected
14. Keyboard shortcuts: 1/2/3 switch modes, R/W/F switch capture types

- [ ] **Step 5: Fix any issues found during testing**

Address any bugs discovered during manual testing.

- [ ] **Step 6: Final commit if any fixes were made**

```bash
git add -A
git commit -m "fix: address issues found during testing"
```
