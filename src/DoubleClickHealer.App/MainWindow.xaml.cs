using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DoubleClickHealer.App.ViewModels;

namespace DoubleClickHealer.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        CenterOnPrimaryScreen();
    }

    private void CenterOnPrimaryScreen()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
