using System.Windows;
using System.Windows.Input;

namespace ShowCuePlayer.Views;

public partial class CueSettingsWindow : Window
{
    public CueSettingsWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
