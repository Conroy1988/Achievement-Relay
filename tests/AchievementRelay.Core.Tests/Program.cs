using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
    ("Untimestamped identity changes need live-session evidence", SuppressesUntimestampedRestartDelta),
    ("Cross-device unlocks reconcile silently after startup", ReconcilesCrossDeviceUnlockAfterStartup),
    ("Timestamped unlocks inside the live session are posted", PostsTimestampedUnlockInsideDeliveryEpoch),
    ("Live Xbox delivery evidence survives an updater restart", PreservesLiveXboxDeliveryEvidenceAcrossRestart),
    ("Unchanged count-only state hydrates identities without posting", HydratesIdentityBaselineWithoutPosting),
    ("Provider identity churn cannot flood historical achievements", SafelyBaselinesProviderIdentityChurn),
    ("Count-only state never posts an unproven untimestamped unlock", DoesNotPostUntimestampedMigrationUnlock),
    ("Count-only state posts only proven live-session timestamps", PostsTimestampedUnlockAfterDeliveryEpoch),
    ("Count-only state rejects future-skewed timestamps", DoesNotPostFutureSkewedBaselineTimestamp),
    ("Gamerscore never infers a migration unlock", DoesNotInferMigrationUnlockFromGamerscore),
    ("Newly discovered historical titles baseline without posting", SafelyBaselinesNewlyDiscoveredHistoricalTitle),
    ("Historical provider corrections cannot post after baseline", SafelyBaselinesHistoricalProviderCorrection),
    ("Restarting with the same identities never reposts", DoesNotRepostKnownIdentitiesAfterRestart),
    ("Incomplete achievement detail is retried without advancing state", RejectsIncompleteAchievementDetail),
    ("Ahead-of-summary achievement detail is retried without advancing state", RejectsOvercompleteAchievementDetail),
    ("OpenXBL rolling-hour guard preserves a safety reserve", ProtectsOpenXblRollingHourAllowance),
    ("OpenXBL provider headers reserve capacity for live monitoring", ProtectsOpenXblProviderRemainingAllowance),
    ("OpenXBL rate-limit reset pauses and recovers automatically", HonorsOpenXblRateLimitReset),
    ("Xbox delivery epochs reset after startup and interruptions", ResetsXboxDeliveryEpochAfterInterruption),
    ("Xbox sync work prioritizes live changes and throttles history", PrioritizesLiveXboxSyncWork),
    ("Pending Xbox sync work survives durable JSON state", PersistsPendingXboxSyncWork),
    ("Steam first snapshot silently baselines old unlocks", SteamFirstSnapshotIsSilentBaseline),
    ("Steam launch race posts only a callback-proven unlock", SteamLaunchRacePostsProvenUnlock),
    ("Steam locked-to-unlocked transition posts once", SteamTransitionPostsOnce),
    ("Steam restart silently baselines offline unlocks", SteamRestartBaselinesOfflineUnlocks),
    ("Steam timestamps cannot bypass the baseline", SteamTimestampCannotAuthorizeUnlock),
    ("Steam event identities are stable and account-specific", SteamEventIdentityIsStable),
    ("Steam rarity accepts provider string and numeric percentages", ParsesSteamRarityPercentages),
    ("Steam bridge Base64 artwork wire format decodes to bytes", SteamBridgeBase64ArtworkWireFormat),
    ("Steam RGBA artwork is encoded as a PNG attachment", SteamArtworkEncodesAsPng),
    ("Steam Discord payload identifies platform and attachment", SteamPayloadIncludesPlatformAndAttachment),
    ("Xbox Discord payload labels the player platform", XboxPayloadUsesPlatformLabel),
    ("Every Discord post links to Achievement Relay", DiscordPostsIncludeProjectLink),
    ("Webhook URL validation is strict", ValidatesWebhookUrls),
    ("Discord payload suppresses mentions", PayloadSuppressesMentions),
    ("Discord payload repairs malformed provider Unicode", PayloadRepairsMalformedUnicode),
    ("Discord identifies estimated provider timestamps", PayloadLabelsEstimatedTimestamp),
    ("Description sharing setting is respected", DescriptionSettingIsRespected),
    ("Connection test suppresses mentions", ConnectionTestSuppressesMentions),
    ("Update manifests are parsed and normalized strictly", ParsesUpdateManifest),
    ("Update manifest signatures authenticate the pinned publisher", VerifiesSignedUpdateManifest),
    ("Newer releases remain optional above the support floor", SelectsOptionalUpdate),
    ("Final packages supersede same-product beta revisions", SelectsFinalPackageUpdate),
    ("Automatic updates launch safely at startup and on required detection", SelectsAutomaticUpdateBehavior),
    ("Installer version resources match the signed package numerically", MatchesInstallerVersionResources),
    ("Non-upgradeable package versions fail closed", RejectsNonUpgradeablePackage),
    ("Only the explicit support floor requires an update", SelectsRequiredUpdate),
    ("Malformed update manifests fail closed", RejectsMalformedUpdateManifest)
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

static void ParsesUpdateManifest()
{
    var manifest = UpdatePolicy.ParseManifest("""
        {
          "schemaVersion": 1,
          "version": "0.4.0",
          "packageVersion": "0.4.0.0",
          "minimumSupportedVersion": "0.3.0",
          "publishedAtUtc": "2026-08-16T12:00:00Z",
          "installer": {
            "assetName": "AchievementRelay_Setup.exe",
            "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "size": 123456789
          }
        }
        """);

    Assert(manifest.Version == "0.4.0", "The release version was not normalized.");
    Assert(manifest.PackageVersion == "0.4.0.0", "The package version was not normalized.");
    Assert(manifest.MinimumSupportedVersion == "0.3.0", "The support floor changed unexpectedly.");
    Assert(manifest.Installer.Sha256 == new string('a', 64), "The SHA-256 was not normalized.");
}

