using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Checks the Ed25519 signature the release script writes beside every update file, so a file swapped on the server is refused even though it came from the right address.
/// </summary>
public static class UpdateSignature
{
    /// <summary>
    /// True when <paramref name="signatureBase64"/> is a valid Ed25519 signature of the file's exact bytes under <paramref name="publicKeyBase64"/>.
    /// </summary>
    public static bool IsValid(string filePath, string signatureBase64, string publicKeyBase64)
    {
        byte[] signature;
        byte[] publicKey;
        try
        {
            signature = Convert.FromBase64String(signatureBase64.Trim());
            publicKey = Convert.FromBase64String(publicKeyBase64.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (signature.Length != 64 || publicKey.Length != 32)
            return false;

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));

        using var stream = File.OpenRead(filePath);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            verifier.BlockUpdate(buffer, 0, read);

        return verifier.VerifySignature(signature);
    }
}

/// <summary>
/// An update refused because its signature could not be checked or did not match. The message names the file, for the log.
/// </summary>
public sealed class UpdateSignatureException : Exception
{
    /// <summary>What the user is told, since the file names in the message mean nothing to them.</summary>
    public const string UserMessage = "This update could not be verified, so it was not installed. Please try again later.";

    public UpdateSignatureException(string message, Exception? inner = null) : base(message, inner) { }
}
