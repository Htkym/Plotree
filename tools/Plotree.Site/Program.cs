using System.Text;
using LithoSharp;
using LithoSharp.Configuration;
using LithoSharp.Content;

var root = Directory.GetCurrentDirectory();
var output = GetArgument(args, "--output") ?? Path.Combine(root, "site", "_site");
var content = Path.Combine(root, "site", "content");
var images = Path.Combine(root, "site", "images");

var site = new SiteSettings
{
    Title = "Plotree",
    Description = "A flowchart-style plot outlining tool for novel and game writers.",
    BaseUrl = "https://htkym.github.io/Plotree/",
    RepositoryUrl = "https://github.com/Htkym/Plotree",
    Language = "en",
    Author = "Htkym",
    TimeZone = "Asia/Tokyo",
};

var customization = new SiteCustomization
{
    Theme = new SiteThemeOptions
    {
        BrandPrefix = "plot your story / ",
        ThemeColor = "#1b1b1b",
        DefaultSocialSubtitle = "Flowchart-style story outlining for writers",
        AdditionalCss = """
            :root {
              --accent: #d69be6;
              --accent-contrast: #1b1b1b;
            }
            .hero { border-color: rgba(214, 155, 230, 0.45); }
            .site-header { border-bottom-color: rgba(214, 155, 230, 0.22); }
            .site-header-search,
            .site-nav a[href$="/archives.html"],
            .site-nav a[href$="/tags.html"],
            .site-nav a[href$="/search.html"],
            .rss-nav-link {
              display: none;
            }
            .post > .eyebrow,
            .post > .tags {
              display: none;
            }
            .post-layout {
              max-width: 58rem;
            }
            .docs-home {
              max-width: 62rem;
              margin: 0 auto;
            }
            .docs-home .hero {
              margin-bottom: 1.5rem;
            }
            .docs-home-actions {
              display: flex;
              flex-wrap: wrap;
              gap: 0.75rem;
              margin-top: 1.5rem;
            }
            .docs-home-actions .button-link {
              margin: 0;
            }
            .docs-section {
              margin-top: 2rem;
            }
            .docs-grid {
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
              gap: 1rem;
              margin-top: 1rem;
            }
            .docs-card {
              border: 1px solid rgba(214, 155, 230, 0.22);
              border-radius: 0.75rem;
              padding: 1.25rem;
              background: rgba(255, 255, 255, 0.03);
            }
            .docs-card h3 {
              margin-top: 0;
            }
            .docs-card p:last-child {
              margin-bottom: 0;
            }
            .docs-card-links {
              display: flex;
              flex-wrap: wrap;
              gap: 0.75rem;
            }
            .docs-card-links a {
              font-weight: 600;
            }
            """,
    },
    ExtraPages =
    [
        new SiteExtraPage
        {
            RelativePath = "support.html",
            Title = "Support / サポート",
            NavLabel = "Support",
            BodyHtml = """
                <section class="hero">
                  <p class="eyebrow">GitHub Issues</p>
                  <h1>Support / サポート</h1>
                  <p>Use GitHub Issues for bug reports, feature requests, and usage questions.</p>
                  <p><a class="button-link" href="https://github.com/Htkym/Plotree/issues/new/choose">Open a support issue</a></p>
                </section>
                <section>
                  <h2>Before opening an issue</h2>
                  <ul>
                    <li>Include the Plotree version or commit, Windows version, and reproduction steps.</li>
                    <li>Do not attach .plotree files, screenshots, logs, or story text containing private material.</li>
                    <li>Use the Bug report, Feature request, or Support question form.</li>
                  </ul>
                </section>
                <section>
                  <h2>サポートについて</h2>
                  <p>不具合報告、機能要望、使い方の質問は GitHub Issue で受け付けます。</p>
                  <p>私的な作品内容、.plotree ファイル、スクリーンショット、ログは投稿しないでください。</p>
                </section>
                """,
        },
    ],
    GenerateLlmsTxt = true,
};

var posts = await new MarkdownPostReader().ReadAllAsync(content);
SiteGenerator.Validate(site, content, posts, customization);
var result = await new SiteGenerator().GenerateAsync(site, posts, output, clean: true, customization);
ReplaceHomePage(Path.Combine(result.OutputDirectory, "index.html"), CreateDocumentationHome());
CopyDirectory(images, Path.Combine(result.OutputDirectory, "images"));