static void VerifiesSignedUpdateManifest()
{
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest(
        new X500DistinguishedName("CN=Achievement Relay Open Source"),
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    var enhancedKeyUsages = new OidCollection
    {
        new("1.3.6.1.5.5.7.3.3")
    };
    request.CertificateExtensions.Add(
        new X509EnhancedKeyUsageExtension(enhancedKeyUsages, critical: false));
    using var certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddYears(1));

    var manifestBytes = Encoding.UTF8.GetBytes("""
        {"schemaVersion":1,"version":"0.4.0"}
        """);
    var signature = rsa.SignData(
        manifestBytes,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    var certificateBytes = certificate.Export(X509ContentType.Cert);
    var fingerprint = Convert.ToHexString(SHA256.HashData(certificateBytes)).ToLowerInvariant();
    var envelope = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        algorithm = "rsa-sha256-pkcs1",
        certificateSha256 = fingerprint,
        certificate = Convert.ToBase64String(certificateBytes),
        signature = Convert.ToBase64String(signature)
    }));

    var trusted = UpdateManifestSignatureVerifier.Verify(
        manifestBytes,
        envelope,
        new HashSet<string>(StringComparer.Ordinal) { fingerprint });
    Assert(trusted.IsValid, $"The pinned manifest signature was rejected: {trusted.Message}");

    var tampered = Encoding.UTF8.GetBytes("""
        {"schemaVersion":1,"version":"0.5.0"}
        """);
    Assert(
        !UpdateManifestSignatureVerifier.Verify(
            tampered,
            envelope,
            new HashSet<string>(StringComparer.Ordinal) { fingerprint }).IsValid,
        "A modified update manifest retained a valid signature.");
    Assert(
        !UpdateManifestSignatureVerifier.Verify(
            manifestBytes,
            envelope,
            new HashSet<string>(StringComparer.Ordinal) { new string('0', 64) }).IsValid,
        "An update manifest signed by an unpinned certificate was accepted.");
}

static void SelectsOptionalUpdate()
{
    var manifest = CreateUpdateManifest("0.4.0", "0.3.0");
    var decision = UpdatePolicy.Evaluate(
        new Version(0, 3, 0),
        new Version(0, 3, 0, 42),
        manifest);

    Assert(decision.Requirement == UpdateRequirement.Optional, "A normal newer release was incorrectly required.");
}

static void SelectsFinalPackageUpdate()
{
    var manifest = CreateUpdateManifest("0.3.0", "0.3.0", "0.3.0.0");
    var beta = UpdatePolicy.Evaluate(
        new Version(0, 3, 0),
        new Version(0, 2, 2, 68),
        manifest);
    var final = UpdatePolicy.Evaluate(
        new Version(0, 3, 0),
        new Version(0, 3, 0, 0),
        manifest);

    Assert(beta.Requirement == UpdateRequirement.Optional, "The final package did not supersede the beta package lane.");
    Assert(final.Requirement == UpdateRequirement.Current, "The final package tried to update itself again.");
}

static void RejectsNonUpgradeablePackage()
{
    var manifest = CreateUpdateManifest("0.4.0", "0.3.0", "0.2.2.99");
    AssertThrows<InvalidDataException>(
        () => UpdatePolicy.Evaluate(
            new Version(0, 3, 0),
            new Version(0, 3, 0, 0),
            manifest),
        "A newer product release with a lower Windows package version was accepted.");
}

static void SelectsAutomaticUpdateBehavior()
{
    Assert(
        UpdatePolicy.SelectAutomaticAction(UpdateRequirement.Optional, isAppLaunch: true) ==
        AutomaticUpdateAction.LaunchInstaller,
        "An optional update found at app launch was not selected for automatic installer launch.");
    Assert(
        UpdatePolicy.SelectAutomaticAction(UpdateRequirement.Optional, isAppLaunch: false) ==
        AutomaticUpdateAction.Prepare,
        "An optional update found while running was not selected for background preparation.");
    Assert(
        UpdatePolicy.SelectAutomaticAction(UpdateRequirement.Required, isAppLaunch: false) ==
        AutomaticUpdateAction.LaunchInstaller,
        "A newly required update was not selected for automatic installer launch.");
    Assert(
        UpdatePolicy.SelectAutomaticAction(UpdateRequirement.Current, isAppLaunch: true) ==
        AutomaticUpdateAction.None,
        "A current installation incorrectly selected an automatic update action.");
}

static void MatchesInstallerVersionResources()
{
    var manifest = CreateUpdateManifest("0.3.0", "0.3.0", "0.2.2.76");
    Assert(
        UpdatePolicy.MatchesInstallerVersionResource("0.3.0.0", "0.2.2.76", manifest),
        "Windows' padded four-part product version did not match the signed three-part release.");
    Assert(
        UpdatePolicy.MatchesInstallerVersionResource("  0.3.0.0             ", "\t0.2.2.76  \r\n", manifest),
        "Windows version-resource padding was not normalized before strict comparison.");
    Assert(
        !UpdatePolicy.MatchesInstallerVersionResource("0.3.0.1", "0.2.2.76", manifest),
        "A nonzero product-version revision was accepted.");
    Assert(
        !UpdatePolicy.MatchesInstallerVersionResource("0.3.0.0", "0.2.2.75", manifest),
        "A mismatched installer package version was accepted.");
    Assert(
        !UpdatePolicy.MatchesInstallerVersionResource("0.3. 0.0", "0.2.2.76", manifest),
        "Whitespace inside a product version was incorrectly accepted.");
}

