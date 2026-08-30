using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed record DiscordCollectorCard(
    byte[] Bytes,
    string FileName,
    string ContentType);

/// <summary>
/// Produces a complete, fixed-size Discord Collector Card using only the
/// Windows drawing stack already shipped with the desktop application.
/// </summary>
public sealed class DiscordCollectorCardRenderer
{
    public const int CardWidth = 1200;
    public const int CardHeight = 675;
    public const int ArtworkShowcaseWidth = 400;
    public const int ArtworkShowcaseHeight = 250;
    public const float AchievementTitleMaximumFontSize = 68;
    public const float AchievementTitleMinimumFontSize = 46;
    public const float AchievementDescriptionFontSize = 32;
    public const float RarityPercentageMaximumFontSize = 96;
    public const string CardFileName = "achievement-relay-card.png";
    public const string CardContentType = "image/png";
    private const int MaximumCardBytes = 7_500_000;

    private static readonly Lazy<byte[]?> BrandImageBytes = new(LoadBrandImageBytes);

    /// <summary>
    /// Creates the canonical, anonymized Gold-tier fallback preview. Windows
    /// validation tooling can persist these returned bytes without introducing
    /// a second mock implementation of the public card design.
    /// </summary>
    public DiscordCollectorCard RenderGoldFallbackPreview() => Render(
        new AchievementEvent
        {
            Id = "collector-card-preview",
            Name = "Against All Odds",
            Description = "Complete the impossible and leave your mark.",
            GameName = "Achievement Relay Showcase",
            Gamerscore = 50,
            IsRare = true,
            RarityKnown = true,
            RarityPercentage = 4.7,
            PlayerName = "Relay Player",
            SourceProvider = "OpenXBL",
            Platform = "Xbox PC",
            UnlockedAt = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)
        },
        new AppSettings { DisplayName = "Relay Player" },
        new AchievementCardArtwork(null, null));

    /// <summary>
    /// Creates a deterministic icon-only landscape preview matching the
    /// artwork shape that exposed the postage-stamp v0.5 Discord layout.
    /// </summary>
    public DiscordCollectorCard RenderArtworkShowcasePreview() => Render(
        new AchievementEvent
        {
            Id = "collector-card-artwork-preview",
            Name = "All for One",
            Description = "Maximized the rank of a Pal.",
            GameName = "Palworld",
            Gamerscore = 30,
            IsRare = true,
            RarityKnown = true,
            RarityPercentage = 3.8,
            PlayerName = "Relay Player",
            SourceProvider = "OpenXBL",
            Platform = "Xbox PC",
            UnlockedAt = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)
        },
        new AppSettings { DisplayName = "Relay Player", IncludeRawDetailsWhenUncertain = true },
        new AchievementCardArtwork(null, CreatePreviewArtwork()));

    public DiscordCollectorCard Render(
        AchievementEvent achievement,
        AppSettings settings,
        AchievementCardArtwork artwork)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(artwork);

        using var canvas = new Bitmap(CardWidth, CardHeight, PixelFormat.Format32bppArgb);
        canvas.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(canvas);
        ConfigureGraphics(graphics);

        using var hero = TryDecodeImage(artwork.HeroImageBytes, 20_000_000);
        using var achievementIcon = TryDecodeImage(artwork.AchievementIconBytes, 4_000_000);
        using var brand = TryDecodeImage(BrandImageBytes.Value, 4_000_000);

        var ambientArtwork = IsWideShowcaseArtwork(hero)
            ? hero
            : IsWideShowcaseArtwork(achievementIcon)
                ? achievementIcon
                : null;
        DrawBackground(graphics, ambientArtwork, brand);
        DrawChrome(graphics);

        var tier = RelayRarityClassifier.Classify(achievement.RarityPercentage);
        var palette = GetTierPalette(tier);
        DrawContentPanels(graphics, palette);
        DrawHeader(graphics, achievement, palette);
        DrawArtworkShowcase(graphics, hero, achievementIcon, brand, palette);
        DrawAchievementDetails(graphics, achievement, settings, palette);
        DrawRarityPanel(graphics, achievement, tier, palette);
        DrawFooter(graphics);

        using var output = new MemoryStream();
        canvas.Save(output, ImageFormat.Png);
        if (output.Length is <= 32 or > MaximumCardBytes)
        {
            throw new InvalidDataException("The generated Collector Card did not meet the attachment size contract.");
        }

        var bytes = output.ToArray();
        if (!bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new InvalidDataException("The generated Collector Card was not a valid PNG.");
        }

        return new DiscordCollectorCard(bytes, CardFileName, CardContentType);
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.Clear(Color.FromArgb(7, 9, 10));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    private static void DrawBackground(Graphics graphics, Image? artwork, Image? brand)
    {
        using (var baseGradient = new LinearGradientBrush(
                   new Rectangle(0, 0, CardWidth, CardHeight),
                   Color.FromArgb(5, 7, 8),
                   Color.FromArgb(42, 7, 10),
                   24f))
        {
            graphics.FillRectangle(baseGradient, 0, 0, CardWidth, CardHeight);
        }

        if (artwork is not null)
        {
            DrawSoftFocusCover(graphics, artwork, new RectangleF(0, 0, CardWidth, CardHeight));

            using var artWash = new LinearGradientBrush(
                new Rectangle(0, 0, CardWidth, CardHeight),
                Color.FromArgb(126, 5, 7, 8),
                Color.FromArgb(76, 5, 7, 8),
                LinearGradientMode.Horizontal)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors =
                    [
                        Color.FromArgb(126, 5, 7, 8),
                        Color.FromArgb(92, 5, 7, 8),
                        Color.FromArgb(68, 5, 7, 8),
                        Color.FromArgb(142, 17, 5, 8)
                    ],
                    Positions = [0f, 0.3f, 0.7f, 1f]
                }
            };
            graphics.FillRectangle(artWash, 0, 0, CardWidth, CardHeight);
        }
        else
        {
            DrawFallbackPattern(graphics, brand);
        }

        using var bottomWash = new LinearGradientBrush(
            new Rectangle(0, 410, CardWidth, 265),
            Color.FromArgb(0, 3, 4, 5),
            Color.FromArgb(188, 3, 4, 5),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(bottomWash, 0, 410, CardWidth, 265);
    }

    private static void DrawFallbackPattern(Graphics graphics, Image? brand)
    {
        using var gridPen = new Pen(Color.FromArgb(22, 216, 43, 50), 1f);
        for (var x = 0; x <= CardWidth; x += 48)
        {
            graphics.DrawLine(gridPen, x, 0, x, CardHeight);
        }

        for (var y = 0; y <= CardHeight; y += 48)
        {
            graphics.DrawLine(gridPen, 0, y, CardWidth, y);
        }

        using (var panelBrush = new SolidBrush(Color.FromArgb(70, 116, 10, 16)))
        {
            graphics.FillPolygon(panelBrush,
            [
                new PointF(540, 0),
                new PointF(1200, 0),
                new PointF(1200, 675),
                new PointF(880, 675)
            ]);
        }

        using var signalPen = new Pen(Color.FromArgb(90, 241, 42, 51), 4f);
        using var signalPenSoft = new Pen(Color.FromArgb(35, 241, 42, 51), 12f);
        for (var inset = 0; inset < 3; inset++)
        {
            var size = 340 + inset * 125;
            var rect = new RectangleF(942 - size / 2f, 342 - size / 2f, size, size);
            graphics.DrawArc(signalPenSoft, rect, 205, 130);
            graphics.DrawArc(signalPen, rect, 205, 130);
        }

        if (brand is not null)
        {
            DrawImageWithOpacity(graphics, brand, new RectangleF(740, 90, 390, 390), 0.2f);
        }
        else
        {
            using var fallbackBrush = new SolidBrush(Color.FromArgb(25, 245, 240, 232));
            using var fallbackFont = CreateFont(250, FontStyle.Bold);
            graphics.DrawString("R", fallbackFont, fallbackBrush, new PointF(810, 90));
        }
    }

    private static void DrawChrome(Graphics graphics)
    {
        using var borderPen = new Pen(Color.FromArgb(228, 215, 35, 43), 3f);
        graphics.DrawRectangle(borderPen, 2, 2, CardWidth - 5, CardHeight - 5);

        using var topPen = new Pen(Color.FromArgb(255, 255, 71, 79), 5f);
        graphics.DrawLine(topPen, 54, 2, 358, 2);

        using var railBrush = new LinearGradientBrush(
            new Rectangle(0, 0, 18, CardHeight),
            Color.FromArgb(226, 208, 28, 36),
            Color.FromArgb(0, 208, 28, 36),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(railBrush, 0, 0, 16, CardHeight);
    }

    private static void DrawContentPanels(Graphics graphics, TierPalette palette)
    {
        var details = new RectangleF(458, 104, 410, 462);
        using var detailsPath = CreateRoundedRectangle(details, 26);
        using var detailsBrush = new LinearGradientBrush(
            details,
            Color.FromArgb(218, 7, 10, 12),
            Color.FromArgb(232, 5, 7, 8),
            LinearGradientMode.Vertical);
        using var detailsBorder = new Pen(Color.FromArgb(105, palette.Mid), 1.5f);
        graphics.FillPath(detailsBrush, detailsPath);
        graphics.DrawPath(detailsBorder, detailsPath);
    }

    private static void DrawHeader(Graphics graphics, AchievementEvent achievement, TierPalette palette)
    {
        using var eyebrowFont = CreateFont(25, FontStyle.Bold);
        using var eyebrowBrush = new SolidBrush(Color.FromArgb(255, 255, 112, 118));
        graphics.DrawString("ACHIEVEMENT UNLOCKED", eyebrowFont, eyebrowBrush, new PointF(58, 42));

        var platform = LimitText(ResolvePlatform(achievement), 42);
        using var platformFont = CreateFont(24, FontStyle.Bold);
        var measured = graphics.MeasureString(platform.ToUpperInvariant(), platformFont);
        var pill = new RectangleF(CardWidth - measured.Width - 104, 31, measured.Width + 52, 48);
        using var pillPath = CreateRoundedRectangle(pill, 18);
        using var pillBrush = new SolidBrush(Color.FromArgb(205, 8, 11, 13));
        using var pillBorder = new Pen(Color.FromArgb(210, palette.Light), 2f);
        graphics.FillPath(pillBrush, pillPath);
        graphics.DrawPath(pillBorder, pillPath);
        using var platformBrush = new SolidBrush(Color.FromArgb(255, 245, 242, 236));
        using var pillFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(platform.ToUpperInvariant(), platformFont, platformBrush, pill, pillFormat);
    }

    private static void DrawArtworkShowcase(
        Graphics graphics,
        Image? hero,
        Image? achievementIcon,
        Image? brand,
        TierPalette palette)
    {
        var outer = new RectangleF(48, 136, ArtworkShowcaseWidth, ArtworkShowcaseHeight);
        using var glowPath = CreateRoundedRectangle(RectangleF.Inflate(outer, 8, 8), 31);
        using var glowBrush = new SolidBrush(Color.FromArgb(38, palette.Light));
        graphics.FillPath(glowBrush, glowPath);

        using var framePath = CreateRoundedRectangle(outer, 24);
        using var frameBrush = new LinearGradientBrush(
            outer,
            Color.FromArgb(245, 26, 29, 31),
            Color.FromArgb(245, 7, 9, 10),
            LinearGradientMode.ForwardDiagonal);
        graphics.FillPath(frameBrush, framePath);

        var state = graphics.Save();
        graphics.SetClip(framePath);
        var primaryArtwork = IsWideShowcaseArtwork(hero)
            ? hero
            : IsWideShowcaseArtwork(achievementIcon)
                ? achievementIcon
                : hero ?? achievementIcon;
        if (primaryArtwork is not null)
        {
            if (IsWideShowcaseArtwork(primaryArtwork))
            {
                DrawImageCoverWithOpacity(graphics, primaryArtwork, outer, 0.42f);
                using var ambientWash = new SolidBrush(Color.FromArgb(42, 4, 6, 7));
                graphics.FillRectangle(ambientWash, outer);
            }

            DrawImageContain(
                graphics,
                primaryArtwork,
                new RectangleF(60, 148, ArtworkShowcaseWidth - 24, ArtworkShowcaseHeight - 24),
                MaximumSafeUpscale(primaryArtwork));
        }
        else if (brand is not null)
        {
            DrawImageContain(graphics, brand, new RectangleF(122, 151, 252, 220), 1f);
        }
        else
        {
            DrawFallbackTrophy(graphics, new RectangleF(148, 158, 200, 200), palette);
        }

        graphics.Restore(state);
        using var framePen = new Pen(Color.FromArgb(230, palette.Light), 4f);
        graphics.DrawPath(framePen, framePath);

        if (hero is null || achievementIcon is null || ReferenceEquals(primaryArtwork, achievementIcon))
        {
            return;
        }

        var iconFrame = new RectangleF(66, 248, 122, 122);
        using var iconPath = CreateRoundedRectangle(iconFrame, 20);
        using var iconBackground = new SolidBrush(Color.FromArgb(238, 7, 9, 10));
        using var iconBorder = new Pen(Color.FromArgb(245, palette.Light), 3f);
        graphics.FillPath(iconBackground, iconPath);
        var iconState = graphics.Save();
        graphics.SetClip(iconPath);
        DrawImageContain(
            graphics,
            achievementIcon,
            new RectangleF(74, 256, 106, 106),
            1f);
        graphics.Restore(iconState);
        graphics.DrawPath(iconBorder, iconPath);
    }

    private static void DrawAchievementDetails(
        Graphics graphics,
        AchievementEvent achievement,
        AppSettings settings,
        TierPalette palette)
    {
        var gameName = Sanitize(achievement.GameName, "Unknown game").ToUpperInvariant();
        using var gameFont = CreateFont(30, FontStyle.Bold);
        using var gameBrush = new SolidBrush(Color.FromArgb(255, palette.Light));
        using var gameFormat = CreateSingleLineFormat();
        graphics.DrawString(gameName, gameFont, gameBrush, new RectangleF(486, 126, 354, 44), gameFormat);

        DrawFittedTitle(
            graphics,
            Sanitize(achievement.Name, "Achievement unlocked"),
            new RectangleF(482, 174, 362, 150),
            Color.FromArgb(255, 248, 245, 239));

        if (settings.IncludeRawDetailsWhenUncertain && !string.IsNullOrWhiteSpace(achievement.Description))
        {
            using var descriptionFont = CreateFont(AchievementDescriptionFontSize, FontStyle.Regular, condensed: false);
            using var descriptionBrush = new SolidBrush(Color.FromArgb(255, 211, 216, 218));
            using var descriptionFormat = new StringFormat
            {
                Trimming = StringTrimming.EllipsisWord,
                FormatFlags = StringFormatFlags.LineLimit
            };
            graphics.DrawString(
                Sanitize(achievement.Description, string.Empty),
                descriptionFont,
                descriptionBrush,
                new RectangleF(486, 337, 354, 174),
                descriptionFormat);
        }

        var player = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? achievement.PlayerName
            : settings.DisplayName;
        var chipX = 48f;
        if (!string.IsNullOrWhiteSpace(player))
        {
            chipX += DrawChip(graphics, chipX, 420, $"PLAYER  {Sanitize(player, "Player")}", palette) + 12;
        }

        if (achievement.Gamerscore is { } gamerscore)
        {
            DrawChip(graphics, chipX, 420, $"+{gamerscore}G", palette);
        }
    }

    private static void DrawRarityPanel(
        Graphics graphics,
        AchievementEvent achievement,
        RelayRarityTier tier,
        TierPalette palette)
    {
        var panel = new RectangleF(886, 96, 266, 470);
        using var panelPath = CreateRoundedRectangle(panel, 26);
        using var panelBrush = new LinearGradientBrush(
            panel,
            Color.FromArgb(225, 16, 19, 21),
            Color.FromArgb(238, 5, 7, 8),
            LinearGradientMode.Vertical);
        using var panelPen = new Pen(Color.FromArgb(190, palette.Mid), 2f);
        graphics.FillPath(panelBrush, panelPath);
        graphics.DrawPath(panelPen, panelPath);

        DrawTierEmblem(graphics, new RectangleF(949, 126, 140, 140), tier, palette);

        var percentage = RelayRarityClassifier.FormatPercentage(achievement.RarityPercentage);
        DrawCenteredFittedText(
            graphics,
            percentage,
            new RectangleF(903, 278, 232, 108),
            RarityPercentageMaximumFontSize,
            58,
            Color.FromArgb(255, palette.Light));

        var population = string.Equals(achievement.SourceProvider, "Steam", StringComparison.OrdinalIgnoreCase)
            ? "OF STEAM PLAYERS"
            : "OF PLAYERS";
        using var populationFont = CreateFont(20, FontStyle.Bold);
        using var mutedBrush = new SolidBrush(Color.FromArgb(255, 180, 187, 190));
        using var centeredFormat = CreateCenteredFormat();
        graphics.DrawString(population, populationFont, mutedBrush, new RectangleF(903, 385, 232, 30), centeredFormat);

        DrawCenteredFittedText(
            graphics,
            $"RELAY {RelayRarityClassifier.DisplayName(tier).ToUpperInvariant()} TIER",
            new RectangleF(903, 432, 232, 44),
            28,
            18,
            Color.FromArgb(255, palette.Light));

        using var descriptionFont = CreateFont(19, FontStyle.Regular, condensed: false);
        graphics.DrawString(
            RelayRarityClassifier.Description(tier),
            descriptionFont,
            mutedBrush,
            new RectangleF(903, 486, 232, 30),
            centeredFormat);
    }

    private static void DrawTierEmblem(
        Graphics graphics,
        RectangleF bounds,
        RelayRarityTier tier,
        TierPalette palette)
    {
        using var glowBrush = new SolidBrush(Color.FromArgb(35, palette.Light));
        graphics.FillEllipse(glowBrush, RectangleF.Inflate(bounds, 14, 14));
        using var shadowPen = new Pen(Color.FromArgb(105, palette.Light), 8f);
        graphics.DrawEllipse(shadowPen, RectangleF.Inflate(bounds, 4, 4));

        using var fill = new LinearGradientBrush(bounds, palette.Light, palette.Dark, LinearGradientMode.ForwardDiagonal);
        using var outline = new Pen(Color.FromArgb(255, palette.Light), 4f)
        {
            LineJoin = LineJoin.Round
        };
        using var inner = new Pen(Color.FromArgb(180, 255, 255, 255), 2f)
        {
            LineJoin = LineJoin.Round
        };

        switch (tier)
        {
            case RelayRarityTier.Bronze:
                graphics.FillEllipse(fill, bounds);
                graphics.DrawEllipse(outline, bounds);
                graphics.DrawArc(inner, RectangleF.Inflate(bounds, -18, -18), 205, 260);
                DrawChevron(graphics, bounds, inner);
                break;

            case RelayRarityTier.Silver:
            {
                var shield = CreateShield(bounds);
                using (shield)
                {
                    graphics.FillPath(fill, shield);
                    graphics.DrawPath(outline, shield);
                    var inset = RectangleF.Inflate(bounds, -22, -18);
                    using var innerShield = CreateShield(inset);
                    graphics.DrawPath(inner, innerShield);
                }

                DrawRelayBars(graphics, bounds, inner);
                break;
            }

            case RelayRarityTier.Gold:
            {
                var star = CreateStar(bounds, 8, 0.52f);
                using (star)
                {
                    graphics.FillPath(fill, star);
                    graphics.DrawPath(outline, star);
                }

                graphics.DrawEllipse(inner, RectangleF.Inflate(bounds, -42, -42));
                DrawRelayBars(graphics, bounds, inner);
                break;
            }

            case RelayRarityTier.Platinum:
            {
                var diamond = CreateDiamond(bounds);
                using (diamond)
                {
                    graphics.FillPath(fill, diamond);
                    graphics.DrawPath(outline, diamond);
                }

                graphics.DrawLine(inner, bounds.Left + 27, bounds.Top + 45, bounds.Right - 27, bounds.Top + 45);
                graphics.DrawLine(inner, bounds.Left + 27, bounds.Top + 45, bounds.Left + bounds.Width / 2, bounds.Bottom - 19);
                graphics.DrawLine(inner, bounds.Right - 27, bounds.Top + 45, bounds.Left + bounds.Width / 2, bounds.Bottom - 19);
                graphics.DrawLine(inner, bounds.Left + bounds.Width / 2, bounds.Top + 17, bounds.Left + bounds.Width / 2, bounds.Bottom - 19);
                break;
            }

            case RelayRarityTier.Unranked:
            default:
            {
                var hexagon = CreateRegularPolygon(bounds, 6, -90);
                using (hexagon)
                {
                    graphics.FillPath(fill, hexagon);
                    graphics.DrawPath(outline, hexagon);
                }

                using var questionFont = CreateFont(70, FontStyle.Bold);
                using var questionBrush = new SolidBrush(Color.FromArgb(235, 245, 242, 236));
                using var format = CreateCenteredFormat();
                graphics.DrawString("?", questionFont, questionBrush, bounds, format);
                break;
            }
        }
    }

    private static void DrawFooter(Graphics graphics)
    {
        using var linePen = new Pen(Color.FromArgb(105, 126, 133, 136), 1f);
        graphics.DrawLine(linePen, 58, 584, 1145, 584);

        using var brandFont = CreateFont(18, FontStyle.Bold);
        using var brandBrush = new SolidBrush(Color.FromArgb(255, 245, 242, 236));
        graphics.DrawString("ACHIEVEMENT RELAY", brandFont, brandBrush, new PointF(58, 609));

        using var taglineFont = CreateFont(15, FontStyle.Regular, condensed: false);
        using var taglineBrush = new SolidBrush(Color.FromArgb(255, 169, 176, 179));
        graphics.DrawString("EVERY UNLOCK, ELEVATED", taglineFont, taglineBrush, new PointF(252, 613));

        using var getFont = CreateFont(16, FontStyle.Bold);
        using var getBrush = new SolidBrush(Color.FromArgb(255, 255, 112, 118));
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        graphics.DrawString("GET THE RELAY", getFont, getBrush, new RectangleF(895, 610, 250, 28), right);
    }

    private static float DrawChip(
        Graphics graphics,
        float x,
        float y,
        string text,
        TierPalette palette)
    {
        using var font = CreateFont(21, FontStyle.Bold);
        var size = graphics.MeasureString(text, font);
        var width = Math.Min(250, size.Width + 34);
        var bounds = new RectangleF(x, y, width, 46);
        using var path = CreateRoundedRectangle(bounds, 12);
        using var background = new SolidBrush(Color.FromArgb(210, 12, 15, 17));
        using var border = new Pen(Color.FromArgb(145, palette.Mid), 1.5f);
        using var foreground = new SolidBrush(Color.FromArgb(255, 226, 225, 221));
        using var format = CreateCenteredFormat();
        graphics.FillPath(background, path);
        graphics.DrawPath(border, path);
        graphics.DrawString(text, font, foreground, bounds, format);
        return width;
    }

    private static void DrawFittedTitle(Graphics graphics, string text, RectangleF bounds, Color color) =>
        DrawFittedText(
            graphics,
            text,
            bounds,
            AchievementTitleMaximumFontSize,
            AchievementTitleMinimumFontSize,
            color,
            centered: false);

    private static void DrawCenteredFittedText(
        Graphics graphics,
        string text,
        RectangleF bounds,
        float maximumSize,
        float minimumSize,
        Color color) =>
        DrawFittedText(graphics, text, bounds, maximumSize, minimumSize, color, centered: true);

    private static void DrawFittedText(
        Graphics graphics,
        string text,
        RectangleF bounds,
        float maximumSize,
        float minimumSize,
        Color color,
        bool centered)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = centered ? StringAlignment.Center : StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = centered ? StringTrimming.EllipsisCharacter : StringTrimming.EllipsisWord,
            FormatFlags = centered ? StringFormatFlags.NoWrap : StringFormatFlags.LineLimit
        };

        for (var size = maximumSize; size >= minimumSize; size -= 2)
        {
            using var font = CreateFont(size, FontStyle.Bold);
            var measured = centered
                ? graphics.MeasureString(text, font)
                : graphics.MeasureString(text, font, (int)bounds.Width);
            if ((measured.Width <= bounds.Width && measured.Height <= bounds.Height) || size <= minimumSize)
            {
                graphics.DrawString(text, font, brush, bounds, format);
                return;
            }
        }
    }

    private static void DrawFallbackTrophy(Graphics graphics, RectangleF bounds, TierPalette palette)
    {
        using var pen = new Pen(Color.FromArgb(245, palette.Light), 10f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var bowl = new RectangleF(bounds.Left + 36, bounds.Top + 16, bounds.Width - 72, bounds.Height * 0.48f);
        graphics.DrawArc(pen, bowl, 0, 180);
        graphics.DrawLine(pen, bowl.Left, bowl.Top + bowl.Height / 2, bowl.Left, bowl.Bottom - 3);
        graphics.DrawLine(pen, bowl.Right, bowl.Top + bowl.Height / 2, bowl.Right, bowl.Bottom - 3);
        graphics.DrawLine(pen, bounds.Left + bounds.Width / 2, bowl.Bottom, bounds.Left + bounds.Width / 2, bounds.Bottom - 28);
        graphics.DrawLine(pen, bounds.Left + 45, bounds.Bottom - 25, bounds.Right - 45, bounds.Bottom - 25);
        graphics.DrawArc(pen, new RectangleF(bounds.Left + 12, bounds.Top + 24, 58, 65), 88, 185);
        graphics.DrawArc(pen, new RectangleF(bounds.Right - 70, bounds.Top + 24, 58, 65), 267, 185);
    }

    private static void DrawChevron(Graphics graphics, RectangleF bounds, Pen pen)
    {
        var center = bounds.Left + bounds.Width / 2;
        graphics.DrawLines(pen,
        [
            new PointF(center - 35, bounds.Top + 57),
            new PointF(center, bounds.Top + 82),
            new PointF(center + 35, bounds.Top + 57)
        ]);
        graphics.DrawLines(pen,
        [
            new PointF(center - 35, bounds.Top + 83),
            new PointF(center, bounds.Top + 108),
            new PointF(center + 35, bounds.Top + 83)
        ]);
    }

    private static void DrawRelayBars(Graphics graphics, RectangleF bounds, Pen pen)
    {
        var centerX = bounds.Left + bounds.Width / 2;
        graphics.DrawLine(pen, centerX - 35, bounds.Top + 81, centerX - 35, bounds.Top + 112);
        graphics.DrawLine(pen, centerX, bounds.Top + 65, centerX, bounds.Top + 112);
        graphics.DrawLine(pen, centerX + 35, bounds.Top + 48, centerX + 35, bounds.Top + 112);
    }

    private static GraphicsPath CreateShield(RectangleF bounds)
    {
        var path = new GraphicsPath();
        path.AddPolygon(
        [
            new PointF(bounds.Left + bounds.Width / 2, bounds.Top),
            new PointF(bounds.Right - 11, bounds.Top + 28),
            new PointF(bounds.Right - 22, bounds.Bottom - 42),
            new PointF(bounds.Left + bounds.Width / 2, bounds.Bottom),
            new PointF(bounds.Left + 22, bounds.Bottom - 42),
            new PointF(bounds.Left + 11, bounds.Top + 28)
        ]);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateDiamond(RectangleF bounds)
    {
        var path = new GraphicsPath();
        path.AddPolygon(
        [
            new PointF(bounds.Left + bounds.Width / 2, bounds.Top),
            new PointF(bounds.Right - 8, bounds.Top + 45),
            new PointF(bounds.Left + bounds.Width / 2, bounds.Bottom),
            new PointF(bounds.Left + 8, bounds.Top + 45)
        ]);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateStar(RectangleF bounds, int points, float innerRatio)
    {
        var path = new GraphicsPath();
        var vertices = new PointF[points * 2];
        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;
        var outerRadius = Math.Min(bounds.Width, bounds.Height) / 2;
        for (var index = 0; index < vertices.Length; index++)
        {
            var radius = index % 2 == 0 ? outerRadius : outerRadius * innerRatio;
            var angle = -Math.PI / 2 + index * Math.PI / points;
            vertices[index] = new PointF(
                centerX + (float)Math.Cos(angle) * radius,
                centerY + (float)Math.Sin(angle) * radius);
        }

        path.AddPolygon(vertices);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateRegularPolygon(RectangleF bounds, int sides, float startDegrees)
    {
        var path = new GraphicsPath();
        var vertices = new PointF[sides];
        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;
        var radius = Math.Min(bounds.Width, bounds.Height) / 2;
        for (var index = 0; index < sides; index++)
        {
            var angle = (startDegrees + index * 360f / sides) * Math.PI / 180;
            vertices[index] = new PointF(
                centerX + (float)Math.Cos(angle) * radius,
                centerY + (float)Math.Sin(angle) * radius);
        }

        path.AddPolygon(vertices);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        var path = new GraphicsPath();
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawImageCover(Graphics graphics, Image image, RectangleF destination)
    {
        var source = CalculateCoverSource(image, destination);
        graphics.DrawImage(image, destination, source, GraphicsUnit.Pixel);
    }

    private static void DrawSoftFocusCover(Graphics graphics, Image image, RectangleF destination)
    {
        const int reductionFactor = 5;
        var reducedWidth = Math.Max(1, (int)Math.Ceiling(destination.Width / reductionFactor));
        var reducedHeight = Math.Max(1, (int)Math.Ceiling(destination.Height / reductionFactor));
        using var reduced = new Bitmap(reducedWidth, reducedHeight, PixelFormat.Format32bppArgb);
        using (var reducedGraphics = Graphics.FromImage(reduced))
        {
            reducedGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            reducedGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            reducedGraphics.CompositingQuality = CompositingQuality.HighQuality;
            DrawImageCover(
                reducedGraphics,
                image,
                new RectangleF(0, 0, reducedWidth, reducedHeight));
        }

        graphics.DrawImage(reduced, destination);
    }

    private static void DrawImageCoverWithOpacity(
        Graphics graphics,
        Image image,
        RectangleF destination,
        float opacity)
    {
        var source = CalculateCoverSource(image, destination);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix
        {
            Matrix00 = 1f,
            Matrix11 = 1f,
            Matrix22 = 1f,
            Matrix33 = Math.Clamp(opacity, 0f, 1f),
            Matrix44 = 1f
        });
        graphics.DrawImage(
            image,
            Rectangle.Round(destination),
            source.X,
            source.Y,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    private static RectangleF CalculateCoverSource(Image image, RectangleF destination)
    {
        var sourceRatio = image.Width / (float)image.Height;
        var destinationRatio = destination.Width / destination.Height;
        if (sourceRatio > destinationRatio)
        {
            var width = image.Height * destinationRatio;
            return new RectangleF((image.Width - width) / 2f, 0, width, image.Height);
        }

        var height = image.Width / destinationRatio;
        return new RectangleF(0, (image.Height - height) / 2f, image.Width, height);
    }

    private static void DrawImageContain(
        Graphics graphics,
        Image image,
        RectangleF destination,
        float maximumUpscale = 1f)
    {
        var scale = Math.Min(destination.Width / image.Width, destination.Height / image.Height);
        scale = Math.Min(scale, Math.Max(1f, maximumUpscale));
        var width = image.Width * scale;
        var height = image.Height * scale;
        var target = new RectangleF(
            destination.Left + (destination.Width - width) / 2f,
            destination.Top + (destination.Height - height) / 2f,
            width,
            height);
        graphics.DrawImage(image, target);
    }

    private static bool IsWideShowcaseArtwork(Image? image)
    {
        if (image is null || image.Width < 320 || image.Height < 160)
        {
            return false;
        }

        var ratio = image.Width / (float)image.Height;
        return ratio is >= 1.25f and <= 3.2f;
    }

    private static float MaximumSafeUpscale(Image image) =>
        Math.Min(image.Width, image.Height) >= 192 ? 1.35f : 1f;

    private static void DrawImageWithOpacity(
        Graphics graphics,
        Image image,
        RectangleF destination,
        float opacity)
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix
        {
            Matrix00 = 1f,
            Matrix11 = 1f,
            Matrix22 = 1f,
            Matrix33 = opacity,
            Matrix44 = 1f
        });
        graphics.DrawImage(
            image,
            Rectangle.Round(destination),
            0,
            0,
            image.Width,
            image.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    private static Bitmap? TryDecodeImage(byte[]? bytes, long maximumPixels)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            var pixels = checked((long)source.Width * source.Height);
            if (source.Width <= 0 || source.Height <= 0 || pixels > maximumPixels)
            {
                return null;
            }

            return new Bitmap(source);
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or OutOfMemoryException or OverflowException)
        {
            return null;
        }
    }

    private static byte[]? LoadBrandImageBytes()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri(
                    "pack://application:,,,/AchievementRelay.App;component/Assets/AchievementRelay.png",
                    UriKind.Absolute));
            if (resource is null)
            {
                return null;
            }

            using (resource.Stream)
            using (var output = new MemoryStream())
            {
                resource.Stream.CopyTo(output);
                return output.ToArray();
            }
        }
        catch (Exception exception) when (exception is IOException or
                                          InvalidOperationException or
                                          NotSupportedException or
                                          TypeInitializationException or
                                          UriFormatException)
        {
            return null;
        }
    }

    private static byte[] CreatePreviewArtwork()
    {
        using var preview = new Bitmap(960, 540, PixelFormat.Format32bppArgb);
        preview.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(preview);
        ConfigureGraphics(graphics);
        using (var sky = new LinearGradientBrush(
                   new Rectangle(0, 0, preview.Width, preview.Height),
                   Color.FromArgb(21, 74, 126),
                   Color.FromArgb(229, 102, 47),
                   LinearGradientMode.ForwardDiagonal))
        {
            graphics.FillRectangle(sky, 0, 0, preview.Width, preview.Height);
        }

        using (var sun = new SolidBrush(Color.FromArgb(235, 255, 224, 126)))
        {
            graphics.FillEllipse(sun, 668, 68, 166, 166);
        }

        using (var distant = new SolidBrush(Color.FromArgb(220, 45, 82, 104)))
        {
            graphics.FillPolygon(distant,
            [
                new PointF(0, 390),
                new PointF(180, 190),
                new PointF(330, 338),
                new PointF(500, 145),
                new PointF(710, 390)
            ]);
        }

        using (var foreground = new SolidBrush(Color.FromArgb(245, 11, 31, 40)))
        {
            graphics.FillPolygon(foreground,
            [
                new PointF(0, 430),
                new PointF(230, 292),
                new PointF(448, 422),
                new PointF(700, 250),
                new PointF(960, 392),
                new PointF(960, 540),
                new PointF(0, 540)
            ]);
        }

        using var output = new MemoryStream();
        preview.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static Font CreateFont(float size, FontStyle style, bool condensed = true) =>
        new(condensed ? "Bahnschrift SemiCondensed" : "Segoe UI", size, style, GraphicsUnit.Pixel);

    private static StringFormat CreateCenteredFormat() => new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
    };

    private static StringFormat CreateSingleLineFormat() => new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
    };

    private static string ResolvePlatform(AchievementEvent achievement)
    {
        if (!string.IsNullOrWhiteSpace(achievement.Platform))
        {
            return Sanitize(achievement.Platform, "Xbox");
        }

        return string.Equals(achievement.SourceProvider, "OpenXBL", StringComparison.OrdinalIgnoreCase)
            ? "Xbox"
            : Sanitize(achievement.SourceProvider, "Achievement Relay");
    }

    private static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var source = value.Trim();
        var normalized = new StringBuilder(Math.Min(source.Length, 1024));
        for (var index = 0; index < source.Length && normalized.Length < 1024; index++)
        {
            var current = source[index];
            if (char.IsHighSurrogate(current) &&
                index + 1 < source.Length &&
                char.IsLowSurrogate(source[index + 1]))
            {
                normalized.Append(current);
                normalized.Append(source[++index]);
            }
            else if (char.IsSurrogate(current) || char.IsControl(current))
            {
                normalized.Append(char.IsControl(current) ? ' ' : '\uFFFD');
            }
            else
            {
                normalized.Append(current);
            }
        }

        return normalized.ToString();
    }

    private static string LimitText(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var contentLength = maximumLength - 1;
        if (contentLength > 0 &&
            char.IsHighSurrogate(value[contentLength - 1]) &&
            char.IsLowSurrogate(value[contentLength]))
        {
            contentLength--;
        }

        return value[..contentLength] + "…";
    }

    private static TierPalette GetTierPalette(RelayRarityTier tier) => tier switch
    {
        RelayRarityTier.Bronze => new TierPalette(
            Color.FromArgb(92, 43, 21),
            Color.FromArgb(184, 106, 58),
            Color.FromArgb(242, 177, 111)),
        RelayRarityTier.Silver => new TierPalette(
            Color.FromArgb(65, 73, 80),
            Color.FromArgb(162, 173, 181),
            Color.FromArgb(239, 245, 248)),
        RelayRarityTier.Gold => new TierPalette(
            Color.FromArgb(103, 67, 5),
            Color.FromArgb(218, 158, 35),
            Color.FromArgb(255, 224, 120)),
        RelayRarityTier.Platinum => new TierPalette(
            Color.FromArgb(35, 77, 92),
            Color.FromArgb(92, 202, 220),
            Color.FromArgb(222, 254, 255)),
        _ => new TierPalette(
            Color.FromArgb(64, 68, 71),
            Color.FromArgb(143, 149, 152),
            Color.FromArgb(225, 229, 230))
    };

    private sealed record TierPalette(Color Dark, Color Mid, Color Light);
}
