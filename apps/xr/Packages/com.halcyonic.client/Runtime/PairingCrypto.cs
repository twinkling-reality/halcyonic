#nullable enable
using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>
    /// The pairing exchange's cryptography around SRP (ADR 0017), as the control plane computes it in
    /// apps/control-plane/src/network/pairing-protocol.ts; fixtures/pairing/vectors.json holds both
    /// to the same results.
    ///
    /// Both proofs are HMAC-SHA-256 under the SRP session key over a transcript that includes the
    /// SHA-256 of the TLS certificate the device's connection presented. A relay that terminates TLS
    /// presents its own certificate, so the proofs fail even when it passes every message on.
    /// </summary>
    public static class PairingCrypto
    {
        /// <summary>A device credential: 32 random bytes in base64url behind this prefix.</summary>
        public const string CredentialPrefix = "hlcd_";

        public const int CredentialBytes = 32;

        private static readonly byte[] Domain = Encoding.UTF8.GetBytes("halcyonic pairing 1\0");

        /// <summary>
        /// SHA-256 over the protocol's name, the device's label (length first), the salt, A and B padded
        /// to the group's 384 bytes, and the certificate's SHA-256.
        /// </summary>
        public static byte[] Transcript(string label, byte[] salt, BigInteger clientPublic, BigInteger serverPublic, byte[] certificateSha256)
        {
            var labelBytes = Encoding.UTF8.GetBytes(label);
            var length = new byte[4];
            length[0] = (byte)(labelBytes.Length >> 24);
            length[1] = (byte)(labelBytes.Length >> 16);
            length[2] = (byte)(labelBytes.Length >> 8);
            length[3] = (byte)labelBytes.Length;
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Concat(
                Domain,
                length,
                labelBytes,
                salt,
                Srp6a.Pad(SrpGroup.Pairing, clientPublic),
                Srp6a.Pad(SrpGroup.Pairing, serverPublic),
                certificateSha256));
        }

        public static byte[] ClientProof(byte[] key, byte[] transcript) => Mac(key, "client proof", transcript);

        /// <summary>Also covers the device id and the sealed credential that come with it.</summary>
        public static byte[] ServerProof(byte[] key, byte[] transcript, byte[] clientProof, string deviceId, byte[] sealedCredential) =>
            Mac(key, "server proof", transcript, clientProof, Encoding.UTF8.GetBytes(deviceId), sealedCredential);

        /// <summary>
        /// The credential's 32 bytes XORed with an HMAC under the session key: a one-time pad, since the
        /// key is new for every attempt. The same operation opens it.
        /// </summary>
        public static byte[] Seal(byte[] credential, byte[] key, byte[] transcript)
        {
            var pad = Mac(key, "credential", transcript);
            if (credential.Length != pad.Length) throw new ArgumentException("A credential is 32 bytes.", nameof(credential));
            var sealedBytes = new byte[pad.Length];
            for (var i = 0; i < pad.Length; i++) sealedBytes[i] = (byte)(credential[i] ^ pad[i]);
            return sealedBytes;
        }

        public static string CredentialFromBytes(byte[] credential) =>
            CredentialPrefix + Convert.ToBase64String(credential).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Compares two MACs in time that does not depend on where they differ.</summary>
        public static bool SameMac(byte[] expected, byte[] candidate)
        {
            if (expected.Length != candidate.Length) return false;
            var difference = 0;
            for (var i = 0; i < expected.Length; i++) difference |= expected[i] ^ candidate[i];
            return difference == 0;
        }

        /// <summary>The code as typed, spaces and dashes allowed; null unless it is eight digits.</summary>
        public static string? NormalizeCode(string typed)
        {
            var digits = new StringBuilder(8);
            foreach (var character in typed)
            {
                if (character >= '0' && character <= '9') digits.Append(character);
                else if (character != ' ' && character != '-') return null;
            }
            return digits.Length == 8 ? digits.ToString() : null;
        }

        /// <summary>SRP's password: the code's eight ASCII digits.</summary>
        public static byte[] CodePassword(string code) => Encoding.ASCII.GetBytes(code);

        public static string Sha256Hex(byte[] data)
        {
            using var sha256 = SHA256.Create();
            var digest = sha256.ComputeHash(data);
            var hex = new StringBuilder(digest.Length * 2);
            foreach (var value in digest) hex.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        public static byte[] FromHex(string hex)
        {
            if (hex.Length % 2 != 0) throw new FormatException("Hexadecimal text has an even length.");
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            }
            return bytes;
        }

        private static byte[] Mac(byte[] key, string label, params byte[][] parts)
        {
            using var hmac = new HMACSHA256(key);
            var labelBytes = Encoding.UTF8.GetBytes(label + "\0");
            var all = new byte[parts.Length + 1][];
            all[0] = labelBytes;
            Array.Copy(parts, 0, all, 1, parts.Length);
            return hmac.ComputeHash(Concat(all));
        }

        private static byte[] Concat(params byte[][] parts)
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
            return message;
        }
    }
}
