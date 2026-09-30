using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using AchievementRelay.App;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;

var tests = new (string Name, Action Run)[]
{
    ("Collection filters preserve platform identity and unknown progress", LibraryPresentationContract),
    ("Redline dashboard summaries are local-date bounded and read-only", RedlineDashboardSummaryContract),
    ("Recent game survives exit, restart and stale Xbox polling", RecentGameActivityContract),
    ("Account imports preserve delivery isolation and concurrent local edits", AccountStoreTests.Run),
    ("Imported history is opt-in, bounded and isolated from delivery state", LibraryHistoryContract),
    ("Sound Studio packs are deterministic, distinct and respect zero", SoundStudioContract),
    ("Quiet hold is bounded and historical entries cannot auto-present", QuietModeContract),
    ("Companion history persists status without duplicating unlocks", CompanionHistoryContract),
    ("Rare celebration chimes are distinct and remain silent at zero", RareChimeContract),
    ("Shared delivery claims exclude concurrent senders and retain uncertain outcomes", SharedClaimContract),
    ("Unlock chime is bounded original PCM with clamped volume", UnlockChimeContract),
    ("Unlock effects start and cleanly stop", UnlockEffectsContract),
    ("Collector Card PNG contract", CollectorCardPngContract),
    ("Collector Card branded fallback", CollectorCardBrandedFallback),
    ("Collector Card artwork composition", CollectorCardArtworkComposition),
    ("Collector Card icon-only artwork becomes a showcase", CollectorCardIconOnlyArtworkShowcase),
    ("Collector Card tiny icons never become a backdrop", CollectorCardTinyIconIsNotPromoted),
    ("Collector Card typography remains readable at Discord size", CollectorCardReadableTypographyContract),
    ("Collector Card unranked state", CollectorCardUnrankedState),
    ("Collector Card long text safety", CollectorCardLongTextSafety),
    ("Collector Card tier emblems are distinct", CollectorCardTierEmblemsAreDistinct),
    ("Signal Strip presentation preserves rarity and platform facts", SignalStripPresentationPreservesFacts),
    ("Signal Strip presentation bounds hostile provider text", SignalStripPresentationBoundsProviderText),
    ("Signal Strip window is passive", SignalStripWindowIsPassive),
    ("Signal Strip real preview is 520 by 76 with distinct tiers", SignalStripPreviewContract)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} app presentation smoke test(s) failed.");
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine($"All {tests.Length} app presentation smoke tests passed.");
}

static void LibraryPresentationContract()
{
    var now = DateTimeOffset.UtcNow;
    LibraryGame Game(string key, string provider, int earned, int? total, int age = 0) =>
        new(key, "Same game", provider, earned, total, null, now.AddMinutes(-age), []);
    var games = new[] { Game("steam", "Steam", 2, 10), Game("xbox", "Xbox", 10, 10, 10),
        Game("unknown", "Steam", 5, null), Game("invalid", "Xbox", 20, 10), Game("zero", "Xbox", 0, 0) };
    Assert(LibraryPresentation.Filter(games, " same ", "Steam", 0, 0).Select(x => x.Key).Order().SequenceEqual(new[] { "steam", "unknown" }), "Platform identity or trimmed search was lost.");
    Assert(LibraryPresentation.Filter(games, "", null, 2, 0).Single().Key == "xbox", "Invalid or unknown totals became completions.");
    Assert(LibraryPresentation.Filter(games, "", null, 1, 0).Single().Key == "steam", "In-progress filter included unverified totals.");
    Assert(LibraryPresentation.Filter(games, "", null, 3, 0).Length == 3, "Unknown/invalid progress was omitted.");
    Assert(LibraryPresentation.Filter(games, "", null, 0, 2)[0].Key == "steam", "Closest sort included completed or unknown totals first.");
    Assert(LibraryPresentation.Percentage(games[0]) == 20 && LibraryPresentation.Percentage(games[3]) == 0, "Verified progress projection is incorrect.");
    Assert(LibraryPresentation.Filter(games, "absent", null, 0, 0).Length == 0 && games[1].ObservedAt == now.AddMinutes(-10), "Empty filtering or immutable snapshots changed.");
}