static void SelectsRequiredUpdate()
{
    var manifest = CreateUpdateManifest("0.4.1", "0.4.0");
    var required = UpdatePolicy.Evaluate(
        new Version(0, 3, 9),
        new Version(0, 3, 9, 0),
        manifest);
    var supported = UpdatePolicy.Evaluate(
        new Version(0, 4, 0),
        new Version(0, 4, 0, 0),
        manifest);

    Assert(required.Requirement == UpdateRequirement.Required, "A version below the explicit support floor was not blocked.");
    Assert(supported.Requirement == UpdateRequirement.Optional, "A supported older version was incorrectly blocked.");
}

static void RejectsMalformedUpdateManifest()
{
    AssertThrows<InvalidDataException>(
        () => UpdatePolicy.ParseManifest("""
            {
              "schemaVersion": 1,
              "version": "0.4.0-beta",
              "packageVersion": "0.4.0.0",
              "minimumSupportedVersion": "0.5.0",
              "publishedAtUtc": "2026-08-16T12:00:00+01:00",
              "installer": {
                "assetName": "something-else.exe",
                "sha256": "not-a-hash",
                "size": -1
              }
            }
            """),
        "A malformed update manifest was accepted.");

    AssertThrows<InvalidDataException>(
        () => UpdatePolicy.ParseManifest("""
            {
              "schemaVersion": 1,
              "version": "0.4.0",
              "packageVersion": "0.4.0",
              "minimumSupportedVersion": "0.3.0",
              "publishedAtUtc": "2026-08-16T12:00:00Z",
              "installer": {
                "assetName": "AchievementRelay_Setup.exe",
                "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "size": 123456789
              }
            }
            """),
        "A three-part Windows package version was accepted.");
}

static UpdateManifest CreateUpdateManifest(
    string version,
    string minimumSupportedVersion,
    string? packageVersion = null) => new()
{
    SchemaVersion = UpdatePolicy.CurrentManifestSchemaVersion,
    Version = version,
    PackageVersion = packageVersion ?? version + ".0",
    MinimumSupportedVersion = minimumSupportedVersion,
    PublishedAtUtc = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
    Installer = new UpdateInstallerAsset
    {
        AssetName = UpdatePolicy.InstallerAssetName,
        Sha256 = new string('a', 64),
        Size = 123456789
    }
};

static void ProtectsOpenXblRollingHourAllowance()
{
    var budget = new OpenXblRequestBudget();
    var startedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    for (var index = 0; index < OpenXblRequestBudget.LocalHourlySafetyCeiling; index++)
    {
        var decision = budget.TryAcquire(OpenXblRequestPriority.Essential, startedAt.AddSeconds(index));
        Assert(decision.Allowed, $"Request {index + 1} was blocked before the local safety ceiling.");
    }

    var blocked = budget.TryAcquire(
        OpenXblRequestPriority.Essential,
        startedAt.AddMinutes(5));
    Assert(!blocked.Allowed, "The app could consume the full provider allowance in one rolling hour.");
    Assert(blocked.RetryAfter > TimeSpan.Zero, "The protected allowance did not return a retry delay.");

    var recovered = budget.TryAcquire(
        OpenXblRequestPriority.Essential,
        startedAt.AddHours(1).AddMinutes(5));
    Assert(recovered.Allowed, "The local request window did not recover after one hour.");
}

static void ProtectsOpenXblProviderRemainingAllowance()
{
    var budget = new OpenXblRequestBudget();
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    budget.ObserveProviderWindow(
        limit: 150,
        remaining: 60,
        resetUtc: observedAt.AddMinutes(40),
        now: observedAt);

    var background = budget.CanStartOperation(
        OpenXblRequestPriority.Background,
        maximumRequests: 12,
        now: observedAt);
    Assert(!background.Allowed, "Historical hydration could consume the capacity reserved for live monitoring.");
    Assert(background.RetryAfter == TimeSpan.FromMinutes(40), "The provider reset time was not honored.");

    var live = budget.CanStartOperation(
        OpenXblRequestPriority.Essential,
        maximumRequests: 12,
        now: observedAt);
    Assert(live.Allowed, "A live achievement check was blocked while sufficient protected capacity remained.");

    budget.ObserveProviderWindow(
        limit: 150,
        remaining: 21,
        resetUtc: observedAt.AddMinutes(40),
        now: observedAt);
    var protectedLive = budget.CanStartOperation(
        OpenXblRequestPriority.Essential,
        maximumRequests: 12,
        now: observedAt);
    Assert(!protectedLive.Allowed, "A multi-page live check could consume the final provider reserve.");
}

static void PrioritizesLiveXboxSyncWork()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var historical = new XboxTitleSyncWork
    {
        TitleId = "historic",
        Name = "Historical title",
        CurrentAchievements = 50,
        LastPlayedAt = observedAt.AddYears(-10),
        FirstObservedUtc = observedAt,
        IsPriority = false
    };
    var live = new XboxTitleSyncWork
    {
        TitleId = "live",
        Name = "Currently played title",
        CurrentAchievements = 2,
        LastPlayedAt = observedAt,
        FirstObservedUtc = observedAt.AddMinutes(1),
        IsPriority = true
    };

    var selected = XboxSyncWorkPlanner.SelectNext(new[] { historical, live }, allowBackground: true);
    Assert(selected?.TitleId == "live", "Historical hydration was selected ahead of a live achievement change.");
    Assert(
        XboxSyncWorkPlanner.SelectNext(new[] { historical }, allowBackground: false) is null,
        "Historical work ignored the background throttle.");
    Assert(
        !XboxSyncWorkPlanner.IsBackgroundWorkDue(observedAt, observedAt.AddMinutes(14), TimeSpan.FromMinutes(15)),
        "Historical work became eligible before the interval elapsed.");
    Assert(
        XboxSyncWorkPlanner.IsBackgroundWorkDue(observedAt, observedAt.AddMinutes(15), TimeSpan.FromMinutes(15)),
        "Historical work did not become eligible after the interval elapsed.");
}

