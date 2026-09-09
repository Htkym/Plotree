using LithoSharp;

internal sealed class PlotreeDocsTemplate : ISiteTemplate
{
    private static readonly DocsSiteTemplate InnerTemplate = new() { EnableSearch = true };

    public async Task<SiteTemplateResult> RenderAsync(
        SiteTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        var generated = await InnerTemplate.RenderAsync(context, cancellationToken);
        var files = generated.Files
            .Select(file => file.RelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                ? file with { Content = Customize(file.RelativePath, file.Content, context) }
                : file)
            .ToArray();

        return new SiteTemplateResult(files);
    }

    private static string Customize(string relativePath, string html, SiteTemplateContext context)
    {
        var language = relativePath.EndsWith("-ja.html", StringComparison.OrdinalIgnoreCase)
            ? "ja"
            : "en";
        html = LocalizeChrome(html, language);
        var navigation = BuildNavigation(relativePath, language, context);
        var sidebarStart = html.IndexOf("<aside id=\"docs-sidebar\"", StringComparison.Ordinal);
        var sidebarEnd = sidebarStart < 0
            ? -1
            : html.IndexOf("</aside>", sidebarStart, StringComparison.Ordinal);

        if (sidebarStart >= 0 && sidebarEnd >= 0)
        {
            html = string.Concat(
                html.AsSpan(0, sidebarStart),
                navigation,
                html.AsSpan(sidebarEnd + "</aside>".Length));
        }

        var languageSwitch = BuildLanguageSwitch(relativePath, context);
        if (languageSwitch.Length > 0)
        {
            html = InsertLanguageSwitch(html, languageSwitch);
        }

        return ReplacePagination(relativePath, html, language, context);
    }

    private static string InsertLanguageSwitch(string html, string languageSwitch)
    {
        const string headerActions = "<div class=\"docs-header-actions\">";
        var actionsStart = html.IndexOf(headerActions, StringComparison.Ordinal);
        var actionsEnd = actionsStart < 0
            ? -1
            : html.IndexOf("</div>", actionsStart, StringComparison.Ordinal);

        return actionsEnd >= 0
            ? html.Insert(actionsEnd, languageSwitch)
            : html.Replace("</header>", $"{languageSwitch}</header>", StringComparison.Ordinal);
    }

    private static string BuildNavigation(
        string relativePath,
        string language,
        SiteTemplateContext context)
    {
        var currentUrl = context.GetSitePath(relativePath.Replace('\\', '/'));
        var labels = language == "ja"
            ? new NavigationLabels("ユーザーマニュアル", "プライバシーポリシー", "サポート")
            : new NavigationLabels("User manual", "Privacy policy", "Support");
        var navigationLabel = language == "ja"
            ? "ドキュメントナビゲーション"
            : "Documentation navigation";
        var manualPath = $"posts/user-manual-{language}.html";
        var privacyPath = $"posts/privacy-policy-{language}.html";

        return $"""
            <aside id="docs-sidebar" class="docs-sidebar" data-docs-sidebar>
            <nav aria-label="{navigationLabel}">
            <ul class="docs-nav-list">
            {NavigationItem(labels.Manual, context.GetSitePath(manualPath), currentUrl)}
            {NavigationItem(labels.Privacy, context.GetSitePath(privacyPath), currentUrl)}
            {NavigationItem(labels.Support, context.GetSitePath("support.html"), currentUrl)}
            </ul>
            </nav>
            </aside>
            """;
    }

    private static string BuildLanguageSwitch(string relativePath, SiteTemplateContext context)
    {
        var languagePair = GetLanguagePair(relativePath);
        if (languagePair is null)
        {
            return string.Empty;
        }

        var (englishPath, japanesePath) = languagePair.Value;
        var currentPath = relativePath.Replace('\\', '/');
        var englishUrl = context.GetSitePath(englishPath);
        var japaneseUrl = context.GetSitePath(japanesePath);
        var englishCurrent = string.Equals(currentPath, englishPath, StringComparison.OrdinalIgnoreCase);
        var japaneseCurrent = string.Equals(currentPath, japanesePath, StringComparison.OrdinalIgnoreCase);
        var label = currentPath.EndsWith("-ja.html", StringComparison.OrdinalIgnoreCase)
            ? "言語"
            : "Language";

        return $"""
                <nav class="docs-language-switch" aria-label="{label}">
                  <span class="docs-language-label">{label}</span>
                  <a href="{englishUrl}"{CurrentAttribute(englishCurrent)}>EN</a>
                  <a href="{japaneseUrl}"{CurrentAttribute(japaneseCurrent)}>JA</a>
                </nav>
            """;
    }

