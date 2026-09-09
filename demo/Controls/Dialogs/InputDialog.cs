using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShowCuePlayer.Controls.Dialogs;

/// <summary>Simple text input dialog for renaming cues.</summary>
public sealed class InputDialog : Window
{
    public string Result { get; private set; } = string.Empty;
    private readonly TextBox _textBox;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 440;
        MinHeight = 205;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        UseLayoutRounding = true;
        Background = (Brush)Application.Current.Resources["BgBaseBrush"];

        var sp = new StackPanel { Margin = new Thickness(16) };

        sp.Children.Add(new TextBlock
        {
            Text = prompt,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        });

        _textBox = new TextBox
        {
            Text = defaultValue,
            Style = (Style)Application.Current.Resources["SearchBoxStyle"],
            Height = 42,
            Padding = new Thickness(10, 0, 10, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Result = _textBox.Text; DialogResult = true; }
            if (e.Key == Key.Escape) DialogResult = false;
        };
        sp.Children.Add(_textBox);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 8)
        };

        var ok = new Button
        {
            Content = "OK", Width = 72, Height = 30,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        ok.Click += (_, _) => { Result = _textBox.Text; DialogResult = true; };

        var cancel = new Button
        {
            Content = "HỦY", Width = 72, Height = 30, Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)Application.Current.Resources["FlatButtonStyle"]
        };
        cancel.Click += (_, _) => DialogResult = false;

        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);

        var root = new StackPanel();
        root.Children.Add(sp);
        root.Children.Add(btnRow);
        Content = root;
        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }
}
