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
        if (stream.Length > 1_000_000) throw new InvalidDataException("Shared delivery claim exceeded its bound.");
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        State = reader.ReadToEnd().Split('\n').LastOrDefault(value => value.Length > 0) ?? "";
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
        if (state is not ("pending" or "sending" or "delivered")) throw new ArgumentException("Unknown claim state.", nameof(state));
        // Append receipts: truncating a sent claim before writing 'delivered'
        // could make a crash look like an unused claim and permit a duplicate.
        var bytes = Encoding.UTF8.GetBytes(state + "\n");
        _stream.Position = _stream.Length;
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
        State = state;
    }
    public void Dispose() => _stream.Dispose();
}
