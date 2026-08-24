using System.Collections.Specialized;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Plotree.Models;
using Plotree.Services;
using Plotree.ViewModels;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace Plotree;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    // VK_OEM_PLUS / VK_OEM_MINUS are not named members of Windows.System.VirtualKey.
    private const VirtualKey MainKeyboardPlusKey = (VirtualKey)0xBB;
    private const VirtualKey MainKeyboardMinusKey = (VirtualKey)0xBD;

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        RegisterMainKeyboardZoomAccelerators();

        // Localized combo items and tooltips (resources aren't reachable via x:Uid for these).
        DirectionCombo.ItemsSource = new[]
        {
            Loc.Get("Direction_LeftToRight"),
            Loc.Get("Direction_TopToBottom"),
        };

        string[] nodeTypeNames =
        [
            Loc.Get("NodeType_Scene"),
            Loc.Get("NodeType_Choice"),
            Loc.Get("NodeType_Ending"),
        ];
        NodeTypeCombo.ItemsSource = nodeTypeNames;
        AppearanceTypeCombo.ItemsSource = nodeTypeNames;

        ToolTipService.SetToolTip(DeleteSelectedButton, Loc.Get("Tooltip_DeleteSelected"));
        ToolTipService.SetToolTip(TogglePanelButton, Loc.Get("Tooltip_TogglePanel"));
        ToolTipService.SetToolTip(AutoLayoutButton, Loc.Get("Tooltip_AutoLayout"));
        ToolTipService.SetToolTip(DirectionCombo, Loc.Get("Tooltip_LayoutDirection"));
        ToolTipService.SetToolTip(ClearTagButton, Loc.Get("Tooltip_RemoveTag"));
        ToolTipService.SetToolTip(AddTagButton, Loc.Get("Tooltip_NewTag"));
        ToolTipService.SetToolTip(AppearanceSettingsButton, Loc.Get("Tooltip_Appearance"));
        ToolTipService.SetToolTip(NodeColorResetButton, Loc.Get("Tooltip_ResetHeaderColor"));
        ToolTipService.SetToolTip(TypeColorResetButton, Loc.Get("Tooltip_ResetHeaderColor"));
        ToolTipService.SetToolTip(BulkDeleteButton, Loc.Get("Tooltip_DeleteSelected"));
        ToolTipService.SetToolTip(BulkUnpinButton, Loc.Get("Tooltip_UnpinSelected"));
        ToolTipService.SetToolTip(CopyButton, Loc.Get("Tooltip_Copy"));
        ToolTipService.SetToolTip(PasteButton, Loc.Get("Tooltip_Paste"));
        AutomationProperties.SetName(CopyButton, Loc.Get("Automation_Copy"));
        AutomationProperties.SetName(PasteButton, Loc.Get("Automation_Paste"));
        ToolTipService.SetToolTip(ZoomInButton, Loc.Get("Tooltip_ZoomIn"));
        ToolTipService.SetToolTip(ZoomOutButton, Loc.Get("Tooltip_ZoomOut"));
        AutomationProperties.SetName(ZoomInButton, Loc.Get("Automation_ZoomIn"));
        AutomationProperties.SetName(ZoomOutButton, Loc.Get("Automation_ZoomOut"));

        // Reflect the persisted language choice in the Settings menu.
        var language = RecentFilesService.GetLanguageOverride();
        (language switch
        {
            "ja-JP" => LanguageJaItem,
            "en-US" => LanguageEnItem,
            _ => LanguageSystemItem,
        }).IsChecked = true;

        CanvasView.ViewModel = ViewModel;
        CanvasView.NodeActivated += OnNodeActivated;
        ViewModel.RecentFiles.CollectionChanged += OnRecentFilesChanged;
        RebuildRecentFilesMenu();

        Loaded += OnPageLoaded;
    }

    private void RegisterMainKeyboardZoomAccelerators()
    {
        var zoomIn = new KeyboardAccelerator
        {
            Key = MainKeyboardPlusKey,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
        };
        zoomIn.Invoked += OnZoomInInvoked;
        KeyboardAccelerators.Add(zoomIn);

        var zoomOut = new KeyboardAccelerator
        {
            Key = MainKeyboardMinusKey,
            Modifiers = VirtualKeyModifiers.Control,
        };
        zoomOut.Invoked += OnZoomOutInvoked;
        KeyboardAccelerators.Add(zoomOut);
    }

    // ----- x:Bind visibility helpers -----

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    // ----- Lifecycle & keyboard -----

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        if (App.LaunchFilePath is { } path)
        {
            await ViewModel.LoadFromPathAsync(path);
        }
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ClearSelection();
        args.Handled = true;
    }

    private void OnDeleteInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Don't steal Delete from text editing.
        if (IsTextInputFocused())
        {
            return;
        }

        if (ViewModel.DeleteSelectedCommand.CanExecute(null))
        {
            ViewModel.DeleteSelectedCommand.Execute(null);
        }

        args.Handled = true;
    }

    private void OnSelectAllInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Inside a text box Ctrl+A must keep selecting text, so leave the key unhandled.
        if (IsTextInputFocused())
        {
            return;
        }

        ViewModel.SelectAllNodesCommand.Execute(null);
        args.Handled = true;
    }

    private void OnCopyInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Keep the standard text copy operation when an editor owns the focus.
        if (IsTextInputFocused())
        {
            return;
        }

        if (ViewModel.CopyCommand.CanExecute(null))
        {
            ViewModel.CopyCommand.Execute(null);
        }

        args.Handled = true;
    }

    private void OnPasteInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Keep the standard text paste operation when an editor owns the focus.
        if (IsTextInputFocused())
        {
            return;
        }

        if (ViewModel.PasteCommand.CanExecute(null))
        {
            ViewModel.PasteCommand.Execute(null);
        }

        args.Handled = true;
    }

    private void OnUndoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        if (ViewModel.UndoCommand.CanExecute(null))
        {
            ViewModel.UndoCommand.Execute(null);
        }

        args.Handled = true;
    }

    private void OnRedoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        if (ViewModel.RedoCommand.CanExecute(null))
        {
            ViewModel.RedoCommand.Execute(null);
        }

        args.Handled = true;
    }

    private void OnZoomInInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Keep Ctrl++ available to text editors that use it for their own behavior.
        if (IsTextInputFocused())
        {
            return;
        }

        CanvasView.ZoomIn();
        args.Handled = true;
    }

    private void OnZoomOutInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Keep Ctrl+- available to text editors that use it for their own behavior.
        if (IsTextInputFocused())
        {
            return;
        }

        CanvasView.ZoomOut();
        args.Handled = true;
    }

    private void OnProjectTitleGotFocus(object sender, RoutedEventArgs e) =>
        ViewModel.BeginProjectTitleTextEdit();

    private void OnNodeTitleGotFocus(object sender, RoutedEventArgs e) =>
        ViewModel.BeginSelectedNodeTextEdit(nameof(NodeViewModel.Title));

    private void OnNodeBodyGotFocus(object sender, RoutedEventArgs e) =>
        ViewModel.BeginSelectedNodeTextEdit(nameof(NodeViewModel.Body));

    private void OnNodeMemoGotFocus(object sender, RoutedEventArgs e) =>
        ViewModel.BeginSelectedNodeTextEdit(nameof(NodeViewModel.Memo));

    private void OnEdgeLabelGotFocus(object sender, RoutedEventArgs e) =>
        ViewModel.BeginSelectedEdgeTextEdit();

    private void OnTextEditLostFocus(object sender, RoutedEventArgs e) =>
        ViewModel.CompleteTextEdit();

    /// <summary>Whether a text-editing control currently owns the keyboard focus.</summary>
    private bool IsTextInputFocused() =>
        FocusManager.GetFocusedElement(XamlRoot) is TextBox or RichEditBox or AutoSuggestBox or PasswordBox;

    private void OnNodeActivated(object? sender, NodeViewModel node)
    {
        ViewModel.IsPanelOpen = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            NodeTitleBox.Focus(FocusState.Programmatic);
            NodeTitleBox.SelectAll();
        });
    }

    // ----- Toolbar handlers -----

    private void OnAddSceneClick(object sender, RoutedEventArgs e) =>
        CanvasView.AddNodeAtViewportCenter(NodeType.Scene);

    private void OnAddChoiceClick(object sender, RoutedEventArgs e) =>
        CanvasView.AddNodeAtViewportCenter(NodeType.Choice);

    private void OnAddEndingClick(object sender, RoutedEventArgs e) =>
        CanvasView.AddNodeAtViewportCenter(NodeType.Ending);

    private void OnAutoLayoutClick(SplitButton sender, SplitButtonClickEventArgs args) =>
        ViewModel.AutoLayoutCommand.Execute(null);

    private void OnZoomInClick(object sender, RoutedEventArgs e) => CanvasView.ZoomIn();

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => CanvasView.ZoomOut();

    // ----- Image export -----

    private async void OnExportPngClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var svg = SvgExportService.Export(ViewModel.Project);
            var size = GetSvgSize(svg);
            var options = GetPngOptions(size);

            using var stagedPng = new InMemoryRandomAccessStream();
            await PngExportService.ExportSvgAsync(svg, stagedPng, options);

            var file = await ViewModel.PickExportFileAsync(Loc.Get("Picker_PngType"), ".png");
            if (file is null)
            {
                return;
            }

            using (var destination = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                stagedPng.Seek(0);
                destination.Seek(0);
                destination.Size = 0;
                await RandomAccessStream.CopyAsync(stagedPng, destination);
                await destination.FlushAsync();
            }

            await ViewModel.ShowExportCompletedAsync(file.Path);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"PNG export failed: {exception}");
            await ViewModel.ShowExportErrorAsync(Loc.Get("Error_PngExportContent"));
        }
    }

    private static Size GetSvgSize(string svg)
    {
        var root = XDocument.Parse(svg).Root
            ?? throw new InvalidOperationException("The SVG export has no document root.");
        var width = ParseSvgDimension(root.Attribute("width")?.Value);
        var height = ParseSvgDimension(root.Attribute("height")?.Value);
        return new Size(width, height);
    }

    private static double ParseSvgDimension(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dimension)
            || !double.IsFinite(dimension)
            || dimension <= 0)
        {
            throw new InvalidOperationException("The SVG export has invalid dimensions.");
        }

        return dimension;
    }

    private static PngExportOptions GetPngOptions(Size size)
    {
        var scale = Math.Min(
            1,
            Math.Min(
                PngExportOptions.MaxPixelDimension / size.Width,
                PngExportOptions.MaxPixelDimension / size.Height));
        scale = Math.Min(
            scale,
            Math.Sqrt(PngExportOptions.MaxPixelCount / (size.Width * size.Height)));

        return new PngExportOptions
        {
            PixelWidth = (uint)Math.Max(1, Math.Floor(size.Width * scale)),
            PixelHeight = (uint)Math.Max(1, Math.Floor(size.Height * scale)),
        };
    }

    /// <summary>Creates a color tag from the toolbar flyout and applies it to the selected node.</summary>
    private void OnAddTagSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
        {
            ViewModel.AddTag(NewTagNameBox.Text, color);
            NewTagNameBox.Text = string.Empty;
            AddTagButton.Flyout.Hide();
        }
    }

    // ----- Project-level node appearance -----

    private async void OnAppearanceSettingsClick(object sender, RoutedEventArgs e)
    {
        // ContentDialog is declared in XAML, so its XamlRoot must be attached before showing.
        AppearanceDialog.XamlRoot = XamlRoot;
        AppearanceDialog.Title = Loc.Get("Dialog_AppearanceTitle");
        AppearanceDialog.CloseButtonText = Loc.Get("Dialog_Close");
        await AppearanceDialog.ShowAsync();
    }

    private void OnTypeColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
        {
            ViewModel.TypeHeaderColor = color;
        }
    }

    private void OnTypeColorResetClick(object sender, RoutedEventArgs e) =>
        ViewModel.TypeHeaderColor = null;

    // ----- Language switching -----

    // Generated by the MSIX resource-splitting target for Strings\ja-JP.
    // Store installs only language packs that match the user's OS languages;
    // this ID lets an English Store install acquire Japanese on demand.
    private const string JapaneseResourcePackageId = "split.language-ja";

    private async void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag })
        {
            return;
        }

        var newLanguage = string.IsNullOrEmpty(tag) ? null : tag;
        if (newLanguage == RecentFilesService.GetLanguageOverride()
            && IsLanguageAvailable(newLanguage))
        {
            return;
        }

        if (!await EnsureLanguageAvailableAsync(newLanguage))
        {
            var errorDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.Get("Error_LanguageDownloadTitle"),
                Content = Loc.Get("Error_LanguageDownloadContent"),
                CloseButtonText = Loc.Get("Dialog_OK"),
                DefaultButton = ContentDialogButton.Close,
            };
            await errorDialog.ShowAsync();
            return;
        }

        RecentFilesService.SetLanguageOverride(newLanguage);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Dialog_LanguageTitle"),
            Content = Loc.Get("Dialog_LanguageContent"),
            CloseButtonText = Loc.Get("Dialog_OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static async Task<bool> EnsureLanguageAvailableAsync(string? language)
    {
        if (IsLanguageAvailable(language))
        {
            return true;
        }

        if (!string.Equals(language, "ja-JP", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var result = await PackageCatalog.OpenForCurrentPackage().AddResourcePackageAsync(
                Package.Current.Id.FamilyName,
                JapaneseResourcePackageId,
                AddResourcePackageOptions.ApplyUpdateIfAvailable);
            return result.ExtendedError is null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsLanguageAvailable(string? language) =>
        string.IsNullOrEmpty(language)
        || Microsoft.Windows.Globalization.ApplicationLanguages.ManifestLanguages.Contains(
            language,
            StringComparer.OrdinalIgnoreCase);

    // ----- Details panel handlers -----

    private void OnNodeColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
        {
            ViewModel.SetNodeHeaderColor(color);
        }
    }

    private void OnAddCharacterClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddCharacter(NewCharacterBox.Text);
        NewCharacterBox.Text = string.Empty;
    }

    private void OnAddGroupClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddGroup(NewGroupNameBox.Text);
        NewGroupNameBox.Text = string.Empty;
    }

    private async void OnEditTagClick(object sender, RoutedEventArgs e)
    {
        if (GetItem<TagOptionViewModel>(sender) is not { } tag)
        {
            return;
        }

        var nameBox = new TextBox
        {
            Header = Loc.Get("Field_TagName"),
            Text = tag.Name,
        };
        AutomationProperties.SetAutomationId(nameBox, "EditTagNameBox");
        var colorBox = new TextBox
        {
            Header = Loc.Get("Field_TagColor"),
            Text = tag.Color,
        };
        AutomationProperties.SetAutomationId(colorBox, "EditTagColorBox");

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(nameBox);
        content.Children.Add(colorBox);
        var dialog = CreateEditDialog("Dialog_EditTagTitle", content);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !ViewModel.EditTag(tag, nameBox.Text, colorBox.Text))
        {
            await ShowInvalidEditAsync();
        }
    }

    private async void OnDeleteTagClick(object sender, RoutedEventArgs e)
    {
        if (GetItem<TagOptionViewModel>(sender) is not { } tag
            || !await ConfirmDeleteAsync("Dialog_DeleteTagTitle", "Dialog_DeleteTagContent", tag.Name))
        {
            return;
        }

        ViewModel.DeleteTag(tag);
    }

    private async void OnEditCharacterClick(object sender, RoutedEventArgs e)
    {
        if (GetItem<CharacterOptionViewModel>(sender) is not { } character)
        {
            return;
        }

        var nameBox = CreateNameEditBox("Field_CharacterName", character.Name, "EditCharacterNameBox");
        var dialog = CreateEditDialog("Dialog_EditCharacterTitle", nameBox);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !ViewModel.EditCharacter(character, nameBox.Text))
        {
            await ShowInvalidEditAsync();
        }
    }

    private async void OnDeleteCharacterClick(object sender, RoutedEventArgs e)
    {
        if (GetItem<CharacterOptionViewModel>(sender) is not { } character
            || !await ConfirmDeleteAsync(
                "Dialog_DeleteCharacterTitle",
                "Dialog_DeleteCharacterContent",
                character.Name))
        {
            return;
        }

        ViewModel.DeleteCharacter(character);
    }

    private async void OnEditGroupClick(object sender, RoutedEventArgs e)
    {
        if (GetItem<CharacterGroupOptionViewModel>(sender) is not { } group)
        {
            return;
        }

        var nameBox = CreateNameEditBox("Field_GroupName", group.Name, "EditGroupNameBox");
        var dialog = CreateEditDialog("Dialog_EditGroupTitle", nameBox);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !ViewModel.EditGroup(group, nameBox.Text))
        {
            await ShowInvalidEditAsync();
        }
    }

    private async void OnDeleteGroupClick(object sender, RoutedEventArgs e)
    {
        if (GetItem<CharacterGroupOptionViewModel>(sender) is not { } group
            || !await ConfirmDeleteAsync("Dialog_DeleteGroupTitle", "Dialog_DeleteGroupContent", group.Name))
        {
            return;
        }

        ViewModel.DeleteGroup(group);
    }

    private void OnEditItemButtonLoaded(object sender, RoutedEventArgs e) =>
        ConfigureManagementButton(sender, "Tooltip_EditItem", "Edit");

    private void OnDeleteItemButtonLoaded(object sender, RoutedEventArgs e) =>
        ConfigureManagementButton(sender, "Tooltip_DeleteItem", "Delete");

    private static T? GetItem<T>(object sender)
        where T : class =>
        sender is FrameworkElement { DataContext: T item } ? item : null;

    private static void ConfigureManagementButton(object sender, string tooltipKey, string automationSuffix)
    {
        if (sender is not Button button)
        {
            return;
        }

        var label = Loc.Get(tooltipKey);
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);

        var itemAutomationId = button.DataContext switch
        {
            TagOptionViewModel tag => tag.AutomationId,
            CharacterOptionViewModel character => character.AutomationId,
            CharacterGroupOptionViewModel group => group.AutomationId,
            _ => null,
        };
        if (itemAutomationId is not null)
        {
            AutomationProperties.SetAutomationId(button, $"{itemAutomationId}_{automationSuffix}");
        }
    }

    private TextBox CreateNameEditBox(string headerKey, string name, string automationId)
    {
        var textBox = new TextBox
        {
            Header = Loc.Get(headerKey),
            Text = name,
        };
        AutomationProperties.SetAutomationId(textBox, automationId);
        return textBox;
    }

    private ContentDialog CreateEditDialog(string titleKey, object content) => new()
    {
        XamlRoot = XamlRoot,
        Title = Loc.Get(titleKey),
        Content = content,
        PrimaryButtonText = Loc.Get("Dialog_Save"),
        CloseButtonText = Loc.Get("Dialog_Cancel"),
        DefaultButton = ContentDialogButton.Primary,
    };

    private async Task<bool> ConfirmDeleteAsync(string titleKey, string contentKey, string name)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get(titleKey),
            Content = Loc.Format(contentKey, name),
            PrimaryButtonText = Loc.Get("Dialog_Delete"),
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowInvalidEditAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Dialog_InvalidEditTitle"),
            Content = Loc.Get("Dialog_InvalidEditContent"),
            CloseButtonText = Loc.Get("Dialog_OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    // ----- Recent files menu -----

    private void OnRecentFilesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildRecentFilesMenu();

    private void RebuildRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();

        if (ViewModel.RecentFiles.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuFlyoutItem
            {
                Text = Loc.Get("Menu_NoRecentFiles"),
                IsEnabled = false,
            });
            return;
        }

        var index = 0;
        foreach (var path in ViewModel.RecentFiles)
        {
            var item = new MenuFlyoutItem
            {
                Text = path,
                Command = ViewModel.OpenRecentCommand,
                CommandParameter = path,
            };
            AutomationProperties.SetAutomationId(item, $"MenuFileRecentItem{index}");
            RecentFilesMenu.Items.Add(item);
            index++;
        }
    }
}
