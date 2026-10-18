using ArgoBooks.Core.Services;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace ArgoBooks.Desktop.Services;

/// <summary>
/// Downloads updates from argorobots.com like Velopack's own web source, then refuses any package whose Ed25519 signature does not match the release key.
/// </summary>
internal sealed class SignedWebSource : SimpleWebSource
{
    private readonly string _publicKey;

    public SignedWebSource(string baseUrl, string publicKey) : base(baseUrl)
    {
        _publicKey = publicKey;
    }

    /// <inheritdoc />
    public override async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
    {
        // A feed naming a package by full address could send the download anywhere, and every release names its packages relative to the feed.
        if (Uri.TryCreate(releaseEntry.FileName, UriKind.Absolute, out _))
            throw new UpdateSignatureException($"The update list points outside the update folder ({releaseEntry.FileName}).");

        await base.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken).ConfigureAwait(false);

        // Velopack installs its own updater from the package as soon as this returns, so the check has to happen here, before the file is renamed into place.
        var signatureUrl = SignatureUrl(releaseEntry.FileName);
        string signature;
        try
        {
            signature = await Downloader.DownloadString(signatureUrl, timeout: Timeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TryDelete(localFile);
            throw new UpdateSignatureException($"The update's signature could not be downloaded from {signatureUrl}: {ex.Message}", ex);
        }

        if (!UpdateSignature.IsValid(localFile, signature, _publicKey))
        {
            TryDelete(localFile);
            throw new UpdateSignatureException($"{releaseEntry.FileName} does not match its signature, so it was not installed.");
        }
    }

    /// <summary>
    /// Where the release script publishes a file's signature: beside it, with ".sig" added.
    /// </summary>
    public string SignatureUrl(string fileName) => new Uri(new Uri(BaseUri.ToString().TrimEnd('/') + "/"), fileName + ".sig").ToString();

    /// <summary>
    /// The address of a file in the update folder.
    /// </summary>
    public string FileUrl(string fileName) => new Uri(new Uri(BaseUri.ToString().TrimEnd('/') + "/"), fileName).ToString();

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
