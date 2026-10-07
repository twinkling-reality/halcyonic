#nullable enable
using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>
    /// The control plane's REST API, for what the realtime stream does not carry: journal history, what
    /// the understanding and evaluation providers say about an execution, the models a runtime lists, and
    /// where projects may live on the host.
    /// </summary>
    public sealed class ControlPlaneApi : IEventHistory, IIntelligenceReader, IDisposable
    {
        /// <summary>
        /// How long a companion reply may take on this side: the control plane's 45 s for a turn, with
        /// room for the network, so the Mac gives up first and says why (ADR 0025).
        /// </summary>
        public static readonly TimeSpan CompanionTimeout = TimeSpan.FromSeconds(55);

        /// <summary>How long any other request may take.</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How long a read of an agent app's models may take: past the 30 s your computer gives the agent
        /// app to list them, which may first start it, so your computer's own reason arrives first.
        /// </summary>
        public static readonly TimeSpan ModelsTimeout = TimeSpan.FromSeconds(40);

        private readonly HttpClient http;
        private readonly HttpClient companion;
        private readonly HttpClient models;
        private readonly HttpMessageHandler handler;
        private readonly Uri baseUri;

        /// <summary>
        /// What a refused credential means for how this headset reaches the computer, the sentence its
        /// connection says too (<see cref="ControlPlaneTarget.AccessRefused"/>).
        /// </summary>
        public string AccessRefused { get; set; } = ConnectionText.AccessRefused;

        /// <summary>
        /// A client for the control plane at <paramref name="baseUri"/>. Without a
        /// <paramref name="handler"/> it is on loopback, and each request carries the access token only
        /// after the control plane proves, just before, that it holds it (<see cref="LoopbackProofHandler"/>);
        /// otherwise the handler answers for who receives the token, as a pinned one does for a paired
        /// control plane. <paramref name="requestTimeout"/> and <paramref name="modelsTimeout"/> stand in for
        /// <see cref="RequestTimeout"/> and <see cref="ModelsTimeout"/>, as a test's shorter ones.
        /// </summary>
        public ControlPlaneApi(Uri baseUri, string accessToken, HttpMessageHandler? handler = null, TimeSpan? requestTimeout = null, TimeSpan? modelsTimeout = null)
        {
            this.baseUri = baseUri;
            // Every client shares the handler, which this object disposes once.
            this.handler = handler ?? new LoopbackProofHandler(accessToken);
            http = new HttpClient(this.handler, disposeHandler: false) { Timeout = requestTimeout ?? RequestTimeout };
            companion = new HttpClient(this.handler, disposeHandler: false) { Timeout = CompanionTimeout };
            models = new HttpClient(this.handler, disposeHandler: false) { Timeout = modelsTimeout ?? ModelsTimeout };
            if (handler != null)
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                companion.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                models.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        /// <summary>The REST address of the control plane that serves a realtime endpoint.</summary>
        public static Uri BaseUriFor(Uri realtimeEndpoint)
        {
            var builder = new UriBuilder(realtimeEndpoint) { Path = "/", Query = string.Empty, Fragment = string.Empty };
            builder.Scheme = realtimeEndpoint.Scheme == "wss" ? "https" : "http";
            return builder.Uri;
        }

        public async Task<EventsResponse> ReadAsync(string workstreamId, long after, int limit, CancellationToken cancellationToken)
        {
            var body = await GetAsync(
                "api/events?after=" + after.ToString(CultureInfo.InvariantCulture)
                    + "&limit=" + limit.ToString(CultureInfo.InvariantCulture)
                    + "&workstream_id=" + Uri.EscapeDataString(workstreamId),
                cancellationToken).ConfigureAwait(false);
            return HalcyonicJson.Deserialize<EventsResponse>(body);
        }

        /// <summary>
        /// What the understanding provider (Salidium) says about an execution. The control plane reads it
        /// through on request; the result states its availability instead of failing.
        /// </summary>
        public async Task<UnderstandingResponse> GetUnderstandingAsync(string executionId, CancellationToken cancellationToken = default)
        {
            var body = await GetAsync("api/executions/" + Uri.EscapeDataString(executionId) + "/understanding", cancellationToken)
                .ConfigureAwait(false);
            return HalcyonicJson.Deserialize<UnderstandingResponse>(body);
        }

        /// <summary>
        /// What the evaluation provider (Seorak) measured about an execution: its estimated cost, outcome
        /// and verification runs. The control plane reads it through on request; the result states its
        /// availability instead of failing. Each read costs Seorak three requests of a budget of 60 a
        /// minute, so fetch it on demand, for example when a workstream is opened, and never poll.
        /// </summary>
        public async Task<EvaluationResponse> GetEvaluationAsync(string executionId, CancellationToken cancellationToken = default)
        {
            var body = await GetAsync("api/executions/" + Uri.EscapeDataString(executionId) + "/evaluation", cancellationToken)
                .ConfigureAwait(false);
            return HalcyonicJson.Deserialize<EvaluationResponse>(body);
        }

        /// <summary>Account-wide provider limits last captured by Seorak. Read only when opened.</summary>
        public async Task<UsageLimitsResponse> GetUsageLimitsAsync(CancellationToken cancellationToken = default)
        {
            var body = await GetAsync("api/usage-limits", cancellationToken).ConfigureAwait(false);
            return HalcyonicJson.Deserialize<UsageLimitsResponse>(body);
        }

        /// <summary>The understanding answer, with when this device received it.</summary>
        public async Task<IntelligenceRead<UnderstandingResponse>> ReadUnderstandingAsync(string executionId, CancellationToken cancellationToken = default)
        {
            var response = await GetUnderstandingAsync(executionId, cancellationToken).ConfigureAwait(false);
            return new IntelligenceRead<UnderstandingResponse>(response, DateTimeOffset.UtcNow, recorded: false);
        }

        /// <summary>The evaluation answer, with when this device received it. Ask on demand, never on a timer.</summary>
        public async Task<IntelligenceRead<EvaluationResponse>> ReadEvaluationAsync(string executionId, CancellationToken cancellationToken = default)
        {
            var response = await GetEvaluationAsync(executionId, cancellationToken).ConfigureAwait(false);
            return new IntelligenceRead<EvaluationResponse>(response, DateTimeOffset.UtcNow, recorded: false);
        }

        /// <summary>
        /// The models a runtime can use now, from the runtime's own list (ADR 0016), for a runtime whose
        /// descriptor says its <c>model_choice</c> is <c>listed</c>. The control plane reads it through on
        /// request and never journals it; the result states its availability instead of failing. Send a
        /// model's <c>ModelRef</c> back unchanged when starting work; never derive meaning from it. Show
        /// where each model runs from <c>Served</c>, not from its name. Listing may start the runtime's
        /// server, so it can take seconds: fetch it when a person opens the choice, and never poll.
        /// </summary>
        public async Task<RuntimeModelsResponse> GetRuntimeModelsAsync(string runtimeId, CancellationToken cancellationToken = default)
        {
            var body = await GetAsync("api/runtimes/" + Uri.EscapeDataString(runtimeId) + "/models", cancellationToken, models)
                .ConfigureAwait(false);
            return HalcyonicJson.Deserialize<RuntimeModelsResponse>(body);
        }

        /// <summary>
        /// Where projects may live on the host (ADR 0020): its project roots and the visible folders
        /// directly inside each, read from the file system on request and never journaled. Offer them to
        /// the person, then send a root's <c>Path</c> and a folder's <c>Name</c> back unchanged in an
        /// <see cref="ExistingFolderChoice"/>, or a new name in a <see cref="NewFolderChoice"/>; never
        /// compose or take apart a path. No roots means the host allows no folder yet. Names are the file
        /// system's, so show them through <see cref="LabelText"/>. Fetch it when the person opens the
        /// choice, and never poll.
        /// </summary>
        public async Task<LocationsResponse> GetLocationsAsync(CancellationToken cancellationToken = default)
        {
            var body = await GetAsync("api/locations", cancellationToken).ConfigureAwait(false);
            return HalcyonicJson.Deserialize<LocationsResponse>(body);
        }

        /// <summary>
        /// Turns one clip of speech into a draft on the Mac (ADR 0021): a WAV made by
        /// <see cref="SpeechClip.Encode"/>. <see cref="HeardTranscription"/> carries untrusted text to show
        /// through <see cref="LabelText"/> as a draft, never sent without the person's confirmation;
        /// <see cref="NothingHeardTranscription"/> means no speech was found. A refusal throws
        /// <see cref="ControlPlaneRequestException"/> with its <c>Code</c>, which <see cref="VoiceText.Refusal"/>
        /// puts in words. Send one clip at a time, only when the person released a hold.
        /// </summary>
        public async Task<TranscriptionResponse> TranscribeAsync(byte[] wav, CancellationToken cancellationToken = default)
        {
            HttpResponseMessage response;
            try
            {
                var content = new ByteArrayContent(wav);
                content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                response = await http.PostAsync(new Uri(baseUri, "api/transcriptions"), content, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException error)
            {
                throw new ControlPlaneRequestException("The control plane could not be reached: " + error.Message, error);
            }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new ControlPlaneRequestException("The control plane refused the request: " + Describe(response, body), CodeOf(body));
                }
                return HalcyonicJson.Deserialize<TranscriptionResponse>(body);
            }
        }

        /// <summary>
        /// Whether Create's companion can be asked now (ADR 0025), read from the Mac's model list
        /// without asking the model. <see cref="UnavailableCompanion"/> carries why by its code, which
        /// <see cref="CompanionText.Unavailable"/> puts in words. Read it when Create opens, never on a timer.
        /// </summary>
        public async Task<CompanionStatus> GetCompanionAsync(CancellationToken cancellationToken = default)
        {
            var body = await GetAsync("api/companion", cancellationToken).ConfigureAwait(false);
            return HalcyonicJson.Deserialize<CompanionStatus>(body);
        }

        /// <summary>
        /// One reply from Create's companion to the exchange so far (ADR 0025). The reply is model text:
        /// untrusted and reported, shown only as the companion's, and nothing in it is sent anywhere
        /// until the person confirms the recap. A refusal throws <see cref="ControlPlaneRequestException"/>
        /// with its <c>Code</c>, which <see cref="CompanionText.Failure"/> puts in words. Ask one at a
        /// time, only when the person has said something or asked for the recap. A turn can take up to
        /// <see cref="CompanionTimeout"/>.
        /// </summary>
        public async Task<CompanionReplyResponse> AskCompanionAsync(CompanionRepliesRequest request, CancellationToken cancellationToken = default)
        {
            HttpResponseMessage response;
            try
            {
                var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/companion/replies"))
                {
                    Content = new StringContent(HalcyonicJson.Serialize(request), System.Text.Encoding.UTF8, "application/json"),
                };
                // Headers first, so the body is read only as far as a reply can be.
                response = await companion.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException error)
            {
                throw new ControlPlaneRequestException("The control plane could not be reached: " + error.Message, error);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // This side's time for a turn ran out, not the caller's: the companion took too long.
                throw new ControlPlaneRequestException("The companion took too long.", "companion_too_slow");
            }
            using (response)
            {
                string? body;
                try
                {
                    body = await ReadBoundedAsync(response, CompanionReplyLimit, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ControlPlaneRequestException("The companion took too long.", "companion_too_slow");
                }
                catch (System.IO.IOException error)
                {
                    throw new ControlPlaneRequestException("The control plane could not be reached: " + error.Message, error);
                }
                if (body == null) throw new ControlPlaneRequestException("The companion's answer is longer than one can be.", "companion_unreadable");
                if (!response.IsSuccessStatusCode)
                {
                    throw new ControlPlaneRequestException("The control plane refused the request: " + Describe(response, body), CodeOf(body));
                }
                return HalcyonicJson.Deserialize<CompanionReplyResponse>(body);
            }
        }

        /// <summary>The most a companion reply's body is read: far more than the contract's bounds allow.</summary>
        public const int CompanionReplyLimit = 64 * 1024;

        /// <summary>The body as text, or null once it passes <paramref name="limit"/> bytes, when the rest is not read.</summary>
        private static async Task<string?> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var buffer = new byte[8192];
            using var collected = new System.IO.MemoryStream();
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (collected.Length + read > limit) return null;
                collected.Write(buffer, 0, read);
            }
            return System.Text.Encoding.UTF8.GetString(collected.ToArray());
        }

        public void Dispose()
        {
            http.Dispose();
            companion.Dispose();
            models.Dispose();
            handler.Dispose();
        }

        private async Task<string> GetAsync(string path, CancellationToken cancellationToken, HttpClient? client = null)
        {
            HttpResponseMessage response;
            try
            {
                response = await (client ?? http).GetAsync(new Uri(baseUri, path), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException error)
            {
                throw new ControlPlaneRequestException("The control plane could not be reached: " + error.Message, error);
            }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new ControlPlaneRequestException("The control plane refused the request: " + Describe(response, body), CodeOf(body));
                }
                return body;
            }
        }

        private static string? CodeOf(string body)
        {
            try
            {
                return HalcyonicJson.Deserialize<ErrorResponse>(body).Error.Code;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Describe(HttpResponseMessage response, string body)
        {
            try
            {
                return HalcyonicJson.Deserialize<ErrorResponse>(body).Error.Message;
            }
            catch (JsonException)
            {
                return ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
