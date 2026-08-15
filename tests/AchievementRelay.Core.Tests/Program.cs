using System.Text.Json;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

var tests = new (string Name, Action Run)[]
{
    ("OpenXBL API keys are normalized without weakening validation", ValidatesOpenXblApiKeys),
    ("OpenXBL account profile is parsed case-insensitively", ParsesOpenXblAccount),
    ("OpenXBL object profiles and display-name fallbacks are supported", ParsesOpenXblObjectAccount),
    ("OpenXBL account envelopes and people profiles are supported", ParsesOpenXblAccountEnvelope),
    ("OpenXBL nested identity and profile fields are combined", ParsesNestedOpenXblAccount),
    ("Incomplete OpenXBL account profile is rejected", RejectsIncompleteOpenXblAccount),
    ("OpenXBL title progress index is parsed", ParsesTitleProgress),
    ("OpenXBL title progress envelopes are supported", ParsesWrappedTitleProgress),
    ("OpenXBL title-history envelopes and userTitles are supported", ParsesTitleHistoryEnvelope),
    ("Modern recent-progress title fields are supported", ParsesModernRecentTitleProgress),
    ("OpenXBL string-wrapped title history is supported", ParsesStringWrappedTitleHistory),
    ("Only unlocked, non-revoked achievements are parsed", ParsesUnlockedAchievements),
    ("Achievement identities are stable and account-specific", AchievementIdentityIsStable),
    ("OpenXBL root arrays and alternate fields are supported", ParsesAlternateAchievementShape),
    ("OpenXBL string-wrapped achievements are supported", ParsesStringWrappedAchievements),
    ("OpenXBL achievement continuation tokens are discovered", ParsesAchievementContinuationToken),
    ("OpenXBL Xbox 360 achievements are supported", ParsesXbox360Achievements),
    ("Xbox 360 sentinel and missing unlock times remain parseable", ParsesUntimestampedXbox360Achievements),
    ("Durable identities detect untimestamped achievements", DetectsUntimestampedAchievementByIdentity),
    ("Unchanged count-only state hydrates identities without posting", HydratesIdentityBaselineWithoutPosting),
    ("Provider identity churn cannot flood historical achievements", SafelyBaselinesProviderIdentityChurn),
    ("Count-only state safely attributes one untimestamped migration unlock", AttributesUniqueUntimestampedMigrationUnlock),
    ("Gamerscore uniquely attributes an untimestamped migration unlock", AttributesUntimestampedMigrationUnlockByGamerscore),
    ("Ambiguous count-only migration baselines without flooding Discord", SafelyBaselinesAmbiguousMigrationUnlock),
    ("Incomplete achievement detail is retried without advancing state", RejectsIncompleteAchievementDetail),
    ("Ahead-of-summary achievement detail is retried without advancing state", RejectsOvercompleteAchievementDetail),
    ("Webhook URL validation is strict", ValidatesWebhookUrls),
    ("Discord payload suppresses mentions", PayloadSuppressesMentions),
    ("Discord identifies estimated provider timestamps", PayloadLabelsEstimatedTimestamp),
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

static void ParsesNestedOpenXblAccount()
{
    const string json = """
        {
          "data": {
            "account": {
              "xboxUserId": "2533274999999996",
              "profile": {
                "uniqueModernGamertag": "NestedRelay#1100"
              }
            }
          }
        }
        """;

    var account = OpenXblResponseParser.ParseAccount(json);
    Assert(account.Xuid == "2533274999999996", $"Unexpected nested XUID: {account.Xuid}");
    Assert(account.Gamertag == "NestedRelay#1100", $"Unexpected nested gamertag: {account.Gamertag}");
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

static void ParsesTitleHistoryEnvelope()
{
    const string json = """
        {
          "data": {
            "titleHistory": {
              "userTitles": [
                {
                  "titleId": "1297287736",
                  "titleName": "History Test Game",
                  "currentAchievements": 8,
                  "currentGamerscore": 80,
                  "lastPlayed": "2026-08-15T15:20:00Z"
                }
              ]
            }
          }
        }
        """;

    var titles = OpenXblResponseParser.ParseTitleProgress(json);
    Assert(titles.Count == 1, $"Expected one title-history summary, found {titles.Count}.");
    Assert(titles[0].TitleId == "1297287736", $"Unexpected title-history ID: {titles[0].TitleId}");
    Assert(titles[0].Name == "History Test Game", $"Unexpected title-history name: {titles[0].Name}");
    Assert(titles[0].CurrentAchievements == 8, "Title-history achievement count was not parsed.");
    Assert(titles[0].CurrentGamerscore == 80, "Title-history Gamerscore was not parsed.");
    Assert(
        titles[0].LastPlayedAt == new DateTimeOffset(2026, 8, 15, 15, 20, 0, TimeSpan.Zero),
        $"Unexpected title-history last-played timestamp: {titles[0].LastPlayedAt:O}");
}

static void ParsesStringWrappedTitleHistory()
{
    const string json = """
        {
          "body": "{\"titles\":[{\"titleId\":\"987654321\",\"name\":\"String Wrapped Game\",\"achievement\":{\"currentAchievements\":2,\"currentGamerscore\":40}}]}"
        }
        """;

    var titles = OpenXblResponseParser.ParseTitleProgress(json);
    Assert(titles.Count == 1, $"Expected one string-wrapped title summary, found {titles.Count}.");
    Assert(titles[0].TitleId == "987654321", $"Unexpected string-wrapped title ID: {titles[0].TitleId}");
    Assert(titles[0].CurrentAchievements == 2, "String-wrapped achievement count was not parsed.");
    Assert(titles[0].CurrentGamerscore == 40, "String-wrapped Gamerscore was not parsed.");
}

static void ParsesModernRecentTitleProgress()
{
    const string json = """
        {
          "titles": [
            {
              "titleId": 12345,
              "name": "Modern Recent Game",
              "earnedAchievements": 12,
              "currentGamerscore": 240,
              "lastUnlock": "2026-08-15T18:30:00Z",
              "platforms": ["XboxOne", "Scarlett"]
            }
          ]
        }
        """;

    var title = OpenXblResponseParser.ParseTitleProgress(json).Single();
    Assert(title.CurrentAchievements == 12, "Modern earnedAchievements was not parsed.");
    Assert(title.CurrentGamerscore == 240, "Modern currentGamerscore was not parsed.");
    Assert(title.Devices.SequenceEqual(new[] { "XboxOne", "Scarlett" }), "Modern platforms were not parsed.");
    Assert(title.LastPlayedAt == new DateTimeOffset(2026, 8, 15, 18, 30, 0, TimeSpan.Zero), "Modern lastUnlock was not parsed.");
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
        $"Unexpected unlock time: {achievement.UnlockedAt}");
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

static void ParsesStringWrappedAchievements()
{
    const string json = """
        {
          "content": "[{\"id\":\"wrapped-1\",\"name\":\"Wrapped Unlock\",\"progressState\":\"Achieved\",\"timeUnlocked\":\"2026-08-15T18:00:00Z\",\"titleName\":\"Wrapped Game\"}]"
        }
        """;

    var achievements = OpenXblResponseParser.ParseAchievements(json, "account-a", "123456789");
    Assert(achievements.Count == 1, "String-wrapped achievement response was not parsed.");
    Assert(achievements[0].Name == "Wrapped Unlock", "String-wrapped achievement name was not parsed.");
    Assert(achievements[0].GameName == "Wrapped Game", "String-wrapped game name was not parsed.");
}

static void ParsesXbox360Achievements()
{
    const string json = """
        {
          "achievements": [
            {
              "id": 36,
              "titleId": 41560855,
              "name": "Legacy Unlock",
              "unlockedOnline": true,
              "unlocked": true,
              "isSecret": false,
              "gamerscore": 15,
              "description": "Complete the legacy objective.",
              "isRevoked": false,
              "timeUnlocked": "2026-08-15T19:45:00Z"
            },
            {
              "id": 37,
              "titleId": 41560855,
              "name": "Still Locked",
              "unlockedOnline": false,
              "unlocked": false,
              "gamerscore": 20,
              "timeUnlocked": "0001-01-01T00:00:00Z"
            }
          ],
          "pagingInfo": { "continuationToken": null, "totalRecords": 2 }
        }
        """;

    var achievements = OpenXblResponseParser.ParseAchievements(json, "account-a");
    Assert(achievements.Count == 1, $"Expected one unlocked Xbox 360 achievement, found {achievements.Count}.");
    Assert(achievements[0].Name == "Legacy Unlock", "Xbox 360 achievement name was not parsed.");
    Assert(achievements[0].Gamerscore == 15, "Xbox 360 Gamerscore was not parsed.");
    Assert(
        achievements[0].UnlockedAt == new DateTimeOffset(2026, 8, 15, 19, 45, 0, TimeSpan.Zero),
        $"Unexpected Xbox 360 unlock time: {achievements[0].UnlockedAt}");
}

static void ParsesAchievementContinuationToken()
{
    const string json = """
        {
          "data": {
            "pagingInfo": {
              "continuationToken": "next/page+token="
            }
          }
        }
        """;

    Assert(
        OpenXblResponseParser.ParseContinuationToken(json) == "next/page+token=",
        "The nested continuation token was not found.");
    Assert(
        OpenXblResponseParser.ParseContinuationToken("\"{\\\"pagingInfo\\\":{\\\"continuationToken\\\":\\\"wrapped-token\\\"}}\"") == "wrapped-token",
        "The string-wrapped continuation token was not found.");
}

static void ParsesUntimestampedXbox360Achievements()
{
    const string json = """
        {
          "achievements": [
            {
              "id": 40,
              "titleId": 41560855,
              "name": "Offline Legacy Unlock",
              "unlocked": true,
              "isRevoked": false,
              "timeUnlocked": "0001-01-01T00:00:00Z"
            },
            {
              "id": 41,
              "titleId": 41560855,
              "name": "Missing-Time Legacy Unlock",
              "unlocked": true,
              "isRevoked": false
            }
          ]
        }
        """;

    var achievements = OpenXblResponseParser.ParseAchievements(json, "account-a");
    Assert(achievements.Count == 2, $"Expected both untimestamped achievements, found {achievements.Count}.");
    Assert(achievements.All(item => item.UnlockedAt is null), "A sentinel or missing time was treated as a real date.");
    Assert(achievements.All(item => item.UnlockTimeEstimated), "Untimestamped achievements were not marked for an estimated display time.");
}

static void DetectsUntimestampedAchievementByIdentity()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var previous = AchievementWithIdentity("old", observedAt.AddDays(-1));
    var added = AchievementWithIdentity("new", null);

    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { previous.Id },
        previousReportedGamerscore: 10,
        currentReportedCount: 2,
        currentReportedGamerscore: 20,
        currentAchievements: new[] { previous, added },
        previousSuccessfulPollUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5));

    Assert(result.IsComplete, "A complete identity response was rejected.");
    Assert(result.NewAchievements.Select(item => item.Id).SequenceEqual(new[] { "new" }), "The new stable identity was not detected.");
    Assert(result.UnidentifiedIncrease == 0, "A stable identity delta was marked ambiguous.");
}

