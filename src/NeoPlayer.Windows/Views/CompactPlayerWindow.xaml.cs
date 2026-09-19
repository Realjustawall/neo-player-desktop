using System.Windows;
using System.Windows.Input;

namespace NeoPlayer.Windows.Views;

public partial class CompactPlayerWindow : Window
{
    public CompactPlayerWindow() => InitializeComponent();
    void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    void Close_Click(object sender, RoutedEventArgs e) => Hide();
}