static void SharedClaimContract()
{
    var directory = Directory.CreateTempSubdirectory("relay-claim-test-");
    var file = Path.Combine(directory.FullName, "claim");
    try
    {
        var constructor = typeof(SharedDeliveryClaim).GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, [typeof(FileStream)], null)!;
        SharedDeliveryClaim Open() => (SharedDeliveryClaim)constructor.Invoke([new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)]);
        using (var claim = Open())
        {
            claim.SetState("sending");
            var excluded = false;
            try { using var concurrent = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { excluded = true; }
            Assert(excluded, "A second sender could acquire the same claim.");
        }
        using (var resumed = Open())
        {
            Assert(resumed.State == "sending", "An interrupted send lost its uncertain state.");
            resumed.SetState("delivered");
        }
        using (var completed = Open()) Assert(completed.State == "delivered", "Completed receipt did not survive reopening.");
        var rejected = false;
        try { using var unsupported = SharedDeliveryClaim.Acquire(directory.FullName, "id", new Uri("https://discord.com/")); }
        catch (IOException) { rejected = true; }
        Assert(rejected, "Local or cloud-synced folders must not be accepted as shared authority.");
    }
    finally { directory.Delete(true); }
}

static void CompanionHistoryContract()
{
    var directory = Directory.CreateTempSubdirectory("relay-journal-test-");
    try
    {
        var paths = new AppPaths(directory.FullName);
        var log = new ActivityLog(paths);
        var journal = new CompanionJournal(paths, log);
        var achievement = CreateAchievement(1);
        journal.RecordAsync(achievement, "Pending").GetAwaiter().GetResult();
        var first = journal.Snapshot.Single();
        journal.RecordAsync(achievement, "Delivered").GetAwaiter().GetResult();
        var loaded = new CompanionJournal(paths, log).Snapshot.Single();
        Assert(loaded.Delivery == "Delivered" && loaded.SessionId == first.SessionId && loaded.ObservedAt == first.ObservedAt, "Retry duplicated an unlock or changed its session.");
        Assert(loaded.Transitions.Select(x => x.Status).SequenceEqual(new[] { "Pending", "Delivered" }), "Delivery timeline lost transition order.");
        Assert(!File.Exists(paths.EventLedgerFile), "Presentation history must not mark events processed.");
        File.WriteAllText(Path.Combine(paths.DataDirectory, "companion-journal.json"), "invalid");
        var broken = new CompanionJournal(paths, log);
        broken.RecordAsync(achievement, "Pending").GetAwaiter().GetResult();
        Assert(broken.StorageError is not null && File.ReadAllText(Path.Combine(paths.DataDirectory, "companion-journal.json")) == "invalid", "Invalid history was overwritten.");
    }
    finally { directory.Delete(true); }
}

static void RedlineDashboardSummaryContract()
{
    var now = new DateTimeOffset(2026, 9, 30, 0, 30, 0, TimeSpan.Zero);
    var zone = TimeZoneInfo.CreateCustomTimeZone("Test UTC+1", TimeSpan.FromHours(1), "Test", "Test");
    JournalEntry Entry(string id, string delivery, DateTimeOffset observed, double? rarity = null) =>
        new(new AchievementEvent { Id = id, Name = id, SourceProvider = "Steam", RarityKnown = rarity.HasValue,
            RarityPercentage = rarity }, observed, "session", delivery, observed);
    var entries = new[] {
        Entry("delivered", "Delivered", now.AddHours(-1), 5),
        Entry("pending", "Pending", now, 20),
        Entry("duplicate", "Pending", now),
        Entry("duplicate", "Delivered", now) with { UpdatedAt = now.AddSeconds(1) },
        Entry("older", "Retry pending", now.AddDays(-1), 1),
        Entry("uncertain", "Delivery uncertain — check Discord", now, double.NaN),
        Entry("filtered", "Filtered", now, 200),
        Entry("remote", "Delivered on another PC", now),
        Entry("imported", "Pending", now) with { Achievement = new AchievementEvent { Id = "imported", Name = "History", SourceProvider = "Xbox", IsHistorical = true } },
        Entry("completion", "Pending", now) with { Achievement = new AchievementEvent { Id = "completion", Name = "Completion", SourceProvider = "Xbox", IsGameCompletion = true } },
        Entry("future", "Pending", now.AddDays(1), 0.1)
    };
    var result = RedlineDashboardSummary.Create(entries, now, zone);
    Assert(result.TodayUnlocks == 6, "Local midnight, duplicate or synthetic-event handling is incorrect.");
    Assert(result.RarestPercentage == 5, "Invalid, old or future rarity was included.");
    Assert(result.NeedsAttention == 3, "Uncertain/retry states were omitted or successful/filtered states were counted.");
    Assert(entries[2].Delivery == "Pending", "Dashboard projection mutated delivery state.");
    var empty = RedlineDashboardSummary.Create([], now, zone);
    Assert(empty.TodayUnlocks == 0 && empty.RarestPercentage is null && empty.NeedsAttention == 0, "Empty dashboard invented statistics.");
}

