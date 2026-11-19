# MedX Backend

MedX Backend 是基于 ASP.NET Core MVC 的患者资料管理 API，使用 PostgreSQL 保存数据，并通过 JWT 保护需要登录的接口。项目还提供 Swagger API 文档、SMTP 密码重置邮件及 Serilog 日志。

## 技术栈

- .NET 10 / ASP.NET Core
- PostgreSQL，通过 Npgsql 访问
- JWT Bearer 身份验证
- Swagger / OpenAPI
- Serilog 控制台与滚动文件日志

## 环境要求

- .NET 10 SDK
- PostgreSQL 数据库
- 如需密码重置邮件，需可用的 SMTP 服务

## 配置

应用使用 ASP.NET Core 配置系统。可以在本机的 `medxbackend/appsettings.Development.json` 中配置开发值，或使用环境变量；不要将真实密码、JWT 密钥或 SMTP 凭据提交到版本控制。

以下是所需配置项的示例，值需按本机环境替换：

```json
{
  "ConnectionStrings": {
    "PostgreSql": "Host=localhost;Port=5432;Database=medx;Username=YOUR_DB_USER;Password=YOUR_DB_PASSWORD"
  },
  "Jwt": {
    "Key": "REPLACE_WITH_A_LONG_RANDOM_SECRET",
    "Issuer": "medxbackend",
    "Audience": "medxclient",
    "ExpireMinutes": 60,
    "RefreshTokenExpireDays": 30
  },
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587,
    "Username": "YOUR_SMTP_USER",
    "Password": "YOUR_SMTP_PASSWORD",
    "From": "no-reply@example.com"
  },
  "PasswordReset": {
    "Url": "http://localhost:5173/reset-password"
  },
  "Barcode": {
    "Format": "Code128"
  },
  "NetworkPrinter": {
    "Host": "YOUR_PRINTER_HOST",
    "Port": 9100,
    "CopiesPerLabel": 1,
    "PrinterName": "MedX Network Printer",
    "PrintWidthDots": 440,
    "LabelWidthMillimeters": 55,
    "LabelHeightMillimeters": 25,
    "GapMillimeters": 2
  }
}
```

`Barcode:Format` 仅接受 `Code39` 或 `Code128`，并同时决定条码 PDF 的格式。网络打印由 `NetworkPrinter` 配置；`CopiesPerLabel` 设置每张标签的打印份数（1–9999，默认 1）。调用打印接口前需确保打印机主机和端口可从应用服务器访问。

对应的环境变量采用双下划线表示配置层级，例如：

```powershell
$env:ConnectionStrings__PostgreSql = "Host=localhost;Port=5432;Database=medx;Username=YOUR_DB_USER;Password=YOUR_DB_PASSWORD"
$env:Jwt__Key = "REPLACE_WITH_A_LONG_RANDOM_SECRET"
$env:Jwt__Issuer = "medxbackend"
$env:Jwt__Audience = "medxclient"
$env:Smtp__Host = "smtp.example.com"
$env:Smtp__Port = "587"
$env:Smtp__From = "no-reply@example.com"
$env:Barcode__Format = "Code128"
$env:NetworkPrinter__Host = "YOUR_PRINTER_HOST"
$env:NetworkPrinter__Port = "9100"
$env:NetworkPrinter__CopiesPerLabel = "1"
```

`Smtp:Username` 和 `Smtp:Password` 在 SMTP 服务要求身份验证时也必须设置。`PasswordReset:Url` 可选；未设置时，邮件链接默认使用 `http://localhost:5173/reset-password`。

## 统一同步入口（需要 JWT）

新增 `medxbackend/Controllers/SyncController.cs` 作为统一入口。
原 LisController 和 CrmController 保留，各自继续处理旧 URL，
Swagger 保留 Lis、Crm 分组，新入口归入 Sync。
三个控制器通过 `ILisSyncApiService`、`ICrmSyncApiService` 共用同步逻辑，
实现在 `medxbackend/Services/LisSyncApiService.cs` 和 `CrmSyncApiService.cs`，
不复制业务逻辑，也不通过控制器互相调用。

新入口使用 `/api/Sync/lis/...` 和 `/api/Sync/crm/...`。
原 `/api/Lis/...`、`/api/Crm/...` URL 全部由原控制器保留，
HTTP 方法、请求字段、验证、返回状态、Token 来源和同步队列行为不变。

| 方法 | 统一入口 | 用途 |
|------|----------|------|
| POST | `/api/Sync/orderpatient` | 根据 formtype 和规则表同步订单及患者资料到启用系统 |
| POST | `/api/Sync/orderpatients` | 先按日期范围准备，再按 centreIds 批量同步，共用一个批次 |
| POST | `/api/Sync/preview` | 只查询日期范围内已存在的同步队列，不修改数据 |
| GET | `/api/Sync/statistics` | 查询同步队列总数、待同步数、成功数和失败数 |
| GET | `/api/Sync/recent` | 查询全局最后一个已完成批次的同步历史，默认返回批次内全部记录 |
| GET / POST | `/api/Sync/lis/token` | 获取 LIS Token |
| GET | `/api/Sync/lis/order/{centreid}` | 查询 LIS 订单 |
| POST | `/api/Sync/lis/createorder` | 同步单个 LIS 订单 |
| POST | `/api/Sync/lis/createorder/retry` | 重试建单 |
| POST | `/api/Sync/lis/createorders` | 批量同步 LIS 订单 |
| POST | `/api/Sync/lis/createorders/preview` | 准备并预览同步队列 |
| POST | `/api/Sync/lis/updateorder` | 更新 LIS Order |
| POST | `/api/Sync/lis/updateorderpatient` | 更新 LIS Patient |
| GET / POST | `/api/Sync/crm/token` | 获取 CRM Token |
| POST | `/api/Sync/crm/syncPatientdetailsFinal` | 同步患者到 CRM |
| POST | `/api/Sync/crm/syncPatientdetailsFinal/preview` | 准备并预览 CRM 队列 |

调用示例见 `medxbackend/Sync.http`，旧接口示例仍保留在 `Lis.http` 和 `Crm.http`。
LIS 建单、重试、手动更新和查询仍需 `X-Lis-Access-Token`；预览不需 LIS Token。
`POST /api/Sync/lis/createorder` 只接收 `{"centreId":"MR2610-00001"}`，
不再暴露 targetSystem 参数。按患者数据库 formtype 检查启用的 LIS 规则，
无规则返回 409，不创建队列也不调用 LIS；有规则时补齐缺失的 pending LIS 队列并调用建单。
此接口仍只建单，不会调用 CRM 或更新已建单患者；多系统创建／更新使用 `/api/Sync/orderpatient`。
原 `/api/Lis/createorder` 及显式重试接口的请求和行为不变。
CRM 同步仍自动使用配置的凭据获取 Token。
患者最终资料更新接口仍位于 PatientController，数据库成功后按 formtype 启用规则
自动更新已有 LIS／CRM 记录，不增加内部 HTTP 请求或自动建单；
LIS 需本地确认已同步，CRM 以 updateOnly 请求确认远端存在，兼容历史队列状态。

