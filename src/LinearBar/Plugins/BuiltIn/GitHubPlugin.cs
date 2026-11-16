using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using LinearBar.Core.GitHub;
using LinearBar.Core.Plugins;
using LinearBar.Core.Usage;

namespace LinearBar.Plugins.BuiltIn;

/// <summary>
/// Unread GitHub notifications and pull requests waiting for your review.
/// </summary>
/// <remarks>
/// Token, in order: the <c>token</c> option, the <c>GH_TOKEN</c>/<c>GITHUB_TOKEN</c>
/// environment variables, or the GitHub CLI (<c>gh auth token</c>) when it is signed in.
/// The notifications API needs a classic token with the <c>notifications</c> scope (the
/// GitHub CLI's token has it).
/// </remarks>
internal sealed class GitHubPlugin : PollingPlugin
{
    private static readonly HttpClient Http = CreateClient();

    private string? _cliToken;
    private DateTimeOffset _cliTokenChecked;

    public override string Id => "github";

    public override string Name => "GitHub";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(2);

    public override bool TryHandle(string action)
    {
        if (action != "github.markread")
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            if (await ResolveTokenAsync(CancellationToken.None).ConfigureAwait(false) is { } token)
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, "https://api.github.com/notifications") { Content = new StringContent("{}") };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using HttpResponseMessage _ = await Http.SendAsync(request).ConfigureAwait(false);
            }

            Refresh();
        });
        return true;
    }

    protected override async Task<PluginUpdate> ProduceAsync(CancellationToken cancellation)
    {
        string? token = await ResolveTokenAsync(cancellation).ConfigureAwait(false);
        if (token is null)
        {
            return new PluginUpdate(NotConnected());
        }

        Task<string> notifications = GetAsync(GitHubData.NotificationsUrl, token, cancellation);
        Task<string> reviews = GetAsync(GitHubData.ReviewRequestsUrl, token, cancellation);
        Task<string> mine = GetAsync(GitHubData.MyPullRequestsUrl, token, cancellation);

        List<GitHubNotification> unread = [];
        try
        {
            unread = GitHubData.ParseNotifications(await notifications.ConfigureAwait(false));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // Fine-grained tokens cannot read notifications; still show pull requests.
        }

        List<GitHubItem> toReview = GitHubData.ParseSearch(await reviews.ConfigureAwait(false));
        List<GitHubItem> open = GitHubData.ParseSearch(await mine.ConfigureAwait(false));
        return new PluginUpdate(Render(unread, toReview, open, DateTimeOffset.Now));
    }

    internal static PluginOutput Render(IReadOnlyList<GitHubNotification> unread, IReadOnlyList<GitHubItem> toReview, IReadOnlyList<GitHubItem> mine, DateTimeOffset now)
    {
        var rows = new List<PluginRow>();

        if (toReview.Count > 0)
        {
            rows.Add(new PluginRow { Icon = "pull-request", Title = $"Revisões pedidas ({toReview.Count})", Color = "#A8A8B4" });
            rows.AddRange(toReview.Take(5).Select(pr => new PluginRow
            {
                Icon = "pull-request",
                Title = pr.Title,
                Detail = $"{pr.Repository}#{pr.Number}{(pr.IsDraft ? " · rascunho" : "")}",
                Value = Ago(pr.Updated, now),
                Open = pr.WebUrl,
                Color = "#A371F7",
            }));
        }

        if (unread.Count > 0)
        {
            rows.Add(new PluginRow { Icon = "bell", Title = $"Notificações ({unread.Count})", Color = "#A8A8B4" });
            rows.AddRange(unread.Take(7).Select(n => new PluginRow
            {
                Icon = n.Type == "PullRequest" ? "pull-request" : n.Type == "Release" ? "sparkle" : "bell",
                Title = n.Title,
                Detail = $"{n.Repository} · {GitHubData.DescribeReason(n.Reason)}",
                Value = Ago(n.Updated, now),
                Open = n.WebUrl,
            }));
        }

        if (mine.Count > 0)
        {
            rows.Add(new PluginRow { Icon = "pull-request", Title = $"Os meus PRs abertos ({mine.Count})", Color = "#A8A8B4" });
            rows.AddRange(mine.Take(4).Select(pr => new PluginRow
            {
                Icon = "pull-request",
                Title = pr.Title,
                Detail = $"{pr.Repository}#{pr.Number}",
                Value = Ago(pr.Updated, now),
                Open = pr.WebUrl,
            }));
        }

        if (rows.Count == 0)
        {
            rows.Add(new PluginRow { Icon = "check", Title = "Tudo em dia", Detail = "Sem notificações nem revisões pendentes" });
        }

        int attention = unread.Count + toReview.Count;
        return new PluginOutput
        {
            Text = attention > 0 ? (toReview.Count > 0 ? $"{unread.Count} · {toReview.Count} PR" : unread.Count.ToString()) : "0",
            Icon = "github",
            Color = toReview.Count > 0 ? "#A371F7" : attention > 0 ? "#58A6FF" : "#8B949E",
            Tooltip = $"GitHub\n{unread.Count} notificações por ler\n{toReview.Count} PRs à espera da tua revisão",
            Panel = new PluginPanel
            {
                Title = "GitHub",
                Subtitle = attention > 0 ? $"{attention} coisas à tua espera" : "Tudo em dia",
                Rows = rows,
                Buttons =
                [
                    new PluginAction { Title = "Notificações", Icon = "external", Open = "https://github.com/notifications" },
                    new PluginAction { Title = "Pull requests", Icon = "external", Open = "https://github.com/pulls/review-requested" },
                    new PluginAction { Title = "Marcar lidas", Icon = "check", Action = "github.markread" },
                ],
            },
        };
    }

    private static PluginOutput NotConnected() => new()
    {
        Text = "Ligar",
        Icon = "github",
        Color = "#8B949E",
        Tooltip = "GitHub por ligar",
        Panel = new PluginPanel
        {
            Title = "GitHub",
            Subtitle = "Ligue a sua conta para ver notificações e revisões pedidas.",
            Rows =
            [
                new PluginRow { Icon = "terminal", Title = "Com o GitHub CLI", Detail = "Instale o gh e corra «gh auth login» — o LinearBar usa a mesma sessão" },
                new PluginRow { Icon = "plug", Title = "Ou com um token", Detail = "\"plugins\": { \"github\": { \"token\": \"ghp_…\" } } (scopes: notifications, repo)" },
            ],
            Buttons = [new PluginAction { Title = "Criar token", Icon = "external", Open = "https://github.com/settings/tokens/new?scopes=notifications,repo&description=LinearBar" }],
        },
    };

    private async Task<string?> ResolveTokenAsync(CancellationToken cancellation)
    {
        string? token = Config.GetString("token");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token.Trim();
        }

        token = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token.Trim();
        }

        // Ask the GitHub CLI at most every 10 minutes.
        if (DateTimeOffset.Now - _cliTokenChecked < TimeSpan.FromMinutes(10))
        {
            return _cliToken;
        }

        _cliTokenChecked = DateTimeOffset.Now;
        _cliToken = await RunGhAsync(cancellation).ConfigureAwait(false);
        return _cliToken;
    }

    private static async Task<string?> RunGhAsync(CancellationToken cancellation)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("gh", "auth token")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string token = output.Trim();
            return process.ExitCode == 0 && token.Length > 0 ? token : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<string> GetAsync(string url, string token, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await Http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("token do GitHub inválido ou expirado");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
    }

    private static string Ago(DateTimeOffset time, DateTimeOffset now)
    {
        TimeSpan age = now - time;
        return age.TotalMinutes < 60 ? $"{Math.Max(1, (int)age.TotalMinutes)} min"
            : age.TotalHours < 24 ? $"{(int)age.TotalHours} h"
            : $"{(int)age.TotalDays} d";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LinearBar/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