static void RecentGameActivityContract()
{
    var directory = Directory.CreateTempSubdirectory("relay-recent-game-");
    try
    {
        var paths = new AppPaths(directory.FullName);
        var store = new RecentGameActivityStore(paths);
        var now = DateTimeOffset.UtcNow;
        store.RecordAsync("old", "Aniimo", "Xbox", now.AddDays(-7)).GetAwaiter().GetResult();
        store.RecordAsync("123", "Steam game", "Steam", now.AddMinutes(-2)).GetAwaiter().GetResult();
        // Polling an old title now must not turn its observation time into play time.
        store.RecordAsync("old", "Aniimo", "Xbox", now.AddDays(-7)).GetAwaiter().GetResult();
        store.RecordAsync("unknown", "Unknown", "Xbox", null).GetAwaiter().GetResult();
        Assert(store.Latest?.Name == "Steam game", "Stale Xbox polling replaced the latest Steam game.");
        store = new RecentGameActivityStore(paths);
        Assert(store.Latest?.GameId == "123", "Last game was lost after restart without an achievement snapshot.");
        store.RecordAsync("next", "Next game", "Steam", now.AddMinutes(-1)).GetAwaiter().GetResult();
        Assert(store.Latest?.Name == "Next game", "Switching Steam games did not update the last game.");
        store.RecordAsync("new", "New Xbox game", "Xbox", now).GetAwaiter().GetResult();
        Assert(store.Latest?.Provider == "Xbox", "Newer Xbox play evidence did not replace Steam.");
        store.RecordAsync("future", "Bad clock", "Xbox", now.AddDays(1)).GetAwaiter().GetResult();
        Assert(store.Latest?.GameId == "new", "Future provider timestamp replaced valid play evidence.");
        Assert(!File.Exists(paths.EventLedgerFile), "Recent game tracking changed delivery state.");
    }
    finally { directory.Delete(true); }
}

static void LibraryHistoryContract()
{
    var directory = Directory.CreateTempSubdirectory("relay-library-test-");
    try
    {
        var paths = new AppPaths(directory.FullName); var library = new CompanionLibrary(paths);
        var achievements = Enumerable.Range(0, 350).Select(i => CreateAchievement(1) with { Id = "history:" + i, PlayerName = "private-name", IsGameCompletion = true }).ToArray();
        library.ObserveAsync("game", "Test game", "Steam", 350, 400, null, achievements, false).GetAwaiter().GetResult();
        Assert(library.Snapshot.Single().History.Length == 0, "History was imported without opt-in.");
        library.ObserveAsync("game", "Test game", "Steam", 350, 400, null, achievements, true).GetAwaiter().GetResult();
        var reloaded = new CompanionLibrary(paths).Snapshot.Single();
        Assert(reloaded.History.Length == 300 && reloaded.Earned == 350 && reloaded.Total == 400, "Import bounds or provider totals were lost.");
        Assert(reloaded.History.All(x => x.IsHistorical && !x.IsGameCompletion && x.PlayerName is null && x.ImageBytes is null), "Historical entry retained unsafe live presentation metadata.");
        Assert(!File.Exists(paths.EventLedgerFile) && !File.Exists(Path.Combine(paths.DataDirectory, "companion-journal.json")), "Import modified live delivery state.");
        library.ObserveAsync("unknown", "Unknown total", "Xbox", 5, null, null, [], false).GetAwaiter().GetResult();
        Assert(library.Snapshot.Last().Total is null, "Missing total was invented.");
    }
    finally { directory.Delete(true); }
}

static void SoundStudioContract()
{
    var packs = Enum.GetValues<UnlockSoundPack>();
    var hashes = packs.Select(x => Convert.ToHexString(SHA256.HashData(UnlockChime.CreateWave(20, RelayRarityTier.Gold, x)))).ToArray();
    Assert(hashes.Distinct().Count() == packs.Length, "Sound packs are not distinct.");
    foreach (var pack in packs)
    {
        Assert(UnlockChime.CreateWave(0, RelayRarityTier.Platinum, pack).AsSpan(44).ToArray().All(x => x == 0), "A sound pack bypassed zero volume.");
        Assert(UnlockChime.CreateWave(20, RelayRarityTier.Gold, pack).SequenceEqual(UnlockChime.CreateWave(20, RelayRarityTier.Gold, pack)), "Sound pack is nondeterministic.");
    }
}