### 最近同步记录

`GET /api/Sync/recent?count=5&targetSystem=CRM` 需要 JWT。
`count` 可省略，省略时返回所选批次内全部历史记录，指定时必须为正整数；
指定 count 时限制历史记录总条数，不限制返回的批次元数据。
`targetSystem` 可省略表示不限制系统，
指定时仅允许 LIS 或 CRM（忽略大小写及首尾空格），空白参数与省略一样表示全部系统。
无效参数返回 400。
调用 `public.get_latest_patientdetails_sync_batch(p_target_system => @targetSystem)`，
由函数按 completed_at DESC、id DESC 选择全局最后一个已完成批次，
再按 targetSystem 过滤，并在批次内按 sync_id 保留最后一次尝试。
指定系统在最新批次中没有历史记录时，results 和 batches 均为空，不回退查询该系统的旧批次。
批次表不再包含 target_system；results 的 targetSystem 来自历史记录，同一批次可返回多个系统。
同一批次内重试后的结果替代该队列早先的尝试，不重复返回。
另查询 `patientdetails_sync_batches` 的最新已完成批次元数据，以保留空批次的展示。
“已完成”不等于成功：包含失败和结果未知的批次，但不选择仍在处理的批次。
函数结果按 `attempted_at DESC NULLS LAST, history_id DESC` 排序，不受当前规则是否启用影响。

返回 `totalCount`（本次实际返回的历史条数）、`results` 和 `batches`。
results 每项包含 `id`（原同步队列 id）、`historyId`、`batchId`、`completedAt`、
`targetSystem`、`centreId`、`formType`、`syncStatus`、`attemptCount`、
`lastAttemptAt`、`syncedAt`、`remoteRecordId`、`lastHttpStatus`、`lastError`、`dailyNo`。
状态、结果时间和错误来自该次历史记录，不使用队列后续同步的结果覆盖；
`attemptCount` 是队列当前累计尝试次数，不是历史快照中的次数。
`dailyNo` 直接使用函数返回的 dailyno，由函数根据 centreId 和 formType 查询 `patientdetails_final.dailyno`；
找不到患者或 dailyno 为空时返回 null，不丢弃历史记录。
若同一 centreId／formType 匹配多个患者，返回数据库错误，而非任意选取或重复返回队列。
`batches` 每项包含 `id`、`batchStatus`、`startedAt`、`completedAt`，不再包含 targetSystem。
不指定系统时，即使最新批次没有历史记录也返回其批次信息，results 为空；不会回退旧批次。
指定系统时，按最新批次内是否存在该系统历史记录决定是否返回批次信息。
无匹配记录返回 200 和空列表，数据库错误返回 500；不会调用外部系统或修改队列。
不返回 history.payload；`lastError` 仍可能含患者身份信息，仅向获授权人员展示。

### 同步批次与历史记录

`POST /api/Sync/orderpatient`（含 `/patient` 别名）、`POST /api/Sync/orderpatients`
和患者本地保存后的规则自动更新，
现在写入 `patientdetails_sync_batches` 和 `patientdetails_sync_history`。
批量接口每次请求只创建一个批次，所有患者及 CRM／LIS 历史共用 batchId。
单条接口和自动保存仍按系统分别建批，通常每个批次只有一条尝试。
不符合自动更新资格而直接跳过的系统不创建批次。
旧 LIS／CRM 接口、手动重试入口及 preview 不自动记录新历史，原行为不变。

发送前，队列原子认领和历史插入在同一 SQL 语句内提交，并保存本次业务请求快照；
完成时原子更新队列最新状态及对应历史行，不覆盖早先历史 payload。
重新同步会新增批次和历史行。并发认领不会抢占同一队列的处理中尝试；
未结束的历史记录也会阻止再次认领，需先核对处理，不能盲目重发。
HTTP 调用期间不持有数据库事务。CRM 的远端成功确认和自动 updateOnly 行为保持不变。

CRM payload 为实际 upsert 请求正文，LIS 建单为实际 CreateOrder 正文；
LIS 更新将 `UpdateOrder` 和 `UpdateOrderPatient` 两份请求正文组合为一个对象，
由一条历史尝试记录两步更新的整体结果。保存后不从患者表重新生成 payload，
不保存 Token、认证头或密码。history.payload 包含患者资料，应限制访问并设置保留期限。
验证／Token 失败可能发生在业务请求发送前，不能把存在快照理解为远端已收到请求；
若尚未能准备业务 payload，则批次可能没有历史行，失败状态仍保存在批次及原有诊断中。

正常结束后批次根据历史结果标记 succeeded／partial_failed／failed／unknown。
自动 CRM 更新发现远端不存在时 API 仍按原行为返回 Skipped，但历史和批次保存 failed，
不会误记为成功写入。取消／异常退出的活动尝试按 unknown 收尾，并使用独立超时保存状态。
历史或批次保存失败会明确报错，不能据此认定远端回滚；进程崩溃或数据库持续不可用时
可能残留 in_progress，需要人工核对远端并恢复，不能自动重发。

部署前必须确保两张新表、约束及 `get_latest_patientdetails_sync_batch(text)` 函数存在。
批次表不再包含 target_system，创建批次使用 DEFAULT VALUES；目标系统仅写入历史记录，
批量接口已将批次创建移到目标系统循环之外，并在每次尝试时明确指定历史的目标系统。
`Database/schema.sql` 已调整为先建 batches 再建 history，
避免全新安装时外键引用不存在。这里只调整脚本和代码，不自动执行生产数据库迁移；
也不根据队列表伪造过去不存在的 payload／批次历史。因此部署后未产生新批次前，recent 可能为空。

### 按订单编号批量同步

`POST /api/Sync/orderpatients` 需要 JWT，自动使用配置的外部 Token 凭据：

```json
{
  "start_date": "2026-11-10",
  "end_date": "2026-11-11",
  "centreIds": ["MR2611-00001", "MR2611-00002", "MC2611-00001"]
}
```

start_date 和 end_date 均必填，包含两端；单日准备传相同日期。
centreIds 必须是非空数组，各项 trim 后不能为空且不超过 255 字符；重复编号去重。
日期缺失／无效／倒置或编号格式错误返回 400，且不写入任何数据。
先读取全部启用规则，按目标系统汇总去重后的 formtypes，并逐日调用
`update_patientdetails_final_centreid`（Matched、patientdetails_final），生成 centreid 和缺失队列，
不重置已有同步状态。准备范围包含全部启用规则对应的患者，不仅是选中的 centreIds。
准备完成后，按当前保存的 centreid 校验全部选中患者和启用规则，再创建批次并调用外部系统：
不存在患者返回 404，编号不唯一或没有启用规则返回 409，不支持的目标返回 422。
准备时流水号缺口／重复返回 409，其他数据库错误返回 500。
准备或预检失败均不会上传订单；此前日期／系统的 centreid 和队列可能已经生成。
生成函数可能改变编号，centreIds 必须匹配准备完成后的编号；旧编号失效时返回 404。

