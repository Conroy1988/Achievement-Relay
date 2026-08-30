using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;

var tests = new (string Name, Action Run)[]
{
    ("Collector Card PNG contract", CollectorCardPngContract),
    ("Collector Card branded fallback", CollectorCardBrandedFallback),
    ("Collector Card artwork composition", CollectorCardArtworkComposition),
    ("Collector Card unranked state", CollectorCardUnrankedState),
    ("Collector Card long text safety", CollectorCardLongTextSafety),
    ("Collector Card tier emblems are distinct", CollectorCardTierEmblemsAreDistinct)
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
    Console.Error.WriteLine($"{failures.Count} Collector Card smoke test(s) failed.");
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine($"All {tests.Length} Collector Card smoke tests passed.");
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
    ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
    Assert(card.Bytes.Length > 10_000, "Collector Card PNG was unexpectedly empty or tiny.");
    Assert(card.Bytes.Length <= 7_500_000, "Collector Card exceeded its Discord attachment budget.");
    Assert(card.Bytes.AsSpan(0, 8).SequenceEqual(signature), "Collector Card did not have a PNG signature.");
    Assert(card.Bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8), "Collector Card did not start with a PNG IHDR chunk.");

    var width = BinaryPrimitives.ReadInt32BigEndian(card.Bytes.AsSpan(16, 4));
    var height = BinaryPrimitives.ReadInt32BigEndian(card.Bytes.AsSpan(20, 4));
    Assert(width == DiscordCollectorCardRenderer.CardWidth, $"Collector Card width was {width}.");
    Assert(height == DiscordCollectorCardRenderer.CardHeight, $"Collector Card height was {height}.");
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

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
