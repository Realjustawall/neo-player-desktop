using System.Windows;
using System.Windows.Input;
using NeoPlayer.Windows.ViewModels;

namespace NeoPlayer.Windows.Views;

public partial class CompactPlayerWindow : Window
{
    public CompactPlayerWindow() => InitializeComponent();
    private void Seek_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.CommitSeek();
    }
}
