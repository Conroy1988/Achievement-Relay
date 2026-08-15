using System.Text.Json;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

var tests = new (string Name, Action Run)[]
{
    ("OpenXBL API keys are normalized without weakening validation", ValidatesOpenXblApiKeys),
    ("OpenXBL account profile is parsed case-insensitively", ParsesOpenXblAccount),
    ("OpenXBL object profiles and display-name fallbacks are supported", ParsesOpenXblObjectAccount),
    ("OpenXBL account envelopes and people profiles are supported", ParsesOpenXblAccountEnvelope),
    ("Incomplete OpenXBL account profile is rejected", RejectsIncompleteOpenXblAccount),
    ("OpenXBL title progress index is parsed", ParsesTitleProgress),
    ("OpenXBL title progress envelopes are supported", ParsesWrappedTitleProgress),
    ("Only unlocked, non-revoked achievements are parsed", ParsesUnlockedAchievements),
    ("Achievement identities are stable and account-specific", AchievementIdentityIsStable),
    ("OpenXBL root arrays and alternate fields are supported", ParsesAlternateAchievementShape),
    ("Webhook URL validation is strict", ValidatesWebhookUrls),
    ("Discord payload suppresses mentions", PayloadSuppressesMentions),
    ("Description sharing setting is respected", DescriptionSettingIsRespected),
    ("Connection test suppresses mentions", ConnectionTestSuppressesMentions)
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

static void ValidatesOpenXblApiKeys()
{
    Assert(
        OpenXblApiKeyValidator.TryNormalize("  test-key_123  ", out var normalized, out _),
        "A simple API key was rejected.");
    Assert(normalized == "test-key_123", "API key whitespace was not trimmed.");
    Assert(
        !OpenXblApiKeyValidator.TryNormalize("test key", out _, out _),
        "An API key containing whitespace was accepted.");
    Assert(
        !OpenXblApiKeyValidator.TryNormalize(new string('a', 513), out _, out _),
        "An oversized API key was accepted.");
}

static void ParsesOpenXblAccount()
{
    const string json = """
        {
          "ProfileUsers": [
            {
              "ID": "2533274999999999",
              "Settings": [
                { "Id": "GameDisplayName", "Value": "Relay Player" },
                { "Id": "Gamertag", "Value": "RelayTester" }
              ]
            }
          ]
        }
        """;

    var account = OpenXblResponseParser.ParseAccount(json);
    Assert(account.Xuid == "2533274999999999", $"Unexpected XUID: {account.Xuid}");
    Assert(account.Gamertag == "RelayTester", $"Unexpected gamertag: {account.Gamertag}");
}

static void ParsesOpenXblObjectAccount()
{
    const string json = """
        {
          "profileUsers": {
            "hostId": 2533274999999998,
            "settings": {
              "GameDisplayName": "Relay Player"
            }
          }
        }
        """;

    var account = OpenXblResponseParser.ParseAccount(json);
    Assert(account.Xuid == "2533274999999998", $"Unexpected object-profile XUID: {account.Xuid}");
    Assert(account.Gamertag == "Relay Player", $"Unexpected display-name fallback: {account.Gamertag}");
}

static void ParsesOpenXblAccountEnvelope()
{
    const string json = """
        {
          "data": {
            "people": [
              {
                "xuid": "2533274999999997",
                "gamertag": "EnvelopeRelay"
              }
            ]
          }
        }
        """;

    var account = OpenXblResponseParser.ParseAccount(json);
    Assert(account.Xuid == "2533274999999997", $"Unexpected enveloped XUID: {account.Xuid}");
    Assert(account.Gamertag == "EnvelopeRelay", $"Unexpected enveloped gamertag: {account.Gamertag}");
}

static void RejectsIncompleteOpenXblAccount()
{
    AssertThrows<JsonException>(
        () => OpenXblResponseParser.ParseAccount("""{"profileUsers":[{"id":"123","settings":[]}]}"""),
        "An account without a gamertag was accepted.");
}

static void ParsesTitleProgress()
{
    const string json = """
        {
          "titles": [
            {
              "titleId": "1842701288",
              "name": "Example PC Game",
              "devices": ["PC", "XboxOne", "PC"],
              "achievement": {
                "currentAchievements": 7,
                "totalAchievements": 42,
                "currentGamerscore": "135",
                "totalGamerscore": 1000
              },
              "titleHistory": {
                "lastTimePlayed": "2026-08-14T11:58:21.8718942Z"
              }
            },
            {
              "titleId": 1777860928,
              "name": "Another Game",
              "achievement": {
                "currentAchievements": 3,
                "currentGamerscore": 50
              }
            }
          ],
          "pagingInfo": { "continuationToken": null, "totalRecords": 2 }
        }
        """;

    var titles = OpenXblResponseParser.ParseTitleProgress(json);
    Assert(titles.Count == 2, $"Expected two title summaries, found {titles.Count}.");
    var title = titles.Single(item => item.TitleId == "1842701288");
    Assert(title.Name == "Example PC Game", $"Unexpected title name: {title.Name}");
    Assert(title.CurrentAchievements == 7, "Current achievement count was not parsed.");
    Assert(title.CurrentGamerscore == 135, "Current Gamerscore was not parsed.");
    Assert(title.Devices.SequenceEqual(new[] { "PC", "XboxOne" }), "Device list was not normalized.");
    Assert(
        title.LastPlayedAt == new DateTimeOffset(2026, 8, 14, 11, 58, 21, 871, TimeSpan.Zero).AddTicks(8942),
        $"Unexpected last-played timestamp: {title.LastPlayedAt:O}");
}

static void ParsesWrappedTitleProgress()
{
    const string json = """
        {
          "data": {
            "items": [
              {
                "titleId": "123456789",
                "name": "Wrapped Game",
                "achievement": {
                  "currentAchievements": 4,
                  "currentGamerscore": 80
                }
              }
            ]
          }
        }
        """;

    var titles = OpenXblResponseParser.ParseTitleProgress(json);
    Assert(titles.Count == 1, $"Expected one wrapped title summary, found {titles.Count}.");
    Assert(titles[0].Name == "Wrapped Game", $"Unexpected wrapped title name: {titles[0].Name}");
}

static void ParsesUnlockedAchievements()
{
    var achievements = OpenXblResponseParser.ParseAchievements(StandardAchievementResponse(), "2533274999999999");

    Assert(achievements.Count == 1, $"Expected one usable achievement, found {achievements.Count}.");
    var achievement = achievements[0];
    Assert(achievement.Name == "Into the Unknown", $"Unexpected name: {achievement.Name}");
    Assert(achievement.Description == "Leave the first planet", $"Unexpected description: {achievement.Description}");
    Assert(achievement.GameName == "Starfield", $"Unexpected game: {achievement.GameName}");
    Assert(achievement.Gamerscore == 15, $"Unexpected gamerscore: {achievement.Gamerscore}");
    Assert(achievement.IsRare, "Rare achievement metadata was not preserved.");
    Assert(achievement.ImageUrl == "https://images.example.test/achievement.png", "Achievement icon was not parsed.");
    Assert(achievement.SourceProvider == "OpenXBL", "Provider metadata was not set.");
    Assert(
        achievement.UnlockedAt == new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
        $"Unexpected unlock time: {achievement.UnlockedAt:O}");
    Assert(achievement.Id.Length == 64, "Achievement identity is not a SHA-256 hex value.");
}

static void AchievementIdentityIsStable()
{
    var first = OpenXblResponseParser.ParseAchievements(StandardAchievementResponse(), "account-a")[0];
    var second = OpenXblResponseParser.ParseAchievements(StandardAchievementResponse(), "account-a")[0];
    var correctedTimestamp = OpenXblResponseParser.ParseAchievements(
        StandardAchievementResponse().Replace(
            "2026-08-14T12:00:00Z",
            "2026-08-14T12:00:01Z",
            StringComparison.Ordinal),
        "account-a")[0];
    var otherAccount = OpenXblResponseParser.ParseAchievements(StandardAchievementResponse(), "account-b")[0];

    Assert(first.Id == second.Id, "Equivalent API responses generated different identities.");
    Assert(first.Id == correctedTimestamp.Id, "An upstream timestamp correction changed the achievement identity.");
    Assert(first.Id != otherAccount.Id, "Different Xbox accounts generated the same identity.");
}

static void ParsesAlternateAchievementShape()
{
    const string json = """
        [
          {
            "id": "alternate-1",
            "scid": "alternate-scid",
            "name": "A Different Path",
            "achievementState": "1",
            "timeUnlocked": "2026-08-14T13:15:00Z",
            "titleName": "Example Game",
            "gamerscore": 25,
            "rarityPercentage": "9.9",
            "image": "https://images.example.test/alternate.png"
          }
        ]
        """;

    var achievements = OpenXblResponseParser.ParseAchievements(json, "account-a");
    Assert(achievements.Count == 1, "Root-array achievement response was not parsed.");
    Assert(achievements[0].GameName == "Example Game", "Alternate title field was not parsed.");
    Assert(achievements[0].Gamerscore == 25, "Direct gamerscore field was not parsed.");
    Assert(achievements[0].IsRare, "Direct rarity percentage was not parsed.");
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
    var achievement = Achievement("@everyone Secret Finder", "Unlocked without pinging anyone.");

    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, new AppSettings()));
    var parse = document.RootElement.GetProperty("allowed_mentions").GetProperty("parse");
    Assert(parse.GetArrayLength() == 0, "Payload allowed Discord mention parsing.");
}