static void HydratesIdentityBaselineWithoutPosting()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 2,
        previousAchievementIds: null,
        previousReportedGamerscore: 20,
        currentReportedCount: 2,
        currentReportedGamerscore: 20,
        currentAchievements: new[]
        {
            AchievementWithIdentity("historic-one", null),
            AchievementWithIdentity("historic-two", null)
        },
        previousSuccessfulPollUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5));

    Assert(result.IsComplete, "A complete unchanged title could not establish its identity baseline.");
    Assert(result.NewAchievements.Count == 0, "Identity hydration would post historical achievements.");
    Assert(result.CurrentAchievementIds.Count == 2, "Identity hydration did not retain the complete set.");
}

static void SafelyBaselinesProviderIdentityChurn()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 2,
        previousAchievementIds: new[] { "old-route-one", "old-route-two" },
        previousReportedGamerscore: 20,
        currentReportedCount: 2,
        currentReportedGamerscore: 20,
        currentAchievements: new[]
        {
            AchievementWithIdentity("new-route-one", null),
            AchievementWithIdentity("new-route-two", null)
        },
        previousSuccessfulPollUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5));

    Assert(result.IsComplete, "Provider identity churn was left in a permanent retry loop.");
    Assert(result.NewAchievements.Count == 0, "Provider identity churn would flood historical achievements.");
    Assert(result.CurrentAchievementIds.Count == 4, "Both provider identity forms were not retained for deduplication.");
}