按每条患者的 formtype 规则分组，依次处理各系统订单，所有系统只建立一个共享批次。
例如 CRM 4 条、LIS 3 条均产生尝试时，一个批次共包含 7 条历史；
3 条订单均匹配 CRM 和 LIS 时，一个批次共包含 6 条历史。
批次状态汇总所有系统的结果，响应各系统的 batchId 相同。
沿用单条入口的建单／更新／安全重试规则；
已有 succeeded 订单会更新，不是只上传 pending；投递阶段不再重新生成 centreId。
LIS 首次建单结果不确定时仍要求人工核对，不自动重建。

普通业务拒绝（如字段校验失败）后继续下一条；Token 获取失败、401／403、
408／429、远端 5xx、连接／超时／无效成功确认或 LIS 两步更新结果不确定时，
停止该系统剩余订单，另一个系统继续。数据库状态保存失败也停止该系统；
若活动历史无法收尾，停止整个批次，避免污染后续尝试。
未发送订单返回 `operation: "NotAttempted"`、`stage: "BatchStopped"`，
不为其伪造已尝试历史。整个请求取消时终止后续处理，并尽力将活动尝试按 unknown 收尾。

全部成功返回 200，部分／全部目标失败返回 207。
响应为 `success`、`results`；results 每项包含 `targetSystem`、`batchId`、
`success`、`stage`、`message` 和 `orders`。
orders 每项包含 `centreId`、`formType`、`result`（沿用单条同步结果结构）。
批次保存失败时，即使个别订单远端成功，目标层 success 仍为 false；
请同时检查目标层和订单层结果，不能据此认定远端回滚。
`GET /api/Sync/recent` 查看全局最后一个已完成批次；最新是批量请求时，可返回两系统的历史。
没有准备出 payload、认领失败或 NotAttempted 的订单不产生尝试历史，因此历史条数可能少于输入数量。
此改动不会合并已有旧批次；单条同步或自动保存随后产生的新批次仍可成为全局最新批次。
原 `/api/Sync/orderpatient` 请求格式和行为不变。

### 同步队列数量统计

`GET /api/Sync/statistics` 需要 JWT，无请求参数，直接只读统计整个
`patientdetails_sync` 表，包含所有日期和目标系统，不受当前规则是否启用影响。

```json
{
  "totalRecords": 100,
  "pendingRecords": 20,
  "succeededRecords": 65,
  "failedRecords": 10
}
```

三个状态计数分别精确对应 `pending`、`succeeded` 和 `failed`。
`in_progress`、`unknown` 只计入 totalRecords，因此三个状态数量之和可能小于总数。
按队列行计数，同一订单同时对应 LIS 和 CRM 时算两条；空表各项返回 0。
数据库查询失败返回 500，不以全零掩盖错误；不会生成队列或触发外部同步。

### 按规则预览指定日期范围的订单

`POST /api/Sync/preview` 需要 JWT，开始日期和结束日期均必填（包含两端）：
原来的 `sequence_date` 已改为 `start_date` 和 `end_date`；单日预览时两者传相同日期。

```json
{
  "start_date": "2026-11-10",
  "end_date": "2026-11-11"
}
```

只调用 `get_patientdetails_sync_by_date(p_start_date, p_end_date)` 查询已有队列。
不调用 PrepareOrders，不生成／修改 centreid、不创建队列、不获取外部 Token、不上传订单。
没有队列的患者不会出现在预览中；准备操作改由 `/api/Sync/orderpatients` 在同步前执行。
不需要传 formtypes、targetSystem 或外部 Token。

返回 `startDate`、`endDate`、`totalCount`、`results`。每项表示一个订单／目标系统组合，
包含 `id`、`targetSystem`、`centreId`、`formType`、`dailyNo`、`syncStatus`、
`attemptCount`、`remoteRecordId`、`lastHttpStatus` 和 `lastError`。
函数按 dailyNo 日期范围、当前启用规则及 `Matched` 患者筛选，
因此历史队列的 sequence_date 为空也不会漏掉匹配订单。列表包含所有同步状态；
已成功记录不会被重新标记为待上传，同一订单对应两个系统时返回两项。
没有启用规则或没有匹配订单时返回 200 和空列表。
`lastError` 可能含患者身份信息，应仅向获授权人员展示。

缺少／无效日期或开始日期晚于结束日期返回 400，且不生成 centreid 或队列。
数据库查询错误返回 500，不以空列表掩盖失败。
此接口在成功或失败时均不会修改数据或向 LIS／CRM 上传。旧 LIS／CRM preview 接口保持不变。

### 根据 formtype 规则同步单条患者

同步失败诊断：`patientdetails_sync.last_error` 保存 JSON 文本，
包含 `system`、`stage`、`httpStatus`（外部原始状态码）和 `responseBody`。
保留完整业务错误结构及全部错误项，不再按 2000 字符截断。
JSON 正文保留对象／数组结构，非 JSON 正文保存为字符串；
空响应保存为空字符串，无远端响应时保存安全错误说明且 httpStatus 可为 null。
凭据和 Token 继续脱敏，结构化患者身份字段也会脱敏；
业务错误消息及非 JSON 错误文本保留患者身份值（如 checksum 错误中的身份证号码），
不替换消息内的姓名、证件号或换行。last_error 因而可能包含患者身份信息，应限制访问权限。
敏感字段／片段以 `[REDACTED]` 替代，因此不等于保存未处理的原始响应。
LIS 建单、自动 Order/Patient 更新，以及规则入口 CRM upsert 和 Token 失败均使用此诊断。
HTTP 成功但确认内容无效时也保留脱敏正文以便核对。
API 即时错误响应沿用原有返回约定，不增加完整诊断字段；
旧指定系统接口的返回约定和是否管理队列的行为不变。
诊断仅影响之后的失败，不能恢复历史已丢弃的错误正文。

规则管理接口（均需 JWT）：

| 方法 | URL | 用途 |
|------|-----|------|
| GET | `/api/Sync/rules?formType=DR` | 列表，formType 可省略，包含禁用规则 |
| GET | `/api/Sync/rules/{id}` | 按自增 id 查询单条 |
| PUT | `/api/Sync/rules/{id}` | 按 id 修改启用状态，正文 `{"isEnabled":false}` |
| DELETE | `/api/Sync/rules/{id}` | 按 id 删除规则 |
| GET | `/api/Sync/rules/{formType}/{targetSystem}` | 查询单条 |
| POST | `/api/Sync/rules` | 新增，正文 `{"formType":"DR","targetSystem":"LIS","isEnabled":true}` |
| PUT | `/api/Sync/rules/{formType}/{targetSystem}` | 修改启用状态，正文 `{"isEnabled":false}`，此字段必填 |
| DELETE | `/api/Sync/rules/{formType}/{targetSystem}` | 删除规则，不删除已有同步队列 |

