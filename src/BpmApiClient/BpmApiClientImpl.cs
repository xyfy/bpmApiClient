using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BpmApiClient.Models;
using Newtonsoft.Json;

namespace BpmApiClient
{
    /// <summary>
    /// BPM API 客户端的 HTTP 实现。
    /// 封装所有 BPM v95 REST 接口的网络调用逻辑，含令牌自动获取与缓存。
    /// </summary>
    public class BpmApiClientImpl : IBpmApiClient
    {
        // -------------------------------------------------------
        // 私有字段
        // -------------------------------------------------------

        /// <summary>
        /// 返回用于当前 HTTP 调用的 HttpClient 的工厂委托。
        /// 使用 IHttpClientFactory 时每次调用都会从工厂获取，确保 handler 随框架周期轮换；
        /// 直接传入 HttpClient 时始终返回同一实例（兼容测试与简单场景）。
        /// </summary>
        private readonly Func<HttpClient> _getHttpClient;

        /// <summary>获取当前 HTTP 调用应使用的 HttpClient，并校验 BaseAddress 已配置。</summary>
        private HttpClient HttpClient
        {
            get
            {
                var client = _getHttpClient();
                if (client == null)
                    throw new InvalidOperationException(
                        "HttpClient 工厂返回了 null，请检查 IHttpClientFactory 或工厂委托配置。");
                if (client.BaseAddress == null)
                    throw new InvalidOperationException(
                        "HttpClient.BaseAddress 未配置。使用 Func<HttpClient> 构造函数时，" +
                        "请在 AddHttpClient 配置阶段设置 BaseAddress（例如 c.BaseAddress = new Uri(baseUrl)）。");
                return client;
            }
        }

        /// <summary>客户端配置（BaseUrl / AppId / Secret / Timeout）。</summary>
        private readonly BpmApiClientOptions _options;

        /// <summary>
        /// 不可变令牌状态，用 volatile 保证跨线程可见性（避免 32 位运行时 DateTime 撕裂）。
        /// </summary>
        private sealed class TokenState
        {
            public readonly string Token;
            public readonly DateTime ExpiresAt;
            public TokenState(string token, DateTime expiresAt) { Token = token; ExpiresAt = expiresAt; }
        }

        /// <summary>当前缓存的令牌状态（null 表示尚未获取）。</summary>
        private volatile TokenState _tokenState;

        /// <summary>令牌刷新锁，防止并发重复刷新。</summary>
        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        // -------------------------------------------------------
        // 构造函数
        // -------------------------------------------------------

        /// <summary>
        /// 初始化 BPM API 客户端（直接注入 HttpClient，适用于单元测试及不需要 handler 轮换的场景）。
        /// </summary>
        /// <param name="httpClient">外部注入的 HttpClient，BaseAddress 将被设置为 options.BaseUrl。</param>
        /// <param name="options">BPM 服务地址及认证配置。</param>
        public BpmApiClientImpl(HttpClient httpClient, BpmApiClientOptions options)
        {
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrWhiteSpace(_options.BaseUrl))
                throw new ArgumentException("BpmApiClientOptions.BaseUrl 不能为空。", nameof(options.BaseUrl));

            ValidateCredentialOptions(_options);

            // 设置 HttpClient 基础属性
            httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            httpClient.Timeout = _options.Timeout;

            _getHttpClient = () => httpClient;
        }

        /// <summary>
        /// 初始化 BPM API 客户端（通过工厂委托获取 HttpClient，适用于生产环境）。
        /// 每次 HTTP 调用都会调用 <paramref name="httpClientFactory"/> 获取 HttpClient，
        /// 结合 IHttpClientFactory.CreateClient() 可实现 handler 定期轮换，避免 DNS 缓存过期问题。
        /// HttpClient 的 BaseAddress / Timeout 应在 AddHttpClient 配置阶段设置，此构造函数不再重复设置。
        /// </summary>
        /// <param name="httpClientFactory">每次调用时返回已配置好的 HttpClient 的委托。</param>
        /// <param name="options">BPM 服务认证配置（AppId / Secret 必填；BaseUrl 由 HttpClient 配置提供）。</param>
        public BpmApiClientImpl(Func<HttpClient> httpClientFactory, BpmApiClientOptions options)
        {
            _getHttpClient = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            ValidateCredentialOptions(_options);
        }