static void QuietModeContract()
{
    RunSta(() =>
    {
        var directory = Directory.CreateTempSubdirectory("relay-quiet-test-");
        try
        {
            using var overlay = new AchievementOverlayService(new ActivityLog(new AppPaths(directory.FullName)));
            overlay.SetQuiet(AchievementOverlayService.QuietMode.Hold, TimeSpan.FromMinutes(30));
            var settings = new AppSettings { AchievementOverlayEnabled = true };
            Assert(!overlay.Enqueue(CreateAchievement(1) with { IsHistorical = true }, settings), "Historical entry reached automatic presentation.");
            for (var i = 0; i < 8; i++) Assert(overlay.Enqueue(CreateAchievement(1) with { Id = "held:" + i }, settings), "Hold dropped an entry before the cap.");
            Assert(!overlay.Enqueue(CreateAchievement(1) with { Id = "overflow" }, settings), "Hold exceeded its queue cap.");
            overlay.SetQuiet(AchievementOverlayService.QuietMode.Hide, TimeSpan.FromMinutes(30));
            Assert(!overlay.Preview(CreateAchievement(1), settings: settings), "Hidden local alerts bypassed quiet mode.");
            Assert(overlay.QuietStatus.Contains("0 queued"), "Hidden mode failed to clear held alerts.");
            return true;
        }
        finally { directory.Delete(true); }
    });
}

static void RareChimeContract()
{
    var common = UnlockChime.CreateWave(15);
    var gold = UnlockChime.CreateWave(15, RelayRarityTier.Gold);
    var platinum = UnlockChime.CreateWave(15, RelayRarityTier.Platinum);
    Assert(!common.SequenceEqual(gold) && !gold.SequenceEqual(platinum), "Rarity chimes must be distinct.");
    Assert(UnlockChime.CreateWave(0, RelayRarityTier.Platinum).AsSpan(44).ToArray().All(x => x == 0), "Rare chime ignored mute.");
}

static void UnlockChimeContract()
{
    var muted = UnlockChime.CreateWave(0);
    var normal = UnlockChime.CreateWave(15);
    var loud = UnlockChime.CreateWave(100);
    Assert(normal.Length == 44144, "Chime must be one second of mono 22050 Hz PCM.");
    Assert(System.Text.Encoding.ASCII.GetString(normal, 0, 4) == "RIFF" &&
           System.Text.Encoding.ASCII.GetString(normal, 8, 4) == "WAVE", "Invalid WAV container.");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(normal.AsSpan(24)) == 22050, "Wrong sample rate.");
    Assert(muted.AsSpan(44).ToArray().All(value => value == 0), "Zero volume is not silent.");
    Assert(UnlockChime.CreateWave(-50).SequenceEqual(muted), "Negative volume was not clamped.");
    Assert(UnlockChime.CreateWave(500).SequenceEqual(loud), "Excess volume was not clamped.");
    Assert(UnlockChime.CreateWave(15).SequenceEqual(normal), "Chime must be deterministic.");
    var peak = 0;
    for (var i = 44; i < normal.Length; i += 2)
        peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(normal.AsSpan(i))));
    Assert(peak > 100 && peak < 5000, "Default chime should be audible but quiet, without clipping.");
    Assert(BinaryPrimitives.ReadInt16LittleEndian(normal.AsSpan(normal.Length - 2)) == 0, "Chime must fade to silence.");
}

static void UnlockEffectsContract()
{
    RunSta(() =>
    {
        var window = new AchievementOverlayWindow(AchievementOverlayPresentation.Create(CreateAchievement(1)));
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(AchievementOverlayWindow).GetMethod("StartUnlockEffects", flags)!.Invoke(window, null);
        var pulse = (System.Windows.Media.ScaleTransform)window.FindName("ArtworkPulse");
        Assert(pulse.HasAnimatedProperties, "Artwork pulse did not start.");
        typeof(AchievementOverlayWindow).GetMethod("StopUnlockEffects", flags)!.Invoke(window, null);
        Assert(!pulse.HasAnimatedProperties, "Artwork animation survived cleanup.");
        Assert(((System.Windows.Shapes.Rectangle)window.FindName("UnlockSweep")).Opacity == 0, "Sweep survived cleanup.");
        Assert(((System.Windows.Controls.TextBlock)window.FindName("PlatinumSparkle")).Opacity == 0, "Sparkle survived cleanup.");
        return true;
    });
}

