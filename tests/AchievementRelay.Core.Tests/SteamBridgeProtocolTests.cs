using System.Text;
using System.Text.Json;
using AchievementRelay.Core.Services;

internal static class SteamBridgeProtocolTests
{
    public static void Run()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // Simulate interleaved native output and genuine records on the same
        // redirected pipe. Diagnostics never become events or reset the reader.
        var records = new[]
        {
            "[S_API] SteamAPI_Init(): Loaded local steamclient library.",
            "{\"type\":\"snapshot\",\"count\":0}",
            "Setting breakpad minidump AppID = 123",
            "Steam diagnostic: {not a protocol object}",
            " \t",
            " {\"type\":\"snapshot\",\"count\":1,\"name\":\"夜 — Réussi 🏆\",\"icon\":\"AQIDBA==\"}"
        };
        var observed = new List<Record>();
        foreach (var line in records)
        {
            var fromPipe = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(line));
            var record = SteamBridgeProtocol.ReadMessage<Record>(fromPipe, options);
            if (record is not null) observed.Add(record);
        }
        if (observed.Count != 2 || observed[0].Count != 0 || observed[1].Count != 1 ||
            observed[1].Name != "夜 — Réussi 🏆" || !observed[1].Icon!.SequenceEqual(new byte[] { 1, 2, 3, 4 }))
            throw new Exception("Native diagnostics interrupted valid Steam records or damaged Unicode/artwork.");

        foreach (var invalid in new[] { "{broken", "{\"count\":\"secret-value\"}", "{\"icon\":\"not-base64\"}" })
        {
            ExpectInvalid(invalid, "invalid JSON", options);
        }
        ExpectInvalid(new string('x', SteamBridgeProtocol.MaximumMessageCharacters + 1), "limit", options);
    }

    private static void ExpectInvalid(string line, string reason, JsonSerializerOptions options)
    {
        try { SteamBridgeProtocol.ReadMessage<Record>(line, options); }
        catch (InvalidDataException exception)
        {
            if (!exception.Message.Contains(reason, StringComparison.Ordinal) || exception.Message.Contains("secret-value"))
                throw new Exception("Protocol diagnostics did not safely distinguish the error.");
            return;
        }
        throw new Exception("Corrupt or oversized Steam data was accepted.");
    }

    private sealed record Record
    {
        public string? Type { get; init; }
        public int Count { get; init; }
        public string? Name { get; init; }
        public byte[]? Icon { get; init; }
    }
}