static void HonorsOpenXblRateLimitReset()
{
    var budget = new OpenXblRequestBudget();
    var limitedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    budget.ObserveRateLimited(limitedAt, TimeSpan.FromMinutes(40));

    var blocked = budget.TryAcquire(OpenXblRequestPriority.Essential, limitedAt.AddMinutes(1));
    Assert(!blocked.Allowed, "A request was allowed inside the provider reset window.");
    Assert(blocked.RetryAfter == TimeSpan.FromMinutes(39), "The remaining reset delay was not preserved.");

    var recovered = budget.TryAcquire(OpenXblRequestPriority.Essential, limitedAt.AddMinutes(40));
    Assert(recovered.Allowed, "The provider request budget did not recover at reset time.");
}

static void ResetsXboxDeliveryEpochAfterInterruption()
{
    var startedAt = new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    var startup = XboxDeliveryWindowPolicy.Resolve(
        currentEpochUtc: null,
        lastSessionSuccessfulPollUtc: null,
        observedAt: startedAt,
        pollIntervalSeconds: 60);
    Assert(startup.EpochUtc == startedAt, "Startup did not create a fresh Xbox delivery epoch.");
    Assert(!startup.HasPriorSuccessfulPoll, "Startup incorrectly inherited a successful poll from another app session.");

    var normalPoll = XboxDeliveryWindowPolicy.Resolve(
        startup.EpochUtc,
        lastSessionSuccessfulPollUtc: startedAt,
        observedAt: startedAt.AddMinutes(1),
        pollIntervalSeconds: 60);
    Assert(normalPoll.EpochUtc == startedAt, "A normal poll unnecessarily reset the Xbox delivery epoch.");
    Assert(normalPoll.HasPriorSuccessfulPoll, "A normal second poll was not accepted as continuous monitoring.");
    Assert(!normalPoll.ReconciledAfterGap, "A normal poll was treated as an interruption.");

    var afterSleep = XboxDeliveryWindowPolicy.Resolve(
        normalPoll.EpochUtc,
        lastSessionSuccessfulPollUtc: startedAt.AddMinutes(1),
        observedAt: startedAt.AddMinutes(12),
        pollIntervalSeconds: 60);
    Assert(afterSleep.EpochUtc == startedAt.AddMinutes(12), "A long monitoring gap did not begin a safe new delivery epoch.");
    Assert(!afterSleep.HasPriorSuccessfulPoll, "The first poll after a long gap was incorrectly treated as continuous.");
    Assert(afterSleep.ReconciledAfterGap, "A long monitoring gap was not marked for silent reconciliation.");

    var afterClockRollback = XboxDeliveryWindowPolicy.Resolve(
        normalPoll.EpochUtc,
        lastSessionSuccessfulPollUtc: startedAt.AddMinutes(2),
        observedAt: startedAt.AddMinutes(1),
        pollIntervalSeconds: 60);
    Assert(afterClockRollback.ReconciledAfterGap, "A backwards clock jump did not fail closed into a new delivery epoch.");
}

static void PersistsPendingXboxSyncWork()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var original = new Dictionary<string, XboxTitleSyncWork>(StringComparer.Ordinal)
    {
        ["legacy-title"] = new XboxTitleSyncWork
        {
            TitleId = "legacy-title",
            Name = "Queued legacy title",
            CurrentAchievements = 75,
            CurrentGamerscore = 1_500,
            LastPlayedAt = observedAt.AddYears(-10),
            FirstObservedUtc = observedAt,
            LastObservedUtc = observedAt.AddMinutes(1),
            IsPriority = false
        }
    };

    var json = JsonSerializer.Serialize(original);
    var restored = JsonSerializer.Deserialize<Dictionary<string, XboxTitleSyncWork>>(json);
    Assert(restored is not null, "Pending work JSON could not be read after restart.");
    Assert(restored!.TryGetValue("legacy-title", out var work), "Pending work was lost during JSON state round-trip.");
    var restoredWork = work ?? throw new InvalidOperationException("Pending work deserialized as null.");
    Assert(restoredWork.CurrentAchievements == 75, "The queued target count changed during persistence.");
    Assert(restoredWork.CurrentGamerscore == 1_500, "The queued Gamerscore changed during persistence.");
    Assert(restoredWork.FirstObservedUtc == observedAt, "The queued observation time changed during persistence.");
    Assert(!restoredWork.IsPriority, "Historical queue priority changed during persistence.");
}

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
        currentReportedCount: 2,
        currentAchievements: new[] { previous, added },
        deliveryEpochUtc: observedAt.AddDays(-30),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: true);

    Assert(result.IsComplete, "A complete identity response was rejected.");
    Assert(result.NewAchievements.Select(item => item.Id).SequenceEqual(new[] { "new" }), "The new stable identity was not detected.");
    Assert(result.UnidentifiedIncrease == 0, "A stable identity delta was marked ambiguous.");
}

static void SuppressesUntimestampedRestartDelta()
{
    var observedAt = new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "known" },
        currentReportedCount: 2,
        currentAchievements: new[]
        {
            AchievementWithIdentity("known", observedAt.AddDays(-2)),
            AchievementWithIdentity("other-device", null)
        },
        deliveryEpochUtc: observedAt.AddMinutes(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.IsComplete, "A complete restart identity response was rejected.");
    Assert(result.NewAchievements.Count == 0, "An untimestamped unlock from an inactive period would repost on another device.");
    Assert(result.UnidentifiedIncrease == 1, "The suppressed restart identity was not reported as silently reconciled.");
    Assert(result.CurrentAchievementIds.Contains("other-device"), "The suppressed identity was not retained in the local baseline.");
}

