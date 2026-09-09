using ShowCuePlayer.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShowCuePlayer.Controls.Dialogs;

/// <summary>Color picker dialog with predefined palette swatches.</summary>
public sealed class ColorPickerDialog : Window
{
    public string SelectedHex { get; private set; } = "#6C63FF";

    public ColorPickerDialog(string currentHex)
    {
        Title = "Choose Color";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 360; Height = 260;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.Resources["BgBaseBrush"];

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock
        {
            Text = "Select a color for this cue:",
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            Margin = new Thickness(0, 0, 0, 12)
        });

        var wrap = new WrapPanel();
        foreach (var hex in ColorHelper.PredefinedColors)
        {
            var captured = hex;
            var btn = new Button
            {
                Width = 36, Height = 36,
                Margin = new Thickness(4),
                Background = ColorHelper.BrushFromHex(hex),
                BorderBrush = Brushes.White,
                BorderThickness = hex == currentHex ? new Thickness(2.5) : new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = hex
            };

            // Rounded template
            var tpl = new ControlTemplate(typeof(Button));
            var bdr = new FrameworkElementFactory(typeof(Border));
            bdr.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            bdr.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            bdr.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            bdr.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            tpl.VisualTree = bdr;
            btn.Template = tpl;

            btn.Click += (_, _) => { SelectedHex = captured; DialogResult = true; };
            wrap.Children.Add(btn);
        }
        sp.Children.Add(wrap);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Cancel", Height = 30, Padding = new Thickness(16, 0, 16, 0),
            Style = (Style)Application.Current.Resources["FlatButtonStyle"]
        };
        cancel.Click += (_, _) => DialogResult = false;
        btnRow.Children.Add(cancel);
        sp.Children.Add(btnRow);
        Content = sp;
    }
}