static void SignalStripPresentationPreservesFacts()
{
    var icon = CreateTestArtwork(32, 32, Color.FromArgb(20, 80, 120), Color.FromArgb(220, 170, 40));
    var presentation = AchievementOverlayPresentation.Create(CreateAchievement(4.7) with
    {
        Gamerscore = 30,
        Platform = "Xbox PC"
    }, icon);

    Assert(presentation.AchievementName == "Against All Odds", "The achievement name changed in the Signal Strip.");
    Assert(presentation.GameAndReward.Contains("+30G", StringComparison.Ordinal), "Gamerscore was omitted from the Signal Strip.");
    Assert(presentation.Platform == "Xbox PC", "The evidence-based platform label changed in the Signal Strip.");
    Assert(presentation.Percentage == "4.7%", "The exact rarity percentage changed in the Signal Strip.");
    Assert(presentation.Tier == RelayRarityTier.Gold && presentation.TierName == "Gold", "The Relay rarity tier changed in the Signal Strip.");
    Assert(presentation.AchievementIconBytes is { Length: > 0 }, "The downloaded achievement artwork was not retained for the Signal Strip.");
    Assert(presentation.AccessibleAnnouncement.Contains("4.7%", StringComparison.Ordinal) &&
           presentation.AccessibleAnnouncement.Contains("Gold", StringComparison.Ordinal) &&
           presentation.AccessibleAnnouncement.Contains("Xbox PC", StringComparison.Ordinal),
        "The Signal Strip accessibility announcement omitted rarity facts.");

    var unranked = AchievementOverlayPresentation.Create(CreateAchievement(null));
    Assert(unranked.AccessibleAnnouncement.Contains("Global rarity unavailable", StringComparison.Ordinal) &&
           !unranked.AccessibleAnnouncement.Contains("—%", StringComparison.Ordinal),
        "The Unranked accessibility announcement spoke an ambiguous percentage.");

    var steam = AchievementOverlayPresentation.Create(CreateAchievement(25) with
    {
        SourceProvider = "Steam",
        Platform = "Steam",
        Gamerscore = null
    });
    Assert(!steam.GameAndReward.Contains("+", StringComparison.Ordinal), "Steam displayed an invented Gamerscore reward.");
}

static void SignalStripPresentationBoundsProviderText()
{
    var hostile = string.Concat(Enumerable.Repeat("Very long\0 achievement\r\nname \u202e\u2066\u200b🏆 ", 40));
    var presentation = AchievementOverlayPresentation.Create(CreateAchievement(2.99) with
    {
        Name = hostile,
        GameName = hostile,
        Platform = hostile
    });

    Assert(presentation.AchievementName.Length <= 72, "The Signal Strip achievement title was not bounded.");
    Assert(presentation.Platform.Length <= 28, "The Signal Strip platform label was not bounded.");
    Assert(!presentation.AchievementName.Any(char.IsControl), "Control characters survived in the Signal Strip title.");
    Assert(!presentation.GameAndReward.Any(char.IsControl), "Control characters survived in the Signal Strip game line.");
    Assert(!presentation.AchievementName.Contains('\u202e') &&
           !presentation.AchievementName.Contains('\u2066') &&
           !presentation.AchievementName.Contains('\u200b'),
        "Bidirectional or zero-width formatting controls survived in the Signal Strip title.");
}

static void SignalStripWindowIsPassive()
{
    var state = RunSta(() =>
    {
        var window = new AchievementOverlayWindow(
            AchievementOverlayPresentation.Create(CreateAchievement(4.7)));
        return new
        {
            window.Width,
            window.Height,
            window.ShowActivated,
            window.ShowInTaskbar,
            window.Topmost,
            window.Focusable,
            window.IsHitTestVisible,
            window.WindowStyle,
            window.AllowsTransparency
        };
    });

    Assert(state.Width == 520 && state.Height == 76, "The Signal Strip footprint changed.");
    Assert(!state.ShowActivated && !state.ShowInTaskbar && state.Topmost, "The Signal Strip window activation contract changed.");
    Assert(!state.Focusable && !state.IsHitTestVisible, "The Signal Strip could intercept focus or pointer input.");
    Assert(state.WindowStyle == System.Windows.WindowStyle.None && state.AllowsTransparency, "The Signal Strip window chrome contract changed.");
    Assert(AchievementOverlayWindow.DisplayDuration == TimeSpan.FromSeconds(5), "The Signal Strip display duration changed.");
    Assert(AchievementOverlayService.MaximumQueuedNotifications == 8, "The Signal Strip safety queue changed.");
}

static void SignalStripPreviewContract()
{
    var percentages = new double?[] { 25, 10, 3, 2.99, null };
    var previews = percentages
        .Select(percentage => RunSta(() => AchievementOverlayWindow.RenderPreview(
            AchievementOverlayPresentation.Create(CreateAchievement(percentage)))))
        .ToArray();

    foreach (var preview in previews)
    {
        AssertPngDimensions(preview, 520, 76, minimumBytes: 2_000, "Signal Strip");
    }

    var emblemIdentities = percentages
        .Select(percentage => RunSta(() =>
        {
            var window = new AchievementOverlayWindow(
                AchievementOverlayPresentation.Create(CreateAchievement(percentage)));
            var glyph = window.FindName("TierGlyph") as System.Windows.Shapes.Path ??
                throw new InvalidOperationException("The Signal Strip tier glyph was unavailable.");
            var mark = window.FindName("TierGlyphMark") as System.Windows.Controls.TextBlock ??
                throw new InvalidOperationException("The Signal Strip tier mark was unavailable.");
            return string.Concat(glyph.Data.ToString(), "|", mark.Text);
        }))
        .ToArray();
    Assert(emblemIdentities.Distinct(StringComparer.Ordinal).Count() == emblemIdentities.Length,
        "Bronze, Silver, Gold, Platinum and Unranked did not use distinct Signal Strip emblem geometry and marks.");
}

