using System.Text.Json;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

var tests = new (string Name, Action Run)[]
{
    ("Non-Xbox notifications are rejected before parsing", RejectsNonXboxNotification),
    ("Known Xbox package is accepted", AcceptsKnownXboxPackage),
    ("Display-name spoof is rejected", RejectsDisplayNameSpoof),
    ("Typical rare achievement is parsed", ParsesRareAchievement),
    ("Webhook URL validation is strict", ValidatesWebhookUrls),
    ("Discord payload suppresses mentions", PayloadSuppressesMentions),
    ("Description sharing setting is respected", DescriptionSettingIsRespected),
    ("Notification fingerprint is stable", FingerprintIsStable)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL  {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} checks passed.");
return failures.Count == 0 ? 0 : 1;

static void RejectsNonXboxNotification()
{
    var classifier = new XboxNotificationClassifier();
    var notification = Notification(
        "Contoso.Chat_abc123",
        "Chat from work",
        "Achievement unlocked",
        "+100G");

    Assert(!classifier.IsXboxSource(notification), "A non-Xbox sender crossed the source filter.");
    Assert(!classifier.IsAchievement(notification), "A non-Xbox notification was classified as an achievement.");
}

static void AcceptsKnownXboxPackage()
{
    var classifier = new XboxNotificationClassifier();
    var notification = Notification(
        "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe",
        "Xbox Game Bar",
        "Achievement unlocked",
        "+10G");

    Assert(classifier.IsXboxSource(notification), "Xbox Game Bar was not recognized.");
    Assert(classifier.IsAchievement(notification), "Xbox achievement content was not recognized.");
}

static void RejectsDisplayNameSpoof()
{
    var classifier = new XboxNotificationClassifier();
    var notification = Notification(
        "Evil.Microsoft.XboxGamingOverlay_8wekyb3d8bbwe",
        "Xbox Game Bar",
        "Achievement unlocked",
        "+10G");

    Assert(!classifier.IsXboxSource(notification), "Display name bypassed the package identity filter.");
}

static void ParsesRareAchievement()
{
    var parser = new AchievementNotificationParser(new XboxNotificationClassifier());
    var parsed = parser.Parse(Notification(
        "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe",
        "Xbox Game Bar",
        "Rare achievement unlocked",
        "Into the Unknown",
        "Leave the first planet",
        "+15G",
        "Game: Starfield"));

    Assert(parsed is not null, "Parser returned no achievement.");
    Assert(parsed!.Name == "Into the Unknown", $"Unexpected name: {parsed.Name}");
    Assert(parsed.Description == "Leave the first planet", $"Unexpected description: {parsed.Description}");
    Assert(parsed.GameName == "Starfield", $"Unexpected game: {parsed.GameName}");
    Assert(parsed.Gamerscore == 15, $"Unexpected gamerscore: {parsed.Gamerscore}");
    Assert(parsed.IsRare, "Rare marker was not preserved.");
}

static void ValidatesWebhookUrls()
{
    const string testWebhookId = "123456789012345678";
    const string testToken = "not-a-real-webhook-token-0123456789";
    var valid = $"https://discord.com/api/webhooks/{testWebhookId}/{testToken}";
    var versioned = $"https://discord.com/api/v10/webhooks/{testWebhookId}/{testToken}";

    Assert(WebhookUrlValidator.TryNormalize(valid, out _, out _), "Standard Discord webhook was rejected.");
    Assert(WebhookUrlValidator.TryNormalize(versioned, out _, out _), "Versioned Discord webhook was rejected.");
    Assert(!WebhookUrlValidator.TryNormalize("http://discord.com/api/webhooks/1/not-safe", out _, out _), "HTTP webhook was accepted.");
    Assert(!WebhookUrlValidator.TryNormalize($"https://example.com/api/webhooks/{testWebhookId}/{testToken}", out _, out _), "Foreign host was accepted.");
}

static void PayloadSuppressesMentions()
{
    var achievement = new AchievementEvent
    {
        Id = "test",
        Name = "@everyone Secret Finder",
        Description = "Unlocked without pinging anyone.",
        Gamerscore = 20,
        SourceApplication = "Xbox Game Bar",
        SourcePackageFamilyName = "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe",
        UnlockedAt = DateTimeOffset.UtcNow
    };

    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, new AppSettings()));
    var parse = document.RootElement.GetProperty("allowed_mentions").GetProperty("parse");
    Assert(parse.GetArrayLength() == 0, "Payload allowed Discord mention parsing.");
}

static void DescriptionSettingIsRespected()
{
    var achievement = new AchievementEvent
    {
        Id = "description-test",
        Name = "Quiet Details",
        Description = "This should remain local.",
        SourceApplication = "Xbox Game Bar",
        SourcePackageFamilyName = "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe",
        UnlockedAt = DateTimeOffset.UtcNow
    };

    var settings = new AppSettings { IncludeRawDetailsWhenUncertain = false };
    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, settings));
    var embed = document.RootElement.GetProperty("embeds")[0];
    Assert(!embed.TryGetProperty("description", out _), "Description was posted while sharing was disabled.");
}

static void FingerprintIsStable()
{
    var notification = Notification(
        "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe",
        "Xbox Game Bar",
        "Achievement unlocked",
        "First Steps",
        "+5G");

    var first = NotificationFingerprint.Create(notification, "First Steps", 5);
    var second = NotificationFingerprint.Create(notification, "First Steps", 5);
    Assert(first == second, "Equivalent notifications generated different fingerprints.");
    Assert(first.Length == 64, "Fingerprint is not a SHA-256 hex value.");
}

static RawNotification Notification(string package, string displayName, params string[] text) => new()
{
    PlatformId = 42,
    ApplicationDisplayName = displayName,
    PackageFamilyName = package,
    CreatedAt = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
    TextElements = text
};

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
