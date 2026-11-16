using System.Text.Json;

namespace LinearBar.Core.GitHub;

/// <summary>An unread GitHub notification.</summary>
public sealed record GitHubNotification(string Id, string Title, string Repository, string Type, string Reason, string WebUrl, DateTimeOffset Updated);

/// <summary>A pull request or issue from a search.</summary>
public sealed record GitHubItem(string Title, string Repository, int Number, string WebUrl, DateTimeOffset Updated, bool IsDraft);

/// <summary>Parses the GitHub REST API responses the GitHub widget uses.</summary>
public static class GitHubData
{
    public const string NotificationsUrl = "https://api.github.com/notifications?per_page=50";

    /// <summary>Open pull requests that wait for the user's review.</summary>
    public const string ReviewRequestsUrl = "https://api.github.com/search/issues?per_page=20&q=" + "is%3Aopen+is%3Apr+review-requested%3A%40me+archived%3Afalse";

    /// <summary>The user's own open pull requests.</summary>
    public const string MyPullRequestsUrl = "https://api.github.com/search/issues?per_page=20&q=" + "is%3Aopen+is%3Apr+author%3A%40me+archived%3Afalse";

    public static List<GitHubNotification> ParseNotifications(string json)
    {
        var list = new List<GitHubNotification>();
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("unread", out JsonElement unread) && unread.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            JsonElement subject = item.GetProperty("subject");
            string repository = item.TryGetProperty("repository", out JsonElement repo) ? Text(repo, "full_name") : string.Empty;
            string type = Text(subject, "type");
            string apiUrl = Text(subject, "url");

            list.Add(new GitHubNotification(
                Text(item, "id"),
                Text(subject, "title"),
                repository,
                type,
                Text(item, "reason"),
                WebUrl(apiUrl, repository, type),
                Date(item, "updated_at")));
        }

        return list;
    }

    public static List<GitHubItem> ParseSearch(string json)
    {
        var list = new List<GitHubItem>();
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            string repositoryUrl = Text(item, "repository_url");
            int marker = repositoryUrl.IndexOf("/repos/", StringComparison.Ordinal);
            string repository = marker >= 0 ? repositoryUrl[(marker + 7)..] : repositoryUrl;

            list.Add(new GitHubItem(
                Text(item, "title"),
                repository,
                item.TryGetProperty("number", out JsonElement n) && n.TryGetInt32(out int number) ? number : 0,
                Text(item, "html_url"),
                Date(item, "updated_at"),
                item.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True));
        }

        return list;
    }

    /// <summary>
    /// Notifications point at API URLs; turn them into the page a browser should open.
    /// "https://api.github.com/repos/o/r/pulls/12" → "https://github.com/o/r/pull/12".
    /// </summary>
    public static string WebUrl(string apiUrl, string repository, string type)
    {
        if (string.IsNullOrEmpty(apiUrl))
        {
            return string.IsNullOrEmpty(repository) ? "https://github.com/notifications" : "https://github.com/" + repository;
        }

        string url = apiUrl.Replace("https://api.github.com/repos/", "https://github.com/", StringComparison.Ordinal);
        url = url.Replace("/pulls/", "/pull/", StringComparison.Ordinal);
        url = url.Replace("/commits/", "/commit/", StringComparison.Ordinal);

        if (type == "Release")
        {
            // Release notifications point at /releases/<id>; the list page is the useful one.
            int releases = url.IndexOf("/releases/", StringComparison.Ordinal);
            if (releases > 0)
            {
                url = url[..(releases + "/releases".Length)];
            }
        }

        return url;
    }

    /// <summary>Human description of why a notification arrived.</summary>
    public static string DescribeReason(string reason) => reason switch
    {
        "review_requested" => "revisão pedida",
        "mention" or "team_mention" => "mencionado",
        "assign" => "atribuído",
        "author" => "autor",
        "comment" => "comentário",
        "ci_activity" => "CI",
        "state_change" => "mudou de estado",
        "subscribed" => "a seguir",
        "security_alert" => "alerta de segurança",
        _ => reason.Replace('_', ' '),
    };

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static DateTimeOffset Date(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), out DateTimeOffset date) ? date : DateTimeOffset.MinValue;
}