        /// <summary>校验 AppId / Secret 必填字段，两个构造函数共用。</summary>
        private static void ValidateCredentialOptions(BpmApiClientOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.AppId))
                throw new ArgumentException("BpmApiClientOptions.AppId 不能为空。", nameof(options.AppId));

            if (string.IsNullOrWhiteSpace(options.Secret))
                throw new ArgumentException("BpmApiClientOptions.Secret 不能为空。", nameof(options.Secret));
        }

        // ============================================================
        // 1.3 获取令牌
        // ============================================================

        /// <summary>
        /// 获取（或从缓存中读取）访问令牌。
        /// 令牌有效期为 30 分钟；此方法在剩余不足 60 秒时自动刷新。
        /// </summary>
        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            // 快速路径：读取 volatile 引用后本地检查，避免 DateTime 撕裂
            var current = _tokenState;
            if (current != null && DateTime.UtcNow < current.ExpiresAt)
                return current.Token;

            // 慢速路径：加锁刷新，避免并发多次请求
            await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 双重检查，防止等锁期间已被其他线程刷新
                current = _tokenState;
                if (current != null && DateTime.UtcNow < current.ExpiresAt)
                    return current.Token;

                // 调用令牌接口：GET /oauth2/access-token?appid=&secret=
                var url = $"oauth2/access-token?appid={Uri.EscapeDataString(_options.AppId)}" +
                          $"&secret={Uri.EscapeDataString(_options.Secret)}";

                using (var resp = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    string newToken;
                    DateTime newExpiresAt;

                    // 规范返回的是 JWT 字符串（直接），部分版本包装在 JSON 内
                    // 若响应以 '{' 开头则尝试反序列化为 TokenResult
                    if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    {
                        var tokenResult = JsonConvert.DeserializeObject<TokenResult>(body);
                        if (string.IsNullOrWhiteSpace(tokenResult?.AccessToken))
                            throw new InvalidOperationException(
                                $"令牌接口返回了 JSON，但其中缺少 access_token 字段。响应体：{body}");
                        newToken = tokenResult.AccessToken;
                        // expiresIn 单位：秒；提前 60 秒刷新，至少保留 30 秒缓存以避免过度刷新；
                        // 同时不得超过服务端声明的有效期，防止短期 token 被延长使用
                        int expiresIn = tokenResult.ExpiresIn > 0 ? tokenResult.ExpiresIn : 1800;
                        newExpiresAt = DateTime.UtcNow.AddSeconds(Math.Min(Math.Max(expiresIn - 60, 30), expiresIn));
                    }
                    else
                    {
                        // 直接是 JWT 字符串
                        newToken = body.Trim();
                        if (string.IsNullOrWhiteSpace(newToken))
                            throw new InvalidOperationException(
                                "令牌接口返回了成功响应，但响应体为空，无法获取访问令牌。");
                        newExpiresAt = DateTime.UtcNow.AddMinutes(29); // 保守 29 分钟
                    }

                    // 原子发布新状态（volatile write）
                    _tokenState = new TokenState(newToken, newExpiresAt);
                }

                return _tokenState.Token;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        // ============================================================
        // 2.2 启动流程
        // ============================================================

        /// <summary>
        /// 启动一个新的 BPM 流程实例。
        /// POST /web-service/bpm/v95/process/init
        /// </summary>
        public Task<InitProcessResult> InitProcessAsync(InitProcessRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.WfDef)) throw new ArgumentException("WfDef 不能为空。", nameof(request.WfDef));
            if (string.IsNullOrWhiteSpace(request.InitUser)) throw new ArgumentException("InitUser 不能为空。", nameof(request.InitUser));

            return PostJsonAsync<InitProcessRequest, InitProcessResult>(
                "web-service/bpm/v95/process/init", request, cancellationToken);
        }

        // ============================================================
        // 2.3 驱动流程（提交/退回）
        // ============================================================

        /// <summary>
        /// 推进或退回一个流程环节。
        /// POST /web-service/bpm/v95/process/forward
        /// </summary>
        public Task<ForwardProcessResult> ForwardProcessAsync(ForwardProcessRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.TaskId)) throw new ArgumentException("TaskId 不能为空。", nameof(request.TaskId));
            if (string.IsNullOrWhiteSpace(request.AsnUser)) throw new ArgumentException("AsnUser 不能为空。", nameof(request.AsnUser));

            return PostJsonAsync<ForwardProcessRequest, ForwardProcessResult>(
                "web-service/bpm/v95/process/forward", request, cancellationToken);
        }

        // ============================================================
        // 2.4 附件上传
        // ============================================================

        /// <summary>
        /// 上传附件到指定流程/环节的附件组件。
        /// POST /web-service/bpm/v95/formdata/upload-file（multipart/form-data）
        /// </summary>
        public async Task<IList<object>> UploadFileAsync(
            string wfId,
            string taskId,
            string user,
            int increment,
            IDictionary<string, (Stream Stream, string FileName)> attachments,
            CancellationToken cancellationToken = default)
        {
            // wfId 和 taskId 至少需要一个
            if (string.IsNullOrWhiteSpace(wfId) && string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("wfId 和 taskId 至少填一个。", nameof(wfId));
            if (attachments == null || attachments.Count == 0)
                throw new ArgumentException("attachments 不能为空。", nameof(attachments));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            // 构建 multipart/form-data 请求
            using (var form = new MultipartFormDataContent())
            {
                // 添加基础字段
                if (!string.IsNullOrWhiteSpace(wfId))
                    form.Add(new StringContent(wfId), "wfId");
                if (!string.IsNullOrWhiteSpace(taskId))
                    form.Add(new StringContent(taskId), "taskId");
                if (!string.IsNullOrWhiteSpace(user))
                    form.Add(new StringContent(user), "user");
                form.Add(new StringContent(increment.ToString()), "increment");

                // 添加每个附件文件
                foreach (var kv in attachments)
                {
                    var streamContent = new StreamContent(kv.Value.Stream);
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    form.Add(streamContent, kv.Key, kv.Value.FileName);
                }

                // 发送请求
                var url = $"web-service/bpm/v95/formdata/upload-file?eco-oauth2-token={Uri.EscapeDataString(token)}";
                using (var resp = await HttpClient.PostAsync(url, form, cancellationToken).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<List<object>>(body) ?? new List<object>();
                }
            }
        }

        // ============================================================
        // 2.5 获取环节信息
        // ============================================================

        /// <summary>
        /// 根据流程 ID 和环节标识查询环节及办理人状态。
        /// GET /web-service/bpm/v95/process/task-state?wfId=&amp;taskDef=
        /// </summary>
        public async Task<TaskStateResult> GetTaskStateAsync(string wfId, string taskDef, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wfId)) throw new ArgumentException("wfId 不能为空。", nameof(wfId));
            if (string.IsNullOrWhiteSpace(taskDef)) throw new ArgumentException("taskDef 不能为空。", nameof(taskDef));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/process/task-state?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(wfId)}&taskDef={Uri.EscapeDataString(taskDef)}";

            return await GetResultAsync<TaskStateResult>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.6 环节办理申请
        // ============================================================

        /// <summary>
        /// 在打开表单前调用，校验当前用户及环节是否允许办理。
        /// GET /web-service/bpm/v95/process/apply?taskId=&amp;asnUser=&amp;asnDate=
        /// </summary>
        public async Task ApplyTaskAsync(string taskId, string asnUser, string asnDate = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("taskId 不能为空。", nameof(taskId));
            if (string.IsNullOrWhiteSpace(asnUser)) throw new ArgumentException("asnUser 不能为空。", nameof(asnUser));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/process/apply?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&taskId={Uri.EscapeDataString(taskId)}&asnUser={Uri.EscapeDataString(asnUser)}";

            if (!string.IsNullOrWhiteSpace(asnDate))
                url += $"&asnDate={Uri.EscapeDataString(asnDate)}";

            // 接口无业务返回数据，仅检查 code == 0
            await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.7 流程撤回
        // ============================================================

        /// <summary>
        /// 将进行中的流程撤回到指定环节。
        /// POST /web-service/bpm/v95/maintain/workflow-backoff
        /// </summary>
        public async Task<BackoffResult> WorkflowBackoffAsync(WorkflowBackoffRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateBackoffRequest(request);

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            // 规范示例使用 query string 而非 JSON body
            var url = $"web-service/bpm/v95/maintain/workflow-backoff?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(request.WfId)}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}" +
                      $"&taskId={Uri.EscapeDataString(request.TaskId)}";

            return await PostQueryStringAsync<BackoffResult>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.8 流程取消
        // ============================================================

        /// <summary>
        /// 取消一个进行中的流程实例。
        /// POST /web-service/bpm/v95/maintain/cancelwf
        /// </summary>
        public async Task CancelWfAsync(CancelWfRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.WfId)) throw new ArgumentException("WfId 不能为空。", nameof(request.WfId));
            if (string.IsNullOrWhiteSpace(request.OpUser)) throw new ArgumentException("OpUser 不能为空。", nameof(request.OpUser));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/maintain/cancelwf?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(request.WfId)}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}";

            await PostQueryStringAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.9 流程环节回滚
        // ============================================================

        /// <summary>
        /// 将流程回滚到之前的指定环节。
        /// POST /web-service/bpm/v95/process/rollback
        /// </summary>
        public async Task<BackoffResult> RollbackAsync(WorkflowBackoffRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateBackoffRequest(request);

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/process/rollback?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(request.WfId)}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}" +
                      $"&taskId={Uri.EscapeDataString(request.TaskId)}";

            return await PostQueryStringAsync<BackoffResult>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.10 获取办理锁
        // ============================================================

        /// <summary>
        /// 获取指定环节的办理锁，防止多人同时编辑。
        /// GET /web-service/bpm/v95/process/try-lock?taskId=&amp;opUser=
        /// </summary>
        public async Task TryLockAsync(ProcessLockRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateLockRequest(request);

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/process/try-lock?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&taskId={Uri.EscapeDataString(request.TaskId)}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}";

            await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.11 释放办理锁
        // ============================================================

        /// <summary>
        /// 释放已持有的环节办理锁。
        /// GET /web-service/bpm/v95/process/release-lock?taskId=&amp;opUser=
        /// </summary>
        public async Task ReleaseLockAsync(ProcessLockRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateLockRequest(request);

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/process/release-lock?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&taskId={Uri.EscapeDataString(request.TaskId)}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}";

            await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.12 预跑测试
        // ============================================================

        /// <summary>
        /// 对指定环节发起预跑测试，返回预计执行路线。
        /// POST /web-service/bpm/v95/trivial/task-linetest
        /// </summary>
        public Task<object> TaskLinetestAsync(TaskLinetestRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.TaskId)) throw new ArgumentException("TaskId 不能为空。", nameof(request.TaskId));

            return PostJsonAsync<TaskLinetestRequest, object>(
                "web-service/bpm/v95/trivial/task-linetest", request, cancellationToken);
        }

        // ============================================================
        // 2.13 获取表单信息
        // ============================================================

        /// <summary>
        /// 根据流程实例 ID 获取完整表单数据（字段因模板而异，返回动态 Map）。
        /// GET /web-service/bpm/v95/formdata/full-get?wfId=
        /// </summary>
        public async Task<object> GetFormDataAsync(string wfId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wfId)) throw new ArgumentException("wfId 不能为空。", nameof(wfId));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/formdata/full-get?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(wfId)}";

            return await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.14 获取待办列表
        // ============================================================

        /// <summary>
        /// 查询指定办理人的待办任务列表（分页）。
        /// POST /web-service/bpm/v95/searchlist/toassign
        /// </summary>
        public Task<object> GetPendingTasksAsync(PendingTaskSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.AsnUser)) throw new ArgumentException("AsnUser 不能为空。", nameof(request.AsnUser));

            return PostJsonAsync<PendingTaskSearchRequest, object>(
                "web-service/bpm/v95/searchlist/toassign", request, cancellationToken);
        }

        // ============================================================
        // 2.15 表单赋值
        // ============================================================

        /// <summary>
        /// 更新进行中流程的表单字段值。
        /// POST /web-service/bpm/v95/formdata/update
        /// </summary>
        public async Task UpdateFormDataAsync(FormDataUpdateRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.WfId)) throw new ArgumentException("WfId 不能为空。", nameof(request.WfId));

            await PostJsonAsync<FormDataUpdateRequest, object>(
                "web-service/bpm/v95/formdata/update", request, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 2.16 获取流程实例列表（监控）
        // ============================================================

        /// <summary>
        /// 以监控视角分页查询流程实例列表。
        /// POST /web-service/bpm/v95/searchlist/tomonitor
        /// </summary>
        public Task<object> GetMonitorListAsync(MonitorSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            return PostJsonAsync<MonitorSearchRequest, object>(
                "web-service/bpm/v95/searchlist/tomonitor", request, cancellationToken);
        }

        // ============================================================
        // 2.17 我发起的列表
        // ============================================================

        /// <summary>
        /// 查询指定发起人发起的流程列表（分页）。
        /// POST /web-service/bpm/v95/searchlist/selfinit
        /// </summary>
        public Task<object> GetSelfInitListAsync(SelfInitSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.InitUser)) throw new ArgumentException("InitUser 不能为空。", nameof(request));

            return PostJsonAsync<SelfInitSearchRequest, object>(
                "web-service/bpm/v95/searchlist/selfinit", request, cancellationToken);
        }

        // ============================================================
        // 2.18 我经办的列表
        // ============================================================

        /// <summary>
        /// 查询指定办理人已处理过的流程列表（分页）。
        /// POST /web-service/bpm/v95/searchlist/selfprocess
        /// </summary>
        public Task<object> GetSelfProcessListAsync(SelfProcessSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.AsnUser)) throw new ArgumentException("AsnUser 不能为空。", nameof(request));

            return PostJsonAsync<SelfProcessSearchRequest, object>(
                "web-service/bpm/v95/searchlist/selfprocess", request, cancellationToken);
        }

        // ============================================================
        // 2.19 抄送我的列表
        // ============================================================

        /// <summary>
        /// 查询抄送给指定用户的流程列表（分页）。
        /// POST /web-service/bpm/v95/searchlist/tocc
        /// </summary>
        public Task<object> GetToCcListAsync(ToCcSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.AsnUser)) throw new ArgumentException("AsnUser 不能为空。", nameof(request));

            return PostJsonAsync<ToCcSearchRequest, object>(
                "web-service/bpm/v95/searchlist/tocc", request, cancellationToken);
        }

        // ============================================================
        // 2.20 流程实例详情
        // ============================================================

        /// <summary>
        /// 根据流程实例 ID 获取流程的完整详情信息。
        /// GET /web-service/bpm/v95/process/wfdetail?wfId=
        /// </summary>
        public async Task<object> GetWfDetailAsync(string wfId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wfId)) throw new ArgumentException("wfId 不能为空。", nameof(wfId));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/process/wfdetail?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(wfId)}";

            return await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 3.1 检查角色
        // ============================================================

        /// <summary>
        /// 检查当前发布模板中是否配置了指定角色。
        /// GET /web-service/bpm/v95/trivial/template-check-rolecode?roleCode=
        /// </summary>
        public async Task<object> CheckRoleCodeAsync(string roleCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(roleCode)) throw new ArgumentException("roleCode 不能为空。", nameof(roleCode));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/trivial/template-check-rolecode?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&roleCode={Uri.EscapeDataString(roleCode)}";

            return await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 3.2 获取流程历史列表
        // ============================================================

        /// <summary>
        /// 获取指定流程实例的历史审批记录列表。
        /// GET /web-service/bpm/v95/trivial/workflow-history?wfId=
        /// </summary>
        public async Task<object> GetWorkflowHistoryAsync(string wfId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wfId)) throw new ArgumentException("wfId 不能为空。", nameof(wfId));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/trivial/workflow-history?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(wfId)}";

            return await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 3.3 获取流程历史环节（办理人）
        // ============================================================

        /// <summary>
        /// 获取流程所有历史环节的办理人信息。
        /// GET /web-service/bpm/v95/process/full-task-trace?wfId=
        /// </summary>
        public async Task<object> GetFullTaskTraceAsync(string wfId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wfId)) throw new ArgumentException("wfId 不能为空。", nameof(wfId));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/process/full-task-trace?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(wfId)}";

            return await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 3.4 获取流程历史环节（层级）
        // ============================================================

        /// <summary>
        /// 按层级获取流程所有历史环节信息。
        /// GET /web-service/bpm/v95/process/full-tasklevel-trace?wfId=
        /// </summary>
        public async Task<object> GetFullTaskLevelTraceAsync(string wfId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wfId)) throw new ArgumentException("wfId 不能为空。", nameof(wfId));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/process/full-tasklevel-trace?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(wfId)}";

            return await GetResultAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 3.5 流程表单打印（下载）
        // ============================================================

        /// <summary>
        /// 下载指定流程实例的表单打印文件（二进制流）。
        /// POST /web-service/bpm/v95/trivial/print-wf?wfId=&amp;printUser=&amp;printDef=
        /// </summary>
        public async Task<byte[]> PrintWfAsync(string wfId, string printUser, string printDef, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wfId)) throw new ArgumentException("wfId 不能为空。", nameof(wfId));
            if (string.IsNullOrWhiteSpace(printUser)) throw new ArgumentException("printUser 不能为空。", nameof(printUser));
            if (string.IsNullOrWhiteSpace(printDef)) throw new ArgumentException("printDef 不能为空。", nameof(printDef));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/trivial/print-wf?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(wfId)}" +
                      $"&printUser={Uri.EscapeDataString(printUser)}" +
                      $"&printDef={Uri.EscapeDataString(printDef)}";

            // 打印接口返回二进制文件，直接读取字节数组
            using (var resp = await HttpClient.PostAsync(url, null, cancellationToken).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        // ============================================================
        // 3.6 添加流程委托
        // ============================================================

        /// <summary>
        /// 为指定用户添加流程委托配置。
        /// POST /web-service/bpm/v95/trivial/delegate-addasn
        /// </summary>
        public async Task DelegateAddAsync(DelegateAddRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.FromUser)) throw new ArgumentException("FromUser 不能为空。", nameof(request));

            await PostJsonAsync<DelegateAddRequest, object>(
                "web-service/bpm/v95/trivial/delegate-addasn", request, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 3.7 取消流程委托
        // ============================================================

        /// <summary>
        /// 取消指定委托人与受委托人之间的流程委托。
        /// POST /web-service/bpm/v95/trivial/delegate-invalidasn
        /// </summary>
        public async Task DelegateInvalidAsync(DelegateInvalidRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.FromUser)) throw new ArgumentException("FromUser 不能为空。", nameof(request));
            if (string.IsNullOrWhiteSpace(request.ToUser)) throw new ArgumentException("ToUser 不能为空。", nameof(request));

            await PostJsonAsync<DelegateInvalidRequest, object>(
                "web-service/bpm/v95/trivial/delegate-invalidasn", request, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 3.8 获取类别列表
        // ============================================================

        /// <summary>
        /// 获取所有流程类别列表。
        /// POST /web-service/bpm/v95/trivial/groups
        /// </summary>
        public async Task<object> GetGroupsAsync(CancellationToken cancellationToken = default)
        {
            return await PostJsonAsync<object, object>(
                "web-service/bpm/v95/trivial/groups", null, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 3.9 获取模板列表
        // ============================================================

        /// <summary>
        /// 获取指定类别（或全部）下已发布的流程模板列表。
        /// POST /web-service/bpm/v95/trivial/templates
        /// </summary>
        public Task<object> GetTemplatesAsync(TemplateSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            return PostJsonAsync<TemplateSearchRequest, object>(
                "web-service/bpm/v95/trivial/templates", request, cancellationToken);
        }

        // ============================================================
        // 4.1 变更流程状态
        // ============================================================

        /// <summary>
        /// 强制变更流程实例的状态（运维用途）。
        /// POST /web-service/bpm/v95/maintain/change-wfstatus（query string 参数）
        /// </summary>
        public async Task ChangeWfStatusAsync(ChangeWfStatusRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.WfId)) throw new ArgumentException("WfId 不能为空。", nameof(request.WfId));
            if (string.IsNullOrWhiteSpace(request.OpUser)) throw new ArgumentException("OpUser 不能为空。", nameof(request.OpUser));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/maintain/change-wfstatus?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(request.WfId)}" +
                      $"&targetStatus={request.TargetStatus}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}";

            await PostQueryStringAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 4.2 变更环节（人员）办理状态
        // ============================================================

        /// <summary>
        /// 强制变更指定人员环节的办理状态（运维用途）。
        /// POST /web-service/bpm/v95/maintain/change-taskstatus（query string 参数）
        /// </summary>
        public async Task ChangeTaskStatusAsync(ChangeTaskStatusRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.TaskId)) throw new ArgumentException("TaskId 不能为空。", nameof(request));
            if (string.IsNullOrWhiteSpace(request.OpUser)) throw new ArgumentException("OpUser 不能为空。", nameof(request));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/maintain/change-taskstatus?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&taskId={Uri.EscapeDataString(request.TaskId)}" +
                      $"&targetStatus={request.TargetStatus}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}";

            await PostQueryStringAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 4.3 变更环节（节点）状态
        // ============================================================

        /// <summary>
        /// 强制变更整个层级节点的状态（运维用途）。
        /// POST /web-service/bpm/v95/maintain/change-levelstatus（query string 参数）
        /// </summary>
        public async Task ChangeLevelStatusAsync(ChangeLevelStatusRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.WfId)) throw new ArgumentException("WfId 不能为空。", nameof(request.WfId));
            if (request.TaskLevel == null && string.IsNullOrWhiteSpace(request.TaskDef))
                throw new ArgumentException("TaskLevel 和 TaskDef 至少填一个。", nameof(request.TaskLevel));
            if (string.IsNullOrWhiteSpace(request.OpUser)) throw new ArgumentException("OpUser 不能为空。", nameof(request.OpUser));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/maintain/change-levelstatus?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&wfId={Uri.EscapeDataString(request.WfId)}" +
                      $"&targetStatus={request.TargetStatus}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}";

            if (request.TaskLevel.HasValue)
                url += $"&taskLevel={request.TaskLevel.Value}";
            if (!string.IsNullOrWhiteSpace(request.TaskDef))
                url += $"&taskDef={Uri.EscapeDataString(request.TaskDef)}";

            await PostQueryStringAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 4.4 变更环节（节点）办理人
        // ============================================================

        /// <summary>
        /// 变更指定环节的办理人（运维用途）。
        /// POST /web-service/bpm/v95/maintain/task-change-assignee（query string 参数）
        /// </summary>
        public async Task TaskChangeAssigneeAsync(TaskChangeAssigneeRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.TaskId)) throw new ArgumentException("TaskId 不能为空。", nameof(request));
            if (string.IsNullOrWhiteSpace(request.Assignee)) throw new ArgumentException("Assignee 不能为空。", nameof(request));
            if (string.IsNullOrWhiteSpace(request.OpUser)) throw new ArgumentException("OpUser 不能为空。", nameof(request));

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"web-service/bpm/v95/maintain/task-change-assignee?eco-oauth2-token={Uri.EscapeDataString(token)}" +
                      $"&taskId={Uri.EscapeDataString(request.TaskId)}" +
                      $"&assignee={Uri.EscapeDataString(request.Assignee)}" +
                      $"&opUser={Uri.EscapeDataString(request.OpUser)}";

            await PostQueryStringAsync<object>(url, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // 私有辅助方法
        // ============================================================

        /// <summary>
        /// 发送 JSON body 的 POST 请求，自动携带令牌，解包 BpmApiResponse 包装，并返回 result。
        /// </summary>
        private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
            string path, TRequest body, CancellationToken cancellationToken)
        {
            // 获取（或刷新）访问令牌
            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            // 令牌放在 header 中（规范建议放 header 比放 url 更安全）
            using (var request = new HttpRequestMessage(HttpMethod.Post, path))
            {
                request.Headers.Add("eco-oauth2-token", token);

                // 序列化请求体为 JSON
                if (body != null)
                {
                    var json = JsonConvert.SerializeObject(body);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using (var resp = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return UnwrapResponse<TResponse>(responseBody);
                }
            }
        }

        /// <summary>
        /// 发送 GET 请求（URL 已含 query string），解包 BpmApiResponse 包装，返回 result。
        /// </summary>
        private async Task<TResponse> GetResultAsync<TResponse>(string url, CancellationToken cancellationToken)
        {
            using (var resp = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return UnwrapResponse<TResponse>(body);
            }
        }

        /// <summary>
        /// 发送 POST 请求（无 body，URL 已含 query string），解包 BpmApiResponse 包装，返回 result。
        /// 运维接口通过 query string 传参，body 为空。
        /// </summary>
        private async Task<TResponse> PostQueryStringAsync<TResponse>(string url, CancellationToken cancellationToken)
        {
            using (var resp = await HttpClient.PostAsync(url, null, cancellationToken).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return UnwrapResponse<TResponse>(body);
            }
        }

        /// <summary>
        /// 解包 BPM 统一响应体，当 code != 0 时抛出异常。
        /// </summary>
        private static TResult UnwrapResponse<TResult>(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return default;

            // 部分接口直接返回数组或其他非包装格式，尝试直接反序列化
            var trimmed = responseBody.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                // 非对象结构（如数组），直接反序列化为目标类型
                return JsonConvert.DeserializeObject<TResult>(responseBody);
            }

            // 尝试解析标准包装格式
            var wrapper = JsonConvert.DeserializeObject<BpmApiResponse<TResult>>(responseBody);
            if (wrapper == null)
                return default;

            if (wrapper.Code != 0)
            {
                // 非零 code 表示 BPM 业务异常
                throw new BpmApiException(wrapper.Code, wrapper.Message ?? "BPM 接口返回异常");
            }

            return wrapper.Result;
        }

        /// <summary>校验撤回/回滚请求的必填字段。</summary>
        private static void ValidateBackoffRequest(WorkflowBackoffRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.WfId)) throw new ArgumentException("WfId 不能为空。", nameof(request.WfId));
            if (string.IsNullOrWhiteSpace(request.OpUser)) throw new ArgumentException("OpUser 不能为空。", nameof(request.OpUser));
            if (string.IsNullOrWhiteSpace(request.TaskId)) throw new ArgumentException("TaskId 不能为空。", nameof(request.TaskId));
        }

        /// <summary>校验加锁/解锁请求的必填字段。</summary>
        private static void ValidateLockRequest(ProcessLockRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TaskId)) throw new ArgumentException("TaskId 不能为空。", nameof(request.TaskId));
            if (string.IsNullOrWhiteSpace(request.OpUser)) throw new ArgumentException("OpUser 不能为空。", nameof(request.OpUser));
        }
    }

    /// <summary>
    /// BPM 业务异常，当接口返回 code != 0 时抛出。
    /// </summary>
    public class BpmApiException : Exception
    {
        /// <summary>BPM 返回的错误码。</summary>
        public int Code { get; }

        /// <summary>初始化 BPM 业务异常。</summary>
        public BpmApiException(int code, string message) : base(message)
        {
            Code = code;
        }
    }
}