static void CollectorCardPngContract()
{
    var card = Render(CreateAchievement(4.7), artwork: null);
    AssertPngContract(card);
    Assert(card.FileName == DiscordCollectorCardRenderer.CardFileName, "Unexpected attachment filename.");
    Assert(card.ContentType == DiscordCollectorCardRenderer.CardContentType, "Unexpected attachment content type.");
}

static void CollectorCardBrandedFallback()
{
    var card = Render(CreateAchievement(4.7), artwork: null);
    AssertPngContract(card);

    var malformedArtwork = new AchievementCardArtwork(
        [0x13, 0x37, 0x00, 0xff],
        [0x89, 0x50, 0x4e]);
    var malformedCard = new DiscordCollectorCardRenderer().Render(
        CreateAchievement(4.7),
        CreateSettings(),
        malformedArtwork);
    AssertPngContract(malformedCard);

    Assert(
        SHA256.HashData(card.Bytes).SequenceEqual(SHA256.HashData(malformedCard.Bytes)),
        "Malformed optional artwork did not fail closed to the canonical no-art presentation.");
}

static void CollectorCardArtworkComposition()
{
    var fallback = Render(CreateAchievement(4.7), artwork: null);
    var hero = CreateTestArtwork(720, 405, Color.FromArgb(16, 83, 150), Color.FromArgb(227, 88, 26));
    var icon = CreateTestArtwork(256, 256, Color.FromArgb(39, 176, 96), Color.FromArgb(111, 45, 145));
    var artworkCard = new DiscordCollectorCardRenderer().Render(
        CreateAchievement(4.7),
        CreateSettings(),
        new AchievementCardArtwork(hero, icon));
    var heroOnlyCard = new DiscordCollectorCardRenderer().Render(
        CreateAchievement(4.7),
        CreateSettings(),
        new AchievementCardArtwork(hero, null));

    AssertPngContract(artworkCard);
    Assert(
        !SHA256.HashData(fallback.Bytes).SequenceEqual(SHA256.HashData(artworkCard.Bytes)),
        "Supplying valid hero and achievement artwork did not change the rendered card.");

    var fallbackHeroRegion = HashRegion(fallback.Bytes, new Rectangle(60, 148, 376, 226));
    var artworkHeroRegion = HashRegion(artworkCard.Bytes, new Rectangle(60, 148, 376, 226));
    Assert(
        !fallbackHeroRegion.SequenceEqual(artworkHeroRegion),
        "The full-bleed artwork showcase did not contain evidence of the supplied hero image.");

    var heroOnlyIconRegion = HashRegion(heroOnlyCard.Bytes, new Rectangle(42, 682, 46, 46));
    var artworkIconRegion = HashRegion(artworkCard.Bytes, new Rectangle(42, 682, 46, 46));
    Assert(
        !heroOnlyIconRegion.SequenceEqual(artworkIconRegion),
        "The foreground achievement icon did not remain visible over the hero artwork.");
}

static void CollectorCardIconOnlyArtworkShowcase()
{
    var fallback = Render(CreateAchievement(4.7), artwork: null);
    var wideAchievementArtwork = CreateTestArtwork(
        960,
        540,
        Color.FromArgb(15, 120, 220),
        Color.FromArgb(245, 82, 28));
    var artworkCard = new DiscordCollectorCardRenderer().Render(
        CreateAchievement(4.7),
        CreateSettings(),
        new AchievementCardArtwork(null, wideAchievementArtwork));

    AssertPngContract(artworkCard);
    Assert(
        !HashRegion(fallback.Bytes, new Rectangle(60, 148, 376, 226)).SequenceEqual(
            HashRegion(artworkCard.Bytes, new Rectangle(60, 148, 376, 226))),
        "A valid landscape achievement image remained trapped in the old thumbnail footprint.");
    Assert(
        !HashRegion(fallback.Bytes, new Rectangle(520, 20, 250, 70)).SequenceEqual(
            HashRegion(artworkCard.Bytes, new Rectangle(520, 20, 250, 70))),
        "A valid landscape achievement image did not supply the ambient card backdrop.");
}

