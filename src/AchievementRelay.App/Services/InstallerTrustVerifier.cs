using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AchievementRelay.App.Services;

internal static class InstallerTrustVerifier
{
    private const string PublisherMetadataName = "AchievementRelay.UpdatePublisherCertificateSha256";
    private static readonly Guid GenericVerifyV2Action = new(
        "00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static IReadOnlySet<string> ReadPinnedPublisherCertificates()
    {
        var value = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, PublisherMetadataName, StringComparison.Ordinal))?
            .Value;

        return (value ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(item => item.Length == 64 && item.All(Uri.IsHexDigit))
            .Select(item => item.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    public static InstallerTrustResult Verify(
        string path,
        IReadOnlySet<string> pinnedPublisherCertificates)
    {
        if (!OperatingSystem.IsWindows())
        {
            return InstallerTrustResult.Failure(
                "Installer trust can only be verified by Windows.");
        }

        if (pinnedPublisherCertificates.Count == 0)
        {
            return InstallerTrustResult.Failure(
                "This build does not contain a trusted update-publisher identity.");
        }

        if (!File.Exists(path))
        {
            return InstallerTrustResult.Failure("The downloaded installer is missing.");
        }

        var trustStatus = VerifyAuthenticode(path);
        if (trustStatus != 0)
        {
            return InstallerTrustResult.Failure(
                $"Windows rejected the installer signature (0x{trustStatus:X8}).");
        }

        try
        {
            // .NET has no X509CertificateLoader API that extracts the signer from an
            // Authenticode-signed PE file; use the dedicated signed-file API only for
            // extraction, then immediately reload the DER certificate through the loader.
#pragma warning disable SYSLIB0057
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            using var certificate = X509CertificateLoader.LoadCertificate(
                signer.Export(X509ContentType.Cert));
            var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData))
                .ToLowerInvariant();
            return pinnedPublisherCertificates.Contains(fingerprint)
                ? InstallerTrustResult.Success(fingerprint)
                : InstallerTrustResult.Failure(
                    "The installer was signed by an unexpected publisher certificate.");
        }
        catch (CryptographicException)
        {
            return InstallerTrustResult.Failure(
                "Windows could not read the installer publisher certificate.");
        }
    }

    private static int VerifyAuthenticode(string path)
    {
        var filePathPointer = IntPtr.Zero;
        var fileInfoPointer = IntPtr.Zero;
        var trustDataPointer = IntPtr.Zero;
        try
        {
            filePathPointer = Marshal.StringToCoTaskMemUni(path);
            var fileInfo = new WinTrustFileInfo
            {
                StructureSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePathPointer
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            var trustData = new WinTrustData
            {
                StructureSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UserInterfaceChoice = WinTrustDataUiChoice.None,
                RevocationChecks = WinTrustDataRevocationChecks.WholeChain,
                UnionChoice = WinTrustDataUnionChoice.File,
                FileInfo = fileInfoPointer,
                StateAction = WinTrustDataStateAction.Ignore,
                ProviderFlags = WinTrustDataProviderFlags.RevocationCheckChainExcludeRoot
            };
            trustDataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPointer, false);

            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2Action, trustDataPointer);
        }
        finally
        {
            if (trustDataPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(trustDataPointer);
            }

            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }

            if (filePathPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(filePathPointer);
            }
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructureSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructureSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public WinTrustDataUiChoice UserInterfaceChoice;
        public WinTrustDataRevocationChecks RevocationChecks;
        public WinTrustDataUnionChoice UnionChoice;
        public IntPtr FileInfo;
        public WinTrustDataStateAction StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public WinTrustDataProviderFlags ProviderFlags;
        public WinTrustDataUiContext UiContext;
    }

    private enum WinTrustDataUiChoice : uint
    {
        None = 2
    }

    private enum WinTrustDataRevocationChecks : uint
    {
        WholeChain = 1
    }

    private enum WinTrustDataUnionChoice : uint
    {
        File = 1
    }

    private enum WinTrustDataStateAction : uint
    {
        Ignore = 0
    }

    [Flags]
    private enum WinTrustDataProviderFlags : uint
    {
        RevocationCheckChainExcludeRoot = 0x00000080
    }

    private enum WinTrustDataUiContext : uint
    {
        Execute = 0
    }
}

internal sealed record InstallerTrustResult(bool IsTrusted, string Message, string? CertificateSha256)
{
    public static InstallerTrustResult Success(string certificateSha256) =>
        new(true, "The installer publisher is trusted.", certificateSha256);

    public static InstallerTrustResult Failure(string message) =>
        new(false, message, null);
}