formType 使用 1–2 位大写字母或数字，新增 targetSystem 只允许 LIS、CRM。
主键为自增 bigint id，formType + targetSystem 为唯一组合。
响应包含 id、formType、targetSystem、isEnabled；旧组合 URL 保留兼容。
id 必须为正整数；PUT 仍只修改启用状态，修改 formType/targetSystem 需删除后新增。
POST 的 isEnabled 默认 true，成功返回 201 和 Location，重复主键返回 409。
查询／更新／删除不存在的规则返回 404。
按 id 删除成功仍返回 204（无正文）；按 formType／targetSystem 删除成功返回 200：

```json
{
  "success": true,
  "message": "Synchronization rule deleted successfully.",
  "formType": "DR",
  "targetSystem": "LIS"
}
```

按组合键删除的客户端应接收 200 和 JSON 正文，而不是只判断 204。
管理规则不会触发同步；新规则在下一次规则同步请求读取时生效。
查询／更新／删除允许指定已有其他大写系统编号，以便清理不支持的规则。

`POST /api/Sync/orderpatient` 接收 `{"centreId":"MR2610-00001"}`，
原 `/api/Sync/patient` 保留为兼容别名，使用同一处理逻辑。
不接收目标系统或 formtype，亦无需 `X-Lis-Access-Token`。
从 `patientdetails_final` 读取唯一患者的 formtype，再读取
`patientdetails_sync_rules` 中同 formtype 且 `is_enabled=true` 的规则。
支持 LIS、CRM；一个 formtype 可以启用两个系统，按 target_system 顺序调用。
某个系统失败仍继续另一个系统，每个系统的验证错误互不影响。
两个系统都使用服务器配置获取 Token，不向调用方返回 Token。

缺少目标系统的队列记录时按 centreid/formtype 创建 pending 记录，不生成或修改 centreid，
已有队列记录不重置。LIS pending 建单、failed 重试建单；
succeeded 或 unknown 且 remote_record_id 与 centreid 完全一致时，
用最新患者资料依次更新 Order 和 Patient，复用现有自动更新服务。
首次建单结果不确定且没有匹配远端编号的 unknown 不自动重试，
返回该系统 409 和 ReconcileOrder 阶段，需人工核对后使用显式建单重试 API。
in_progress 不重复发送。CRM 按 Lab_No__c upsert，认领队列为 in_progress，
记录成功／失败／不确定结果，下次调用可重新 upsert，但不抢占 in_progress。
这些队列状态由新规则入口管理，旧 CRM 接口的行为不变。

全部系统成功返回 200；任意系统失败返回 207，
正文含 centreId、formType、success、results。
results 每项含 targetSystem、success、operation、httpStatus、stage、message，
必须检查各项结果；其中 httpStatus 表示该系统调用流程的 HTTP 状态。
记录不存在返回 404；centreid 多笔匹配或没有启用规则返回 409；
启用规则含不支持的系统返回 422，且不会向任何系统提交；
读取规则／患者失败返回 500，不会静默回退到默认系统。
规则在请求开始时读取为快照；禁用规则不删除已有队列或中断已开始的请求。
旧指定系统 API 行为不变；PatientController 保存后的规则更新不创建远端记录，
CRM 使用 updateOnly 更新已有记录，不依赖历史队列已保存成功状态。

## CRM integration

`POST /api/Crm/token` obtains a Salesforce access token using the
`client_credentials` grant. The endpoint requires the application's JWT in
`Authorization: Bearer <JWT>` and accepts `application/x-www-form-urlencoded`.
Optional `client_id` and `client_secret` form values override server configuration;
omitted or blank values use the configured credentials. `grant_type` also falls
back to `Crm:grant_type`, then defaults to `client_credentials` if not configured.
Only `client_credentials` is supported.

`GET /api/Crm/token` also obtains a token, using only the server-configured
`Crm` values. Include the application's JWT; no request body or credential query
parameters are needed or accepted as overrides. Both methods share validation,
error handling, and `Cache-Control: no-store` / `Pragma: no-cache` response headers.
The outbound Salesforce OAuth request remains a form-encoded POST.

Configure all four values in the `Crm` section of `medxbackend/appsettings.json`
or a local environment-specific JSON configuration:

```json
{
  "Crm": {
    "BaseUrl": "https://YOUR_MY_DOMAIN.my.salesforce.com",
    "grant_type": "client_credentials",
    "client_id": "YOUR_CONSUMER_KEY",
    "client_secret": "YOUR_CONSUMER_SECRET"
  }
}
```

In JSON, use the nested `Crm` section, not the environment variable name
`Crm__BaseUrl`. Environment variables can override the JSON values. Keep real
credentials out of version control; prefer a secret store for deployed secrets:

```powershell
$env:Crm__BaseUrl = "https://YOUR_MY_DOMAIN.my.salesforce.com"
$env:Crm__grant_type = "client_credentials"
$env:Crm__client_id = "YOUR_CONSUMER_KEY"
$env:Crm__client_secret = "YOUR_CONSUMER_SECRET"
```

The server posts to `/services/oauth2/token` on the configured origin. Redirects
are disabled and TLS certificates are validated. Successful responses return
`200` with `access_token`, `instance_url`, `token_type`, and available Salesforce
metadata. Salesforce does not always return an expiry; none is invented.
Responses disable caching, and credentials, tokens, and upstream response bodies
are not logged by this integration.

Invalid input returns `400`; missing/invalid JWT returns `401`; invalid base URL
configuration returns `500`. Salesforce token rejection returns `400`/`401` for
those upstream statuses, otherwise `502`. Network errors and invalid upstream
responses return `502`; timeouts return `504`. Integration errors use Problem
Details without exposing Salesforce response bodies.

See `medxbackend/Crm.http` for example requests (URL-encode form credential values
containing reserved characters).

### Synchronize final patient export rows

#### Preview before synchronizing

`POST /api/Crm/syncPatientdetailsFinal/preview` now follows LIS `PreviewOrders`,
including its local database side effects. Include the application JWT and:

```json
{
  "sequenceDate": "2026-10-29",
  "formTypes": ["DR"],
  "status": "Matched"
}
```

`sequenceDate` defaults to the database's current date; `formTypes` defaults to
`["DR"]` and `status` to `"Matched"`. Form types are trimmed and deduplicated.
This replaces the previous Daily No./payload-preview request and response.

The method calls `update_patientdetails_final_centreid` with target `CRM`,
source `patientdetails_final`, and the selected date, form types and status.
**It can generate/update centreids and prepare local synchronization queue rows.**
Like LIS, it does not obtain a token or send records to Salesforce.

