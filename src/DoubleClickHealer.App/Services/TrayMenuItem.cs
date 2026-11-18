namespace DoubleClickHealer.App.Services;

public sealed class TrayMenuItem
{
    public string Text { get; init; } = string.Empty;

    public Action? Action { get; init; }

    public bool CloseOnInvoke { get; init; } = true;

    public IReadOnlyList<TrayMenuItem>? Children { get; init; }
}
