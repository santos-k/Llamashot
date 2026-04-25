// Resolve ambiguities between System.Drawing (WinForms) and WPF types
// We primarily use WPF types, so alias the WPF ones as defaults

global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using Color = System.Windows.Media.Color;
global using Brush = System.Windows.Media.Brush;
global using Pen = System.Windows.Media.Pen;
global using FontFamily = System.Windows.Media.FontFamily;
global using Cursor = System.Windows.Input.Cursor;
global using Cursors = System.Windows.Input.Cursors;
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Clipboard = System.Windows.Clipboard;
global using Canvas = System.Windows.Controls.Canvas;
global using Keyboard = System.Windows.Input.Keyboard;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using Rectangle = System.Windows.Shapes.Rectangle;
global using Image = System.Windows.Controls.Image;
global using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
global using TextBox = System.Windows.Controls.TextBox;
global using Button = System.Windows.Controls.Button;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
global using PrintDialog = System.Windows.Controls.PrintDialog;
global using TextBlock = System.Windows.Controls.TextBlock;
global using Label = System.Windows.Controls.Label;
global using ComboBox = System.Windows.Controls.ComboBox;
global using CheckBox = System.Windows.Controls.CheckBox;