static void CollectorCardTinyIconIsNotPromoted()
{
    var fallback = Render(CreateAchievement(4.7), artwork: null);
    var tinyIcon = CreateTestArtwork(64, 64, Color.FromArgb(10, 180, 120), Color.FromArgb(210, 30, 120));
    var tinyCard = new DiscordCollectorCardRenderer().Render(
        CreateAchievement(4.7),
        CreateSettings(),
        new AchievementCardArtwork(null, tinyIcon));

    AssertPngContract(tinyCard);
    Assert(
        HashRegion(fallback.Bytes, new Rectangle(520, 20, 250, 70)).SequenceEqual(
            HashRegion(tinyCard.Bytes, new Rectangle(520, 20, 250, 70))),
        "A tiny square achievement icon was stretched into the full-card backdrop.");
    Assert(
        !HashRegion(fallback.Bytes, new Rectangle(894, 112, 214, 198)).SequenceEqual(
            HashRegion(tinyCard.Bytes, new Rectangle(894, 112, 214, 198))),
        "A tiny icon was discarded instead of being shown at a safe contained size.");

    var tinyHeroCard = new DiscordCollectorCardRenderer().Render(
        CreateAchievement(4.7),
        CreateSettings(),
        new AchievementCardArtwork(tinyIcon, null));
    Assert(
        HashRegion(fallback.Bytes, new Rectangle(520, 20, 250, 70)).SequenceEqual(
            HashRegion(tinyHeroCard.Bytes, new Rectangle(520, 20, 250, 70))),
        "A tiny hero asset was stretched into the full-card backdrop.");

    var wideIcon = CreateTestArtwork(960, 540, Color.FromArgb(20, 110, 210), Color.FromArgb(240, 92, 32));
    var wideIconOnlyCard = new DiscordCollectorCardRenderer().Render(
        CreateAchievement(4.7),
        CreateSettings(),
        new AchievementCardArtwork(null, wideIcon));
    var tinyHeroWithWideIconCard = new DiscordCollectorCardRenderer().Render(
        CreateAchievement(4.7),
        CreateSettings(),
        new AchievementCardArtwork(tinyIcon, wideIcon));
    Assert(
        SHA256.HashData(wideIconOnlyCard.Bytes).SequenceEqual(
            SHA256.HashData(tinyHeroWithWideIconCard.Bytes)),
        "A tiny hero asset displaced a valid wide achievement image from the showcase.");
}

static void CollectorCardReadableTypographyContract()
{
    Assert(
        DiscordCollectorCardRenderer.CardWidth == 1200 &&
        DiscordCollectorCardRenderer.CardHeight == 750,
        "The approved full-width Collector Card aspect ratio changed.");
    Assert(
        DiscordCollectorCardRenderer.ArtworkShowcaseWidth >= 400 &&
        DiscordCollectorCardRenderer.ArtworkShowcaseHeight >= 250,
        "Game artwork no longer has a dominant showcase-sized bay.");
    Assert(
        DiscordCollectorCardRenderer.AchievementTitleMaximumFontSize >= 68 &&
        DiscordCollectorCardRenderer.AchievementTitleMinimumFontSize >= 46,
        "Achievement title typography can shrink back to the unreadable v0.5 size.");
    Assert(
        DiscordCollectorCardRenderer.AchievementDescriptionFontSize >= 30 &&
        DiscordCollectorCardRenderer.RarityPercentageMaximumFontSize >= 28,
        "Description or rarity typography no longer survives normal Discord downscaling.");
    using var canvas = new Bitmap(1200, 750);
    using var graphics = Graphics.FromImage(canvas);
    using var font = new Font("Segoe UI", DiscordCollectorCardRenderer.AchievementDescriptionFontSize,
        FontStyle.Regular, GraphicsUnit.Pixel);
    var measured = graphics.MeasureString(
        "Throw something into something else with the saucer’s Abducto Beam.", font, 800);
    Assert(measured.Height <= 90, "The approved Steam description no longer fits its two-line area.");

}

static void CollectorCardUnrankedState()
{
    var unranked = Render(CreateAchievement(null), artwork: null);
    var bronze = Render(CreateAchievement(25), artwork: null);
    AssertPngContract(unranked);
    Assert(
        !HashTierEmblem(unranked.Bytes).SequenceEqual(HashTierEmblem(bronze.Bytes)),
        "The Unranked emblem was not visually distinct from Bronze.");
}

