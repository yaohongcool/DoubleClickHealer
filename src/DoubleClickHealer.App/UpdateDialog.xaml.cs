using System;
using System.Windows;
using DoubleClickHealer.App.Services;

namespace DoubleClickHealer.App;

public partial class UpdateDialog : Window
{
    private readonly Action? _primaryAction;

    public UpdateDialog(string headline, string message, string primaryText, Action? primaryAction = null)
    {
        InitializeComponent();
        AppTheme.ApplySolidWindow(this);

        HeadlineText.Text = headline;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;
        _primaryAction = primaryAction;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AppTheme.ApplyTitleBar(this);
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        _primaryAction?.Invoke();
        Close();
    }
}
