using ShowCuePlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShowCuePlayer.Controls.Dialogs;

/// <summary>Properties dialog showing full cue metadata.</summary>
public sealed class CuePropertiesDialog : Window
{
    public CuePropertiesDialog(CueCardViewModel vm)
    {
        Title = "Cue Properties";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 460; Height = 420;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.Resources["BgBaseBrush"];

        var scroll = new ScrollViewer { Margin = new Thickness(20) };
        var stack = new StackPanel();

        void AddRow(string label, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock
            {
                Text = label,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                FontSize = 11
            };
            var val = new TextBlock
            {
                Text = value,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(val, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(val);
            stack.Children.Add(grid);
        }

        AddRow("Title", vm.Title);
        AddRow("Artist", vm.Artist);
        AddRow("File", System.IO.Path.GetFileName(vm.Model.FilePath));
        AddRow("Duration", vm.Duration);
        AddRow("Volume", $"{vm.VolumePercent:F0}%");
        AddRow("Hotkey", vm.HotkeyText);
        AddRow("Color", vm.ColorHex);
        AddRow("Loop", vm.IsLooping ? "Yes" : "No");
        AddRow("Repeat", vm.IsRepeating ? "Yes" : "No");
        AddRow("Fade In", $"{vm.Model.FadeInSeconds:F1}s");
        AddRow("Fade Out", $"{vm.Model.FadeOutSeconds:F1}s");
        AddRow("Play Count", vm.Model.PlayCount.ToString());
        AddRow("Date Added", vm.Model.DateAdded.ToString("yyyy-MM-dd HH:mm"));

        var meta = vm.Model.Metadata;
        if (meta != null)
        {
            AddRow("Bitrate", $"{meta.Bitrate} kbps");
            AddRow("Sample Rate", $"{meta.SampleRate} Hz");
            AddRow("Channels", meta.Channels == 2 ? "Stereo" : meta.Channels == 1 ? "Mono" : meta.Channels.ToString());
            AddRow("Codec", meta.Codec);
            AddRow("File Size", $"{meta.FileSizeBytes / 1024.0 / 1024.0:F2} MB");
        }

        scroll.Content = stack;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var btn = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20), Height = 30,
            Padding = new Thickness(20, 0, 20, 0),
            Style = (Style)Application.Current.Resources["FlatButtonStyle"]
        };
        btn.Click += (_, _) => Close();

        Grid.SetRow(scroll, 0);
        Grid.SetRow(btn, 1);
        root.Children.Add(scroll);
        root.Children.Add(btn);
        Content = root;
    }
}