It then returns `{"totalCount":0,"results":[]}` (with actual records when present).
Each result contains `id`, `targetSystem`, `centreId`, `formType`, `sequenceDate`,
`syncStatus`, `attemptCount`, `lastAttemptAt`, `syncedAt`, `remoteRecordId`,
`lastHttpStatus`, `lastError`, and `createdAt`, matching the LIS queue record shape.
Only `target_system = 'CRM'` records for the selected date/form types are returned;
LIS queue rows are excluded. Existing CRM queue rows are included regardless of
sync status. Empty results return 200. Requests with invalid selection criteria
return 400; centreid generation blocked by gaps/duplicates returns 409.
Database errors return 500, and preparation may already have completed if the
subsequent queue read fails. Cancellation does not roll back completed preparation.

CRM credentials are not required. Responses are marked `no-store`.
Actual synchronization below selects by centreid and CRM queue membership and does not update
queue statuses; preview queue preparation alone is not proof of remote success.

#### Submit synchronization

`POST /api/Crm/syncPatientdetailsFinal` requires the application's JWT and a JSON
body such as `{"centreid":"MR2610-00001","targetSystem":"CRM"}`.
Both fields are required and trimmed. Only the exact target value `CRM` is
accepted; other targets return 400 before any database, token or Salesforce call.
The previous `dailyNos` and `statuses` sync requests are no longer supported.

The sync selects `patientdetails_final` using a parameterized centreid and requires
a matching `patientdetails_sync` row with the same centreid, formtype and requested
target system. Queue existence is checked using EXISTS, so multiple queue rows do
not duplicate the patient payload. There is no patient or queue status filter.
Both sync and Excel export use `IPatientDetailsExportService`, but the
Excel/CSV endpoint still uses `export_patientdetails_final` and its original status
filter. No Excel file is generated or parsed during sync.

If the centreid has no matching final patient and CRM queue membership, the request
returns 404 Problem Details before obtaining a CRM token or writing to Salesforce.
Missing/blank centreid or targetSystem values return 400.

The sync's `Lab No.` comes from the saved `patientdetails_final.centreid`, not the
Excel function's selection-dependent row numbering. Empty centreid values are
reported as row failures and are not generated automatically. All other columns
follow the current Excel export projection, including product joining, date
formatting, gestational-week defaults, and ID/passport fallback. No database
migration is required.

The endpoint automatically obtains a token using the configured CRM credentials,
then sequentially sends PATCH requests to the returned `instance_url`:

```text
/services/data/v68.0/sobjects/Lab_Booking__c/Lab_No__c/{URL-encoded Lab No.}
```

**Prerequisite:** `Lab_No__c` must be configured as an External ID in Salesforce,
and should be Unique. The integration user needs object/field create and update
permissions. This uses Salesforce's External ID upsert endpoint, not POST to the
object collection: an existing Lab No. updates the record (204, or 200 with a
valid confirmation containing `success: true`, a non-empty `id`, and `created: false`);
otherwise a new record is created (201, or a valid 200 confirmation with `created: true`).
Non-empty or invalid `errors` invalidate the confirmation. Update-only requests never
accept a creation confirmation as success. The External ID is supplied in the URL, not duplicated
in the JSON body.

| Export column | Salesforce field |
|---|---|
| Lab No. (saved centreid) | Lab_No__c (External ID in URL) |
| Daily No. | Daily_No__c |
| English Name | English_Name__c |
| Chinese Name | Chinese_Name__c |
| Product Code | Product_Code__c |
| Date of Birth | Date_of_Birth__c (text, dd/MM/yyyy) |
| Maternal Age | Maternal_Age__c |
| Gestational Week | Gestational_Week__c |
| Pregnancy | Pregnancy__c |
| Ethnic Origin | Ethnic_Origin__c |
| ID/Passport No. | ID_Passport_No__c |
| Clinic Code | Clinic_Code__c |
| Sample Type | Sample_Type__c |
| Patient Ref No. | Patient_Ref_No__c |
| Date Received | Date_Received__c (yyyy-MM-dd) |
| Date Collected | Date_Collected__c (yyyy-MM-dd) |
| TBC Status | TBC_Status__c |
| Hold Status | Hold_Status__c |
| Remarks | Remarks__c |

`History of Abortion` and `IVF` are intentionally omitted. `Pending_Report__c`,
`Edit_Now__c` and `FOC__c` are always sent as JSON `false`, including on updates.
Other non-date fields are sent as strings as in the supplied Salesforce example;
arrays are joined with `, `. Database NULL becomes JSON null (clearing the CRM
field on update); empty strings remain empty. Dates accept database date/time
values or date strings in dd/MM/yyyy, d/M/yyyy, or yyyy-MM-dd format. They are
formatted without a timezone conversion.

Each row must contain the mapped export columns and a non-empty Lab No.
Missing columns, invalid dates, path-segment markers (`.`/`..`) as Lab Nos.,
and duplicate Lab Nos. within a batch are reported
as local row failures and are not sent. Duplicate matching is case-insensitive.
An unmatched centreid/CRM queue selection returns 404 before writes; invalid
centreid or targetSystem input returns 400.

After obtaining a token, the endpoint returns HTTP 200 with a batch report,
**not a guarantee that every row succeeded**:

```json
{
  "success": true,
  "total": 2,
  "created": 1,
  "updated": 1,
  "failed": 0,
  "unknown": 0,
  "notAttempted": 0,
  "items": [
    { "rowNumber": 1, "labNo": "TEST-001", "dailyNo": "TEST-D001",
      "outcome": "created", "salesforceId": "SALESFORCE_RECORD_ID", "httpStatus": 201 },
    { "rowNumber": 2, "labNo": "TEST-002", "dailyNo": "TEST-D002",
      "outcome": "updated", "httpStatus": 204 }
  ]
}
```

Inspect `success` and every item's `outcome`: `created`, `updated`, `failed`,
`unknown`, or `notAttempted`. Local row validation failures and HTTP 400/409/422 rejections allow subsequent rows
to continue. All other unexpected HTTP statuses (including authentication/permission
failures, rate limits, server errors and redirects), network errors, timeouts,
or malformed success confirmations stop the remaining batch. Network/timeouts/server failures
can leave an **unknown write result**: reconcile in Salesforce before retrying.
There are no automatic write retries, token refresh retries, rollbacks or local
status updates. Upsert avoids creating another record for the same External ID
under a correctly configured Unique constraint, but repeated updates can still
trigger Salesforce automation and overwrite fields. Do not run overlapping syncs
for the same rows.

Upstream messages and patient payloads are not logged or returned as error
details; row reports include a Salesforce machine-readable error code when
available. Initial token errors retain the token endpoint's Problem Details
responses. Requests cancelled by the caller propagate cancellation; writes already
accepted by Salesforce are not rolled back.

## 数据库

`Database/schema.sql` 包含部分辅助表和患者资料相关 PostgreSQL 函数。执行该脚本前，请先确认数据库已包含项目依赖的核心表：`users`、`password_reset_tokens`、`patientdetails` 和 `patientdetails_final`。这些核心表的建表语句不在当前脚本中，需由现有数据库迁移或部署流程提供。