    private static string LocalizeChrome(string html, string language)
    {
        if (language != "ja")
        {
            return html;
        }

        return html
            .Replace("<html lang=\"en\">", "<html lang=\"ja\">", StringComparison.Ordinal)
            .Replace(">Menu<", ">メニュー<", StringComparison.Ordinal)
            .Replace("aria-label=\"Documentation navigation\"", "aria-label=\"ドキュメントナビゲーション\"", StringComparison.Ordinal)
            .Replace(">On this page<", ">この記事の内容<", StringComparison.Ordinal)
            .Replace("aria-label=\"On this page\"", "aria-label=\"この記事の内容\"", StringComparison.Ordinal)
            .Replace("aria-label=\"Dark mode\"", "aria-label=\"ダークモード\"", StringComparison.Ordinal)
            .Replace("aria-label=\"Document navigation\"", "aria-label=\"文書ナビゲーション\"", StringComparison.Ordinal);
    }

    private static string ReplacePagination(
        string relativePath,
        string html,
        string language,
        SiteTemplateContext context)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        var isManual = normalizedPath.Equals($"posts/user-manual-{language}.html", StringComparison.OrdinalIgnoreCase);
        var isPrivacy = normalizedPath.Equals($"posts/privacy-policy-{language}.html", StringComparison.OrdinalIgnoreCase);
        if (!isManual && !isPrivacy)
        {
            return html;
        }

        var start = html.IndexOf("<nav class=\"docs-pagination\"", StringComparison.Ordinal);
        var end = start < 0
            ? -1
            : html.IndexOf("</nav>", start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            return html;
        }

        var previous = language == "ja" ? "前へ" : "Previous";
        var next = language == "ja" ? "次へ" : "Next";
        var manualLabel = language == "ja" ? "ユーザーマニュアル" : "User manual";
        var privacyLabel = language == "ja" ? "プライバシーポリシー" : "Privacy policy";
        var body = isManual
            ? $"""
              <span></span>
              <a class="docs-pagination-next" rel="next" href="{context.GetSitePath($"posts/privacy-policy-{language}.html")}"><small>{next}</small><span>{privacyLabel}</span></a>
              """
            : $"""
              <a class="docs-pagination-previous" rel="prev" href="{context.GetSitePath($"posts/user-manual-{language}.html")}"><small>{previous}</small><span>{manualLabel}</span></a>
              <span></span>
              """;
        var paginationLabel = language == "ja" ? "文書ナビゲーション" : "Document navigation";
        var pagination = $"<nav class=\"docs-pagination\" aria-label=\"{paginationLabel}\">{body}</nav>";
        return string.Concat(html.AsSpan(0, start), pagination, html.AsSpan(end + "</nav>".Length));
    }

    private static string NavigationItem(string label, string url, string currentUrl)
    {
        var current = string.Equals(url, currentUrl, StringComparison.OrdinalIgnoreCase);
        return $"""<li><a class="docs-nav-link{(current ? " is-current" : string.Empty)}" href="{url}"{CurrentAttribute(current)}>{label}</a></li>""";
    }

    private static string CurrentAttribute(bool current) => current ? " aria-current=\"page\"" : string.Empty;

    private static (string English, string Japanese)? GetLanguagePair(string relativePath)
    {
        const string manualEnglish = "posts/user-manual-en.html";
        const string manualJapanese = "posts/user-manual-ja.html";
        const string privacyEnglish = "posts/privacy-policy-en.html";
        const string privacyJapanese = "posts/privacy-policy-ja.html";

        return relativePath.Replace('\\', '/') switch
        {
            manualEnglish or manualJapanese => (manualEnglish, manualJapanese),
            privacyEnglish or privacyJapanese => (privacyEnglish, privacyJapanese),
            _ => null,
        };
    }

    private sealed record NavigationLabels(string Manual, string Privacy, string Support);
}