static void ReconcilesCrossDeviceUnlockAfterStartup()
{
    var observedAt = new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    var sessionStartedAt = observedAt.AddMinutes(-1);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "known" },
        currentReportedCount: 2,
        currentAchievements: new[]
        {
            AchievementWithIdentity("known", observedAt.AddDays(-2)),
            AchievementWithIdentity("posted-on-other-device", observedAt.AddHours(-8))
        },
        deliveryEpochUtc: sessionStartedAt,
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.IsComplete, "A complete cross-device reconciliation was rejected.");
    Assert(result.NewAchievements.Count == 0, "An achievement unlocked before this device started would be posted again.");
    Assert(result.UnidentifiedIncrease == 1, "The cross-device achievement was not counted as a silent reconciliation.");
    Assert(result.CurrentAchievementIds.Contains("posted-on-other-device"), "The reconciled identity was not retained for future deduplication.");
}

static void PostsTimestampedUnlockInsideDeliveryEpoch()
{
    var observedAt = new DateTimeOffset(2026, 8, 17, 8, 10, 0, TimeSpan.Zero);
    var sessionStartedAt = observedAt.AddMinutes(-10);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "known" },
        currentReportedCount: 2,
        currentAchievements: new[]
        {
            AchievementWithIdentity("known", observedAt.AddDays(-2)),
            AchievementWithIdentity("live", observedAt.AddSeconds(-30))
        },
        deliveryEpochUtc: sessionStartedAt,
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.NewAchievements.Select(item => item.Id).SequenceEqual(new[] { "live" }), "A timestamped unlock from this live session was not posted.");
    Assert(result.UnidentifiedIncrease == 0, "A proven live unlock was marked as ambiguous.");
}

static void PreservesLiveXboxDeliveryEvidenceAcrossRestart()
{
    var liveEpoch = new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    var observedAt = liveEpoch.AddMinutes(2);
    var work = new XboxTitleSyncWork
    {
        TitleId = "xbox-360-live",
        CurrentAchievements = 2,
        FirstObservedUtc = observedAt,
        LastObservedUtc = observedAt,
        LiveDeliveryEpochUtc = liveEpoch,
        AllowsUntimestampedDelivery = true,
        IsPriority = true
    };

    var restored = JsonSerializer.Deserialize<XboxTitleSyncWork>(JsonSerializer.Serialize(work)) ??
                   throw new InvalidOperationException("Live Xbox work could not be restored after restart.");
    var restoredLiveEpoch = restored.LiveDeliveryEpochUtc ??
                            throw new InvalidOperationException("The proven live delivery epoch was lost during persistence.");
    Assert(restoredLiveEpoch == liveEpoch, "The proven live delivery epoch changed during persistence.");
    Assert(restored.AllowsUntimestampedDelivery, "Untimestamped live-delivery evidence was lost during persistence.");

    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "known" },
        currentReportedCount: 2,
        currentAchievements: new[]
        {
            AchievementWithIdentity("known", liveEpoch.AddDays(-1)),
            AchievementWithIdentity("retry-after-update", null)
        },
        deliveryEpochUtc: restoredLiveEpoch,
        observedAt: observedAt.AddMinutes(1),
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: restored.AllowsUntimestampedDelivery);

    Assert(
        result.NewAchievements.Select(item => item.Id).SequenceEqual(new[] { "retry-after-update" }),
        "A proven live Xbox delivery would be lost when the updater restarted the app.");
}

static void HydratesIdentityBaselineWithoutPosting()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 2,
        previousAchievementIds: null,
        currentReportedCount: 2,
        currentAchievements: new[]
        {
            AchievementWithIdentity("historic-one", null),
            AchievementWithIdentity("historic-two", null)
        },
        deliveryEpochUtc: observedAt.AddHours(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

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
        currentReportedCount: 2,
        currentAchievements: new[]
        {
            AchievementWithIdentity("new-route-one", null),
            AchievementWithIdentity("new-route-two", null)
        },
        deliveryEpochUtc: observedAt.AddHours(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.IsComplete, "Provider identity churn was left in a permanent retry loop.");
    Assert(result.NewAchievements.Count == 0, "Provider identity churn would flood historical achievements.");
    Assert(result.CurrentAchievementIds.Count == 4, "Both provider identity forms were not retained for deduplication.");
}

static void DoesNotPostUntimestampedMigrationUnlock()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var previous = AchievementWithIdentity("old", observedAt.AddDays(-10));
    var added = AchievementWithIdentity("new", null);

    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: null,
        currentReportedCount: 2,
        currentAchievements: new[] { previous, added },
        deliveryEpochUtc: observedAt.AddHours(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.NewAchievements.Count == 0, "An untimestamped count-only migration would post without a verified identity baseline.");
    Assert(result.CurrentAchievementIds.Count == 2, "The complete migration identity baseline was not returned.");
    Assert(result.UnidentifiedIncrease == 1, "The silently baselined migration increase was not reported.");
}

