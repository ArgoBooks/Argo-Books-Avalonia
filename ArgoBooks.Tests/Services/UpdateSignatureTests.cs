using ArgoBooks.Core.Services;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for UpdateSignature, which refuses an update file whose Ed25519 signature does not match.
/// </summary>
public class UpdateSignatureTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"argo-update-signature-{Guid.NewGuid():N}.bin");
    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly string _publicKey;

    public UpdateSignatureTests()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        _privateKey = (Ed25519PrivateKeyParameters)pair.Private;
        _publicKey = Convert.ToBase64String(((Ed25519PublicKeyParameters)pair.Public).GetEncoded());

        // Larger than one read buffer, so the file is checked across several reads.
        var bytes = new byte[200_000];
        new Random(42).NextBytes(bytes);
        File.WriteAllBytes(_file, bytes);
    }

    public void Dispose()
    {
        try { File.Delete(_file); } catch (IOException) { }
    }

    private string Sign(byte[] data)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        signer.BlockUpdate(data, 0, data.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    [Fact]
    public void IsValid_AcceptsTheSignatureOfTheFile()
    {
        var signature = Sign(File.ReadAllBytes(_file));

        Assert.True(UpdateSignature.IsValid(_file, signature, _publicKey));
    }

    [Fact]
    public void IsValid_RefusesAFileChangedAfterSigning()
    {
        var signature = Sign(File.ReadAllBytes(_file));
        using (var stream = new FileStream(_file, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Seek(150_000, SeekOrigin.Begin);
            stream.WriteByte(0x5A);
        }

        Assert.False(UpdateSignature.IsValid(_file, signature, _publicKey));
    }

    [Fact]
    public void IsValid_RefusesASignatureMadeWithAnotherKey()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var otherKey = Convert.ToBase64String(((Ed25519PublicKeyParameters)generator.GenerateKeyPair().Public).GetEncoded());

        Assert.False(UpdateSignature.IsValid(_file, Sign(File.ReadAllBytes(_file)), otherKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void IsValid_RefusesASignatureThatIsNotOne(string signature)
    {
        Assert.False(UpdateSignature.IsValid(_file, signature, _publicKey));
    }
}
