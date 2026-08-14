namespace AchievementRelay.Core.Models;

public sealed record RelayResult(bool Success, string Message, int? StatusCode = null)
{
    public static RelayResult Ok(string message = "Delivered") => new(true, message);

    public static RelayResult Fail(string message, int? statusCode = null) => new(false, message, statusCode);
}