static void PostsTimestampedUnlockAfterDeliveryEpoch()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var deliveryEpoch = observedAt.AddHours(-1);
    var historical = AchievementWithIdentity(
        "historic",
        new DateTimeOffset(2009, 8, 24, 12, 47, 0, TimeSpan.Zero));
    var newUnlock = AchievementWithIdentity("new", observedAt.AddMinutes(-1));

    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: null,
        currentReportedCount: 2,
        currentAchievements: new[] { historical, newUnlock },
        deliveryEpochUtc: deliveryEpoch,
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.IsComplete, "A complete post-baseline migration response was rejected.");
    Assert(result.NewAchievements.Select(item => item.Id).SequenceEqual(new[] { "new" }), "A proven post-baseline timestamp was not delivered.");
    Assert(result.CurrentAchievementIds.Count == 2, "The full identity baseline was not persisted after migration.");
}

static void DoesNotPostFutureSkewedBaselineTimestamp()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 0,
        previousAchievementIds: null,
        currentReportedCount: 1,
        currentAchievements: new[]
        {
            AchievementWithIdentity("future-skew", observedAt.AddHours(2))
        },
        deliveryEpochUtc: observedAt.AddHours(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.IsComplete, "A complete future-skewed response was rejected instead of safely baselined.");
    Assert(result.NewAchievements.Count == 0, "A future-skewed timestamp would be accepted as a new unlock.");
    Assert(result.UnidentifiedIncrease == 1, "The future-skewed entry was not classified for silent baseline.");
    Assert(result.CurrentAchievementIds.SequenceEqual(new[] { "future-skew" }), "The future-skewed identity was not retained for deduplication.");
}

static void DoesNotInferMigrationUnlockFromGamerscore()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var fivePoint = AchievementWithIdentity("historic-five", null) with { Gamerscore = 5 };
    var tenPoint = AchievementWithIdentity("new-ten", null) with { Gamerscore = 10 };
    var twentyPoint = AchievementWithIdentity("historic-twenty", null) with { Gamerscore = 20 };

    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 2,
        previousAchievementIds: null,
        currentReportedCount: 3,
        currentAchievements: new[] { fivePoint, tenPoint, twentyPoint },
        deliveryEpochUtc: observedAt.AddHours(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.NewAchievements.Count == 0, "Gamerscore inference would post an unproven historical achievement.");
    Assert(result.UnidentifiedIncrease == 1, "The unproven migration increase was not silently baselined.");
}

static void SafelyBaselinesNewlyDiscoveredHistoricalTitle()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 0,
        previousAchievementIds: null,
        currentReportedCount: 4,
        currentAchievements: new[]
        {
            AchievementWithIdentity("gears-2009-a", new DateTimeOffset(2009, 8, 24, 2, 32, 0, TimeSpan.Zero)),
            AchievementWithIdentity("gears-2009-b", new DateTimeOffset(2009, 8, 24, 12, 47, 0, TimeSpan.Zero)),
            AchievementWithIdentity("gta-2013-a", new DateTimeOffset(2013, 9, 17, 17, 4, 0, TimeSpan.Zero)),
            AchievementWithIdentity("gta-2013-b", new DateTimeOffset(2013, 9, 18, 19, 53, 0, TimeSpan.Zero))
        },
        deliveryEpochUtc: observedAt.AddHours(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.IsComplete, "A complete newly discovered historical title was rejected.");
    Assert(result.NewAchievements.Count == 0, "A newly discovered old title would flood its achievement backlog.");
    Assert(result.UnidentifiedIncrease == 4, "The complete historical backlog was not classified for silent baseline.");
    Assert(result.CurrentAchievementIds.Count == 4, "The historical identities were not retained as the new baseline.");
}

static void SafelyBaselinesHistoricalProviderCorrection()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "known" },
        currentReportedCount: 2,
        currentAchievements: new[]
        {
            AchievementWithIdentity("known", observedAt.AddDays(-1)),
            AchievementWithIdentity("newly-revealed-old", new DateTimeOffset(2013, 9, 18, 20, 49, 0, TimeSpan.Zero))
        },
        deliveryEpochUtc: observedAt.AddHours(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.IsComplete, "A complete provider correction was rejected.");
    Assert(result.NewAchievements.Count == 0, "A historical provider correction would post as a new unlock.");
    Assert(result.UnidentifiedIncrease == 1, "The historical correction was not reported as silently baselined.");
    Assert(result.CurrentAchievementIds.Contains("newly-revealed-old"), "The corrected identity was not retained for future deduplication.");
}

static void DoesNotRepostKnownIdentitiesAfterRestart()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var current = new[]
    {
        AchievementWithIdentity("known-a", observedAt.AddMinutes(-20)),
        AchievementWithIdentity("known-b", null)
    };
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 2,
        previousAchievementIds: current.Select(item => item.Id).ToArray(),
        currentReportedCount: 2,
        currentAchievements: current,
        deliveryEpochUtc: observedAt.AddHours(-1),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(result.IsComplete, "A complete restart snapshot was rejected.");
    Assert(result.NewAchievements.Count == 0, "Known identities would repost after restart.");
    Assert(result.UnidentifiedIncrease == 0, "An unchanged restart snapshot was marked as changed.");
}

static void RejectsIncompleteAchievementDetail()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "old" },
        currentReportedCount: 2,
        currentAchievements: new[] { AchievementWithIdentity("old", observedAt.AddDays(-1)) },
        deliveryEpochUtc: observedAt.AddDays(-30),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

    Assert(!result.IsComplete, "A detail response below the provider's reported count was accepted.");
}