Console.WriteLine($"Generated {result.PostCount} page(s) into {result.OutputDirectory}.");

static string? GetArgument(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void CopyDirectory(string sourceDirectory, string destinationDirectory)
{
    if (!Directory.Exists(sourceDirectory))
    {
        throw new DirectoryNotFoundException($"The image source directory '{sourceDirectory}' does not exist.");
    }

    foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
    }

    foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var destination = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(file, destination, overwrite: true);
    }
}

static void ReplaceHomePage(string path, string content)
{
    var page = File.ReadAllText(path);
    const string mainStartMarker = "<main>";
    const string mainEndMarker = "</main>";
    var mainStart = page.IndexOf(mainStartMarker, StringComparison.Ordinal);
    var mainEnd = page.IndexOf(mainEndMarker, StringComparison.Ordinal);

    if (mainStart < 0 || mainEnd < 0 || mainEnd <= mainStart)
    {
        throw new InvalidOperationException("LithoSharp did not generate the expected main content area.");
    }

    var contentStart = mainStart + mainStartMarker.Length;
    var updatedPage = page[..contentStart] + Environment.NewLine + content + Environment.NewLine + "  " + page[mainEnd..];
    File.WriteAllText(path, updatedPage, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static string CreateDocumentationHome() => """
  <div class="docs-home">
    <section class="hero">
      <p class="eyebrow">Documentation / ドキュメント</p>
      <h1>Plotree Documentation</h1>
      <p>Learn how to plan branching stories, organize story details, and export your work with Plotree.</p>
      <p>Plotreeで分岐する物語を設計し、情報を整理して出力するためのドキュメントです。</p>
      <div class="docs-home-actions">
        <a class="button-link" href="/Plotree/posts/user-manual-en.html">English user manual</a>
        <a class="button-link" href="/Plotree/posts/user-manual-ja.html">日本語ユーザーマニュアル</a>
      </div>
    </section>

    <section class="docs-section" aria-labelledby="guides-heading">
      <h2 id="guides-heading">Guides / ガイド</h2>
      <div class="docs-grid">
        <article class="docs-card">
          <h3>User manual / ユーザーマニュアル</h3>
          <p>Create nodes and connections, manage characters and tags, arrange your graph, and export routes.</p>
          <p>ノードと接続の作成、登場人物やタグの管理、グラフの配置、ルートの出力方法を説明します。</p>
          <p class="docs-card-links">
            <a href="/Plotree/posts/user-manual-en.html">English</a>
            <a href="/Plotree/posts/user-manual-ja.html">日本語</a>
          </p>
        </article>
        <article class="docs-card">
          <h3>Getting started / はじめに</h3>
          <p>Find installation information, source code, and the latest release on GitHub.</p>
          <p>インストール方法、ソースコード、最新リリースはGitHubで確認できます。</p>
          <p class="docs-card-links">
            <a href="https://github.com/Htkym/Plotree#readme">English README</a>
            <a href="https://github.com/Htkym/Plotree/blob/main/README.ja.md">日本語 README</a>
          </p>
        </article>
      </div>
    </section>

    <section class="docs-section" aria-labelledby="help-heading">
      <h2 id="help-heading">Help and policies / サポートと方針</h2>
      <div class="docs-grid">
        <article class="docs-card">
          <h3>Support / サポート</h3>
          <p>Report bugs, suggest features, or ask usage questions through GitHub Issues.</p>
          <p>不具合報告、機能要望、使い方の質問はGitHub Issueから送信できます。</p>
          <p class="docs-card-links"><a href="/Plotree/support.html">Open support information</a></p>
        </article>
        <article class="docs-card">
          <h3>Privacy policy / プライバシーポリシー</h3>
          <p>Plotree works locally and does not collect telemetry or personal data.</p>
          <p>Plotreeはローカルで動作し、テレメトリや個人データを収集しません。</p>
          <p class="docs-card-links">
            <a href="/Plotree/posts/privacy-policy-en.html">English</a>
            <a href="/Plotree/posts/privacy-policy-ja.html">日本語</a>
          </p>
        </article>
      </div>
    </section>
  </div>
  """;