数据库准备完成后，可从仓库根目录使用 `psql` 执行脚本：

```powershell
psql -h localhost -U YOUR_DB_USER -d medx -f Database/schema.sql
```

## 构建与运行

在仓库根目录执行：

```powershell
dotnet restore medxbackend/medxbackend.csproj
dotnet build medxbackend/medxbackend.csproj
dotnet run --project medxbackend/medxbackend.csproj --launch-profile http
```

`http` 启动配置使用 `http://localhost:5032`。Swagger UI 地址为：

```text
http://localhost:5032/swagger
```

开发环境的运行日志输出到控制台，并写入 `medxbackend/logs/medxbackend-YYYYMMDD.log`；日志文件最多保留 30 个滚动文件。

## API 概览

除用户认证相关接口外，`Customer`、`Product` 和 `Patient` 控制器要求在请求中提供 JWT：

```http
Authorization: Bearer <access-token>
```

Swagger UI 可用于查看请求与响应结构；登录后可在 Swagger 的 Authorize 功能中设置访问令牌。

### 用户与令牌

这些接口允许匿名访问：

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/User/register` | 注册账号 |
| POST | `/api/User/login` | 登录并获取访问令牌和刷新令牌 |
| POST | `/api/User/refresh` | 轮换刷新令牌 |
| POST | `/api/User/forgot-password` | 请求密码重置邮件 |
| POST | `/api/User/reset-password` | 使用有效令牌重置密码 |

### LIS 系统对接（需要 JWT）

配置 `Lis:BaseUrl`（或环境变量 `Lis__BaseUrl`）为实际 LIS 服务地址，例如 `https://lis.example.com/LIS-WebAPI`，不包含 `/api/token`。生产环境应使用 HTTPS。

单笔和批量建单的 `SiteCode` 读取 JSON 中 `Lis` 节点下的 `SiteCode`，当前配置为 `"SiteCode": "MS"`，也可用环境变量 `Lis__SiteCode` 覆盖。读取时去除首尾空格；未配置时保留原来的 `LAB`，配置为空时由现有订单校验拒绝提交。

### 查询 LIS 订单

`GET /api/Lis/order/{centreid}` 需要本系统 JWT 和 `X-Lis-Access-Token`
请求头（LIS Token 不带 Bearer 前缀）。后端使用现有 LIS 客户端发起
`GET {Lis:BaseUrl}/api/Order/{URL-encoded centreid}`，保留配置的 BaseUrl 路径。
centreid 去除首尾空格，不能为空、超过 255 字符或为 `.` / `..`。
无需请求体，不查询或修改本地患者／队列，也不自动获取 LIS Token。

成功时返回 LIS JSON 及成功状态码，204 返回无内容。外部
400/401/403/404 保留对应状态，其他失败／网络错误／无效 JSON 返回 502，
超时返回 504。错误使用 Problem Details，不透传外部错误正文。
请求取消传递至 LIS 调用，响应设置 no-store/no-cache，避免缓存患者数据。

### 更新 LIS 订单和患者

现有同步入口 `POST /api/Lis/createorder`、`POST /api/Lis/createorders`、
`POST /api/Lis/createorder/retry` 不预查远端订单，直接调用外部
`POST api/CreateOrder/`。LIS 返回精确匹配同一 centreid 的
`Duplicate Data: <centreid>` 时视为已存在，标记队列 succeeded，并返回
`alreadySynced: true`。重复仅表明订单存在，**不表示 LIS 订单或患者资料已更新**；
需要更新时使用下面的两个独立更新接口。

原有队列状态限制不变：普通单笔／批量仅处理 pending，
retry 仅处理 failed／unknown；succeeded／in_progress 不会自动重发。
批量仍在首次失败时停止，并返回 207 及未处理数量。
发生写入异常时不会自动重试。

`POST /api/Lis/updateorder` 和 `POST /api/Lis/updateorderpatient` 均接收
`{"centreId":"MR2610-00001"}`，要求本系统 JWT 和 `X-Lis-Access-Token`
请求头。按 centreid 匹配 LIS 队列，并通过 centreid/formtype 关联最终患者表，
从最新数据库资料构造请求；不生成新编号。没有匹配返回 404，多笔匹配返回 409。

- 订单更新：外部 `PATCH {Lis:BaseUrl}/api/UpdateOrder/`，发送 `LabId`
  （centreid）、`ModifiedBy`（患者 userid 对应的 users.username）、
  `SiteCode`（Lis 配置）、`ReferringDoctorCode`（refcustordr）、
  `BillingDoctorCode`（billtocustordr）。以上字段不能为空。
- 患者更新：外部 `PATCH {Lis:BaseUrl}/api/UpdateOrderPatient/`，发送
  `LabId`、`ModifiedBy` 和嵌套的 `patient` 对象，字段为 `HKID`、`patientNameEng`、
  `patientNameChi`、`sex`、`ageType=AGE`、`dob`、`age`、`ageUnit=Y`。
  DOB 保留数据库 timestamp 时钟值并以 UTC `Z` 序列化；空 DOB 省略。
  姓名至少一个非空、性别和年龄须通过校验。

外部 JSON 保留上述字段大小写。复用现有 LIS 客户端、超时和证书配置。
成功 JSON 按外部成功状态返回，204 返回无内容；外部
400/401/403/404/409/422 保留状态，其他失败为 502，超时为 504。
更新错误不透传外部响应正文。无效 JSON／网络失败可能发生在远端已更新后，
需先在 LIS 核对再重试。这两个接口独立调用，不自动重试、不更新建单队列状态，
也不保证两次更新的原子性。示例见 `medxbackend/Lis.http`。

`Lis:username`、`Lis:password`、`Lis:grant_type` 是获取 Token 的默认值。账号和密码在仓库配置中留空，请在本机配置、User Secrets 或环境变量 `Lis__username`、`Lis__password` 中填写，勿将真实密码提交到版本控制。`grant_type` 默认且仅支持 `password`。

`Lis:ValidateServerCertificate` 控制 LIS HTTP 客户端的 HTTPS 证书校验：`true` 或未配置时正常校验；`false` 时跳过所有服务器证书校验（包括信任链、域名和有效期）。当前配置为 `false`，仅用于受控测试环境；关闭校验可能导致账号、密码和 Token 被中间人窃取，生产环境应设为 `true` 并配置可信证书。也可通过环境变量 `Lis__ValidateServerCertificate` 设置，修改后重启后端生效。此设置不影响本系统 JWT 校验或其他 HTTP 客户端。

调用 `POST /api/Lis/token`，携带本系统的 `Authorization: Bearer <access-token>`，使用 `Content-Type: application/x-www-form-urlencoded` 提交表单（不再接收 JSON）：

```text
username=YOUR_LIS_USERNAME&password=YOUR_LIS_PASSWORD&grant_type=password
```