static void RejectsOvercompleteAchievementDetail()
{
    var observedAt = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
    var result = AchievementDeltaDetector.Detect(
        previousReportedCount: 1,
        previousAchievementIds: new[] { "old" },
        currentReportedCount: 1,
        currentAchievements: new[]
        {
            AchievementWithIdentity("old", observedAt.AddDays(-1)),
            AchievementWithIdentity("detail-ahead", null)
        },
        deliveryEpochUtc: observedAt.AddDays(-30),
        observedAt: observedAt,
        futureClockTolerance: TimeSpan.FromMinutes(5),
        allowUntimestampedIdentityDelta: false);

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

static void SteamFirstSnapshotIsSilentBaseline()
{
    var detectedAt = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
    var snapshot = new[]
    {
        SteamAchievement("old", true, detectedAt.AddYears(-2)),
        SteamAchievement("locked", false, null)
    };

    var delta = SteamAchievementDeltaDetector.Detect(
        null,
        snapshot,
        Array.Empty<string>(),
        detectedAt.AddSeconds(2));

    Assert(delta.BaselineEstablished, "The first complete Steam snapshot was not marked as a baseline.");
    Assert(delta.NewAchievements.Count == 0, "A historical Steam unlock escaped the initial baseline.");
    Assert(delta.CurrentUnlockedApiNames.SetEquals(new[] { "old" }), "The baseline did not retain the old unlock identity.");
}

static void SteamLaunchRacePostsProvenUnlock()
{
    var detectedAt = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
    var delta = SteamAchievementDeltaDetector.Detect(
        null,
        new[]
        {
            SteamAchievement("old", true, detectedAt.AddYears(-1)),
            SteamAchievement("just-unlocked", true, detectedAt.AddSeconds(1))
        },
        new[] { "just-unlocked" },
        detectedAt.AddSeconds(2));

    Assert(delta.BaselineEstablished, "The launch-race snapshot was not treated as the first baseline.");
    Assert(delta.NewAchievements.Count == 1 && delta.NewAchievements[0].ApiName == "just-unlocked",
        "The callback-proven Steam unlock was not isolated from history.");
}

static void SteamTransitionPostsOnce()
{
    var observedAt = new DateTimeOffset(2026, 8, 16, 10, 5, 0, TimeSpan.Zero);
    var first = SteamAchievementDeltaDetector.Detect(
        new[] { "known" },
        new[]
        {
            SteamAchievement("known", true, observedAt.AddDays(-1)),
            SteamAchievement("new", true, observedAt)
        },
        new[] { "new" },
        observedAt);
    Assert(first.NewAchievements.Count == 1 && first.NewAchievements[0].ApiName == "new",
        "The Steam locked-to-unlocked identity change was not detected.");

    var afterRestart = SteamAchievementDeltaDetector.Detect(
        new[] { "known", "new" },
        new[]
        {
            SteamAchievement("known", true, observedAt.AddDays(-1)),
            SteamAchievement("new", true, observedAt)
        },
        Array.Empty<string>(),
        observedAt.AddMinutes(1));
    Assert(afterRestart.NewAchievements.Count == 0, "A known Steam identity would repost after restart.");

    var schemaExpansion = SteamAchievementDeltaDetector.Detect(
        new[] { "known" },
        new[]
        {
            SteamAchievement("known", true, observedAt.AddDays(-1)),
            SteamAchievement("newly-visible-history", true, observedAt.AddYears(-1))
        },
        Array.Empty<string>(),
        observedAt);
    Assert(schemaExpansion.NewAchievements.Count == 0,
        "A newly visible schema identity without a live transition was mistaken for an unlock.");
}

static void SteamRestartBaselinesOfflineUnlocks()
{
    var detectedAt = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
    var delta = SteamAchievementDeltaDetector.Detect(
        new[] { "known" },
        new[]
        {
            SteamAchievement("known", true, detectedAt.AddDays(-2)),
            SteamAchievement("offline", true, detectedAt.AddHours(-1))
        },
        Array.Empty<string>(),
        detectedAt.AddSeconds(2));

    Assert(delta.NewAchievements.Count == 0, "An offline Steam unlock was mistaken for a live transition.");
    Assert(delta.CurrentUnlockedApiNames.Contains("offline"), "The offline unlock was not silently added to the durable baseline.");
}

static void SteamTimestampCannotAuthorizeUnlock()
{
    var detectedAt = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
    var delta = SteamAchievementDeltaDetector.Detect(
        null,
        new[] { SteamAchievement("recent-but-unproven", true, detectedAt.AddSeconds(1)) },
        Array.Empty<string>(),
        detectedAt.AddSeconds(2));
    Assert(delta.NewAchievements.Count == 0, "A Steam timestamp authorized an unlock without a direct live transition.");
}

static void SteamEventIdentityIsStable()
{
    var first = SteamAchievementDeltaDetector.CreateEventId("76561198000000001", 123, "ACH_WIN");
    var same = SteamAchievementDeltaDetector.CreateEventId("76561198000000001", 123, "ACH_WIN");
    var otherAccount = SteamAchievementDeltaDetector.CreateEventId("76561198000000002", 123, "ACH_WIN");
    var otherGame = SteamAchievementDeltaDetector.CreateEventId("76561198000000001", 124, "ACH_WIN");
    Assert(first == same, "Steam event identity is not deterministic.");
    Assert(first != otherAccount && first != otherGame, "Steam event identity is not scoped to the account and game.");
}

static void ParsesSteamRarityPercentages()
{
    const string json = """
        {
          "achievementpercentages": {
            "achievements": [
              { "name": "NEW_ACHIEVEMENT_1_10", "percent": "91.0" },
              { "name": "NUMERIC_PERCENT", "percent": 3.5 },
              { "name": "INVALID_PERCENT", "percent": "unknown" },
              { "name": "OUT_OF_RANGE", "percent": 101 }
            ]
          }
        }
        """;

    var rarity = SteamRarityResponseParser.Parse(json);
    Assert(rarity.TryGetValue("NEW_ACHIEVEMENT_1_10", out var stringPercentage) && stringPercentage == 91.0,
        "Steam's string percentage response was not parsed.");
    Assert(rarity.TryGetValue("NUMERIC_PERCENT", out var numericPercentage) && numericPercentage == 3.5,
        "Steam's numeric percentage response was not parsed.");
    Assert(!rarity.ContainsKey("INVALID_PERCENT") && !rarity.ContainsKey("OUT_OF_RANGE"),
        "An invalid Steam percentage was accepted.");
}

static void SteamBridgeBase64ArtworkWireFormat()
{
    var decoded = JsonSerializer.Deserialize<byte[]>("\"AQIDBA==\"");
    Assert(decoded is not null && decoded.SequenceEqual(new byte[] { 1, 2, 3, 4 }),
        "The main app could not decode the helper's Base64 icon representation.");
}

static void SteamArtworkEncodesAsPng()
{
    var png = RgbaPngEncoder.Encode(1, 1, new byte[] { 12, 34, 56, 255 });
    Assert(png.Length > 40, "Steam artwork PNG was unexpectedly short.");
    Assert(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "Steam artwork did not contain the PNG signature.");
    Assert(png.AsSpan(12, 4).SequenceEqual("IHDR"u8), "Steam artwork omitted the PNG IHDR chunk.");
    Assert(png.AsSpan(png.Length - 8, 4).SequenceEqual("IEND"u8), "Steam artwork omitted the PNG IEND chunk.");
}

static void SteamPayloadIncludesPlatformAndAttachment()
{
    var achievement = Achievement("Steam Winner", "Unlocked locally") with
    {
        SourceProvider = "Steam",
        PlayerName = "Local Steam Player",
        RarityKnown = true,
        RarityPercentage = 3.5,
        IsRare = true,
        ImageBytes = new byte[] { 1 },
        ImageFileName = "steam-achievement.png",
        ImageContentType = "image/png"
    };
    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, new AppSettings()));
    var embed = document.RootElement.GetProperty("embeds")[0];
    Assert(embed.GetProperty("thumbnail").GetProperty("url").GetString() == "attachment://steam-achievement.png",
        "Steam artwork was not referenced as a Discord attachment.");
    var fields = embed.GetProperty("fields").EnumerateArray().ToArray();
    Assert(fields.Any(field => field.GetProperty("name").GetString() == "Platform" &&
                               field.GetProperty("value").GetString() == "Steam"),
        "Steam was not identified as the Discord achievement platform.");
    Assert(fields.Any(field => field.GetProperty("name").GetString() == "Player" &&
                               field.GetProperty("value").GetString() == "Local Steam Player"),
        "The local Steam player name was not used as a Discord fallback.");
    Assert(fields.Any(field => field.GetProperty("name").GetString() == "Rarity" &&
                               field.GetProperty("value").GetString()?.Contains("3.5% of Steam players", StringComparison.Ordinal) == true),
        "The Steam global unlock percentage was not included in the Discord payload.");
}

