using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShowCuePlayer.Controls.Dialogs;

/// <summary>
/// Hotkey capture dialog. Press any key combination to assign to a cue.
/// </summary>
public sealed class HotkeyDialog : Window
{
    public string Result { get; private set; } = string.Empty;
    private readonly TextBlock _display;

    public HotkeyDialog(string currentHotkey)
    {
        Title = "Assign Hotkey";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 380; Height = 210;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.Resources["BgBaseBrush"];

        var sp = new StackPanel { Margin = new Thickness(20) };

        sp.Children.Add(new TextBlock
        {
            Text = "Press the key combination you want to assign:",
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        _display = new TextBlock
        {
            Text = string.IsNullOrEmpty(currentHotkey) ? "Press a key..." : currentHotkey,
            FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentPrimaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };
        sp.Children.Add(_display);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var clear = new Button
        {
            Content = "Clear", Height = 30, Padding = new Thickness(12, 0, 12, 0),
            Style = (Style)Application.Current.Resources["FlatButtonStyle"],
            Margin = new Thickness(0, 0, 8, 0)
        };
        clear.Click += (_, _) => { Result = string.Empty; DialogResult = true; };

        var assign = new Button
        {
            Content = "Assign", Height = 30, Padding = new Thickness(16, 0, 16, 0),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        assign.Click += (_, _) => { if (!string.IsNullOrEmpty(Result)) DialogResult = true; };

        var cancel = new Button
        {
            Content = "Cancel", Height = 30, Padding = new Thickness(12, 0, 12, 0),
            Style = (Style)Application.Current.Resources["FlatButtonStyle"],
            Margin = new Thickness(8, 0, 0, 0)
        };
        cancel.Click += (_, _) => DialogResult = false;

        btnRow.Children.Add(clear);
        btnRow.Children.Add(assign);
        btnRow.Children.Add(cancel);
        sp.Children.Add(btnRow);
        Content = sp;

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        var mods = Keyboard.Modifiers;
        var parts = new System.Collections.Generic.List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        Result = string.Join("+", parts);
        _display.Text = Result;
    }
}
