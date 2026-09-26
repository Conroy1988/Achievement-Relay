using System.Text.Json;

namespace AchievementRelay.Core.Services;

/// <summary>Separates JSON protocol records from native Steam diagnostic output.</summary>
public static class SteamBridgeProtocol
{
    public const int MaximumMessageCharacters = 8_000_000;

    public static T? ReadMessage<T>(string line, JsonSerializerOptions options) where T : class
    {
        if (line.Length > MaximumMessageCharacters)
        {
            throw new InvalidDataException("Steam helper output exceeded the 8,000,000-character limit.");
        }

        // The native Steam library shares stdout with our helper. It can write
        // diagnostic text there independently of Console.Error. Only object
        // records belong to our protocol; never treat text as unlock evidence
        // or restart the observer (and discard its live baseline) because of it.
        var content = line.AsSpan().TrimStart();
        if (content.IsEmpty || content[0] != '{')
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, options);
        }
        catch (JsonException)
        {
            // Deliberately exclude raw output, property values and exception
            // text: they can contain player identities and provider data.
            throw new InvalidDataException("Steam helper returned an invalid JSON record or field type.");
        }
    }
}
