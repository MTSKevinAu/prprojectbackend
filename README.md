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
  }
}
```

对应的环境变量采用双下划线表示配置层级，例如：

```powershell
$env:ConnectionStrings__PostgreSql = "Host=localhost;Port=5432;Database=medx;Username=YOUR_DB_USER;Password=YOUR_DB_PASSWORD"
$env:Jwt__Key = "REPLACE_WITH_A_LONG_RANDOM_SECRET"
$env:Jwt__Issuer = "medxbackend"
$env:Jwt__Audience = "medxclient"
$env:Smtp__Host = "smtp.example.com"
$env:Smtp__Port = "587"
$env:Smtp__From = "no-reply@example.com"
```

`Smtp:Username` 和 `Smtp:Password` 在 SMTP 服务要求身份验证时也必须设置。`PasswordReset:Url` 可选；未设置时，邮件链接默认使用 `http://localhost:5173/reset-password`。

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

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/Patient/getNextPatientdetails` | 获取下一笔患者资料或编号 |
| GET | `/api/Patient/getPatientdetailsFieldDifferences` | 查询字段差异 |
| POST | `/api/Patient/exportPatientdetailsFinal` | 按一个或多个状态导出最终患者资料（`excel` 或 `csv`） |
| POST | `/api/Patient/insertPatientdetails` | 新增患者资料 |
| POST | `/api/Patient/insertPatientdetailsFinal` | 新增最终患者资料 |
| POST | `/api/Patient/createPatientdetail` | 直接创建患者资料 |
| GET | `/api/Patient/getPatientdetail` | 按每日编号查询患者资料 |
| POST | `/api/Patient/getPatientdetailsByDailyno` | 按每日编号查询患者资料 |
| POST | `/api/Patient/getAdjacentPatientdetails` | 获取上一笔或下一笔患者资料 |
| POST | `/api/Patient/getPatientdetailsByInputtypeAndFormtype` | 按输入类型和表单类型查询 |
| POST | `/api/Patient/deletePatientdetailsByDailyno` | 删除指定患者资料 |
| POST | `/api/Patient/resetPatientdetails` | 清空患者资料，必须明确传入 `confirm=true` |
| POST | `/api/Patient/updatePatientdetailsByDailyno` | 更新患者资料 |
| GET | `/api/Patient/getEthnicOriginOptions` | 获取种族选项 |
| GET | `/api/Patient/getPregnancyOptions` | 获取妊娠选项 |
| GET | `/api/Patient/getSpecimenOptions` | 获取样本类型选项 |

## 部署注意事项

- `resetPatientdetails` 会清空患者主表和最终资料表中的数据，只应由受控操作调用。
- 当前 CORS 策略允许任意来源；生产部署前应将允许来源限制为实际前端域名。
- 当前用户密码摘要使用 MD5。生产环境应迁移至专用密码哈希算法，并制定现有账号的兼容迁移方案。
- 生产环境应通过安全的密钥管理方式提供数据库、JWT 和 SMTP 凭据，并使用 HTTPS。
