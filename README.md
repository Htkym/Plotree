# Plotree

[English](README.md) | [日本語](README.ja.md)

Plotree is a Windows desktop app for outlining branching stories for novels and games. Build a flowchart of scenes, choices, and endings, then keep the writing context for each part of the story in the same project.

![A branching story map in Plotree](site/images/screenshots/01-overview-en.png)

Use the canvas to connect narrative beats, edit a node's synopsis and memo in the details panel, and organize the project with color tags, characters, groups, layouts, and card appearance settings.

## Install

Choose either installation method:

- [Microsoft Store](https://apps.microsoft.com/detail/9pfl8jdmmtx6) (recommended): certificate trust and updates are handled automatically.
- Direct installation from [GitHub Releases](https://github.com/Htkym/Plotree/releases/latest): download and extract `Plotree_<version>_DirectInstaller.zip`, then run `Install-Plotree.bat`.

The GitHub version uses a self-signed certificate, so administrator approval is required during the first installation. The installer verifies that the included certificate matches the MSIX bundle before adding it to the Windows Trusted People store. The GitHub version does not update automatically; run the installer again for each new release.

## User manuals

[English user manual](https://htkym.github.io/Plotree/posts/user-manual-en.html) | [日本語版ユーザーマニュアル](https://htkym.github.io/Plotree/posts/user-manual-ja.html)

## Support

Use [GitHub Issues](https://github.com/Htkym/Plotree/issues/new/choose) for bug reports, feature requests, and usage questions. Do not attach `.plotree` files or screenshots containing private story material.

See [Support](SUPPORT.md) and the [privacy policy](https://htkym.github.io/Plotree/posts/privacy-policy-en.html) for details.

## Highlights

- Create scene, choice, and ending nodes, then drag from a node handle to create labeled connections.
- Select multiple nodes to move, tag, assign characters, unpin, or delete them together.
- Copy and paste selected nodes from the toolbar, context menu, or Ctrl+C / Ctrl+V. Connections between copied nodes and their referenced tags and characters are preserved.
- Navigate large graphs with automatic horizontal and vertical scroll bars, canvas panning, mouse-wheel scrolling, and zoom controls.
- Combine automatic layered layout with manually pinned nodes.
- Track node body text, writing memos, color tags, characters, and character groups.
- Export the graph as PNG or SVG, or export story routes as Markdown or plain text.
- Switch the interface between English and Japanese from **Settings > Language**.

![Editing a selected story node](site/images/screenshots/02-node-details-en.png)

## Requirements

- Windows 10 version 1809 or later, or Windows 11
- .NET 10 SDK for building from source

## Build

```powershell
dotnet build .\Plotree.slnx -p:Platform=x64
```

Use `winapp run` or the WinUI BuildAndRun workflow to launch a development build rather than starting the executable directly.

After creating an MSIX bundle and its `.cer` file, build the GitHub direct installer with:

```powershell
.\scripts\New-DirectInstaller.ps1 -PackageDirectory .\src\Plotree\AppPackages\<version>
```

## License

See [LICENSE](LICENSE). Third-party notices are available in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
