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
    public const int CardHeight = 750;
    public const int ArtworkShowcaseWidth = 1200;
    public const int ArtworkShowcaseHeight = 660;
    public const float AchievementTitleMaximumFontSize = 88;
    public const float AchievementTitleMinimumFontSize = 46;
    public const float AchievementDescriptionFontSize = 32;
    public const float RarityPercentageMaximumFontSize = 28;
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

    public DiscordCollectorCard RenderSteamPreview() => Render(
        new AchievementEvent
        {
            Id = "steam-poster-preview", Name = "Rejected for Probing",
            Description = "Throw something into something else with the saucer’s Abducto Beam.",
            GameName = "Destroy All Humans!", PlayerName = "Relay Player",
            SourceProvider = "Steam", Platform = "Steam", RarityPercentage = 75.2,
            RarityKnown = true
        },
        new AppSettings { DisplayName = "Relay Player", IncludeRawDetailsWhenUncertain = true },
        new AchievementCardArtwork(CreatePreviewArtwork(), null));

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
        var tier = RelayRarityClassifier.Classify(achievement.RarityPercentage);
        var palette = GetTierPalette(tier);
        DrawHeader(graphics, achievement, palette);
        DrawAchievementDetails(graphics, achievement, settings, palette);
        DrawPosterFooter(graphics, achievement, settings, achievementIcon, tier, palette);
        if (ambientArtwork is null)
        {
            DrawFallbackArtwork(graphics, hero ?? achievementIcon, palette);
        }

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
        graphics.Clear(Color.FromArgb(9, 10, 14));
        if (artwork is not null)
        {
            // Keep the provider artwork sharp and dominant. Text contrast comes
            // from the gradient, never from blurring the entire game image.
            DrawImageCover(graphics, artwork, new RectangleF(0, 0, CardWidth, ArtworkShowcaseHeight));
        }
        else
        {
            DrawFallbackPattern(graphics, brand);
        }

        using var wash = new LinearGradientBrush(
            new Rectangle(0, 0, CardWidth, 660), Color.Transparent, Color.Black,
            LinearGradientMode.Vertical)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(65, 6, 7, 10), Color.FromArgb(0, 6, 7, 10),
                    Color.FromArgb(90, 6, 7, 10), Color.FromArgb(65, 6, 7, 10), Color.FromArgb(110, 6, 7, 10)],
                Positions = [0f, 0.24f, 0.46f, 0.73f, 1f]
            }
        };
        graphics.FillRectangle(wash, 0, 0, CardWidth, 660);
        // A feathered vertical mask avoids a visible horizontal panel edge.
        for (var y = 280; y < 660; y++)
        {
            var opacity = Math.Clamp((y - 280) / 70f, 0f, 1f);
            using var row = new LinearGradientBrush(new Rectangle(0, y, 1120, 1),
                Color.FromArgb((int)(240 * opacity), 6, 7, 10), Color.Transparent, LinearGradientMode.Horizontal);
            graphics.FillRectangle(row, 0, y, 1120, 1);
        }
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

    private static void DrawBrandBars(Graphics graphics, float x, float y, float scale = 1f)
    {
        using var red = new SolidBrush(Color.FromArgb(244, 46, 60));
        for (var i = 0; i < 2; i++)
        {
            var left = x + i * 23 * scale;
            graphics.FillPolygon(red, [new PointF(left + 12 * scale, y),
                new PointF(left + 25 * scale, y), new PointF(left + 13 * scale, y + 34 * scale),
                new PointF(left, y + 34 * scale)]);
        }
    }

    private static void DrawHeader(Graphics graphics, AchievementEvent achievement, TierPalette palette)
    {
        DrawBrandBars(graphics, 42, 37);
        using var labelFont = CreateFont(23, FontStyle.Bold);
        using var white = new SolidBrush(Color.FromArgb(248, 245, 239));
        var completion = achievement.IsGameCompletion && achievement.VerifiedAchievementTotal > 0;
        graphics.DrawString(completion ? "100% COMPLETE" : "ACHIEVEMENT UNLOCKED",
            labelFont, white, new PointF(104, 39));

        var platform = LimitText(ResolvePlatform(achievement).ToUpperInvariant(), 32);
        using var platformFont = CreateFont(23, FontStyle.Bold);
        var width = Math.Clamp(graphics.MeasureString(platform, platformFont).Width + 42, 124, 440);
        var pill = new RectangleF(CardWidth - width - 42, 29, width, 50);
        using var path = CreateRoundedRectangle(pill, 16);
        using var dark = new SolidBrush(Color.FromArgb(200, 8, 11, 13));
        using var border = new Pen(Color.FromArgb(95, 245, 242, 236), 1);
        using var format = CreateCenteredFormat();
        graphics.FillPath(dark, path);
        graphics.DrawPath(border, path);
        graphics.DrawString(platform, platformFont, white, pill, format);
    }

    private static void DrawAchievementDetails(
        Graphics graphics, AchievementEvent achievement, AppSettings settings, TierPalette palette)
    {
        using var gameFont = CreateFont(28, FontStyle.Bold);
        using var gameBrush = new SolidBrush(Color.FromArgb(225, 222, 213));
        using var singleLine = CreateSingleLineFormat();
        graphics.DrawString(Sanitize(achievement.GameName, "Unknown game").ToUpperInvariant(),
            gameFont, gameBrush, new RectangleF(42, 326, 1116, 43), singleLine);

        DrawFittedTitle(graphics, Sanitize(achievement.Name, "Achievement unlocked"),
            new RectangleF(36, 368, 760, 196), Color.FromArgb(255, 248, 245, 239));

        if (settings.IncludeRawDetailsWhenUncertain && !string.IsNullOrWhiteSpace(achievement.Description))
        {
            using var font = CreateFont(AchievementDescriptionFontSize, FontStyle.Regular, condensed: false);
            using var brush = new SolidBrush(Color.FromArgb(222, 223, 225));
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit };
            graphics.DrawString(Sanitize(achievement.Description, string.Empty), font, brush,
                new RectangleF(42, 566, 800, 90), format);
        }
    }

    private static void DrawPosterFooter(Graphics graphics, AchievementEvent achievement,
        AppSettings settings, Image? icon, RelayRarityTier tier, TierPalette palette)
    {
        using var background = new SolidBrush(Color.FromArgb(6, 7, 10));
        graphics.FillRectangle(background, 0, 660, CardWidth, 90);
        using var red = new Pen(Color.FromArgb(180, 229, 39, 53), 2);
        graphics.DrawLine(red, 42, 660, 1158, 660);
        var iconBounds = new RectangleF(42, 682, 46, 46);
        using var circle = new GraphicsPath();
        circle.AddEllipse(iconBounds);
        using var iconBack = new SolidBrush(Color.FromArgb(30, 32, 38));
        graphics.FillPath(iconBack, circle);
        if (icon is not null)
        {
            var state = graphics.Save();
            graphics.SetClip(circle);
            DrawImageCover(graphics, icon, iconBounds);
            graphics.Restore(state);
        }
        else
        {
            DrawBrandBars(graphics, 52, 695, 0.5f);
        }
        using var font = CreateFont(28, FontStyle.Bold);
        using var white = new SolidBrush(Color.FromArgb(238, 236, 230));
        using var format = CreateSingleLineFormat();
        var player = string.IsNullOrWhiteSpace(settings.DisplayName) ? achievement.PlayerName : settings.DisplayName;
        graphics.DrawString(Sanitize(player, "Player"), font, white, new RectangleF(102, 678, 280, 54), format);
        if (achievement.Gamerscore is { } score)
        {
            graphics.DrawString($"+{score}G", font, white, new RectangleF(392, 678, 142, 54), format);
        }

        // Existing tier artwork uses a 140px coordinate system. Scale the whole
        // drawing so its internal geometry stays correct at medallion size.
        var emblemState = graphics.Save();
        graphics.TranslateTransform(588, 681);
        graphics.ScaleTransform(0.34f, 0.34f);
        DrawTierEmblem(graphics, new RectangleF(0, 0, 140, 140), tier, palette);
        graphics.Restore(emblemState);
        var population = string.Equals(achievement.SourceProvider, "Steam", StringComparison.OrdinalIgnoreCase)
            ? "Steam players" : "players";
        var rarity = tier == RelayRarityTier.Unranked ? "Rarity unavailable"
            : $"{RelayRarityClassifier.FormatPercentage(achievement.RarityPercentage)} of {population}";
        using var rarityFont = CreateFont(RarityPercentageMaximumFontSize, FontStyle.Regular, condensed: false);
        graphics.DrawString(rarity, rarityFont, white, new RectangleF(650, 678, 454, 54), format);
        DrawBrandBars(graphics, 1114, 693, 0.7f);
    }

    private static void DrawFallbackArtwork(Graphics graphics, Image? artwork, TierPalette palette)
    {
        // A tiny/square icon remains an honest contained asset, never a stretched
        // poster. The full layout is still usable when provider art is absent.
        var bounds = new RectangleF(894, 112, 214, 198);
        if (artwork is not null)
            DrawImageContain(graphics, artwork, bounds, MaximumSafeUpscale(artwork));
        else
            DrawFallbackTrophy(graphics, bounds, palette);
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

    private static void DrawFittedTitle(Graphics graphics, string text, RectangleF bounds, Color color) =>
        DrawFittedText(
            graphics,
            text,
            bounds,
            AchievementTitleMaximumFontSize,
            AchievementTitleMinimumFontSize,
            color,
            centered: false);

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
            LineAlignment = StringAlignment.Near,
            Trimming = centered ? StringTrimming.EllipsisCharacter : StringTrimming.EllipsisWord,
            FormatFlags = centered ? StringFormatFlags.NoWrap : StringFormatFlags.LineLimit
        };

        for (var size = maximumSize; size >= minimumSize; size -= 2)
        {
            using var font = new Font("Arial Black", size, FontStyle.Bold, GraphicsUnit.Pixel);
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
