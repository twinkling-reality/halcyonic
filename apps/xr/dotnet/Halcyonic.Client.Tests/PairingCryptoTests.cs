using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The device's side of pairing (ADR 0017): SRP-6a against RFC 5054's own test vectors, and the
/// whole exchange against the vectors the control plane computed (fixtures/pairing/vectors.json).
/// </summary>
public class PairingCryptoTests
{
    private static BigInteger Hex(string text) =>
        BigInteger.Parse("0" + text.Replace(" ", string.Empty), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static byte[] Bytes(string text) => PairingCrypto.FromHex(text.Replace(" ", string.Empty).ToLowerInvariant());

    /// <summary>RFC 5054 Appendix A, the 1024-bit group, which Appendix B's vectors use with SHA-1.</summary>
    private static readonly SrpGroup RfcGroup = new(
        "EEAF0AB9 ADB38DD6 9C33F80A FA8FC5E8 60726187 75FF3C0B 9EA2314C"
        + "9C256576 D674DF74 96EA81D3 383B4813 D692C6E0 E0D5D8E2 50B98BE4"
        + "8E495C1D 6089DAD1 5DC7D7B4 6154D6B6 CE8EF4AD 69B15D49 82559B29"
        + "7BCF1885 C529F566 660E57EC 68EDBC3C 05726CC0 2FD4CBF4 976EAA9A"
        + "FD5138FE 8376435B 9FC61D2F C0EB06E3",
        2,
        HashAlgorithmName.SHA1);

    [Test]
    public void ReproducesRfc5054TestVectors()
    {
        var identity = Encoding.UTF8.GetBytes("alice");
        var password = Encoding.UTF8.GetBytes("password123");
        var salt = Bytes("BEB25379 D1A8581E B5A72767 3A2441EE");
        var a = Hex("60975527 035CF2AD 1989806F 0407210B C81EDC04 E2762A56 AFD529DD DA2D4393");
        var b = Hex("E487CB59 D31AC550 471E81F0 0F6928E0 1DDA08E9 74A004F4 9E61F5D1 05284D20");
        var expectedB = Hex(
            "BD0C6151 2C692C0C B6D041FA 01BB152D 4916A1E7 7AF46AE1 05393011 BAF38964 DC46A067 0DD125B9"
            + "5A981652 236F99D9 B681CBF8 7837EC99 6C6DA044 53728610 D0C6DDB5 8B318885 D7D82C7F 8DEB75CE"
            + "7BD4FBAA 37089E6F 9C6059F3 88838E7A 00030B33 1EB76840 910440B1 B27AAEAE EB4012B7 D7665238"
            + "A8E3FB00 4B117B58");

        Assert.That(Srp6a.Multiplier(RfcGroup), Is.EqualTo(Hex("7556AA04 5AEF2CDD 07ABAF0F 665C3E81 8913186F")));
        var x = Srp6a.PrivateKey(RfcGroup, identity, password, salt);
        Assert.That(x, Is.EqualTo(Hex("94B7555A ABE9127C C58CCF49 93DB6CF8 4D16C124")));
        var v = BigInteger.ModPow(RfcGroup.G, x, RfcGroup.N);
        Assert.That(v, Is.EqualTo(Hex(
            "7E273DE8 696FFC4F 4E337D05 B4B375BE B0DDE156 9E8FA00A 9886D812 9BADA1F1 822223CA 1A605B53"
            + "0E379BA4 729FDC59 F105B478 7E5186F5 C671085A 1447B52A 48CF1970 B4FB6F84 00BBF4CE BFBB1681"
            + "52E08AB5 EA53D15C 1AFF87B2 B9DA6E04 E058AD51 CC72BFC9 033B564E 26480D78 E955A5E2 9E7AB245"
            + "DB2BE315 E2099AFB")));
        var client = new SrpClient(RfcGroup, identity, password, a);
        Assert.That(client.A, Is.EqualTo(Hex(
            "61D5E490 F6F1B795 47B0704C 436F523D D0E560F0 C64115BB 72557EC4 4352E890 3211C046 92272D8B"
            + "2D1A5358 A2CF1B6E 0BFCF99F 921530EC 8E393561 79EAE45E 42BA92AE ACED8251 71E1E8B9 AF6D9C03"
            + "E1327F44 BE087EF0 6530E69F 66615261 EEF54073 CA11CF58 58F0EDFD FE15EFEA B349EF5D 76988A36"
            + "72FAC47B 0769447B")));
        Assert.That((Srp6a.Multiplier(RfcGroup) * v + BigInteger.ModPow(RfcGroup.G, b, RfcGroup.N)) % RfcGroup.N, Is.EqualTo(expectedB));
        var u = Srp6a.Scrambler(RfcGroup, client.A, expectedB);
        Assert.That(u, Is.EqualTo(Hex("CE38B959 3487DA98 554ED47D 70A7AE5F 462EF019")));
        Assert.That(Srp6a.ClientSecret(RfcGroup, expectedB, x, a, u), Is.EqualTo(Hex(
            "B0DC82BA BCF30674 AE450C02 87745E79 90A3381F 63B387AA F271A10D 233861E3 59B48220 F7C4693C"
            + "9AE12B0A 6F67809F 0876E2D0 13800D6C 41BB59B6 D5979B5C 00A172B4 A2A5903A 0BDCAF8A 709585EB"
            + "2AFAFA8F 3499B200 210DCC1F 10EB3394 3CD67FC8 8A2F39A4 BE5BEC4E C0A3212D C346D7E4 74B29EDE"
            + "8A469FFE CA686E5A")));
    }

    [Test]
    public void ComputesWhatTheControlPlaneComputes()
    {
        var vectors = JObject.Parse(File.ReadAllText(Repository.PathTo("fixtures/pairing/vectors.json")));
        var inputs = vectors["inputs"]!;
        var expected = vectors["expected"]!;
        string Input(string name) => (string)inputs[name]!;
        string Expected(string name) => (string)expected[name]!;

        var group = SrpGroup.Pairing;
        var password = PairingCrypto.CodePassword(Input("code"));
        var salt = Convert.FromBase64String(Input("salt"));
        var x = Srp6a.PrivateKey(group, Srp6a.PairingIdentity, password, salt);
        var v = BigInteger.ModPow(group.G, x, group.N);
        Assert.That(Srp6a.Multiplier(group).ToString("x", CultureInfo.InvariantCulture).TrimStart('0'), Is.EqualTo(Expected("k")));
        Assert.That(Convert.ToBase64String(Srp6a.Pad(group, v)), Is.EqualTo(Expected("verifier")));

        var client = new SrpClient(group, Srp6a.PairingIdentity, password, Srp6a.FromBytes(PairingCrypto.FromHex(Input("a"))));
        Assert.That(Convert.ToBase64String(Srp6a.Pad(group, client.A)), Is.EqualTo(Expected("client_public")));
        var serverPublic = Srp6a.FromBytes(Convert.FromBase64String(Expected("server_public")));
        var b = Srp6a.FromBytes(PairingCrypto.FromHex(Input("b")));
        Assert.That((Srp6a.Multiplier(group) * v + BigInteger.ModPow(group.G, b, group.N)) % group.N, Is.EqualTo(serverPublic));
        Assert.That(Srp6a.Scrambler(group, client.A, serverPublic).ToString("x", CultureInfo.InvariantCulture).TrimStart('0'), Is.EqualTo(Expected("scrambler")));

        var key = client.SessionKey(salt, serverPublic);
        Assert.That(key, Is.Not.Null);
        Assert.That(Convert.ToBase64String(key!), Is.EqualTo(Expected("session_key")));
        var transcript = PairingCrypto.Transcript(Input("label"), salt, client.A, serverPublic, PairingCrypto.FromHex(Input("certificate_sha256")));
        Assert.That(Convert.ToBase64String(transcript), Is.EqualTo(Expected("transcript")));
        var proof = PairingCrypto.ClientProof(key!, transcript);
        Assert.That(Convert.ToBase64String(proof), Is.EqualTo(Expected("client_proof")));
        var credential = Convert.FromBase64String(Input("credential"));
        var sealedCredential = PairingCrypto.Seal(credential, key!, transcript);
        Assert.That(Convert.ToBase64String(sealedCredential), Is.EqualTo(Expected("sealed_credential")));
        Assert.That(PairingCrypto.Seal(sealedCredential, key!, transcript), Is.EqualTo(credential), "sealing again opens it");
        Assert.That(
            Convert.ToBase64String(PairingCrypto.ServerProof(key!, transcript, proof, Input("device_id"), sealedCredential)),
            Is.EqualTo(Expected("server_proof")));
        Assert.That(PairingCrypto.CredentialFromBytes(credential), Is.EqualTo(Expected("credential")));
        Assert.That(PairingCrypto.Sha256Hex(Encoding.UTF8.GetBytes(Expected("credential"))), Is.EqualTo(Expected("credential_sha256")));
    }

    [Test]
    public void TheClientRefusesABOf0ModN()
    {
        var client = SrpClient.Create(SrpGroup.Pairing, Srp6a.PairingIdentity, PairingCrypto.CodePassword("12345678"));
        Assert.That(client.SessionKey(new byte[16], BigInteger.Zero), Is.Null);
        Assert.That(client.SessionKey(new byte[16], SrpGroup.Pairing.N), Is.Null);
        Assert.That(client.SessionKey(new byte[16], SrpGroup.Pairing.N + 1), Is.Null);
    }

    [Test]
    public void TakesTheCodeAsTyped()
    {
        Assert.That(PairingCrypto.NormalizeCode("4821 0937"), Is.EqualTo("48210937"));
        Assert.That(PairingCrypto.NormalizeCode("4821-0937"), Is.EqualTo("48210937"));
        Assert.That(PairingCrypto.NormalizeCode("48210937"), Is.EqualTo("48210937"));
        foreach (var bad in new[] { "4821093", "482109370", "4821O937", "", "４８２１０９３７" })
        {
            Assert.That(PairingCrypto.NormalizeCode(bad), Is.Null, bad);
        }
    }

    [Test]
    public void ComparesMacsWhole()
    {
        Assert.That(PairingCrypto.SameMac(new byte[32], new byte[32]), Is.True);
        var other = new byte[32];
        other[31] = 1;
        Assert.That(PairingCrypto.SameMac(new byte[32], other), Is.False);
        Assert.That(PairingCrypto.SameMac(new byte[32], new byte[31]), Is.False);
    }
}
