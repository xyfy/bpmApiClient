using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BpmApiClient;
using BpmApiClient.Models;
using Newtonsoft.Json;
using Xunit;

namespace BpmApiClient.Tests
{
    /// <summary>
    /// BpmApiClientImpl 单元测试。
    /// 通过自定义 MockHttpMessageHandler 拦截 HTTP 调用，
    /// 无需真实 BPM 服务器即可验证客户端逻辑。
    /// </summary>
    public class BpmApiClientImplTests
    {
        // -------------------------------------------------------
        // 测试辅助：模拟 HttpMessageHandler
        // -------------------------------------------------------

        /// <summary>
        /// 可配置的 HTTP 消息处理器，用于在单元测试中模拟 HTTP 响应。
        /// </summary>
        private class MockHttpMessageHandler : HttpMessageHandler
        {
            /// <summary>请求处理委托，接收请求并返回模拟响应。</summary>
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_handler(request));
            }
        }

        /// <summary>
        /// 创建返回固定 JSON 响应的 HttpClient。
        /// </summary>
        /// <param name="responseBody">HTTP 响应体 JSON 字符串</param>
        /// <param name="statusCode">HTTP 状态码，默认 200</param>
        private static HttpClient CreateHttpClient(
            string responseBody,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var handler = new MockHttpMessageHandler(_ =>
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                });
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("http://bpm-test-server/")
            };
        }

        /// <summary>
        /// 构造一个标准 BPM 成功响应的 JSON（code=0）。
        /// </summary>
        private static string BpmOkResponse(object result) =>
            JsonConvert.SerializeObject(new { code = 0, message = (string)null, result });

        /// <summary>
        /// 构造一个标准 BPM 失败响应的 JSON（code!=0）。
        /// </summary>
        private static string BpmErrorResponse(int code, string message) =>
            JsonConvert.SerializeObject(new { code, message, result = (object)null });

        /// <summary>
        /// 创建带标准配置的 BpmApiClientImpl 实例。
        /// </summary>
        private static BpmApiClientImpl CreateClient(HttpClient httpClient) =>
            new BpmApiClientImpl(httpClient, new BpmApiClientOptions
            {
                BaseUrl = "http://bpm-test-server",
                AppId = "test_appid",
                Secret = "test_secret"
            });

        // -------------------------------------------------------
        // 1.3 获取令牌 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证首次调用 GetAccessTokenAsync 时，能正确解析 JWT 令牌字符串。
        /// </summary>
        [Fact]
        public async Task GetAccessToken_WhenTokenReturnedAsString_ShouldReturnToken()
        {
            // Arrange：令牌接口直接返回 JWT 字符串（非 JSON 对象）
            const string expectedToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test";
            var httpClient = CreateHttpClient(expectedToken);
            var client = CreateClient(httpClient);

            // Act
            var token = await client.GetAccessTokenAsync();

            // Assert
            Assert.Equal(expectedToken, token);
        }

        /// <summary>
        /// 验证令牌被缓存：第二次调用不应再发 HTTP 请求。
        /// </summary>
        [Fact]
        public async Task GetAccessToken_SecondCall_ShouldUseCachedToken()
        {
            // Arrange：计数 HTTP 请求次数
            int callCount = 0;
            var handler = new MockHttpMessageHandler(_ =>
            {
                callCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("cached_token", Encoding.UTF8, "text/plain")
                };
            });
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://bpm-test-server/") };
            var client = CreateClient(httpClient);

            // Act：连续调用两次
            await client.GetAccessTokenAsync();
            await client.GetAccessTokenAsync();

            // Assert：只应发出一次 HTTP 请求
            Assert.Equal(1, callCount);
        }

        /// <summary>
        /// 验证令牌接口返回 JSON 对象时（含 access_token 字段）能正确解析。
        /// </summary>
        [Fact]
        public async Task GetAccessToken_WhenTokenReturnedAsJson_ShouldParseAccessToken()
        {
            // Arrange：令牌接口返回标准 OAuth2 JSON 格式
            var tokenJson = JsonConvert.SerializeObject(new
            {
                access_token = "json_format_token",
                token_type = "bearer",
                expires_in = 1800
            });
            var httpClient = CreateHttpClient(tokenJson);
            var client = CreateClient(httpClient);

            // Act
            var token = await client.GetAccessTokenAsync();

            // Assert
            Assert.Equal("json_format_token", token);
        }

        // -------------------------------------------------------
        // 2.2 启动流程 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证 InitProcessAsync 能正确解析 BPM 返回的流程实例信息。
        /// </summary>
        [Fact]
        public async Task InitProcess_WithValidRequest_ShouldReturnWfId()
        {
            // Arrange：模拟 BPM 返回启动流程成功响应
            int callIndex = 0;
            var handler = new MockHttpMessageHandler(req =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    // 第一次调用：获取令牌
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("test_token", Encoding.UTF8, "text/plain")
                    };
                }
                // 第二次调用：启动流程
                var result = new
                {
                    wfId = "1301006535580729346",
                    wfStatus = 1,
                    startTaskId = "14545100355807296746",
                    nextList = new object[] { }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BpmOkResponse(result), Encoding.UTF8, "application/json")
                };
            });
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://bpm-test-server/") };
            var client = CreateClient(httpClient);

            var request = new InitProcessRequest { WfDef = "expense-approval", InitUser = "zhangsan" };

            // Act
            var result2 = await client.InitProcessAsync(request);

            // Assert
            Assert.Equal("1301006535580729346", result2.WfId);
            Assert.Equal(1, result2.WfStatus);
        }

        /// <summary>
        /// 验证 InitProcessAsync 对必填参数的校验：WfDef 为空时应抛出异常。
        /// </summary>
        [Fact]
        public async Task InitProcess_WithEmptyWfDef_ShouldThrowArgumentException()
        {
            // Arrange
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);
            var request = new InitProcessRequest { WfDef = "", InitUser = "zhangsan" };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => client.InitProcessAsync(request));
        }

        /// <summary>
        /// 验证 InitProcessAsync 对必填参数的校验：InitUser 为空时应抛出异常。
        /// </summary>
        [Fact]
        public async Task InitProcess_WithEmptyInitUser_ShouldThrowArgumentException()
        {
            // Arrange
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);
            var request = new InitProcessRequest { WfDef = "expense-approval", InitUser = "" };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => client.InitProcessAsync(request));
        }

        /// <summary>
        /// 验证 InitProcessAsync 传 null 时抛出 ArgumentNullException。
        /// </summary>
        [Fact]
        public async Task InitProcess_WithNullRequest_ShouldThrowArgumentNullException()
        {
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);

            await Assert.ThrowsAsync<ArgumentNullException>(() => client.InitProcessAsync(null));
        }

        // -------------------------------------------------------
        // 2.3 驱动流程 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证 ForwardProcessAsync 对必填字段（TaskId）的校验。
        /// </summary>
        [Fact]
        public async Task ForwardProcess_WithEmptyTaskId_ShouldThrowArgumentException()
        {
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);
            var request = new ForwardProcessRequest { TaskId = "", AsnUser = "zhangsan" };

            await Assert.ThrowsAsync<ArgumentException>(() => client.ForwardProcessAsync(request));
        }

        /// <summary>
        /// 验证 ForwardProcessAsync 对必填字段（AsnUser）的校验。
        /// </summary>
        [Fact]
        public async Task ForwardProcess_WithEmptyAsnUser_ShouldThrowArgumentException()
        {
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);
            var request = new ForwardProcessRequest { TaskId = "task_001", AsnUser = "" };

            await Assert.ThrowsAsync<ArgumentException>(() => client.ForwardProcessAsync(request));
        }

        // -------------------------------------------------------
        // BPM 业务异常 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证当 BPM 返回非零 code 时，应抛出 BpmApiException。
        /// </summary>
        [Fact]
        public async Task InitProcess_WhenBpmReturnsErrorCode_ShouldThrowBpmApiException()
        {
            // Arrange：令牌直接返回，初始化流程返回错误码
            int callIndex = 0;
            var handler = new MockHttpMessageHandler(_ =>
            {
                callIndex++;
                if (callIndex == 1)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("test_token")
                    };
                // BPM 返回业务错误
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BpmErrorResponse(500, "流程不存在"))
                };
            });
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://bpm-test-server/") };
            var client = CreateClient(httpClient);
            var request = new InitProcessRequest { WfDef = "not-exist", InitUser = "zhangsan" };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<BpmApiException>(() => client.InitProcessAsync(request));
            Assert.Equal(500, ex.Code);
            Assert.Contains("流程不存在", ex.Message);
        }

        // -------------------------------------------------------
        // 2.5 获取环节信息 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证 GetTaskStateAsync 参数校验：wfId 为空时抛出异常。
        /// </summary>
        [Fact]
        public async Task GetTaskState_WithEmptyWfId_ShouldThrowArgumentException()
        {
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);

            await Assert.ThrowsAsync<ArgumentException>(() => client.GetTaskStateAsync("", "TASK1"));
        }

        /// <summary>
        /// 验证 GetTaskStateAsync 参数校验：taskDef 为空时抛出异常。
        /// </summary>
        [Fact]
        public async Task GetTaskState_WithEmptyTaskDef_ShouldThrowArgumentException()
        {
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);

            await Assert.ThrowsAsync<ArgumentException>(() => client.GetTaskStateAsync("wf_001", ""));
        }

        // -------------------------------------------------------
        // 2.7 流程撤回 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证 WorkflowBackoffAsync 参数校验：WfId 为空时抛出异常。
        /// </summary>
        [Fact]
        public async Task WorkflowBackoff_WithEmptyWfId_ShouldThrowArgumentException()
        {
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);
            var request = new WorkflowBackoffRequest { WfId = "", OpUser = "admin", TaskId = "task_001" };

            await Assert.ThrowsAsync<ArgumentException>(() => client.WorkflowBackoffAsync(request));
        }

        // -------------------------------------------------------
        // 4.3 变更环节节点状态 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证 ChangeLevelStatusAsync：TaskLevel 和 TaskDef 都为空时抛出异常。
        /// </summary>
        [Fact]
        public async Task ChangeLevelStatus_WithoutTaskLevelOrDef_ShouldThrowArgumentException()
        {
            var httpClient = CreateHttpClient("test_token");
            var client = CreateClient(httpClient);
            var request = new ChangeLevelStatusRequest
            {
                WfId = "wf_001",
                TargetStatus = 1,
                OpUser = "admin"
                // TaskLevel 和 TaskDef 均未填写
            };

            await Assert.ThrowsAsync<ArgumentException>(() => client.ChangeLevelStatusAsync(request));
        }

        // -------------------------------------------------------
        // 构造函数校验 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证构造函数：BaseUrl 为空时应抛出异常。
        /// </summary>
        [Fact]
        public void Constructor_WithEmptyBaseUrl_ShouldThrowArgumentException()
        {
            var httpClient = new HttpClient();
            var options = new BpmApiClientOptions { BaseUrl = "", AppId = "id", Secret = "sec" };

            Assert.Throws<ArgumentException>(() => new BpmApiClientImpl(httpClient, options));
        }

        /// <summary>
        /// 验证构造函数：options 为 null 时应抛出 ArgumentNullException。
        /// </summary>
        [Fact]
        public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
        {
            var httpClient = new HttpClient();

            Assert.Throws<ArgumentNullException>(() => new BpmApiClientImpl(httpClient, null));
        }

        /// <summary>
        /// 验证构造函数：HttpClient 为 null 时应抛出 ArgumentNullException。
        /// </summary>
        [Fact]
        public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
        {
            var options = new BpmApiClientOptions { BaseUrl = "http://test/", AppId = "id", Secret = "sec" };

            Assert.Throws<ArgumentNullException>(() => new BpmApiClientImpl((HttpClient)null, options));
        }

        // -------------------------------------------------------
        // 模型序列化 测试
        // -------------------------------------------------------

        /// <summary>
        /// 验证 InitProcessRequest 序列化时使用正确的 JSON 字段名（wfDef / initUser）。
        /// </summary>
        [Fact]
        public void InitProcessRequest_ShouldSerializeWithCorrectJsonNames()
        {
            // Arrange
            var request = new InitProcessRequest
            {
                WfDef = "expense-approval",
                InitUser = "zhangsan",
                FormData = new List<FormDataItem>
                {
                    new FormDataItem { Def = "amount", Val = "1000" }
                }
            };

            // Act
            var json = JsonConvert.SerializeObject(request);

            // Assert：JSON 字段名必须与 BPM 规范一致
            Assert.Contains("\"wfDef\"", json);
            Assert.Contains("\"initUser\"", json);
            Assert.Contains("\"formData\"", json);
            Assert.Contains("\"def\"", json);
            Assert.Contains("\"val\"", json);
        }

        /// <summary>
        /// 验证 ForwardProcessRequest 序列化时 null 可选字段不出现在 JSON 中。
        /// </summary>
        [Fact]
        public void ForwardProcessRequest_NullOptionalFields_ShouldNotSerialize()
        {
            // Arrange：只填必填字段
            var request = new ForwardProcessRequest
            {
                TaskId = "task_001",
                AsnUser = "zhangsan"
            };

            // Act
            var json = JsonConvert.SerializeObject(request);

            // Assert：可选字段不应出现在 JSON 中
            Assert.DoesNotContain("approveCode", json);
            Assert.DoesNotContain("formData", json);
            Assert.DoesNotContain("gridData", json);
        }

        /// <summary>
        /// 验证 BpmApiResponse 反序列化时 code=0 为成功，code!=0 为失败。
        /// </summary>
        [Fact]
        public void BpmApiResponse_Deserialization_ShouldWorkCorrectly()
        {
            // Arrange
            var successJson = "{\"code\":0,\"message\":null,\"result\":{\"wfId\":\"123\"}}";
            var errorJson = "{\"code\":500,\"message\":\"错误\",\"result\":null}";

            // Act
            var success = JsonConvert.DeserializeObject<BpmApiResponse<Dictionary<string, string>>>(successJson);
            var error = JsonConvert.DeserializeObject<BpmApiResponse<object>>(errorJson);

            // Assert
            Assert.Equal(0, success.Code);
            Assert.Equal("123", success.Result["wfId"]);
            Assert.Equal(500, error.Code);
            Assert.Equal("错误", error.Message);
        }
        /// <summary>
        /// 验证构造函数：AppId 为空时应抛出异常。
        /// </summary>
        [Fact]
        public void Constructor_WithEmptyAppId_ShouldThrowArgumentException()
        {
            var httpClient = new HttpClient();
            var options = new BpmApiClientOptions { BaseUrl = "http://test/", AppId = "", Secret = "sec" };

            Assert.Throws<ArgumentException>(() => new BpmApiClientImpl(httpClient, options));
        }

        /// <summary>
        /// 验证构造函数：Secret 为空时应抛出异常。
        /// </summary>
        [Fact]
        public void Constructor_WithEmptySecret_ShouldThrowArgumentException()
        {
            var httpClient = new HttpClient();
            var options = new BpmApiClientOptions { BaseUrl = "http://test/", AppId = "id", Secret = "" };

            Assert.Throws<ArgumentException>(() => new BpmApiClientImpl(httpClient, options));
        }

        // -------------------------------------------------------
        // 附件上传 (UploadFileAsync) 测试
        // -------------------------------------------------------

        /// <summary>
        /// 创建多 handler 的 HttpClient，依次按 callIndex 返回不同响应。
        /// </summary>
        private static HttpClient CreateSequentialHttpClient(
            params Func<HttpRequestMessage, HttpResponseMessage>[] handlers)
        {
            int index = 0;
            var handler = new MockHttpMessageHandler(req =>
            {
                var i = index < handlers.Length ? index : handlers.Length - 1;
                index++;
                return handlers[i](req);
            });
            return new HttpClient(handler) { BaseAddress = new Uri("http://bpm-test-server/") };
        }

        /// <summary>
        /// 验证 UploadFileAsync 正常路径：能将 wfId/taskId/user/increment 正确放入 multipart 字段，
        /// 文件流和文件名也能正确传递，并返回服务端响应列表。
        /// </summary>
        [Fact]
        public async Task UploadFile_WithValidArgs_ShouldSendMultipartAndReturnResult()
        {
            // Arrange：在 handler 内部提取断言所需数据，避免 HttpRequestMessage Dispose 后访问
            string capturedQuery = null;
            string capturedContentType = null;
            var httpClient = CreateSequentialHttpClient(
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("token_for_upload", Encoding.UTF8, "text/plain")
                },
                req =>
                {
                    capturedQuery = req.RequestUri?.Query;
                    capturedContentType = req.Content?.Headers?.ContentType?.MediaType;
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("[{\"fileId\":\"abc\"}]", Encoding.UTF8, "application/json")
                    };
                });
            var client = CreateClient(httpClient);
            var fileContent = Encoding.UTF8.GetBytes("hello");
            using var ms = new MemoryStream(fileContent);
            var attachments = new Dictionary<string, (Stream, string)>
            {
                ["attach1"] = (ms, "hello.txt")
            };

            // Act
            var result = await client.UploadFileAsync("wf_001", null, "zhangsan", 0, attachments);

            // Assert：请求已发出且响应被正确解析
            Assert.NotNull(result);
            Assert.Single(result);
            // 请求 URL 应包含令牌 query string
            Assert.Contains("eco-oauth2-token=", capturedQuery);
            // 内容类型应为 multipart
            Assert.Contains("multipart", capturedContentType);
        }

        /// <summary>
        /// 验证 UploadFileAsync 当服务端返回非 2xx 时抛出 HttpRequestException。
        /// </summary>
        [Fact]
        public async Task UploadFile_WhenServerReturnsError_ShouldThrow()
        {
            var httpClient = CreateSequentialHttpClient(
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("token_upload", Encoding.UTF8, "text/plain")
                },
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
            var client = CreateClient(httpClient);
            using var ms1 = new MemoryStream(new byte[] { 1 });
            var attachments = new Dictionary<string, (Stream, string)>
            {
                ["f"] = (ms1, "f.bin")
            };

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.UploadFileAsync("wf_001", null, "user1", 0, attachments));
        }

        /// <summary>
        /// 验证 UploadFileAsync 参数校验：wfId 和 taskId 都为空时应抛出 ArgumentException。
        /// </summary>
        [Fact]
        public async Task UploadFile_WithBothWfIdAndTaskIdEmpty_ShouldThrowArgumentException()
        {
            var httpClient = CreateHttpClient("token_upload");
            var client = CreateClient(httpClient);
            using var ms2 = new MemoryStream(new byte[] { 1 });
            var attachments = new Dictionary<string, (Stream, string)>
            {
                ["f"] = (ms2, "f.bin")
            };

            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.UploadFileAsync(null, null, "user1", 0, attachments));
        }

        /// <summary>
        /// 验证 UploadFileAsync 参数校验：attachments 为空时应抛出 ArgumentException。
        /// </summary>
        [Fact]
        public async Task UploadFile_WithEmptyAttachments_ShouldThrowArgumentException()
        {
            var httpClient = CreateHttpClient("token_upload");
            var client = CreateClient(httpClient);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.UploadFileAsync("wf_001", null, "user1", 0,
                    new Dictionary<string, (Stream, string)>()));
        }

    }
}
