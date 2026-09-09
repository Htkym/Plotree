using LithoSharp;
using LithoSharp.Configuration;
using LithoSharp.Content;

var root = Directory.GetCurrentDirectory();
var output = GetArgument(args, "--output") ?? Path.Combine(root, "site", "_site");
var content = Path.Combine(root, "site", "content");
var images = Path.Combine(root, "site", "images");
var favicon = Path.Combine(root, "site", "favicon");

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
    Template = new PlotreeDocsTemplate(),
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
            .docs-header { border-bottom-color: rgba(214, 155, 230, 0.22); }
            .docs-sidebar { border-inline-end-color: rgba(214, 155, 230, 0.22); }
            .docs-language-switch {
              display: inline-flex;
              align-items: center;
              gap: 0.25rem;
              margin-inline-start: auto;
              padding: 0.2rem;
              border: 1px solid rgba(214, 155, 230, 0.3);
              border-radius: 999px;
              background: rgba(214, 155, 230, 0.08);
            }
            .docs-language-label {
              padding-inline: 0.45rem;
              color: var(--muted);
              font-size: 0.72rem;
              letter-spacing: 0.05em;
              text-transform: uppercase;
            }
            .docs-language-switch a {
              min-inline-size: 2.25rem;
              padding: 0.25rem 0.45rem;
              color: var(--muted);
              border-radius: 999px;
              font-size: 0.76rem;
              text-align: center;
              text-decoration: none;
            }
            .docs-language-switch a:hover,
            .docs-language-switch a:focus-visible,
            .docs-language-switch a[aria-current="page"] {
              color: var(--accent-contrast);
              background: var(--accent);
            }
            @media (max-width: 52rem) {
              .docs-language-label { display: none; }
              .docs-language-switch { margin-inline-start: auto; }
            }
            """,
    },
    FaviconSourceDirectory = favicon,
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