static void AttributesUniqueUntimestampedMigrationUnlock()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var previous = AchievementWithIdentity("old", observedAt.AddDays(-10));
    var added = AchievementWithIdentity("new", null);

    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: null,
        previousReportedGamerscore: 10,
        currentReportedCount: 2,
        currentReportedGamerscore: 20,
        currentAchievements: new[] { previous, added },
        previousSuccessfulPollUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5));

    Assert(result.NewAchievements.Select(item => item.Id).SequenceEqual(new[] { "new" }), "The sole untimestamped count increase was not attributed.");
    Assert(result.CurrentAchievementIds.Count == 2, "The complete migration identity baseline was not returned.");
    Assert(result.UnidentifiedIncrease == 0, "A uniquely attributable migration unlock was marked ambiguous.");
}

static void SafelyBaselinesAmbiguousMigrationUnlock()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: null,
        previousReportedGamerscore: 10,
        currentReportedCount: 2,
        currentReportedGamerscore: 20,
        currentAchievements: new[]
        {
            AchievementWithIdentity("historic-untimed", null),
            AchievementWithIdentity("possibly-new", null)
        },
        previousSuccessfulPollUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5));

    Assert(result.IsComplete, "An ambiguity that can be safely baselined was left in a retry loop.");
    Assert(result.NewAchievements.Count == 0, "Ambiguous historical achievements would have flooded Discord.");
    Assert(result.UnidentifiedIncrease == 1, "The ambiguous count increase was not reported.");
    Assert(result.CurrentAchievementIds.Count == 2, "The ambiguity did not produce a durable identity baseline.");
}