static void DescriptionSettingIsRespected()
{
    var achievement = Achievement("Quiet Details", "This should remain local.");
    var settings = new AppSettings { IncludeRawDetailsWhenUncertain = false };

    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, settings));
    var embed = document.RootElement.GetProperty("embeds")[0];
    Assert(!embed.TryGetProperty("description", out _), "Description was posted while sharing was disabled.");
}

static void ConnectionTestSuppressesMentions()
{
    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.CreateConnectionTest(new AppSettings()));
    var parse = document.RootElement.GetProperty("allowed_mentions").GetProperty("parse");
    Assert(parse.GetArrayLength() == 0, "Connection test allowed Discord mention parsing.");
}

static AchievementEvent Achievement(string name, string description) => new()
{
    Id = "test",
    Name = name,
    Description = description,
    Gamerscore = 20,
    SourceProvider = "OpenXBL",
    UnlockedAt = DateTimeOffset.UtcNow
};

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string StandardAchievementResponse() => """
    {
      "achievements": [
        {
          "id": "achievement-1",
          "serviceConfigId": "00000000-0000-0000-0000-000000000001",
          "name": "Into the Unknown",
          "description": "Keep playing to reveal this achievement",
          "unlockedDescription": "Leave the first planet",
          "progressState": "Achieved",
          "isRevoked": false,
          "progression": { "timeUnlocked": "2026-08-14T12:00:00Z" },
          "titleAssociations": [ { "id": 1717, "name": "Starfield" } ],
          "rewards": [ { "type": "Gamerscore", "value": "15" } ],
          "mediaAssets": [
            { "type": "Background", "url": "https://images.example.test/background.png" },
            { "type": "Icon", "url": "https://images.example.test/achievement.png" }
          ],
          "rarity": { "currentCategory": "Rare", "currentPercentage": 4.25 }
        },
        {
          "id": "achievement-locked",
          "name": "Not Yet",
          "progressState": "NotStarted",
          "progression": { "timeUnlocked": "2026-08-14T12:05:00Z" }
        },
        {
          "id": "achievement-revoked",
          "name": "Taken Back",
          "progressState": "Achieved",
          "isRevoked": true,
          "progression": { "timeUnlocked": "2026-08-14T12:10:00Z" }
        }
      ]
    }
    """;
