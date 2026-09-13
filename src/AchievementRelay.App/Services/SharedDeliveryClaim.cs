using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AchievementRelay.App.Services;

/// <summary>Exclusive SMB file claim; sync clients cannot provide this guarantee.</summary>
public sealed class SharedDeliveryClaim : IDisposable
{
    private readonly FileStream _stream;
    public string State { get; private set; }
    private SharedDeliveryClaim(FileStream stream)
    {
        _stream = stream;
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        State = reader.ReadToEnd();
        if (State is not ("" or "pending" or "sending" or "delivered"))
            throw new InvalidDataException("Shared delivery claim is invalid.");
    }
    public static SharedDeliveryClaim Acquire(string folder, string eventId, Uri webhook)
    {
        if (!folder.StartsWith(@"\\", StringComparison.Ordinal) || folder.StartsWith(@"\\?", StringComparison.Ordinal) ||
            folder.StartsWith(@"\\.", StringComparison.Ordinal))
            throw new IOException("Use a shared Windows network folder (UNC), not a cloud-synced folder.");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(webhook.AbsoluteUri + "\n" + eventId)));
        var stream = new FileStream(Path.Combine(folder, key + ".relay-claim"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
        try { return new SharedDeliveryClaim(stream); }
        catch { stream.Dispose(); throw; }
    }
    public void SetState(string state)
    {
        var bytes = Encoding.UTF8.GetBytes(state);
        _stream.Position = 0;
        _stream.SetLength(0);
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
        State = state;
    }
    public void Dispose() => _stream.Dispose();
}