static void XboxPayloadUsesPlatformLabel()
{
    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(
        Achievement("Xbox Winner", "Unlocked through OpenXBL"),
        new AppSettings()));
    var fields = document.RootElement.GetProperty("embeds")[0].GetProperty("fields").EnumerateArray().ToArray();
    Assert(fields.Any(field => field.GetProperty("name").GetString() == "Platform" &&
                               field.GetProperty("value").GetString() == "Xbox"),
        "The Xbox player platform was exposed as the provider implementation name.");
}

static void DiscordPostsIncludeProjectLink()
{
    var payloads = new[]
    {
        DiscordWebhookPayloadFactory.Create(Achievement("Relay Link", "Achievement post"), new AppSettings()),
        DiscordWebhookPayloadFactory.CreateConnectionTest(new AppSettings())
    };

    foreach (var payload in payloads)
    {
        using var document = JsonDocument.Parse(payload);
        var fields = document.RootElement.GetProperty("embeds")[0].GetProperty("fields").EnumerateArray();
        Assert(fields.Any(field =>
                field.GetProperty("value").GetString() ==
                "[Get the relay](https://github.com/Conroy1988/Achievement-Relay)"),
            "A Discord post omitted the public Achievement Relay project link.");
    }
}

static void PayloadSuppressesMentions()
{
    var achievement = Achievement("@everyone Secret Finder", "Unlocked without pinging anyone.");

    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, new AppSettings()));
    var parse = document.RootElement.GetProperty("allowed_mentions").GetProperty("parse");
    Assert(parse.GetArrayLength() == 0, "Payload allowed Discord mention parsing.");
}

static void PayloadRepairsMalformedUnicode()
{
    var achievement = Achievement("Steam \uD800 Winner", "Provider metadata contains malformed UTF-16.");

    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, new AppSettings()));
    var title = document.RootElement.GetProperty("embeds")[0].GetProperty("title").GetString();
    Assert(title?.Contains('\uFFFD') == true, "Malformed provider Unicode was not replaced safely.");
}

static void PayloadLabelsEstimatedTimestamp()
{
    var achievement = Achievement("Legacy Time", "Provider omitted its timestamp") with
    {
        UnlockTimeEstimated = true
    };

    using var document = JsonDocument.Parse(DiscordWebhookPayloadFactory.Create(achievement, new AppSettings()));
    var footer = document.RootElement.GetProperty("embeds")[0].GetProperty("footer").GetProperty("text").GetString();
    Assert(footer?.Contains("platform supplied no unlock time", StringComparison.Ordinal) == true, "Estimated time was not disclosed in the Discord embed.");
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

static SteamAchievementObservation SteamAchievement(
    string apiName,
    bool unlocked,
    DateTimeOffset? unlockedAt) => new()
{
    ApiName = apiName,
    Name = apiName,
    IsUnlocked = unlocked,
    UnlockedAt = unlockedAt
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
