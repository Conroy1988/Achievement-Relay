using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using AchievementRelay.App;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;

var tests = new (string Name, Action Run)[]
{
    ("Collector Card PNG contract", CollectorCardPngContract),
    ("Collector Card branded fallback", CollectorCardBrandedFallback),
    ("Collector Card artwork composition", CollectorCardArtworkComposition),
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

    AssertPngContract(artworkCard);
    Assert(
        !SHA256.HashData(fallback.Bytes).SequenceEqual(SHA256.HashData(artworkCard.Bytes)),
        "Supplying valid hero and achievement artwork did not change the rendered card.");

    var fallbackHeroRegion = HashRegion(fallback.Bytes, new Rectangle(650, 80, 200, 240));
    var artworkHeroRegion = HashRegion(artworkCard.Bytes, new Rectangle(650, 80, 200, 240));
    Assert(
        !fallbackHeroRegion.SequenceEqual(artworkHeroRegion),
        "The hero-art region did not contain evidence of the supplied image.");

    var fallbackIconRegion = HashRegion(fallback.Bytes, new Rectangle(72, 185, 202, 202));
    var artworkIconRegion = HashRegion(artworkCard.Bytes, new Rectangle(72, 185, 202, 202));
    Assert(
        !fallbackIconRegion.SequenceEqual(artworkIconRegion),
        "The achievement-icon region did not contain evidence of the supplied image.");
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
    HashRegion(pngBytes, new Rectangle(930, 126, 180, 180));

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
