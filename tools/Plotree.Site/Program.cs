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
