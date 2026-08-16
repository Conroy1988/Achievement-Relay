using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;

namespace AchievementRelay.SteamBridge;

internal static class Program
{
    private const int ProtocolVersion = 1;
    private const int MaximumSnapshotIconBytes = 1024 * 1024;
    private static readonly TimeSpan StatsRequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StatsRefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly object OutputGate = new object();
    private static volatile bool _stopRequested;

    public static int Main(string[] args)
    {
        if (args.Any(value => string.Equals(value, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            var passed = RunSelfTest();
            WriteMessage(new BridgeStatus { Status = "self-test", Message = passed ? "ok" : "failed" });
            return passed ? 0 : 10;
        }

        uint appId;
        int parentProcessId;
        if (!TryReadUInt32(args, "--app-id", out appId) || appId == 0 ||
            !TryReadInt32(args, "--parent-pid", out parentProcessId) || parentProcessId <= 0)
        {
            Console.Error.WriteLine("Usage: AchievementRelay.SteamBridge --app-id <id> --parent-pid <id>");
            return 2;
        }

        Task.Run(() =>
        {
            try
            {
                Console.In.ReadLine();
            }
            catch (IOException)
            {
                // Closing the redirected pipe is also a shutdown signal.
            }
            finally
            {
                _stopRequested = true;
            }
        });

        using (var statsReady = new ManualResetEventSlim(false))
        {
            var transitionGate = new object();
            var sessionTransitions = new HashSet<string>(StringComparer.Ordinal);
            var statsGeneration = 0;
            Action<SteamId, Result> statsReceived = (steamId, _) =>
            {
                // Valve reports Result.Fail when a user has no saved stats yet;
                // the callback itself is the readiness boundary. Restrict it
                // to the local user exactly as Facepunch's own readiness flag
                // does so another-user requests can never open this gate.
                if (steamId.Equals(SteamClient.SteamId))
                {
                    statsReady.Set();
                }
            };
            Action<SteamId> statsUnloaded = steamId =>
            {
                if (steamId.Equals(SteamClient.SteamId))
                {
                    statsReady.Reset();
                    Interlocked.Increment(ref statsGeneration);
                    lock (transitionGate)
                    {
                        sessionTransitions.Clear();
                    }
                }
            };
            Action<Achievement, int, int> achievementProgress = (achievement, currentProgress, maximumProgress) =>
            {
                // Steam documents 0/0 as the completed-achievement callback.
                // Unlike a timestamp or a newly visible schema entry, receipt
                // of this callback during this helper lifetime is direct proof
                // of a live store operation, including the launch-to-baseline
                // race before the first complete snapshot is ready.
                if (currentProgress == 0 && maximumProgress == 0 &&
                    !string.IsNullOrWhiteSpace(achievement.Identifier) &&
                    achievement.Identifier.Length <= 512)
                {
                    lock (transitionGate)
                    {
                        sessionTransitions.Add(achievement.Identifier);
                    }
                }
            };
            SteamUserStats.OnUserStatsReceived += statsReceived;
            SteamUserStats.OnUserStatsUnloaded += statsUnloaded;
            SteamUserStats.OnAchievementProgress += achievementProgress;

            try
            {
                SteamClient.Init(appId);
                WriteMessage(new BridgeStatus { Status = "connected", AppId = appId, Message = "Steamworks initialized." });
                if (!TryRequestLocalStats(statsReady, forceRequest: false, out var statsMessage))
                {
                    WriteMessage(new BridgeStatus
                    {
                        Status = "error",
                        AppId = appId,
                        Message = "Steam did not return the local player's achievement stats within 20 seconds. Achievement Relay will restart the observer automatically."
                    });
                    return 5;
                }

                WriteMessage(new BridgeStatus { Status = "stats-ready", AppId = appId, Message = statsMessage });
                return RunObservationLoop(
                    appId,
                    parentProcessId,
                    statsReady,
                    () => Volatile.Read(ref statsGeneration),
                    transitionGate,
                    sessionTransitions);
            }
            catch (Exception)
            {
                WriteMessage(new BridgeStatus
                {
                    Status = "error",
                    AppId = appId,
                    Message = "The local Steamworks client did not initialize. Keep Steam signed in and launch the game through Steam."
                });
                return 4;
            }
            finally
            {
                SteamUserStats.OnUserStatsReceived -= statsReceived;
                SteamUserStats.OnUserStatsUnloaded -= statsUnloaded;
                SteamUserStats.OnAchievementProgress -= achievementProgress;
                try
                {
                    SteamClient.Shutdown();
                }
                catch (Exception)
                {
                    // Process termination is the final isolation boundary.
                }
            }
        }
    }

    private static int RunObservationLoop(
        uint appId,
        int parentProcessId,
        ManualResetEventSlim statsReady,
        Func<int> getStatsGeneration,
        object transitionGate,
        HashSet<string> sessionTransitions)
    {
        var names = new string[0];
        Dictionary<string, bool>? previous = null;
        var lastHeartbeat = DateTimeOffset.MinValue;
        var lastSchemaRefresh = DateTimeOffset.MinValue;
        var stableSchemaReads = 0;
        var schemaReady = false;
        var statsWereReady = false;
        var lastStatsRefresh = DateTimeOffset.UtcNow;
        var observedStatsGeneration = getStatsGeneration();
        string? observedSteamId = null;

        while (!_stopRequested && IsParentAlive(parentProcessId))
        {
            var currentStatsGeneration = getStatsGeneration();
            if (currentStatsGeneration != observedStatsGeneration)
            {
                // Observe every unload even when a matching stats-received
                // callback follows between two polling ticks. Without this
                // generation boundary, a reload could be compared with the
                // prior in-memory account state and look like live progress.
                names = new string[0];
                previous = null;
                stableSchemaReads = 0;
                schemaReady = false;
                lastSchemaRefresh = DateTimeOffset.MinValue;
                statsWereReady = false;
                lastStatsRefresh = DateTimeOffset.UtcNow;
                observedSteamId = null;
                observedStatsGeneration = currentStatsGeneration;
            }

            if (!statsReady.IsSet)
            {
                // Never read achievement state before Steam confirms the
                // current user's stats. An early all-locked snapshot followed
                // by the real states would otherwise look like a backlog of
                // live unlocks. A stats unload also starts a fresh baseline.
                if (statsWereReady)
                {
                    names = new string[0];
                    previous = null;
                    stableSchemaReads = 0;
                    schemaReady = false;
                    lastSchemaRefresh = DateTimeOffset.MinValue;
                    statsWereReady = false;
                }

                if (!TryRequestLocalStats(statsReady, forceRequest: false, out var statsMessage))
                {
                    WriteMessage(new BridgeStatus
                    {
                        Status = "error",
                        AppId = appId,
                        Message = "Steam unloaded the local achievement stats and did not return a fresh copy within 20 seconds. Achievement Relay will restart the observer automatically."
                    });
                    return 5;
                }

                WriteMessage(new BridgeStatus { Status = "stats-ready", AppId = appId, Message = statsMessage });
                lastStatsRefresh = DateTimeOffset.UtcNow;
                continue;
            }

            var currentSteamId = SteamClient.SteamId.ToString();
            if (string.IsNullOrWhiteSpace(currentSteamId))
            {
                Thread.Sleep(250);
                continue;
            }

            if (observedSteamId != null &&
                !string.Equals(observedSteamId, currentSteamId, StringComparison.Ordinal))
            {
                // Defense in depth for a provider/account transition that did
                // not surface an unload callback. Discard all in-memory proof
                // and require a stable schema plus a fresh history baseline.
                names = new string[0];
                previous = null;
                stableSchemaReads = 0;
                schemaReady = false;
                lastSchemaRefresh = DateTimeOffset.MinValue;
                statsWereReady = false;
                lock (transitionGate)
                {
                    sessionTransitions.Clear();
                }
            }

            observedSteamId = currentSteamId;
            statsWereReady = true;
            if (previous is not null &&
                DateTimeOffset.UtcNow - lastStatsRefresh >= StatsRefreshInterval)
            {
                // RequestUserStats is explicitly documented as a snapshot that
                // is not updated automatically. Refresh it on a restrained
                // cadence so changes made by the actual game process (or a
                // separate local Steamworks process) become observable here.
                if (!TryRequestLocalStats(statsReady, forceRequest: true, out _))
                {
                    WriteMessage(new BridgeStatus
                    {
                        Status = "error",
                        AppId = appId,
                        Message = "Steam did not refresh the local player's achievement stats within 20 seconds. Achievement Relay will restart the observer automatically."
                    });
                    return 5;
                }

                lastStatsRefresh = DateTimeOffset.UtcNow;
            }

            if (DateTimeOffset.UtcNow - lastSchemaRefresh >= TimeSpan.FromSeconds(3))
            {
                lastSchemaRefresh = DateTimeOffset.UtcNow;
                try
                {
                    var discovered = SteamUserStats.Achievements
                        .Select(item => item.Identifier)
                        .Where(item => !string.IsNullOrWhiteSpace(item) && item.Length <= 512)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(item => item, StringComparer.Ordinal)
                        .ToArray();
                    if (discovered.Length > 0)
                    {
                        if (names.Length > 0 && discovered.SequenceEqual(names, StringComparer.Ordinal))
                        {
                            stableSchemaReads++;
                        }
                        else
                        {
                            names = discovered;
                            stableSchemaReads = 1;
                        }

                        schemaReady = schemaReady || stableSchemaReads >= 3;
                    }
                }
                catch (Exception)
                {
                    // User stats can take a moment to become available after launch.
                    // Never emit an empty snapshot that could later look like unlocks.
                }
            }

            if (!schemaReady || names.Length == 0)
            {
                Thread.Sleep(500);
                continue;
            }

            var observations = new List<BridgeAchievement>(names.Length);
            var complete = true;
            var localPlayer = new Friend(SteamClient.SteamId);
            foreach (var apiName in names)
            {
                try
                {
                    var achievement = new Achievement(apiName);
                    // The current-user accessor can reflect a same-App-ID
                    // change immediately. The Friend accessor reads the
                    // explicitly refreshed user snapshot. Their union covers
                    // both Steam client behaviours without ever mutating it.
                    var unlocked = achievement.State || localPlayer.GetAchievement(apiName, false);
                    var item = new BridgeAchievement
                    {
                        ApiName = apiName,
                        Name = LimitText(string.IsNullOrWhiteSpace(achievement.Name) ? apiName : achievement.Name, 512) ?? apiName,
                        Description = LimitText(achievement.Description, 4096),
                        IsUnlocked = unlocked,
                        UnlockedAt = ToIsoTimestamp(localPlayer.GetAchievementUnlockTime(apiName))
                            ?? ToIsoTimestamp(achievement.UnlockTime)
                    };
                    observations.Add(item);
                }
                catch (Exception)
                {
                    complete = false;
                }
            }

            if (!statsReady.IsSet ||
                getStatsGeneration() != currentStatsGeneration ||
                !string.Equals(SteamClient.SteamId.ToString(), currentSteamId, StringComparison.Ordinal))
            {
                // Seqlock-style validation: an unload/account change during
                // enumeration invalidates the whole snapshot. The next tick
                // sees the generation/account boundary and rebuilds a silent
                // baseline instead of publishing mixed-session state.
                continue;
            }

            var current = observations.ToDictionary(item => item.ApiName, item => item.IsUnlocked, StringComparer.Ordinal);
            var newlyUnlocked = previous == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(current
                    // A newly visible schema identity is history, not a live
                    // transition. It must have been observed locked in this
                    // helper session before an unlocked state can be signaled.
                    .Where(item => item.Value &&
                                   previous.TryGetValue(item.Key, out var wasUnlocked) &&
                                   !wasUnlocked)
                    .Select(item => item.Key), StringComparer.Ordinal);
            lock (transitionGate)
            {
                // Keep every directly proven transition for this helper
                // lifetime and repeat it on complete heartbeats. The main app
                // durably deduplicates identities; repetition means a local
                // state-write failure cannot silently strand an observation.
                foreach (var apiName in newlyUnlocked)
                {
                    sessionTransitions.Add(apiName);
                }

                foreach (var apiName in sessionTransitions)
                {
                    if (current.TryGetValue(apiName, out var isUnlocked) && isUnlocked)
                    {
                        newlyUnlocked.Add(apiName);
                    }
                }
            }
            var changed = previous == null ||
                          current.Count != previous.Count ||
                          current.Any(item => !previous.TryGetValue(item.Key, out var oldValue) || oldValue != item.Value);
            var heartbeatDue = DateTimeOffset.UtcNow - lastHeartbeat >= TimeSpan.FromSeconds(15);

            if (complete && (changed || heartbeatDue))
            {
                var attachedIconBytes = 0;
                foreach (var item in observations.Where(item =>
                             changed && newlyUnlocked.Contains(item.ApiName)))
                {
                    TryAttachIcon(item);
                    if (item.IconRgba != null &&
                        attachedIconBytes + item.IconByteCount <= MaximumSnapshotIconBytes)
                    {
                        attachedIconBytes += item.IconByteCount;
                    }
                    else
                    {
                        item.IconRgba = null;
                        item.IconByteCount = 0;
                        item.IconWidth = 0;
                        item.IconHeight = 0;
                    }
                }

                WriteMessage(new BridgeSnapshot
                {
                    AppId = appId,
                    SteamId = currentSteamId,
                    PlayerName = LimitText(SteamClient.Name, 128),
                    ObservedAt = DateTimeOffset.UtcNow.ToString("O"),
                    TotalAchievements = names.Length,
                    Complete = observations.Count == names.Length,
                    InitialSnapshot = previous == null,
                    TransitionedApiNames = newlyUnlocked.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    Achievements = observations.ToArray()
                });
                lastHeartbeat = DateTimeOffset.UtcNow;
                previous = current;
            }

            Thread.Sleep(500);
        }

        return 0;
    }

    private static bool TryRequestLocalStats(
        ManualResetEventSlim statsReady,
        bool forceRequest,
        out string message)
    {
        message = string.Empty;
        try
        {
            if (!forceRequest && statsReady.IsSet)
            {
                message = "Steam supplied the local player's achievement stats during initialization.";
                return true;
            }

            var steamId = SteamClient.SteamId;
            if (steamId.Equals(default(SteamId)))
            {
                return false;
            }

            // Facepunch 2.5.2 intentionally makes RequestCurrentStats a no-op
            // because Steam normally hydrates a game before its process starts.
            // Achievement Relay is a separate helper launched after the game,
            // so explicitly request the signed-in local user's current record.
            // Completion itself is the safe readiness boundary: Valve can
            // return Result.Fail for a player with no saved stats yet.
            var request = new Friend(steamId).RequestUserStatsAsync();
            if (!CompleteStatsRequest(request, statsReady, StatsRequestTimeout, out var storedStatsFound))
            {
                return false;
            }

            message = storedStatsFound
                ? "The local player's achievement stats are ready."
                : "Steam returned no stored player stats; the achievement schema is ready for a silent baseline.";
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool CompleteStatsRequest(
        Task<bool> request,
        ManualResetEventSlim statsReady,
        TimeSpan timeout,
        out bool storedStatsFound)
    {
        storedStatsFound = false;
        try
        {
            if (!request.Wait(timeout))
            {
                return false;
            }

            storedStatsFound = request.GetAwaiter().GetResult();
            statsReady.Set();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool RunSelfTest()
    {
        using (var ready = new ManualResetEventSlim(false))
        {
            if (!CompleteStatsRequest(Task.FromResult(true), ready, TimeSpan.FromSeconds(1), out var stored) ||
                !stored || !ready.IsSet)
            {
                return false;
            }
        }

        using (var ready = new ManualResetEventSlim(false))
        {
            // Result.Fail is how Steam represents a brand-new player with no
            // stored stats. A completed response must still open the baseline.
            if (!CompleteStatsRequest(Task.FromResult(false), ready, TimeSpan.FromSeconds(1), out var stored) ||
                stored || !ready.IsSet)
            {
                return false;
            }
        }

        using (var ready = new ManualResetEventSlim(false))
        {
            var neverCompletes = new TaskCompletionSource<bool>();
            if (CompleteStatsRequest(neverCompletes.Task, ready, TimeSpan.Zero, out _) || ready.IsSet)
            {
                return false;
            }
        }

        // DataContractJsonSerializer encodes byte arrays as numeric JSON
        // collections, while the main app's System.Text.Json receiver expects
        // byte[] values as Base64 strings. Exercise the exact helper wire
        // representation so an unlock with artwork cannot break the protocol.
        var iconJson = SerializeMessage(new BridgeAchievement
        {
            ApiName = "icon-probe",
            Name = "Icon probe",
            IconRgba = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            IconByteCount = 4,
            IconWidth = 1,
            IconHeight = 1
        });
        if (iconJson.IndexOf("\"iconRgba\":\"AQIDBA==\"", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        return true;
    }

    private static void TryAttachIcon(BridgeAchievement target)
    {
        try
        {
            var icon = new Achievement(target.ApiName).GetIconAsync(3000).GetAwaiter().GetResult();
            if (!icon.HasValue || icon.Value.Data == null ||
                icon.Value.Width == 0 || icon.Value.Height == 0 ||
                icon.Value.Width > 512 || icon.Value.Height > 512 ||
                icon.Value.Data.Length != checked((int)(icon.Value.Width * icon.Value.Height * 4)))
            {
                return;
            }

            target.IconRgba = Convert.ToBase64String(icon.Value.Data);
            target.IconByteCount = icon.Value.Data.Length;
            target.IconWidth = checked((int)icon.Value.Width);
            target.IconHeight = checked((int)icon.Value.Height);
        }
        catch (Exception)
        {
            // Icons are optional; an unlock must never be lost because artwork is late.
        }
    }

    private static bool IsParentAlive(int processId)
    {
        try
        {
            using (var process = Process.GetProcessById(processId))
            {
                return !process.HasExited;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string? ToIsoTimestamp(DateTime? value)
    {
        if (!value.HasValue || value.Value.Year < 2000)
        {
            return null;
        }

        var utc = value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
        return new DateTimeOffset(utc, TimeSpan.Zero).ToString("O");
    }

    private static void WriteMessage<T>(T value)
    {
        var json = SerializeMessage(value);
        lock (OutputGate)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    private static string SerializeMessage<T>(T value)
    {
        var serializer = new DataContractJsonSerializer(typeof(T));
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static bool TryReadUInt32(string[] args, string name, out uint value)
    {
        value = 0;
        var index = Array.FindIndex(args, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && uint.TryParse(args[index + 1], out value);
    }

    private static bool TryReadInt32(string[] args, string name, out int value)
    {
        value = 0;
        var index = Array.FindIndex(args, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out value);
    }

    private static string? LimitText(string? value, int maximumLength)
    {
        if (value is null || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized.Substring(0, maximumLength);
    }

    [DataContract]
    private sealed class BridgeStatus
    {
        [DataMember(Name = "protocolVersion", Order = 0)] public int ProtocolVersion = Program.ProtocolVersion;
        [DataMember(Name = "type", Order = 1)] public string Type = "status";
        [DataMember(Name = "status", Order = 2)] public string Status = string.Empty;
        [DataMember(Name = "appId", Order = 3, EmitDefaultValue = false)] public uint AppId;
        [DataMember(Name = "message", Order = 4, EmitDefaultValue = false)] public string? Message;
    }

    [DataContract]
    private sealed class BridgeSnapshot
    {
        [DataMember(Name = "protocolVersion", Order = 0)] public int ProtocolVersion = Program.ProtocolVersion;
        [DataMember(Name = "type", Order = 1)] public string Type = "snapshot";
        [DataMember(Name = "appId", Order = 2)] public uint AppId;
        [DataMember(Name = "steamId", Order = 3)] public string SteamId = string.Empty;
        [DataMember(Name = "playerName", Order = 4)] public string? PlayerName;
        [DataMember(Name = "observedAt", Order = 5)] public string ObservedAt = string.Empty;
        [DataMember(Name = "totalAchievements", Order = 6)] public int TotalAchievements;
        [DataMember(Name = "complete", Order = 7)] public bool Complete;
        [DataMember(Name = "initialSnapshot", Order = 8)] public bool InitialSnapshot;
        [DataMember(Name = "transitionedApiNames", Order = 9)] public string[] TransitionedApiNames = new string[0];
        [DataMember(Name = "achievements", Order = 10)] public BridgeAchievement[] Achievements = new BridgeAchievement[0];
    }

    [DataContract]
    private sealed class BridgeAchievement
    {
        [DataMember(Name = "apiName", Order = 0)] public string ApiName = string.Empty;
        [DataMember(Name = "name", Order = 1)] public string Name = string.Empty;
        [DataMember(Name = "description", Order = 2, EmitDefaultValue = false)] public string? Description;
        [DataMember(Name = "isUnlocked", Order = 3)] public bool IsUnlocked;
        [DataMember(Name = "unlockedAt", Order = 4, EmitDefaultValue = false)] public string? UnlockedAt;
        [DataMember(Name = "iconRgba", Order = 5, EmitDefaultValue = false)] public string? IconRgba;
        [DataMember(Name = "iconWidth", Order = 6, EmitDefaultValue = false)] public int IconWidth;
        [DataMember(Name = "iconHeight", Order = 7, EmitDefaultValue = false)] public int IconHeight;
        [IgnoreDataMember] public int IconByteCount;
    }
}
