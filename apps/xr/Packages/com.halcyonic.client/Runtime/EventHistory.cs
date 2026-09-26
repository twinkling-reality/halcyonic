#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>Journal history, for what a snapshot does not carry.</summary>
    public interface IEventHistory
    {
        /// <summary>A workstream's events after a position, oldest first, at most <paramref name="limit"/> (1 to 1000).</summary>
        Task<EventsResponse> ReadAsync(string workstreamId, long after, int limit, CancellationToken cancellationToken);
    }

    public static class EventHistory
    {
        private const int PageSize = 1000;

        /// <summary>
        /// Every event of a workstream in the given journal, oldest first. Fails if the control plane
        /// now serves another journal, because positions from different journals do not mix.
        /// </summary>
        public static async Task<IReadOnlyList<StoredEvent>> ReadAllAsync(
            this IEventHistory history,
            string workstreamId,
            string journalId,
            CancellationToken cancellationToken = default)
        {
            var events = new List<StoredEvent>();
            long after = 0;
            while (true)
            {
                var page = await history.ReadAsync(workstreamId, after, PageSize, cancellationToken).ConfigureAwait(false);
                if (page.Journal.JournalId != journalId)
                {
                    throw new HistoryUnavailableException("The control plane now serves another journal.");
                }
                events.AddRange(page.Events);
                if (page.Events.Count < PageSize) return events;
                after = page.Events[page.Events.Count - 1].Position;
            }
        }
    }

    /// <summary><see cref="IEventHistory"/> over the control plane's REST API (<c>GET /api/events</c>).</summary>
    public sealed class HttpEventHistory : IEventHistory, IDisposable
    {
        private readonly HttpClient http;
        private readonly Uri baseUri;

        public HttpEventHistory(Uri baseUri, string accessToken, HttpMessageHandler? handler = null)
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
            var query = "api/events?after=" + after.ToString(CultureInfo.InvariantCulture)
                + "&limit=" + limit.ToString(CultureInfo.InvariantCulture)
                + "&workstream_id=" + Uri.EscapeDataString(workstreamId);
            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(new Uri(baseUri, query), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException error)
            {
                throw new HistoryUnavailableException("The control plane could not be reached: " + error.Message, error);
            }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HistoryUnavailableException("The control plane refused the history request: " + Describe(response, body));
                }
                return HalcyonicJson.Deserialize<EventsResponse>(body);
            }
        }

        public void Dispose() => http.Dispose();

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

    public sealed class HistoryUnavailableException : Exception
    {
        public HistoryUnavailableException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}
