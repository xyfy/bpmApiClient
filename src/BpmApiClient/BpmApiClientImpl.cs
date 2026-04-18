using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BpmApiClient.Models;
using Newtonsoft.Json;

namespace BpmApiClient
{
    public class BpmApiClientImpl : IBpmApiClient
    {
        private readonly HttpClient _httpClient;

        public BpmApiClientImpl(HttpClient httpClient, BpmApiClientOptions options)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(options));
            }

            _httpClient.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            _httpClient.Timeout = options.Timeout;
        }

        public Task<StartProcessResponse> StartProcessAsync(StartProcessRequest request, CancellationToken cancellationToken = default)
        {
            return PostAsync<StartProcessRequest, StartProcessResponse>("api/bpm/process/start", request, cancellationToken);
        }

        public Task<WorkflowTaskActionResponse> ApproveTaskAsync(string taskId, WorkflowTaskActionRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                throw new ArgumentException("TaskId is required.", nameof(taskId));
            }

            return PostAsync<WorkflowTaskActionRequest, WorkflowTaskActionResponse>($"api/bpm/tasks/{taskId}/approve", request, cancellationToken);
        }

        public Task<WorkflowTaskActionResponse> RejectTaskAsync(string taskId, WorkflowTaskActionRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                throw new ArgumentException("TaskId is required.", nameof(taskId));
            }

            return PostAsync<WorkflowTaskActionRequest, WorkflowTaskActionResponse>($"api/bpm/tasks/{taskId}/reject", request, cancellationToken);
        }

        public Task<ProcessDetailResponse> GetProcessAsync(string processInstanceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(processInstanceId))
            {
                throw new ArgumentException("ProcessInstanceId is required.", nameof(processInstanceId));
            }

            return GetAsync<ProcessDetailResponse>($"api/bpm/process/{processInstanceId}", cancellationToken);
        }

        public async Task<IReadOnlyCollection<WorkflowTaskSummary>> GetPendingTasksAsync(string assignee, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(assignee))
            {
                throw new ArgumentException("Assignee is required.", nameof(assignee));
            }

            var result = await GetAsync<List<WorkflowTaskSummary>>($"api/bpm/tasks/pending?assignee={Uri.EscapeDataString(assignee)}", cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var payload = JsonConvert.SerializeObject(request);
            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            using (var response = await _httpClient.PostAsync(path, content, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonConvert.DeserializeObject<TResponse>(body) ?? throw new InvalidOperationException("Response body is empty.");
            }
        }

        private async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
        {
            using (var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonConvert.DeserializeObject<TResponse>(body) ?? throw new InvalidOperationException("Response body is empty.");
            }
        }
    }
}
