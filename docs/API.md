# BPM API 接口文档

> 基于《BPM流程接口开发规范V1.10》，C# .NET Standard 2.0 客户端类库接口说明

---

## 目录

1. [概述](#1-概述)
2. [快速开始](#2-快速开始)
3. [认证接口](#3-认证接口)
4. [流程接口（2.2~2.20）](#4-流程接口)
5. [辅助接口（3.1~3.9）](#5-辅助接口)
6. [运维接口（4.1~4.4）](#6-运维接口)
7. [公共数据结构](#7-公共数据结构)
8. [错误处理](#8-错误处理)

---

## 1. 概述

本项目包含两个子项目：

| 项目 | 框架 | 说明 |
|---|---|---|
| `BpmApiClient` | .NET Standard 2.0 | BPM REST API 客户端类库，封装所有接口的 HTTP 调用 |
| `BpmApiHost` | .NET Core 2.2 | Web API 宿主程序，对外代理 BPM 接口 |

**调用链路：**
```
业务系统 → BpmApiHost (ASP.NET Core 2.2) → BpmApiClientImpl → BPM v95 服务器
```

**认证方式：**  
接口通过 `appid` 和 `secret` 获取 AccessToken（JWT），有效期 30 分钟。  
客户端内部自动缓存令牌，到期前 60 秒自动刷新，调用方无需手动管理。

---

## 2. 快速开始

### 2.1 配置 appsettings.json

```json
{
  "BpmApiClient": {
    "BaseUrl": "http://your-bpm-server-address",
    "AppId": "your-app-id",
    "Secret": "your-secret",
    "Timeout": "00:00:30"
  }
}
```

### 2.2 Startup.cs 注册服务

```csharp
var bpmOptions = new BpmApiClientOptions();
Configuration.GetSection("BpmApiClient").Bind(bpmOptions);

services.AddSingleton<HttpClient>(sp => new HttpClient
{
    BaseAddress = new Uri(bpmOptions.BaseUrl),
    Timeout = bpmOptions.Timeout
});
services.AddSingleton(bpmOptions);
services.AddSingleton<IBpmApiClient, BpmApiClientImpl>();
```

### 2.3 注入并调用

```csharp
public class MyService
{
    private readonly IBpmApiClient _bpm;

    public MyService(IBpmApiClient bpm) => _bpm = bpm;

    public async Task<string> StartExpenseApproval(string userId)
    {
        var result = await _bpm.InitProcessAsync(new InitProcessRequest
        {
            WfDef = "expense-approval",
            InitUser = userId
        });
        return result.WfId; // 返回流程实例 ID
    }
}
```

---

## 3. 认证接口

### 3.1 获取令牌

| 项目 | 说明 |
|---|---|
| 规范章节 | 1.3 |
| BPM 原始接口 | `GET /oauth2/access-token?appid=&secret=` |
| 客户端方法 | `Task<string> GetAccessTokenAsync(CancellationToken)` |
| Host 端点 | `GET /api/bpm/token` |

**说明：** 令牌有效期约 30 分钟，客户端自动缓存，通常无需显式调用此方法。

---

## 4. 流程接口

### 4.1 启动流程（2.2）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.2 |
| BPM 原始接口 | `POST /web-service/bpm/v95/process/init` |
| 客户端方法 | `Task<InitProcessResult> InitProcessAsync(InitProcessRequest)` |
| Host 端点 | `POST /api/bpm/process/init` |

**请求体（`InitProcessRequest`）：**

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| wfDef | string | ✅ | 流程模板自定义标识 |
| initUser | string | ✅ | 流程发起人账号 |
| forward | bool? | - | 开始环节是否自动流转，默认 false |
| wfDesc | string | - | 流程名称 |
| instCode | string | - | 流程编码 |
| avoiders | string | - | 回避用户 ID，多个用 "####" 分隔 |
| formData | FormDataItem[] | - | 表单组件赋值 |
| gridData | GridDataItem[] | - | 数据方阵赋值 |
| assigneeData | AssigneeDataItem[] | - | 环节经办人员指定 |

**返回（`InitProcessResult`）：**

| 参数 | 类型 | 说明 |
|---|---|---|
| wfId | string | 流程实例 ID |
| wfStatus | int | 流程状态：11=未启动，1=进行中，3=结束，13=异常结束 |
| startTaskId | string | 开始环节 ID |
| nextList | NextTaskInfo[] | 下一环节列表 |

---

### 4.2 驱动流程（提交/退回）（2.3）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.3 |
| BPM 原始接口 | `POST /web-service/bpm/v95/process/forward` |
| 客户端方法 | `Task<ForwardProcessResult> ForwardProcessAsync(ForwardProcessRequest)` |
| Host 端点 | `POST /api/bpm/process/forward` |

**请求体（`ForwardProcessRequest`）：**

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| taskId | string | ✅ | 当前环节 ID |
| asnUser | string | ✅ | 流程办理人账号 |
| approveCode | string | - | 意见编码：0=不同意，1=同意 |
| approveDesc | string | - | 意见描述 |
| cosignFlag | string | - | C=意见征询，D=委托办理 |
| cosignUser | string | - | 征询/委托人（cosignFlag 不为空时必填） |
| backType | int? | - | 0=不退回，1=退回到人，2=退回到环节 |
| backTasks | string | - | 退回到环节：格式 "流程ID@环节ID"，多个用 "####" 分隔 |
| backDispatcher | string | - | ALL=全部办理人，HIS=历史办理人 |
| formData | FormDataItem[] | - | 表单赋值 |
| gridData | GridDataItem[] | - | 数据方阵赋值 |
| assigneeData | AssigneeDataItem[] | - | 指定经办人 |

---

### 4.3 附件上传（2.4）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.4 |
| BPM 原始接口 | `POST /web-service/bpm/v95/formdata/upload-file`（multipart/form-data） |
| 客户端方法 | `Task<IList<object>> UploadFileAsync(wfId, taskId, user, increment, attachments)` |
| Host 端点 | `POST /api/bpm/formdata/upload-file` |

**说明：**
- `wfId` 和 `taskId` 至少填一个
- `attachments` 的 key 格式：普通附件用 `def#0#fileName`，数据方阵行附件用 `def#rowInd#fileName`
- `increment`：0=替换更新（默认），1=增量更新

---

### 4.4 获取环节信息（2.5）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.5 |
| BPM 原始接口 | `GET /web-service/bpm/v95/process/task-state?wfId=&taskDef=` |
| 客户端方法 | `Task<TaskStateResult> GetTaskStateAsync(wfId, taskDef)` |
| Host 端点 | `GET /api/bpm/process/task-state?wfId=&taskDef=` |

**返回（`TaskStateResult`）：**

| 参数 | 类型 | 说明 |
|---|---|---|
| wfId | string | 流程实例 ID |
| taskDef | string | 环节标识 |
| taskList | TaskStateItem[] | 办理环节列表（含状态、办理人信息） |

**`TaskStateItem.status` 枚举值：**

| 值 | 含义 |
|---|---|
| 1 | 待办 |
| 3 | 办理中 |
| 6 | 已完成 |
| -1 | 未到达 |
| 11 | 已取消 |

---

### 4.5 环节办理申请（2.6）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.6 |
| BPM 原始接口 | `GET /web-service/bpm/v95/process/apply?taskId=&asnUser=&asnDate=` |
| 客户端方法 | `Task ApplyTaskAsync(taskId, asnUser, asnDate?)` |
| Host 端点 | `GET /api/bpm/process/apply` |

**说明：** 在打开表单前调用，校验当前用户是否有权限办理该环节。

---

### 4.6 流程撤回（2.7）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.7 |
| BPM 原始接口 | `POST /web-service/bpm/v95/maintain/workflow-backoff` |
| 客户端方法 | `Task<BackoffResult> WorkflowBackoffAsync(WorkflowBackoffRequest)` |
| Host 端点 | `POST /api/bpm/maintain/workflow-backoff` |

**请求体（`WorkflowBackoffRequest`）：**

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| wfId | string | ✅ | 流程实例 ID |
| opUser | string | ✅ | 操作人账号 |
| taskId | string | ✅ | 撤回到的环节 ID |

---

### 4.7 流程取消（2.8）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.8 |
| BPM 原始接口 | `POST /web-service/bpm/v95/maintain/cancelwf` |
| 客户端方法 | `Task CancelWfAsync(CancelWfRequest)` |
| Host 端点 | `POST /api/bpm/maintain/cancelwf` |

---

### 4.8 流程环节回滚（2.9）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.9 |
| BPM 原始接口 | `POST /web-service/bpm/v95/process/rollback` |
| 客户端方法 | `Task<BackoffResult> RollbackAsync(WorkflowBackoffRequest)` |
| Host 端点 | `POST /api/bpm/process/rollback` |

---

### 4.9 获取办理锁（2.10）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.10 |
| BPM 原始接口 | `GET /web-service/bpm/v95/process/try-lock?taskId=&opUser=` |
| 客户端方法 | `Task TryLockAsync(ProcessLockRequest)` |
| Host 端点 | `GET /api/bpm/process/try-lock` |

---

### 4.10 释放办理锁（2.11）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.11 |
| BPM 原始接口 | `GET /web-service/bpm/v95/process/release-lock?taskId=&opUser=` |
| 客户端方法 | `Task ReleaseLockAsync(ProcessLockRequest)` |
| Host 端点 | `GET /api/bpm/process/release-lock` |

---

### 4.11 预跑测试（2.12）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.12 |
| BPM 原始接口 | `POST /web-service/bpm/v95/trivial/task-linetest` |
| 客户端方法 | `Task<object> TaskLinetestAsync(TaskLinetestRequest)` |
| Host 端点 | `POST /api/bpm/trivial/task-linetest` |

---

### 4.12 获取表单信息（2.13）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.13 |
| BPM 原始接口 | `GET /web-service/bpm/v95/formdata/full-get?wfId=` |
| 客户端方法 | `Task<object> GetFormDataAsync(wfId)` |
| Host 端点 | `GET /api/bpm/formdata/full-get?wfId=` |

---

### 4.13 获取待办列表（2.14）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.14 |
| BPM 原始接口 | `POST /web-service/bpm/v95/searchlist/toassign` |
| 客户端方法 | `Task<object> GetPendingTasksAsync(PendingTaskSearchRequest)` |
| Host 端点 | `POST /api/bpm/searchlist/toassign` |

**请求体（`PendingTaskSearchRequest`）：**

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| asnUser | string | ✅ | 办理人账号 |
| page | int? | - | 页码，默认 1 |
| rows | int? | - | 每页记录数，默认 5 |
| group_id | string | - | 流程类别 ID |
| template_id | string | - | 流程模板标识 |
| init_user | string | - | 发起人 ID |

---

### 4.14 表单赋值（2.15）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.15 |
| BPM 原始接口 | `POST /web-service/bpm/v95/formdata/update` |
| 客户端方法 | `Task UpdateFormDataAsync(FormDataUpdateRequest)` |
| Host 端点 | `POST /api/bpm/formdata/update` |

---

### 4.15 获取流程实例列表（2.16）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.16 |
| BPM 原始接口 | `POST /web-service/bpm/v95/searchlist/tomonitor` |
| 客户端方法 | `Task<object> GetMonitorListAsync(MonitorSearchRequest)` |
| Host 端点 | `POST /api/bpm/searchlist/tomonitor` |

---

### 4.16 我发起的列表（2.17）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.17 |
| BPM 原始接口 | `POST /web-service/bpm/v95/searchlist/selfinit` |
| 客户端方法 | `Task<object> GetSelfInitListAsync(SelfInitSearchRequest)` |
| Host 端点 | `POST /api/bpm/searchlist/selfinit` |

---

### 4.17 我经办的列表（2.18）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.18 |
| BPM 原始接口 | `POST /web-service/bpm/v95/searchlist/selfprocess` |
| 客户端方法 | `Task<object> GetSelfProcessListAsync(SelfProcessSearchRequest)` |
| Host 端点 | `POST /api/bpm/searchlist/selfprocess` |

---

### 4.18 抄送我的列表（2.19）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.19 |
| BPM 原始接口 | `POST /web-service/bpm/v95/searchlist/tocc` |
| 客户端方法 | `Task<object> GetToCcListAsync(ToCcSearchRequest)` |
| Host 端点 | `POST /api/bpm/searchlist/tocc` |

---

### 4.19 流程实例详情（2.20）

| 项目 | 说明 |
|---|---|
| 规范章节 | 2.20 |
| BPM 原始接口 | `GET /web-service/bpm/v95/process/wfdetail?wfId=` |
| 客户端方法 | `Task<object> GetWfDetailAsync(wfId)` |
| Host 端点 | `GET /api/bpm/process/wfdetail?wfId=` |

---

## 5. 辅助接口

### 5.1 检查已配置的角色（3.1）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.1 |
| BPM 原始接口 | `GET /web-service/bpm/v95/trivial/template-check-rolecode?roleCode=` |
| 客户端方法 | `Task<object> CheckRoleCodeAsync(roleCode)` |
| Host 端点 | `GET /api/bpm/trivial/template-check-rolecode?roleCode=` |

---

### 5.2 获取流程历史列表（3.2）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.2 |
| BPM 原始接口 | `GET /web-service/bpm/v95/trivial/workflow-history?wfId=` |
| 客户端方法 | `Task<object> GetWorkflowHistoryAsync(wfId)` |
| Host 端点 | `GET /api/bpm/trivial/workflow-history?wfId=` |

---

### 5.3 获取流程历史环节—办理人（3.3）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.3 |
| BPM 原始接口 | `GET /web-service/bpm/v95/process/full-task-trace?wfId=` |
| 客户端方法 | `Task<object> GetFullTaskTraceAsync(wfId)` |
| Host 端点 | `GET /api/bpm/process/full-task-trace?wfId=` |

---

### 5.4 获取流程历史环节—层级（3.4）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.4 |
| BPM 原始接口 | `GET /web-service/bpm/v95/process/full-tasklevel-trace?wfId=` |
| 客户端方法 | `Task<object> GetFullTaskLevelTraceAsync(wfId)` |
| Host 端点 | `GET /api/bpm/process/full-tasklevel-trace?wfId=` |

---

### 5.5 流程表单打印（3.5）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.5 |
| BPM 原始接口 | `POST /web-service/bpm/v95/trivial/print-wf?wfId=&printUser=&printDef=` |
| 客户端方法 | `Task<byte[]> PrintWfAsync(wfId, printUser, printDef)` |
| Host 端点 | `POST /api/bpm/trivial/print-wf` |

---

### 5.6 添加流程委托（3.6）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.6 |
| BPM 原始接口 | `POST /web-service/bpm/v95/trivial/delegate-addasn` |
| 客户端方法 | `Task DelegateAddAsync(DelegateAddRequest)` |
| Host 端点 | `POST /api/bpm/trivial/delegate-addasn` |

---

### 5.7 取消流程委托（3.7）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.7 |
| BPM 原始接口 | `POST /web-service/bpm/v95/trivial/delegate-invalidasn` |
| 客户端方法 | `Task DelegateInvalidAsync(DelegateInvalidRequest)` |
| Host 端点 | `POST /api/bpm/trivial/delegate-invalidasn` |

---

### 5.8 获取类别列表（3.8）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.8 |
| BPM 原始接口 | `POST /web-service/bpm/v95/trivial/groups` |
| 客户端方法 | `Task<object> GetGroupsAsync()` |
| Host 端点 | `GET /api/bpm/trivial/groups` |

---

### 5.9 获取模板列表（3.9）

| 项目 | 说明 |
|---|---|
| 规范章节 | 3.9 |
| BPM 原始接口 | `POST /web-service/bpm/v95/trivial/templates` |
| 客户端方法 | `Task<object> GetTemplatesAsync(TemplateSearchRequest)` |
| Host 端点 | `POST /api/bpm/trivial/templates` |

---

## 6. 运维接口

### 6.1 变更流程状态（4.1）

| 项目 | 说明 |
|---|---|
| 规范章节 | 4.1 |
| BPM 原始接口 | `POST /web-service/bpm/v95/maintain/change-wfstatus` |
| 客户端方法 | `Task ChangeWfStatusAsync(ChangeWfStatusRequest)` |
| Host 端点 | `POST /api/bpm/maintain/change-wfstatus` |

**`targetStatus` 枚举值：**

| 值 | 含义 |
|---|---|
| 1 | 进行中 |
| 3 | 已完成 |
| 13 | 异常结束 |
| 11 | 取消 |

---

### 6.2 变更环节（人员）办理状态（4.2）

| 项目 | 说明 |
|---|---|
| 规范章节 | 4.2 |
| BPM 原始接口 | `POST /web-service/bpm/v95/maintain/change-taskstatus` |
| 客户端方法 | `Task ChangeTaskStatusAsync(ChangeTaskStatusRequest)` |
| Host 端点 | `POST /api/bpm/maintain/change-taskstatus` |

---

### 6.3 变更环节（节点）状态（4.3）

| 项目 | 说明 |
|---|---|
| 规范章节 | 4.3 |
| BPM 原始接口 | `POST /web-service/bpm/v95/maintain/change-levelstatus` |
| 客户端方法 | `Task ChangeLevelStatusAsync(ChangeLevelStatusRequest)` |
| Host 端点 | `POST /api/bpm/maintain/change-levelstatus` |

**说明：** `taskLevel`（环节层级）和 `taskDef`（环节标识）二选一填写。

---

### 6.4 变更环节（节点）办理人（4.4）

| 项目 | 说明 |
|---|---|
| 规范章节 | 4.4 |
| BPM 原始接口 | `POST /web-service/bpm/v95/maintain/task-change-assignee` |
| 客户端方法 | `Task TaskChangeAssigneeAsync(TaskChangeAssigneeRequest)` |
| Host 端点 | `POST /api/bpm/maintain/task-change-assignee` |

---

## 7. 公共数据结构

### 7.1 FormDataItem（表单组件赋值）

```json
{ "def": "组件自定义标识", "val": "值（多选用####分隔）" }
```

### 7.2 GridDataItem（数据方阵赋值）

```json
{
  "def": "方阵标识",
  "val": [
    { "rowInd": 1, "cells": { "列标识": "列值" } }
  ]
}
```

### 7.3 AssigneeDataItem（环节经办人）

```json
{ "def": "环节标识（环节备注栏查看）", "val": "办理人账号（多个####分隔）" }
```

### 7.4 NextTaskInfo（下一环节信息）

| 字段 | 类型 | 说明 |
|---|---|---|
| taskName | string | 环节名称 |
| taskDef | string | 环节层级自定义标识 |
| taskId | string | 环节 ID |
| taskType | string | NORMAL/COUNTERSIGN/START/DELEGATE/BACK |
| driveType | string | PARALLEL=会签/EXCLUSIVE=或签/SEQUENCE=顺序签/PREEMPT=抢占签 |
| assigeeName | string | 办理人姓名 |
| assigneeLink | string | 办理人 ID |
| assigneeAccount | string | 办理人账号 |

### 7.5 BPM 通用响应格式

```json
{
  "code": 0,
  "message": null,
  "result": { }
}
```

- `code = 0`：成功
- `code != 0`：业务异常，`message` 为错误描述，客户端抛出 `BpmApiException`

---

## 8. 错误处理

### 8.1 BpmApiException

当 BPM 返回 `code != 0` 时，`BpmApiClientImpl` 抛出 `BpmApiException`：

```csharp
try
{
    var result = await _bpmClient.InitProcessAsync(request);
}
catch (BpmApiException ex)
{
    Console.WriteLine($"BPM 错误码: {ex.Code}, 描述: {ex.Message}");
}
```

### 8.2 参数校验异常

必填参数为空时，方法抛出 `ArgumentException` 或 `ArgumentNullException`，无需调用远端服务即可提前失败。

### 8.3 HTTP 状态码

客户端遵循 HTTP 状态码语义：非 2xx 状态码时调用 `EnsureSuccessStatusCode()` 抛出 `HttpRequestException`。

---

*文档版本：V1.0，对应 BPM 流程接口开发规范 V1.10*