三个表单字段均可省略或留空：每个字段分别优先采用非空请求值，否则使用对应配置；`grant_type` 在请求和配置均为空时回退为 `password`。配置好默认账号密码后，可携带上述 Content-Type 提交空表单；无需在 Swagger 中重复填写。合并后缺少账号或密码、或 grant_type 不支持时返回 400，不发送请求到 LIS。不会把配置中的密码预填到 Swagger 或响应中。表单值需进行 URL 编码，Swagger 会自动处理。后端同样使用 `application/x-www-form-urlencoded` 调用 `{Lis:BaseUrl}/api/token`。成功时返回 `access_token`、`token_type`、`expires_in`、`userName`、`.issued` 和 `.expires`，日期文本保持 LIS 原始格式，响应禁止缓存。LIS 返回 400/401 时对应返回 400/401；连接失败、其他上游错误或无效响应返回 502，30 秒超时返回 504，地址未配置返回 500。不向客户端透传上游错误正文。

创建订单使用 `POST /api/Lis/createorder`，请求体为 `application/json`。`Authorization: Bearer <本系统 JWT>` 用于本系统认证，另加 `X-Lis-Access-Token: <LIS access_token>`（不加 Bearer 前缀）。后端仅向 LIS 设置 `Authorization: Bearer <LIS access_token>`，调用 `{Lis:BaseUrl}/api/CreateOrder/`，复用上述证书开关。LIS Token 与本系统 JWT 不通用，不写入配置或日志。

```json
{
  "referringDoctorCode": "CTM",
  "BillingCompanyCode": "ABCM",
  "SiteCode": "SITE_A",
  "createdBy": "USER_A",
  "patient": {
    "patientNameEng": "TEST PATIENT",
    "sex": "M",
    "ageType": "AGE",
    "dob": "1999-09-09T00:00:00Z",
    "age": 22,
    "ageUnit": "Y"
  },
  "TestProfiles": [
    { "TestProfileCode": "HEPVAC", "IsBillable": true }
  ],
  "Samples": [
    { "SampleCode": "CB", "Status": "P", "LabelCount": 2 }
  ]
}
```

由本地患者资料建立 LIS 订单时，`referringDoctorCode`、`BillingDoctorCode`、
`createdBy`、`patient` 及其患者字段使用上例的大小写；`SiteCode` 使用
`Lis:SiteCode`（默认 `LAB`，当前配置为 `MS`）。`patient` 的姓名、性别、DOB、年龄取自
`patientdetails_final`，`createdBy` 取自对应用户名；
`TestProfiles` 从 `patientdetails_final.productcode` 逐项生成，
每个 profile 的 `IsBillable` 为 `true`；productcode 含连字符时，会移除第一个连字符及其前面的内容
（例如 `AO-PSDNA` 发送为 `PSDNA`）。值依患者资料而异，不使用示例中的固定患者资料。

订单校验包括：转介医生或公司至少一项；收费医生或公司至少一项；现金付款必须有 `CashAmount`；`ReportByInterface=true` 必须有 `InterfaceProfile`。`PaymentMethod` 未填写时按 `PaymentOption`（默认 A）判断付款方式。患者中英文姓名至少一项；`AgeType=DOB` 必须有 `DOB`，`AgeType=AGE` 必须有 `Age` 和 `AgeUnit`。日期使用带时区的 ISO 8601，例如 `1999-09-09T00:00:00+08:00`。`RegistrationDatetime` 不支持用户设置，不向 LIS 发送，由 LIS 使用服务器时间。患者 `ID` 按文档示例暂作为字符串处理。

`TestProfiles`、`Samples` 暂按数组实现，可省略或为空；每个 Test Profile 必须有 `TestProfileCode`，`IsBillable` 默认 true，显式 null 无效。每个 Sample 必须有 `SampleCode`、`Status`（P/A/L）和非负整数 `LabelCount`；`CollectionDatetime`、`ReceivedDatetime` 和 `SpecimenComment` 可选。样本日期脚注 #1 尚未提供，暂不增加条件限制。`ReportByXXX` 未填写时不向 LIS 发送，保留医生/公司的报告偏好，明确传 `false` 则发送 false。

CreateOrder 的请求 Content-Type 和集合数组格式按常规 JSON 接口实现，仍需与 LIS 确认。未提供响应字段定义，因此保留 LIS 成功 JSON 及 HTTP 状态码（204 不含正文），不假设订单编号字段。LIS 的 400/401/403/409/422 对应返回相同状态，其他上游错误、连接失败或无效响应返回 502，超时返回 504；不透传上游错误正文中的患者资料。同步失败时，`patientdetails_sync.last_http_status` 保存上游状态码，`last_error` 保存提取并脱敏的 LIS 错误消息或连接诊断（最多 2000 字符）；无法安全提取时只保存状态码和内容类型，不保存完整响应或患者资料。创建订单不自动重试，超时或响应解析失败时订单可能已经创建，重试前先在 LIS 确认。调用示例见 `medxbackend/Lis.http`。

### 菜单权限（需要 JWT）

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/User/menus` | 获取当前用户角色可访问的菜单权限 |

### 客户（需要 JWT）

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/Customer/createcustomer` | 新建客户 |
| GET、POST | `/api/Customer/getcustomers` | 获取并分组客户 |
| GET、POST | `/api/Customer/getallcustomers` | 获取全部客户 |
| POST | `/api/Customer/updatecustomer` | 更新客户 |
| POST | `/api/Customer/deletecustomer` | 删除客户 |

