using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Dialogs;

namespace SteamShare.UI;

internal class Program
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .UseSkia()
                     .UseHarfBuzz()
                     .WithInterFont()
                     .LogToTrace();

    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
}
