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
        private readonly HttpClient http;
        private readonly Uri baseUri;

        public ControlPlaneApi(Uri baseUri, string accessToken, HttpMessageHandler? handler = null)
        {
            this.baseUri = baseUri;
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            http.Timeout = TimeSpan.FromSeconds(15);
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
            var body = await GetAsync("api/runtimes/" + Uri.EscapeDataString(runtimeId) + "/models", cancellationToken)
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

        public void Dispose() => http.Dispose();

        private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(new Uri(baseUri, path), cancellationToken).ConfigureAwait(false);
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
                    throw new ControlPlaneRequestException("The control plane refused the request: " + Describe(response, body));
                }
                return body;
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