static void CollectorCardLongTextSafety()
{
    var longText = string.Concat(Enumerable.Repeat("A very long achievement title 🏆 ", 300));
    var controlText = "Relay\0\r\nPlayer\t" + new string('\ud800', 20);
    var achievement = CreateAchievement(2.99) with
    {
        Name = longText,
        Description = longText,
        GameName = longText,
        PlayerName = controlText,
        Platform = longText
    };
    var settings = CreateSettings() with { DisplayName = controlText };
    var card = new DiscordCollectorCardRenderer().Render(
        achievement,
        settings,
        new AchievementCardArtwork(null, null));

    AssertPngContract(card);
}

static void CollectorCardTierEmblemsAreDistinct()
{
    var percentages = new double?[] { 25, 10, 3, 2.99, null };
    var hashes = percentages
        .Select(percentage => Convert.ToHexString(HashTierEmblem(Render(CreateAchievement(percentage), null).Bytes)))
        .ToArray();

    Assert(
        hashes.Distinct(StringComparer.Ordinal).Count() == hashes.Length,
        "Bronze, Silver, Gold, Platinum, and Unranked did not produce five distinct emblem regions.");
}

static DiscordCollectorCard Render(AchievementEvent achievement, byte[]? artwork)
{
    var renderer = new DiscordCollectorCardRenderer();
    var cardArtwork = artwork is null
        ? new AchievementCardArtwork(null, null)
        : new AchievementCardArtwork(artwork, artwork);
    return renderer.Render(achievement, CreateSettings(), cardArtwork);
}

static AchievementEvent CreateAchievement(double? percentage) => new()
{
    Id = percentage?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unranked",
    Name = "Against All Odds",
    Description = "Complete the impossible and leave your mark.",
    GameName = "Achievement Relay Showcase",
    Gamerscore = 50,
    IsRare = percentage is >= 0 and < 10,
    RarityKnown = percentage is not null,
    RarityPercentage = percentage,
    PlayerName = "Relay Player",
    SourceProvider = "OpenXBL",
    Platform = "Xbox PC",
    UnlockedAt = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)
};

static AppSettings CreateSettings() => new()
{
    DisplayName = "Relay Player",
    IncludeRawDetailsWhenUncertain = true
};

static void AssertPngContract(DiscordCollectorCard card)
{
    AssertPngDimensions(
        card.Bytes,
        DiscordCollectorCardRenderer.CardWidth,
        DiscordCollectorCardRenderer.CardHeight,
        minimumBytes: 10_000,
        "Collector Card");
    Assert(card.Bytes.Length <= 7_500_000, "Collector Card exceeded its Discord attachment budget.");
}

static void AssertPngDimensions(
    byte[] bytes,
    int expectedWidth,
    int expectedHeight,
    int minimumBytes,
    string label)
{
    ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
    Assert(bytes.Length > minimumBytes, $"{label} PNG was unexpectedly empty or tiny.");
    Assert(bytes.AsSpan(0, 8).SequenceEqual(signature), $"{label} did not have a PNG signature.");
    Assert(bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8), $"{label} did not start with a PNG IHDR chunk.");

    var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
    var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
    Assert(width == expectedWidth, $"{label} width was {width}.");
    Assert(height == expectedHeight, $"{label} height was {height}.");
}

static byte[] HashTierEmblem(byte[] pngBytes) =>
    HashRegion(pngBytes, new Rectangle(616, 670, 72, 76));

static byte[] HashRegion(byte[] pngBytes, Rectangle region)
{
    using var input = new MemoryStream(pngBytes, writable: false);
    using var source = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
    using var bitmap = new Bitmap(source);
    Assert(
        region.Left >= 0 &&
        region.Top >= 0 &&
        region.Right <= bitmap.Width &&
        region.Bottom <= bitmap.Height,
        "Requested card sample region was outside the image.");

    using var sampled = bitmap.Clone(region, PixelFormat.Format32bppArgb);
    using var output = new MemoryStream();
    sampled.Save(output, ImageFormat.Png);
    return SHA256.HashData(output.ToArray());
}

static byte[] CreateTestArtwork(int width, int height, Color left, Color right)
{
    using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    bitmap.SetResolution(96, 96);
    using var graphics = Graphics.FromImage(bitmap);
    using var gradient = new LinearGradientBrush(
        new Rectangle(0, 0, width, height),
        left,
        right,
        LinearGradientMode.ForwardDiagonal);
    graphics.FillRectangle(gradient, 0, 0, width, height);
    using var marker = new SolidBrush(Color.FromArgb(235, 245, 242, 236));
    graphics.FillEllipse(marker, width / 4f, height / 4f, width / 2f, height / 2f);

    using var output = new MemoryStream();
    bitmap.Save(output, ImageFormat.Png);
    return output.ToArray();
}

static T RunSta<T>(Func<T> action)
{
    T? result = default;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            result = action();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    return result!;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