### 产品（需要 JWT）

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/Product/createproduct` | 新建产品 |
| POST | `/api/Product/getproducts` | 获取产品列表 |
| POST | `/api/Product/updateproduct` | 更新产品 |
| POST | `/api/Product/deleteproduct` | 删除产品 |

### 患者资料（需要 JWT）

`GET /api/Patient/getPatientdetailsFinalFullByDailyno?dailyNo=DR261106-0001`
返回 `patientdetails_final` 唯一匹配记录的全部字段，直接返回 JSON 对象，
保留数据库原始列名（如 dailyno、patientnameen、centreid、productcode）和 null 值，
不转换为更新接口的 camelCase 请求字段。需要系统 JWT，不调用 LIS／CRM、不修改数据库，
响应禁止缓存。dailyNo 必填、最多 255 字符，查询前去除首尾空格；
无匹配返回 404，多条匹配返回 409，数据库异常返回安全的 500。
原 `getPatientdetailsFinalByDailyno` 基本资料接口不变。示例见 `medxbackend/Patient.http`。

`POST /api/Patient/updatePatientdetailsFinalByDailyno` 调用
`update_patientdetails_final_by_dailyno`，按必填的 `dailyNo`（最多 255 字符）
和 `formType`（1–2 字符）定位。请求字段及完整示例见 `medxbackend/Patient.http`。
此 POST 接口执行**部分更新**：未传或显式 null 的患者字段保留原值，
包括 productCode、dateReceived；不自动将 dateReceived 改为当前时间。
此 API 不接收或传递 centreId 更新值，最终表原编号保持不变；
SQL function 同步录入记录时沿用最终表原编号。
非 null 值（包括 0、空字符串和空数组）仍会更新。字符串传空字符串则保存空字符串，
productCode 传 [] 保存空数组。按当前函数定义，不能通过 null 清空其他字段。
日期接收带偏移的 ISO 8601，存入 timestamp without time zone 时保留输入的时钟值，
不换算 UTC。p_userid 来自 JWT，仅作本次操作者，保留原始记录 userid。
无需传 inputType；SQL function 在同一事务内同步 inputtype 1、2 的完整患者信息，
最终表 status 设为 Matched。本地提交成功后根据 formtype 启用规则自动更新 LIS／CRM。
本地更新成功后先读取 `patientdetails_sync_rules` 中 formtype 对应的启用系统，
仅对这些系统检查 `patientdetails_sync`，两者都启用则分别更新，一个失败仍继续另一个。
没有启用规则时只保存本地；不新增同步队列，不自动建单。
LIS：
仅当存在唯一 LIS 记录且 `sync_status = succeeded`，或状态为 `unknown`
且保存的 `remote_record_id` 与 `centreid` 完全一致（已确认建单，后续更新失败）时，
才自动获取配置的 LIS Token，
依次 PATCH `api/UpdateOrder/` 和 `api/UpdateOrderPatient/`。
患者更新 JSON 使用嵌套 `patient` 对象，包含 `patientNameEng`、
`HKID`、`patientNameChi`、`sex`、`ageType`、`dob`、`age`、`ageUnit`。
此自动同步使用 `ageType=DOB`，以出生日期同步患者年龄资料。
没有 LIS 记录、状态为 in_progress，或其他不满足上述条件的状态时不调用 LIS，
仍返回本地更新结果并附 `lisSync.status=skipped`。
两次 LIS 调用都成功时，`lisSync.status=succeeded`；任一步失败则本地更新不回滚，
同步记录标记为 unknown 并保存失败阶段／HTTP 状态／原因，接口返回 502
并明确 `localDatabaseUpdated=true`。订单更新成功但患者更新失败时需要先核对 LIS，
因为两个外部调用不具备原子性。同步过程会将队列暂时设为 in_progress 以避免并发重复更新。
更新失败后保留已确认的 LIS 订单编号；下次修改会重新依次更新 Order 和 Patient，
使用最新本地资料。首次建单结果不确定且没有匹配远端编号的 unknown 记录不会自动更新。
CRM：已有队列状态为 pending、succeeded、failed 或 unknown 时均尝试仅更新，
兼容旧 syncPatientdetailsFinal 已写入 CRM 但没有保存本地成功状态的历史记录。
使用 Salesforce External ID PATCH 的 `updateOnly=true`，不新建远端记录。
明确返回 HTTP 404 且 errorCode=NOT_FOUND 时，syncResults 标记 Skipped，
队列保存 failed 及完整诊断（并非已同步成功）；其他错误仍作为更新失败返回。
无启用 CRM 规则、无队列或 in_progress 时跳过，不新增队列。
CRM 认领时保留之前成功同步的编号，更新失败后下次保存可以再次更新。
成功返回保持原患者字段及 lisSync，并新增 syncResults（各系统结果，operation=Skipped 表示跳过）。
外部更新失败返回 502，规则读取或状态保存失败返回 500，
ProblemDetails 包含 localDatabaseUpdated=true、syncResults，并保留原 lisSyncStatus 等字段。
不会回滚已经提交的本地更新。前端无需再额外调用统一同步接口。

成功返回 200，正文为 result_status（updated）及展开的最终患者字段。
参数无效返回 400；最终记录不存在返回 404（前提是两条录入记录齐全）；
录入记录缺失／重复或最终记录不唯一返回 409，数据库函数回滚更新。
数据库异常返回不含内部细节的 500。需要数据库已部署 schema 中的新函数定义；
本 API 不自动迁移数据库。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/Barcode/pdf` | 按 `Barcode:Format` 生成患者条码 PDF |
| POST | `/api/Barcode/pdf/print` | 生成患者条码 PDF 并发送到网络打印机 |
| POST | `/api/Patient/getNextPatientdetails` | 获取下一笔患者资料或编号 |
| GET | `/api/Patient/getPatientdetailsFieldDifferences` | 查询字段差异 |
| POST | `/api/Patient/exportPatientdetailsFinal` | 按一个或多个状态导出最终患者资料（`excel` 或 `csv`） |
| POST | `/api/Patient/insertPatientdetails` | 新增患者资料 |
| POST | `/api/Patient/insertPatientdetailsFinal` | 新增最终患者资料 |
| POST | `/api/Patient/createPatientdetail` | 直接创建患者资料 |
| GET | `/api/Patient/getPatientdetail` | 按每日编号查询患者资料 |
| POST | `/api/Patient/getPatientdetailsByDailyno` | 按每日编号查询患者资料 |
| GET | `/api/Patient/getPatientdetailsFinalByDailyno` | 按每日编号查询最终患者基本资料 |
| GET | `/api/Patient/getPatientdetailsFinalFullByDailyno` | 按每日编号查询最终患者完整资料 |
| POST | `/api/Patient/getAdjacentPatientdetails` | 获取上一笔或下一笔患者资料 |
| POST | `/api/Patient/getPatientdetailsByInputtypeAndFormtype` | 按输入类型和表单类型查询 |
| POST | `/api/Patient/getPatientdetailsDailynoGaps` | 按表单类型和日期查询每日编号缺口 |
| POST | `/api/Patient/deletePatientdetailsByDailyno` | 删除指定患者资料 |
| POST | `/api/Patient/deletePatientdetailsByDailynos` | 按每日编号及其对应输入类型批量删除患者资料 |
| POST | `/api/Patient/resetPatientdetails` | 清空患者资料，必须明确传入 `confirm=true` |
| POST | `/api/Patient/updatePatientdetailsByDailyno` | 更新患者资料 |
| POST | `/api/Patient/updatePatientdetailsFinalByDailyno` | 部分更新最终资料并同步两条录入记录 |
| GET | `/api/Patient/getEthnicOriginOptions` | 获取种族选项 |
| GET | `/api/Patient/getPregnancyOptions` | 获取妊娠选项 |
| GET | `/api/Patient/getSpecimenOptions` | 获取样本类型选项 |

## 部署注意事项

- `resetPatientdetails` 会清空患者主表和最终资料表中的数据，只应由受控操作调用。
- 当前 CORS 策略允许任意来源；生产部署前应将允许来源限制为实际前端域名。
- 当前用户密码摘要使用 MD5。生产环境应迁移至专用密码哈希算法，并制定现有账号的兼容迁移方案。
- 生产环境应通过安全的密钥管理方式提供数据库、JWT 和 SMTP 凭据，并使用 HTTPS。
