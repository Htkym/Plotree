# Copilot Instructions for Plotree

## Build, test, and run

- This is an unpackaged WinUI 3 desktop app targeting `net10.0-windows10.0.26100.0`; use an explicit supported platform, normally x64:

  ```powershell
  dotnet build .\Plotree.slnx -p:Platform=x64
  dotnet build .\src\Plotree\Plotree.csproj -c Release -p:Platform=x64
  ```

- For an interactive launch, invoke the `winui-dev-workflow` skill and use its `BuildAndRun.ps1` workflow. Do not rely on `dotnet run`; this project uses `WindowsPackageType=None`.
- There is currently no test project or test framework, so there is no full-suite or single-test command. There is also no repository-defined lint or formatting command.

## Architecture

- `src\Plotree` is the single WinUI project. `App` initializes the language override before XAML loads, captures an optional `.plotree` command-line argument, and exposes the app `Window`, HWND, and UI dispatcher. `MainWindow` hosts `MainPage` and intercepts close requests for unsaved-change confirmation.
- `MainPageViewModel` is the root application state. It owns the `PlotProject`, file commands, selection, dirty state, graph-facing wrapper collections, and the `GraphChanged` event. `NodeViewModel` and `EdgeViewModel` wrap serializable models for the UI.
- `Views\PlotCanvas` is an imperative editor surface: it rebuilds node cards and edge visuals after `GraphChanged`, handles pan/zoom, dragging, selection, and connection creation, and positions edges using the fixed `NodeViewModel.CardWidth` and `CardHeight`.
- `Models` define the persisted graph. `ProjectFileService` and `ProjectSerializer` load/save formatted, camel-case `.plotree` JSON; `PlotreeJsonContext` supplies source-generated metadata needed by trimmed Release builds. `AutoLayoutService` applies a layered layout, and `RouteEnumerator` plus `PlotExporter` produce route-based Markdown or text exports.
- UI text is localized in both `Strings\ja-JP\Resources.resw` and `Strings\en-US\Resources.resw`. XAML uses `x:Uid`; code-behind and view models use `Loc.Get` or `Loc.Format`.

## Repository conventions

- Use the appropriate WinUI skill or agent for WinUI-specific implementation, debugging, and review work.
- Make graph mutations through `MainPageViewModel`; after structural changes, call its graph-rebuild path rather than updating `PlotCanvas` visual dictionaries directly. Property changes on node wrappers update the visual positions and connected edges.
- Preserve graph rules enforced by the view model: the Start node cannot be deleted or retitled as another type, edges cannot be self-loops or duplicates, edges cannot target Start, and Ending nodes have no outgoing-handle UI.
- Node dragging sets `IsPinned`; auto-layout retains pinned positions while keeping their layout slots. `RelayoutAll` is the only operation that clears pins before laying out every node.
- Any editable model-backed property must mark the project dirty. Keep `Project`, `Nodes`, `Edges`, tags, and character wrapper collections synchronized through `RebuildGraph` when replacing or structurally changing a project.
- Maintain `.plotree` compatibility: update `ProjectSerializer.CurrentVersion` and add in-place migrations for format changes. Register newly serialized root types in `PlotreeJsonContext` so trimmed Release builds can serialize them.
- Add or change user-visible strings in both resource files. Preserve matching resource keys and use `Loc` for strings that cannot be expressed by `x:Uid`.
- Set `XamlRoot` on `ContentDialog` instances. Initialize WinRT file pickers with `App.WindowHandle`, and queue deferred UI work with `App.DispatcherQueue`.
- Preserve the existing `AutomationProperties.AutomationId` values on interactive controls; add stable IDs for new controls that need UI automation.
