#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where the device keeps its pairing, credential included (ADR 0017). The Unity layer chooses
    /// the place: app-internal storage on the headset. Wrapping the file with a key the Android
    /// Keystore holds would be another implementation of this interface.
    /// </summary>
    public interface IPairingStore
    {
        /// <summary>The pairing, or null when this device has not paired.</summary>
        /// <exception cref="InvalidDataException">What is stored cannot be read.</exception>
        PairedControlPlane? Load();

        void Save(PairedControlPlane pairing);

        /// <summary>Removes the pairing, credential included.</summary>
        void Forget();
    }

    /// <summary>
    /// The pairing as a JSON file, replaced whole on every save. Saving, forgetting and keeping what
    /// a pairing learned (<see cref="Remember"/>, from a background thread) take turns, so a pairing
    /// forgotten or replaced is never written back.
    /// </summary>
    public sealed class FilePairingStore : IPairingStore
    {
        private static readonly ConcurrentDictionary<string, object> Gates = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

        private readonly string path;
        private readonly object gate;

        public FilePairingStore(string path)
        {
            this.path = path;
            gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new object());
        }

        public PairedControlPlane? Load()
        {
            lock (gate)
            {
                var pairing = Read();
                if (pairing != null) pairing.Source = this;
                return pairing;
            }
        }

        public void Save(PairedControlPlane pairing)
        {
            lock (gate) Write(pairing);
        }

        public void Forget()
        {
            lock (gate)
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// Keeps where else the control plane said it can be reached with the pairing, only while the
        /// file still holds that same pairing: one forgotten, or replaced by pairing again, stays as it is.
        /// </summary>
        /// <returns>Whether the file holds that pairing, with these addresses now.</returns>
        public bool Remember(string deviceId, string credential, string certificateSha256, IReadOnlyList<string> addresses)
        {
            lock (gate)
            {
                PairedControlPlane? now;
                try
                {
                    now = Read();
                }
                catch (InvalidDataException)
                {
                    return false;
                }
                if (now == null
                    || !string.Equals(now.DeviceId, deviceId, StringComparison.Ordinal)
                    || !string.Equals(now.Credential, credential, StringComparison.Ordinal)
                    || !string.Equals(now.CertificateSha256, certificateSha256, StringComparison.Ordinal))
                {
                    return false;
                }
                if (!now.Addresses.SequenceEqual(addresses, StringComparer.Ordinal))
                {
                    Write(new PairedControlPlane(now.Host, now.Port, now.CertificateSha256, now.DeviceId, now.Credential, addresses));
                }
                return true;
            }
        }

        private PairedControlPlane? Read()
        {
            if (!File.Exists(path)) return null;
            try
            {
                var stored = JsonConvert.DeserializeObject<StoredPairing>(File.ReadAllText(path));
                if (stored?.Host == null || stored.CertificateSha256 == null || stored.DeviceId == null || stored.Credential == null)
                {
                    throw new InvalidDataException("The pairing file is incomplete.");
                }
                return new PairedControlPlane(stored.Host, stored.Port, stored.CertificateSha256, stored.DeviceId, stored.Credential, stored.Addresses);
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException)
            {
                throw new InvalidDataException("The pairing file cannot be read.", error);
            }
        }

        private void Write(PairedControlPlane pairing)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var stored = new StoredPairing
            {
                Host = pairing.Host,
                Port = pairing.Port,
                CertificateSha256 = pairing.CertificateSha256,
                DeviceId = pairing.DeviceId,
                Credential = pairing.Credential,
                Addresses = pairing.Addresses.Count == 0 ? null : pairing.Addresses.ToList(),
            };
            var temporary = path + ".new";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(stored, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private sealed class StoredPairing
        {
            [JsonProperty("host")]
            public string? Host { get; set; }

            [JsonProperty("port")]
            public int Port { get; set; }

            [JsonProperty("certificate_sha256")]
            public string? CertificateSha256 { get; set; }

            [JsonProperty("device_id")]
            public string? DeviceId { get; set; }

            [JsonProperty("credential")]
            public string? Credential { get; set; }

            /// <summary>Absent in files written before a pairing kept where else to reach its computer.</summary>
            [JsonProperty("addresses", NullValueHandling = NullValueHandling.Ignore)]
            public List<string>? Addresses { get; set; }
        }
    }
}
