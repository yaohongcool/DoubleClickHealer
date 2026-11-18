using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using DoubleClickHealer.App.Services;

namespace DoubleClickHealer.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        AppTheme.ApplySolidWindow(this);

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = LocalizationService.Format("StrVersionFormat", version);

        CenterOnPrimaryScreen();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AppTheme.ApplyTitleBar(this);
    }

    private void CenterOnPrimaryScreen()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = LocalizationService.Get("StrChecking");

        try
        {
            Version? latest = await UpdateChecker.GetLatestVersionAsync();
            Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

            if (!IsVisible) return;

            if (latest != null && UpdateChecker.IsNewer(latest, current))
            {
                ShowUpdateDialog(
                    LocalizationService.Get("StrNewVersionTitle"),
                    LocalizationService.Format("StrNewVersionMessage", latest.ToString(3), current.ToString(3)),
                    LocalizationService.Get("StrDownload"),
                    () => Process.Start(new ProcessStartInfo(UpdateChecker.ReleasesUrl) { UseShellExecute = true }));
            }
            else
            {
                ShowUpdateDialog(
                    LocalizationService.Get("StrCheckUpdate"),
                    LocalizationService.Get("StrUpToDate"),
                    LocalizationService.Get("StrOk"));
            }
        }
        catch
        {
            if (IsVisible)
            {
                ShowUpdateDialog(
                    LocalizationService.Get("StrCheckUpdate"),
                    LocalizationService.Get("StrCheckFailed"),
                    LocalizationService.Get("StrOk"));
            }
        }
        finally
        {
            CheckUpdateButton.Content = LocalizationService.Get("StrCheckUpdate");
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void ShowUpdateDialog(string headline, string message, string primaryText, Action? primaryAction = null)
    {
        var dialog = new UpdateDialog(headline, message, primaryText, primaryAction) { Owner = this };
        dialog.ShowDialog();
    }
}
