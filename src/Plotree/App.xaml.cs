using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Plotree;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>A .plotree file passed on the command line, or null.</summary>
    public static string? LaunchFilePath { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        ApplyLanguageOverride();
        InitializeComponent();
    }

    /// <summary>
    /// Applies the persisted UI language before any XAML loads, so x:Uid and
    /// <see cref="Services.Loc"/> both resolve in the chosen language.
    /// Without an override the app follows the OS display language.
    /// </summary>
    private static void ApplyLanguageOverride()
    {
        try
        {
            if (Services.RecentFilesService.GetLanguageOverride() is { } language)
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            }
        }
        catch (Exception)
        {
            // Fall back to the system language when the override cannot be applied.
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var commandLineArgs = Environment.GetCommandLineArgs();
        if (commandLineArgs.Length > 1
            && commandLineArgs[1].EndsWith(".plotree", StringComparison.OrdinalIgnoreCase)
            && System.IO.File.Exists(commandLineArgs[1]))
        {
            LaunchFilePath = commandLineArgs[1];
        }

        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
    }
}
