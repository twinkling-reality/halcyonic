#nullable enable
using System;
using System.IO;
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

    /// <summary>The pairing as a JSON file, replaced whole on every save.</summary>
    public sealed class FilePairingStore : IPairingStore
    {
        private readonly string path;

        public FilePairingStore(string path)
        {
            this.path = path;
        }

        public PairedControlPlane? Load()
        {
            if (!File.Exists(path)) return null;
            try
            {
                var stored = JsonConvert.DeserializeObject<StoredPairing>(File.ReadAllText(path));
                if (stored?.Host == null || stored.CertificateSha256 == null || stored.DeviceId == null || stored.Credential == null)
                {
                    throw new InvalidDataException("The pairing file is incomplete.");
                }
                return new PairedControlPlane(stored.Host, stored.Port, stored.CertificateSha256, stored.DeviceId, stored.Credential);
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException)
            {
                throw new InvalidDataException("The pairing file cannot be read.", error);
            }
        }

        public void Save(PairedControlPlane pairing)
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
            };
            var temporary = path + ".new";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(stored, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public void Forget()
        {
            if (File.Exists(path)) File.Delete(path);
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
        }
    }
}
