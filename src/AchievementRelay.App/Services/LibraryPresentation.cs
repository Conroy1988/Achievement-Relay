namespace AchievementRelay.App.Services;

/// <summary>Read-only collection projections; never evidence for delivery or live activity.</summary>
public static class LibraryPresentation
{
    public static bool HasVerifiedTotal(LibraryGame game) => game.Earned >= 0 && game.Total is > 0 && game.Total >= game.Earned;
    public static bool IsComplete(LibraryGame game) => HasVerifiedTotal(game) && game.Earned == game.Total;
    public static double Percentage(LibraryGame game) => HasVerifiedTotal(game) ? 100d * game.Earned / game.Total!.Value : 0;

    public static LibraryGame[] Filter(IEnumerable<LibraryGame> games, string search, string? provider, int completion, int sort)
    {
        var matching = games.Where(x => x.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            && (provider is null || x.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)))
            .Where(x => completion switch {
                1 => HasVerifiedTotal(x) && !IsComplete(x),
                2 => IsComplete(x),
                3 => !HasVerifiedTotal(x),
                _ => true
            });
        return (sort switch {
            1 => matching.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            2 => matching.OrderBy(x => HasVerifiedTotal(x) && !IsComplete(x) ? x.Total!.Value - x.Earned : int.MaxValue).ThenBy(x => x.Name),
            _ => matching.OrderByDescending(x => x.ObservedAt).ThenBy(x => x.Name)
        }).ToArray();
    }
}
