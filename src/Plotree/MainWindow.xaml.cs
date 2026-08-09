using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Plotree.ViewModels;

namespace Plotree;

/// <summary>
/// The application window. Hosts a Frame that displays <see cref="MainPage"/>,
/// keeps the window title in sync with the ViewModel, and guards close
/// against unsaved changes.
/// </summary>
public sealed partial class MainWindow : Window
{
    private MainPageViewModel? _viewModel;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.Closing += OnAppWindowClosing;

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

        if (RootFrame.Content is MainPage page)
        {
            _viewModel = page.ViewModel;
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainPageViewModel.WindowTitle))
                {
                    UpdateTitle();
                }
            };
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        if (_viewModel is null)
        {
            return;
        }

        Title = _viewModel.WindowTitle;
        AppTitleBar.Title = _viewModel.WindowTitle;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || _viewModel is not { IsDirty: true })
        {
            return;
        }

        // Cancel now; re-close after the user confirms in the async dialog.
        args.Cancel = true;

        DispatcherQueue.TryEnqueue(async () =>
        {
            if (await _viewModel.ConfirmDiscardAsync())
            {
                _allowClose = true;
                Close();
            }
        });
    }
}
