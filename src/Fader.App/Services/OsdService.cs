using Fader.App.Views;

namespace Fader.App.Services;

public sealed class OsdService
{
    private OsdWindow? _window;

    public void Show(string message, bool success)
    {
        _window ??= new OsdWindow();
        _window.Present(message, success);
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
    }
}
