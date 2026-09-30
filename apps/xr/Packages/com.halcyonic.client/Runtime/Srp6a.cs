#nullable enable
using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>A group for SRP: a safe prime N, a generator, and the hash (RFC 5054 Appendix A).</summary>
    public sealed class SrpGroup
    {
        public SrpGroup(string primeHex, int generator, HashAlgorithmName hash)
        {
            var digits = primeHex.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
            N = BigInteger.Parse("0" + digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            G = generator;
            Length = Srp6a.ToBytes(N).Length;
            Hash = hash;
        }

        public BigInteger N { get; }

        public BigInteger G { get; }

        /// <summary>The length of N in bytes, which PAD() pads to.</summary>
        public int Length { get; }

        public HashAlgorithmName Hash { get; }

        /// <summary>RFC 5054 Appendix A, the 3072-bit group, with SHA-256: the group pairing uses.</summary>
        public static readonly SrpGroup Pairing = new SrpGroup(
            "FFFFFFFF FFFFFFFF C90FDAA2 2168C234 C4C6628B 80DC1CD1 29024E08"
            + "8A67CC74 020BBEA6 3B139B22 514A0879 8E3404DD EF9519B3 CD3A431B"
            + "302B0A6D F25F1437 4FE1356D 6D51C245 E485B576 625E7EC6 F44C42E9"
            + "A637ED6B 0BFF5CB6 F406B7ED EE386BFB 5A899FA5 AE9F2411 7C4B1FE6"
            + "49286651 ECE45B3D C2007CB8 A163BF05 98DA4836 1C55D39A 69163FA8"
            + "FD24CF5F 83655D23 DCA3AD96 1C62F356 208552BB 9ED52907 7096966D"
            + "670C354E 4ABC9804 F1746C08 CA18217C 32905E46 2E36CE3B E39E772C"
            + "180E8603 9B2783A2 EC07A28F B5C55DF0 6F4C52C9 DE2BCBF6 95581718"
            + "3995497C EA956AE5 15D22618 98FA0510 15728E5A 8AAAC42D AD33170D"
            + "04507A33 A85521AB DF1CBA64 ECFB8504 58DBEF0A 8AEA7157 5D060C7D"
            + "B3970F85 A6E1E4C7 ABF5AE8C DB0933D7 1E8C94E0 4A25619D CEE3D226"
            + "1AD2EE6B F12FFA06 D98A0864 D8760273 3EC86A64 521F2B18 177B200C"
            + "BBE11757 7A615D6C 770988C0 BAD946E2 08E24FA0 74E5AB31 43DB5BFC"
            + "E0FD108E 4B82D120 A93AD2CA FFFFFFFF FFFFFFFF",
            5,
            HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// SRP-6a (RFC 5054 section 2): the device's side of pairing (ADR 0017). The device proves it holds
    /// the code shown on the control plane's machine without sending it; an attacker in the middle
    /// gets one guess per attempt and nothing to test offline. The control plane's side is in
    /// apps/control-plane/src/network/srp.ts; both reproduce RFC 5054's test vectors and the shared
    /// vectors in fixtures/pairing.
    /// </summary>
    public static class Srp6a
    {
        /// <summary>The SRP user name for pairing. The code is the password; there is no account.</summary>
        public static readonly byte[] PairingIdentity = Encoding.UTF8.GetBytes("halcyonic pairing");

        /// <summary>k = H(N | PAD(g)).</summary>
        public static BigInteger Multiplier(SrpGroup group) =>
            FromBytes(Hash(group, ToBytes(group.N), Pad(group, group.G)));

        /// <summary>x = H(s | H(I | ":" | P)).</summary>
        public static BigInteger PrivateKey(SrpGroup group, byte[] identity, byte[] password, byte[] salt) =>
            FromBytes(Hash(group, salt, Hash(group, identity, Encoding.UTF8.GetBytes(":"), password)));

        /// <summary>u = H(PAD(A) | PAD(B)).</summary>
        public static BigInteger Scrambler(SrpGroup group, BigInteger a, BigInteger b) =>
            FromBytes(Hash(group, Pad(group, a), Pad(group, b)));

        /// <summary>The premaster secret as the client computes it: (B - k * g^x)^(a + u * x) mod N.</summary>
        public static BigInteger ClientSecret(SrpGroup group, BigInteger serverPublic, BigInteger x, BigInteger a, BigInteger u)
        {
            var n = group.N;
            var baseValue = ((serverPublic - Multiplier(group) * BigInteger.ModPow(group.G, x, n)) % n + n) % n;
            return BigInteger.ModPow(baseValue, a + u * x, n);
        }

        /// <summary>K = H(PAD(S)): both proofs and the credential's encryption are keyed with it.</summary>
        public static byte[] SessionKey(SrpGroup group, BigInteger secret) => Hash(group, Pad(group, secret));

        /// <summary>The big-endian bytes of a non-negative integer, without leading zeros.</summary>
        public static byte[] ToBytes(BigInteger value)
        {
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value), "SRP values are never negative.");
            return value.IsZero ? new byte[0] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        }

        /// <summary>PAD(): the big-endian bytes, left-padded with zeros to the length of N.</summary>
        public static byte[] Pad(SrpGroup group, BigInteger value)
        {
            var bytes = ToBytes(value);
            if (bytes.Length > group.Length) throw new ArgumentOutOfRangeException(nameof(value), "The value is longer than N.");
            var padded = new byte[group.Length];
            Buffer.BlockCopy(bytes, 0, padded, group.Length - bytes.Length, bytes.Length);
            return padded;
        }

        public static BigInteger FromBytes(byte[] bytes) => new BigInteger(bytes, isUnsigned: true, isBigEndian: true);

        internal static byte[] Hash(SrpGroup group, params byte[][] parts)
        {
            var length = 0;
            foreach (var part in parts) length += part.Length;
            var message = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, message, offset, part.Length);
                offset += part.Length;
            }
            using var algorithm = group.Hash == HashAlgorithmName.SHA1 ? (HashAlgorithm)SHA1.Create() : SHA256.Create();
            return algorithm.ComputeHash(message);
        }
    }

    /// <summary>The device's side of one pairing attempt.</summary>
    public sealed class SrpClient
    {
        private readonly SrpGroup group;
        private readonly byte[] identity;
        private readonly byte[] password;
        private readonly BigInteger secret;

        public SrpClient(SrpGroup group, byte[] identity, byte[] password, BigInteger a)
        {
            this.group = group;
            this.identity = identity;
            this.password = password;
            secret = a;
            A = BigInteger.ModPow(group.G, a, group.N);
        }

        /// <summary>A = g^a mod N.</summary>
        public BigInteger A { get; }

        /// <summary>A client for one attempt, with a fresh 256-bit a.</summary>
        public static SrpClient Create(SrpGroup group, byte[] identity, byte[] password)
        {
            var a = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(a);
            }
            return new SrpClient(group, identity, password, Srp6a.FromBytes(a));
        }

        /// <summary>
        /// The session key, or null when B is invalid: RFC 5054 requires the client to abort when B mod N
        /// is 0, or when the scrambler u is 0.
        /// </summary>
        public byte[]? SessionKey(byte[] salt, BigInteger serverPublic)
        {
            if (serverPublic.Sign <= 0 || serverPublic >= group.N) return null;
            var u = Srp6a.Scrambler(group, A, serverPublic);
            if (u.IsZero) return null;
            var x = Srp6a.PrivateKey(group, identity, password, salt);
            return Srp6a.SessionKey(group, Srp6a.ClientSecret(group, serverPublic, x, secret, u));
        }
    }
}