static void AttributesUntimestampedMigrationUnlockByGamerscore()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var fivePoint = AchievementWithIdentity("historic-five", null) with { Gamerscore = 5 };
    var tenPoint = AchievementWithIdentity("new-ten", null) with { Gamerscore = 10 };
    var twentyPoint = AchievementWithIdentity("historic-twenty", null) with { Gamerscore = 20 };

    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 2,
        previousAchievementIds: null,
        previousReportedGamerscore: 25,
        currentReportedCount: 3,
        currentReportedGamerscore: 35,
        currentAchievements: new[] { fivePoint, tenPoint, twentyPoint },
        previousSuccessfulPollUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5));

    Assert(result.NewAchievements.Select(item => item.Id).SequenceEqual(new[] { "new-ten" }), "Gamerscore did not isolate the only possible untimestamped unlock.");
    Assert(result.UnidentifiedIncrease == 0, "A unique Gamerscore match was marked ambiguous.");
}

static void RejectsIncompleteAchievementDetail()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "old" },
        previousReportedGamerscore: 10,
        currentReportedCount: 2,
        currentReportedGamerscore: 20,
        currentAchievements: new[] { AchievementWithIdentity("old", observedAt.AddDays(-1)) },
        previousSuccessfulPollUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5));

    Assert(!result.IsComplete, "A detail response below the provider's reported count was accepted.");
}

static void RejectsOvercompleteAchievementDetail()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "old" },
        previousReportedGamerscore: 10,
        currentReportedCount: 1,
        currentReportedGamerscore: 10,
        currentAchievements: new[]
        {
            AchievementWithIdentity("old", observedAt.AddDays(-1)),
            AchievementWithIdentity("detail-ahead", null)
        },
        previousSuccessfulPollUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5));

    Assert(!result.IsComplete, "A detail response ahead of the provider's reported count was accepted.");
}

static void ValidatesWebhookUrls()
{
    const string testWebhookId = "123456789012345678";
    const string testToken = "not-a-real-webhook-token-0123456789";
    var valid = $"https://discord.com/api/webhooks/{testWebhookId}/{testToken}";
    var versioned = $"https://discord.com/api/v10/webhooks/{testWebhookId}/{testToken}";
    var legacyHost = $"https://discordapp.com/api/webhooks/{testWebhookId}/{testToken}";

    Assert(WebhookUrlValidator.TryNormalize(valid, out _, out _), "Standard Discord webhook was rejected.");
    Assert(WebhookUrlValidator.TryNormalize(versioned, out _, out _), "Versioned Discord webhook was rejected.");
    Assert(
        WebhookUrlValidator.TryNormalize(legacyHost, out var canonicalLegacy, out _) &&
        canonicalLegacy?.Host == "discord.com",
        "Legacy Discord webhook hosts were not normalized before redirect-safe delivery.");
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

static void PayloadLabelsEstimatedTimestamp()
{
    var achievement = Achievement("Legacy Time", "Provider omitted its timestamp") with
    {
        UnlockTimeEstimated = true
    };

    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, new AppSettings()));
    var footer = document.RootElement.GetProperty("embeds")[0].GetProperty("footer").GetProperty("text").GetString();
    Assert(footer?.Contains("Xbox supplied no unlock time", StringComparison.Ordinal) == true, "Estimated time was not disclosed in the Discord embed.");
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

static AchievementEvent AchievementWithIdentity(string id, DateTimeOffset? unlockedAt) => new()
{
    Id = id,
    Name = id,
    Gamerscore = 10,
    SourceProvider = "OpenXBL",
    UnlockedAt = unlockedAt,
    UnlockTimeEstimated = unlockedAt is null
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
