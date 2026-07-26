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

    public HotkeyDialog(string currentHotkey, string? targetName = null)
    {
        Result = currentHotkey ?? string.Empty;
        Title = "Gán hotkey";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 380; Height = 210;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.Resources["BgBaseBrush"];

        var sp = new StackPanel { Margin = new Thickness(20) };

        sp.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(targetName)
                ? "Bấm tổ hợp phím bạn muốn gán:"
                : $"Bấm tổ hợp phím cho “{targetName}”: \nHotkey hoạt động cả khi app đang thu nhỏ.",
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        _display = new TextBlock
        {
            Text = string.IsNullOrEmpty(currentHotkey) ? "BẤM PHÍM..." : currentHotkey,
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
            Content = "XÓA PHÍM", Height = 30, Padding = new Thickness(12, 0, 12, 0),
            Style = (Style)Application.Current.Resources["FlatButtonStyle"],
            Margin = new Thickness(0, 0, 8, 0)
        };
        clear.Click += (_, _) => { Result = string.Empty; DialogResult = true; };

        var assign = new Button
        {
            Content = "GÁN", Height = 30, Padding = new Thickness(16, 0, 16, 0),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        assign.Click += (_, _) => { if (!string.IsNullOrEmpty(Result)) DialogResult = true; };

        var cancel = new Button
        {
            Content = "HỦY", Height = 30, Padding = new Thickness(12, 0, 12, 0),
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

        Result = Helpers.HotkeyUtil.Format(key, Keyboard.Modifiers);
        if (string.IsNullOrWhiteSpace(Result)) return;
        _display.Text = Result;
    }
}
