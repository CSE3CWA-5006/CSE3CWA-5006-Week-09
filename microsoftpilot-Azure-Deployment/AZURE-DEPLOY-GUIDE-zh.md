# 从零把 ASP.NET Core 应用部署到 Azure（小白完整版）

> 这份文档是按**真实部署过程**写的，包含我实际踩到的每一个坑。
> 照着从上往下走就行，每步都有 ✅ 检查点 和 ⚠️ 坑。
> 最终目标：本地能跑的 ASP.NET Core 应用 → 变成 `https://xxx.azurewebsites.net` 上能登录的网站。

---

## 0. 先搞懂 5 个名词（不然后面全是懵的）

| 名词 | 通俗解释 | 在哪儿找 |
|---|---|---|
| **App registration**（应用注册） | 你应用的"身份证" | `portal.azure.com` → **Microsoft Entra ID** → **App registrations** |
| **Client ID** | 身份证号（一串 GUID） | 应用注册的 **Overview** 页 |
| **Client secret** | 身份证密码 | 应用注册 → **Certificates & secrets** |
| **App Service**（Web App） | 跑你代码的那台服务器 | `portal.azure.com` → **App Services** |
| **Redirect URI**（回调地址） | 登录完成后，微软把用户送回哪个地址 | 应用注册 → **Authentication** |

### 一次登录到底发生了什么

```
浏览器 ──①访问──► 你的站点 (xxx.azurewebsites.net)
          ◄─②302 跳到微软登录页─┘
浏览器 ──③输入账号密码──► 微软
          ◄─④带着 code 跳回 redirect_uri─┘
你的站点 ──⑤用 code + secret 换 token──► 微软
          ◄─⑥拿到 token，去读 Graph 数据─┘
```

**核心规则：第 ④ 步的地址必须是"你站点真实的 https 地址 + `/signin-oidc`"，并且原封不动登记在应用注册里。差一个字符，微软就拒绝。**

---

## 1. 第一步：注册 Entra 应用

位置：`portal.azure.com` → 搜索 **Microsoft Entra ID** → **App registrations** → **New registration**

| 字段 | 填什么 |
|---|---|
| Name | 随便，例如 `MyAppRegistration` |
| Supported account types | 要支持**个人微软账号**（@outlook.com）→ 选 *Accounts in any organizational directory and personal Microsoft accounts* |
| Redirect URI | 先填 **Web** + `https://localhost:5001/signin-oidc`（本地开发用） |

点 Register。

### 1.1 抄下 Client ID

注册完在 **Overview** 页，复制 **Application (client) ID**。

```
✅ 检查点：你手上有了一串形如 11111111-2222-3333-4444-555555555555 的 ID
```

### 1.2 建 Client Secret

左侧 **Certificates & secrets** → **Client secrets** → **New client secret** → 有效期随便 → **Add**

```
⚠️ 坑 1（超高频）：要复制的是 "Value" 那一列，不是 "Secret ID"！
   Value 只在创建后显示一次，刷新页面就永远看不到了。
   看不到就重新建一个，别纠结。
```

### 1.3 加回调和权限

**Authentication** → **Add Redirect URI** → 选 **Web** → 填入：

```
https://localhost:5001/signin-oidc                  ← 本地开发
https://你的应用名.azurewebsites.net/signin-oidc     ← 线上（第二步拿到真实域名后再回来加）
```

**API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated permissions** → 勾：

| 权限 | 用途 |
|---|---|
| `User.Read` | 读个人资料 |
| `Mail.Read` | 读邮件 |
| `Calendars.Read` | 读日历 |
| `Files.Read` | 读 OneDrive 文件 |

最后点 **Grant admin consent**（个人账号通常点一下就行）。

```
⚠️ 坑 2：回调地址结尾必须是 /signin-oidc
   这是 ASP.NET Core 的默认回调路径，对应代码里的 CallbackPath。
   写成 /signin-oidc/ 或者少写字母都会导致 redirect_uri is not valid。
```

---

## 2. 第二步：创建 App Service（Web App）

位置：`portal.azure.com` → **Create a resource** → 搜 **Web App** → **Create**

### 2.1 Basics 参数

| 字段 | 选什么 | 为什么 |
|---|---|---|
| Subscription | 你的订阅（例如 Visual Studio Professional） | — |
| Resource Group | 新建一个，例如 `mypilot-rg` | 方便以后一起删 |
| Name | 例如 `myapp` | 全局唯一 |
| Publish | **Code** | 我们发布代码，不是容器 |
| Runtime stack | **.NET 10 (LTS)**（和 csproj 的 `TargetFramework` 一致） | 版本必须对 |
| Operating System | **Linux** | 免费 F1 更常见的可用组合 |
| Region | 离用户近的，例如 **Australia East** | 延迟低 |
| Pricing plan | **Free F1**（`0.00 AUD/Month`） | 免费 |

```
⚠️ 坑 3（域名随机后缀）：新门户默认开启 "Secure unique default hostname"，
   你的真实域名会是：  你输入的名字 + "-" + 随机串 + "." + 区域 + "-01.azurewebsites.net"
   例如：myapp-abc123.australiaeast-01.azurewebsites.net

   ★ 所有需要写"你的域名"的地方（特别是回调地址），
     必须用 App Service → Overview → Default domain 里显示的那个，不能自己拼。
```

### 2.2 建完立刻做的两件事

1. 复制 **Overview → Default domain** —— 这就是你的站点地址
2. 回到第一步的 Entra 应用注册，把这个地址加进回调：

```
https://<Default domain>/signin-oidc
```

```
✅ 检查点：浏览器打开 https://<Default domain>/health
          （如果你的代码有健康检查端点）现在应该能返回 200
```

---

## 3. 第三步：把 secret 交给 Azure（应用设置）

**千万不要把 secret 写在仓库里的 `appsettings.json`** —— 那会跟着代码进 GitHub。
正确做法是放在 App Service 的"应用设置"里，它会在运行时作为**环境变量**注入给应用。

位置：App Service → **Settings → Environment variables** → 选 **App settings** 标签

### 3.1 加这三条

| Name | Value | 说明 |
|---|---|---|
| `AzureAd__TenantId` | `consumers` | 个人账号用 `consumers`；组织账号用 `common` 或租户 ID |
| `AzureAd__ClientId` | 第一步的 Client ID | |
| `AzureAd__ClientSecret` | 第一步的 Client Secret **Value** | |

```
⚠️ 坑 4（名字格式）：中间是【两个下划线】，不是两个点也不是一个下划线
   AzureAd__ClientSecret   →  对应配置项 AzureAd:ClientSecret
```

```
⚠️ 坑 5（我实际踩了，导致线上停机）：改完必须点两次！
   ① 点工具栏的 "Apply"
   ② 弹出 "Save changes / 应用可能重启，是否继续？" 的对话框时，必须点 "Confirm"

   只点 Apply 不点 Confirm = 一个字都没保存。
   但界面上仍然显示你填的内容、刷新后也还在（那是浏览器里的待保存状态），
   应用却完全读不到 —— 这个假象非常坑。

   验证方法：保存后按 F5 刷新页面，如果 "Apply" 按钮变成灰色（不可点），才算真的保存了。
```

```
✅ 检查点：三个变量都在列表里，Source 列显示 "App Service"，且 Apply 按钮是灰的
```

---

## 4. 第四步：下载发布配置文件（Publish Profile）

位置：App Service → **Overview** → 右侧工具条的 **"…"**（更多） → **Download publish profile**

下载到一个 `xxx.PublishSettings` 文件（XML 格式）。

### 4.1 打开它，找到这三个值

里面有三段（MSDeploy / FTP / ZipDeploy）。**Linux 应用用 ZipDeploy 那段**：

```xml
<publishProfile profileName="..." publishMethod="ZipDeploy"
    publishUrl="你的应用名-abc123.scm.australiaeast-01.azurewebsites.net:443"
    userName="$你的应用名"
    userPWD="一串随机密码"
    destinationAppUrl="https://你的域名" />
```

| 配置值 | 用途 |
|---|---|
| `publishUrl` | SCM（部署管理）地址，注意**没有 `https://` 前缀**，只有 host:443 |
| `userName` | 通常是 `$你的应用名`（带美元符号） |
| `userPWD` | 部署密码 |

```
⚠️ 坑 6：新版 App Service 默认【关闭】SCM/FTP Basic Auth 发布凭据，
   所以下载下来的用户名密码用不了，发布时报 401/403。

   两种解法：
   A. 用 Visual Studio 登录 Azure 账号直接发布（不需要密码，见第五步方式 A）
   B. 打开凭据：
      App Service → Configuration → General settings →
      SCM Basic Auth Publishing Credentials = On
      → Apply → 弹窗点 Confirm
```

---

## 5. 第五步：用 Visual Studio Community 发布

### 方式 A：用 Azure 账号发布（推荐小白，不用管密码）

1. 打开 VS Community，**用你的 Azure 账号登录**（右上角头像）
2. 解决方案资源管理器里**右键项目** → **Publish**
3. 选 **Azure** → Next → **Azure App Service (Linux)** → Next
4. 选 **Select Existing** → 选订阅 → 在列表里找到你刚建的 App Service → **Finish**
5. 点 **Publish**

### 方式 B：导入发布配置文件

1. 右键项目 → **Publish** → **Import Profile**
2. 选第四步下载的 `.PublishSettings` 文件
3. **Publish**

> 方式 B 需要第四步 ⚠️ 坑 6 里说的 SCM Basic Auth 是打开的，否则会 401。

### 方式 C：命令行（等价于点 Publish，适合脚本或无 GUI 环境）

把发布配置文件转成 `Properties/PublishProfiles/AzureAppService.pubxml`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="4.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <WebPublishMethod>ZipDeploy</WebPublishMethod>
    <PublishProvider>AzureWebSite</PublishProvider>
    <PublishProtocol>ZipDeploy</PublishProtocol>
    <LastUsedBuildConfiguration>Release</LastUsedBuildConfiguration>
    <LastUsedPlatform>Any CPU</LastUsedPlatform>
    <SiteUrlToLaunchAfterPublish>https://你的域名</SiteUrlToLaunchAfterPublish>
    <LaunchSiteAfterPublish>false</LaunchSiteAfterPublish>
    <ResourceId>/subscriptions/你的订阅ID/resourceGroups/你的资源组/providers/Microsoft.Web/sites/你的应用名</ResourceId>
    <DeployIisAppPath>你的应用名</DeployIisAppPath>
    <UserName>$你的应用名</UserName>
    <Password>部署密码</Password>
    <PublishUrl>https://SCM主机:443</PublishUrl>
    <DestinationAppUrl>https://你的域名</DestinationAppUrl>
    <IgnoreDeployManagedRuntimeVersion>False</IgnoreDeployManagedRuntimeVersion>
    <_DestinationType>AzureWebSite</_DestinationType>
  </PropertyGroup>
</Project>
```

然后（用 VS 自带的 MSBuild）：

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  .\你的项目.csproj `
  /t:Publish /p:Configuration=Release /p:PublishProfile=AzureAppService
```

成功时最后一行是：

```
Zip Deployment succeeded.
```

```
⚠️ 坑 7（PublishUrl 怎么填）：
   只写 scheme + SCM 主机 + 端口，例如
       https://myapp-abc123.scm.australiaeast-01.azurewebsites.net:443
   发布任务会自动在末尾拼上 /api/zipdeploy。
   如果你自己再写 /msdeploy.axd?site=xxx，就会变成 ...?site=xxx/api/zipdeploy → 404 NotFound。
```

```
⚠️ 坑 8（部署密码别提交）：AzureAppService.pubxml 里有部署密码，
   一定要在 .gitignore 里加：
       Properties/PublishProfiles/*.pubxml
       *.PublishSettings
```

---

## 6. 第六步：回调地址（redirect_uri）—— 最容易翻车的一步

### 6.1 回调地址的完整规则

```
https://<App Service 的 Default domain>/signin-oidc
```

三个必须全对：

1. **协议必须是 https**（微软只允许 localhost 用 http）
2. **域名必须是真实的 Default domain**（含随机后缀）
3. **路径必须是 `/signin-oidc`**

要同时登记本地和线上两条：

```
https://localhost:5001/signin-oidc
https://myapp-abc123.australiaeast-01.azurewebsites.net/signin-oidc
```

### 6.2 报 `redirect_uri is not valid` 怎么排查

先看浏览器地址栏里那次跳转带的 `redirect_uri=` 到底是什么，和登记的值逐字符对比：

| 现象 | 原因 | 解决 |
|---|---|---|
| 地址栏里是 `http://...`（少个 s） | **应用自己以为跑在 http 上** | 见下面 ⚠️ 坑 9 |
| 域名对不上（比如少了 `-abc123`） | 回调地址写的是你输入的名字，不是真实域名 | 用 Default domain 重写 |
| 路径不对（`/signin-oidc/`、`/callback`） | 路径写错 | 改成 `/signin-oidc` |
| 域名路径都对，还是报错 | 这条 URI 根本没登记在应用注册里 | 去 Authentication 里补 |

---

### ⚠️ 坑 9（最经典、最隐蔽的一个）：回调地址变成 http://

**现象**：应用发给微软的 `redirect_uri` 是

```
http://你的应用.azurewebsites.net/signin-oidc     ← 少了个 s
```

**原因**：Azure 在前面做 TLS 终止。浏览器到 Azure 是 HTTPS，但 Azure 转发给你的应用时是**明文 HTTP**，只多带一个请求头：

```
X-Forwarded-Proto: https
```

ASP.NET Core **默认不读这个头**。所以应用"以为"自己在 http 上跑，拼出来的绝对地址就是 http。而微软**只允许 localhost 用 http**，`*.azurewebsites.net` 用 http 一律拒绝。

**关键认知**：

```
外部浏览器看到的： https://xxx.azurewebsites.net   ← 用户侧确实是 HTTPS
应用内部看到的：   http://xxx.azurewebsites.net    ← 应用以为的（这就是 bug）
```

打开 Azure 的 "HTTPS Only" 也**治不了**这个问题 —— 它只保证浏览器到 Azure 是加密的，请求进到应用内部仍然是 http。

**解决**：在 `Program.cs` 最前面加上转发头中间件

```csharp
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Azure 前端 IP 不固定，无法列白名单，所以清空可信代理列表
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
});

var app = builder.Build();

// 必须放在管道最前面：HTTPS 重定向和 OIDC 挑战都要读 scheme
app.UseForwardedHeaders();
```

改完**重新发布**即可。

> 本地跑不受影响：本地没有 `X-Forwarded-Proto` 头，中间件等于什么都不做。

---

## 7. 第七步：HTTPS

### 7.1 线上

* Azure 的 `*.azurewebsites.net` 域名**自带官方证书**，你什么都不用买、不用配
* App Service → **Configuration → General settings → HTTPS Only = On** → Apply → Confirm
  * 打开后，用户访问 `http://…` 会被自动跳转到 https
  * **实测**：Azure 前端返回的是 **301**（永久重定向）；本地 Kestrel 的 `UseHttpsRedirection()` 返回的是 **307**，两者不一样，别混淆
  * 但注意：它**不能**修 ⚠️ 坑 9 那个"应用以为自己是 http"的问题
* 回调地址永远只登记 `https://` 版本

### 7.2 本地开发

本地也要用 https，否则和线上行为不一致、Cookie 和回调都会出问题。

1. 项目里 `Properties/launchSettings.json` 要有 https 配置：

```json
"https": {
  "commandName": "Project",
  "applicationUrl": "https://localhost:5001;http://localhost:5000",
  "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
}
```

2. 信任本地开发证书（**只需做一次**）：

```powershell
dotnet dev-certs https --trust
```

弹窗点"是"。之后浏览器打开 `https://localhost:5001` 就不会有证书警告了。

3. 本地登录的回调地址 `https://localhost:5001/signin-oidc` **也必须登记在应用注册里**，
   否则本地登录同样报 `redirect_uri is not valid`。

---

## 8. 第八步：验证

### 8.1 两个命令行检查（不需要登录）

```powershell
$base = "https://你的域名"

# ① 应用是否活着
Invoke-WebRequest "$base/health" -UseBasicParsing | Select-Object StatusCode, Content
# 期望：200  OK（如果你的应用有 /health 端点）

# ② 登录跳转是否正确
$r = Invoke-WebRequest "$base/" -MaximumRedirection 0 -UseBasicParsing -SkipHttpErrorCheck
$loc = [string]$r.Headers.Location
[System.Uri]::UnescapeDataString([regex]::Match($loc, 'redirect_uri=([^&]+)').Groups[1].Value)
# 期望：https://你的域名/signin-oidc   ← 必须是 https！
```

### 8.2 真人验证

浏览器打开站点 → 用你的微软账号登录 → 应该能回到应用并看到数据。

```
✅ 检查点：登录过程没有 redirect_uri is not valid，没有 AADSTS 错误码
```

---

## 9. 出问题了去哪儿看日志

这是新手最缺的技能：**Azure 不会把错误直接告诉你，要去翻日志**。

### 9.1 看应用启动日志（最有用）

应用启动就崩的情况，日志在：

```
https://<你的SCM主机>/api/vfs/LogFiles/StartupLogs/
```

里面会有 `xxxx_failure.log` 或 `xxxx_success.log`，明确写着异常信息，例如：

```
Unhandled exception. System.InvalidOperationException:
Entra configuration is missing or still a placeholder: AzureAd:ClientId ...
   at Program.<Main>$ ...
```

用 PowerShell 读（需要部署凭据）：

```powershell
$scm = "https://你的应用-xxxx.scm.australiaeast-01.azurewebsites.net"
$b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("部署用户名:部署密码"))
$hdr = @{ Authorization = "Basic $b64" }

# 列目录
Invoke-RestMethod "$scm/api/vfs/LogFiles/StartupLogs/" -Headers $hdr | Select name, size, mtime

# 读某个日志
(Invoke-WebRequest "$scm/api/vfs/LogFiles/StartupLogs/xxx_failure.log" -Headers $hdr -UseBasicParsing).Content
```

### 9.2 看容器/平台日志

```
https://<SCM主机>/api/vfs/LogFiles/          ← 平台日志（容器启动、退出码）
```

如果看到 `Container has finished running with exit code: 134`（Linux 下 134 = SIGABRT），
基本就是应用抛了未处理异常，去 StartupLogs 找具体那行。

### 9.3 门户里的入口

| 想看什么 | 去哪里 |
|---|---|
| 应用整体状态 | App Service → Overview → Status（Running / Quota exceeded） |
| 最近的部署记录 | App Service → Deployment Center → Logs |
| 诊断建议 | App Service → Diagnose and solve problems |
| 实时日志流 | App Service → Log stream |

```
⚠️ 坑 10：站点被平台停用（403 Site Disabled）时，Kudu（SCM）也会一起 403，
   这时所有日志接口都读不到。先让站点回到 Running 再看日志。
```

---

## 10. 常见错误速查表

| 报错 / 现象 | 最可能的原因 | 解决 |
|---|---|---|
| `redirect_uri is not valid`，地址是 `http://` | 应用没读 `X-Forwarded-Proto` | 加 `UseForwardedHeaders()` 并重新发布（⚠️ 坑 9） |
| `redirect_uri is not valid`，地址正确 | 这条 URI 没登记 | 去 Entra → Authentication → Web 里加上 |
| `AADSTS700016` / 找不到应用 | Client ID 写错，或 TenantId 和账号类型不匹配 | 核对 `AzureAd__ClientId` / `AzureAd__TenantId` |
| 启动即崩，日志说配置缺失 | 应用设置没真正保存 | 重新保存并**点 Confirm**（⚠️ 坑 5） |
| 发布报 401/403 | SCM Basic Auth 关闭 | 打开 SCM Basic Auth（⚠️ 坑 6），或用 VS 账号发布 |
| 发布报 404 NotFound | `PublishUrl` 多写了 `/msdeploy.axd?site=...` | 只写 `https://SCM主机:443`（⚠️ 坑 7） |
| 站点 403 Site Disabled + Status: Quota exceeded | Free F1 每天 60 分钟 CPU 用完了 | 等额度重置，或换一个新计划 |
| 登录成功但 Graph 调用 403 | 委派权限没同意 | 在应用注册里加权限并 Grant consent |
| 本地登录报同样的 redirect 错误 | 没登记 `https://localhost:5001/signin-oidc` | 加上；并 `dotnet dev-certs https --trust` |

---

## 11. 费用与配额（很重要）

| 项 | 说明 |
|---|---|
| **Free F1 价格** | `0.00 AUD/月`，**运行和停止都是 0** |
| **F1 配额** | 每个 App Service 计划 **每天 60 分钟 CPU 时间**（已核对 Microsoft Learn 官方限制页：`CPU time (day)` = **60 minutes**） |
| **用超了会怎样** | 应用被平台停用，状态显示 `Quota exceeded`，**但不收费**；额度重置后自动恢复 |
| **什么会烧配额** | CPU 时间。**反复重启/崩溃循环最费** —— 一个 12 秒就崩的容器被平台反复重启，几十分钟就能把一天的额度耗光 |

另外注意：

* 配额是按 **App Service 计划**算的，不是按代码。所以回滚代码**不能**解决配额问题。
* 想马上恢复又不想花钱：把应用移到一个**新的 Free F1 计划**（新计划有独立额度）。
* 真正烧钱的资源是：SQL 数据库、虚拟机、Application Insights 数据采集、付费版 App Service 计划。确认这些不存在就不会有费用。
* **实测账单（2026-09-16，Cost analysis）**：某订阅本月实际 **AU$0.60**，明细全部在 **US West 3**，来自遗留的旧资源
  （几台早先遗留的 App Service 计划/应用，以及一个 Log Analytics 工作区）。
  **新建的 Australia East Free F1 应用贡献 AU$0.00** —— 按"位置"分解里根本没有 Australia East 这一项。
* 所以结论要分两句：**Free F1 本身确实是 0**；但**"整个订阅 0 费用"必须清点所有资源**才能下结论，
  别只看当前在用的那一个（旧账户里经常躺着几个忘了删的付费计划）。

---

## 12. 最终检查清单

部署完成后逐条打勾：

- [ ] Entra 应用注册已创建，**Client ID** 已抄下
- [ ] **Client Secret 的 Value** 已抄下（不是 Secret ID）
- [ ] 回调地址登记了 **https 的线上地址 + `/signin-oidc`**
- [ ] 回调地址登记了 **https://localhost:5001/signin-oidc**（本地开发）
- [ ] Graph 委派权限已添加并授权
- [ ] App Service 已创建（Linux / .NET 版本和项目一致 / Free F1）
- [ ] 已从 **Overview → Default domain** 抄下真实域名（含随机后缀）
- [ ] 三个应用设置已添加：`AzureAd__TenantId` / `AzureAd__ClientId` / `AzureAd__ClientSecret`
- [ ] 应用设置保存时**点了 Confirm**，且 Apply 按钮变灰
- [ ] 代码里有 `app.UseForwardedHeaders()`（放管道最前面）
- [ ] `https://你的域名/health` 返回 **200 OK**
- [ ] `/` 的重定向里 `redirect_uri` 是 **https://**
- [ ] 浏览器用真账号登录成功，能看到数据
- [ ] `.gitignore` 已忽略 `*.PublishSettings`、`Properties/PublishProfiles/*.pubxml`、`appsettings.Production.json`
- [ ] 仓库里**没有**任何真实 secret / 部署密码

---

## 附：这份文档对应的真实部署记录

本文里的所有坑都来自一次真实的从零部署（2026-09-16）：

| 遇到的坑 | 后果 |
|---|---|
| Client Secret 差点复制成 Secret ID | 会在换 token 时失败 |
| 域名带随机后缀，差点写错回调地址 | `redirect_uri is not valid` |
| 应用设置只点了 Apply 没点 Confirm | **线上停机**：容器 exit 134 崩溃循环，最终把 F1 一天额度耗尽 |
| 缺 `UseForwardedHeaders()` | 回调地址变成 `http://`，微软直接拒绝登录 |
| 新 App Service 默认关闭 SCM Basic Auth | 发布配置文件用不了（401/403） |
| `PublishUrl` 多写了 `/msdeploy.axd?site=xxx` | 发布 404 NotFound |

希望这份清单能让你一次过。

---

## 附录 A：每步是"必须"还是"可选"

### 必须做（少任何一个都跑不起来）

| # | 步骤 | 少了的后果 |
|---|---|---|
| 1 | App registration + Client ID + Client Secret **Value** | 应用启动即失败 |
| 2 | 回调地址 `https://<真实域名>/signin-oidc` | 登录报 `redirect_uri is not valid` |
| 3 | 创建 App Service | 没有地方跑代码 |
| 4 | 三条应用设置（若代码从环境变量读 secret） | 应用启动即失败（exit 134） |
| 5 | 发布代码（VS 或命令行均可） | 站点是空的 |
| 6 | 代码里的 `app.UseForwardedHeaders()` | Azure 上登录必挂（回调变 http） |

### 可选（按需才做）

| 步骤 | 什么时候才需要 |
|---|---|
| 本地 `https://localhost:5001` 回调 + `dotnet dev-certs https --trust` | 只在你要**本地调试登录**时 |
| Graph API 权限清单 | 取决于代码读哪些数据 |
| 打开 **SCM Basic Auth** | 只在用**发布配置文件 / 命令行**发布时；用 VS 账号发布**不需要** |
| 打开 **FTP Basic Auth** | 只有要**下载或使用发布配置文件**时才需要 |
| HTTPS Only | 新应用默认就是 On，通常不用动 |
| Application Insights | 想做监控时（可能产生费用） |
| 自定义域名 + 证书 | 想用自己的域名时 |

---

## 附录 B：这份指南的验证状态（严格审核版）

### B1. 在 Azure 上**亲手验证过**的（有实测证据）

| # | 步骤 | 实测证据 |
|---|---|---|
| 1 | 创建 App Service：Linux / .NET 10 / Free F1 / 选区域 | 走过两次创建向导，`Your deployment is complete`，之后 `/health` 返回 200 |
| 2 | **域名带随机后缀** | 只输入 `myapp`，实际域名是 `…-abc123.australiaeast-01.azurewebsites.net` |
| 3 | 应用设置 + **双下划线**命名 | `appsettings.json` 里 ClientId/Secret 为空时，靠 `AzureAd__*` 环境变量仍能正常启动 |
| 4 | **只点 Apply 不点 Confirm = 没保存** | 亲历故障：容器 `exit 134`，StartupLogs 显示 `Entra configuration is missing…`；点 Confirm 后立刻恢复 |
| 5 | 回调地址不登记 → 登录被拒 | 微软返回 `invalid_request: … redirect_uri is not valid`；登记后正常渲染登录页 |
| 6 | 回调地址 `http://` vs `https://`（ForwardedHeaders） | 修复前实测抓到 `redirect_uri=http://…`，加上中间件后变成 `https://…` |
| 7 | `PublishUrl` 只能写 `https://SCM主机:443` | 实测多写 `/msdeploy.axd?site=` → **404 NotFound**；改对后成功 |
| 8 | ZipDeploy 发布 | 成功 3 次，日志 `Zip Deployment succeeded.` |
| 9 | 部署凭据的两种形式 | 下载 profile 里的 `$应用名` ✅；用户级 `your-deploy-user` ✅；`应用名\用户名` 反而 **401** |
| 10 | 新 App **默认关闭** SCM/FTP Basic Auth | 实测 `#scm = false`；FTPS 页显示 "FTP authentication has been disabled"；那里 "Download publish profile" 是灰的 |
| 11 | HTTPS 强制跳转 | 实测 `http://…/health` → **301** → https |
| 12 | `*.azurewebsites.net` 自带证书 | 实测证书校验通过：`CN=*.azurewebsites.net`，签发者 Microsoft TLS G2 RSA CA |
| 13 | 日志位置 `StartupLogs` | 实读，拿到完整异常堆栈 |
| 14 | `exit code 134` 对应未处理异常 | 日志中 `Aborted (core dumped)` 与应用异常对应 |
| 15 | 配额耗尽 → `403 Site Disabled` | 实测状态 `Quota exceeded`，且 Kudu 同样返回 403 |
| 16 | **换新 Free 计划可立刻恢复** | 实测：删除旧应用+旧计划、新建后 `/health` 立即返回 200 |
| 17 | `TenantId = consumers` 适配个人账号 | 实测跳转到 `login.microsoftonline.com/consumers/oauth2/…` |
| 18 | 本地 `https://localhost:5001` 能正常访问、证书受信任 | `dotnet dev-certs https --check --trust` 返回 "A trusted certificate was found … CN=localhost"；不带 `-SkipCertificateCheck` 请求 `/health` 返回 **200** |
| 19 | 本地 http→https 是 **307**（与 Azure 的 301 不同） | 实测 `http://localhost:5000/health` → **307** → `https://localhost:5001/health` |
| 20 | Client Secret 的 Value 创建后无法再查看 | 门户里现有 secret 的 Value 列显示 `7o4******************`，列头提示 "Client secret values cannot be viewed, except for immediately after creation." |
| 21 | "F1 每天 60 分钟 CPU" | 核对 Microsoft Learn 官方限制页：`CPU time (day)` = **60 minutes** |
| 22 | 实际账单是否符合"0 费用" | Cost analysis 实测本月 **AU$0.60**，全部在 US West 3 旧资源；**新建的 Australia East Free F1 应用 = AU$0.00** |

### B2. **没有亲测**、按官方文档/常规做法写的（用的时候留意）

| # | 步骤 | 说明 |
|---|---|---|
| 1 | 从零**创建 App registration**（Name / 账户类型 / Register） | 当时复用了已有的注册，只改了 Authentication |
| 2 | **新建 Client Secret 的完整点击流程**（New client secret → 填 Description → Add → 立刻复制 Value） | 只验证了"创建后无法再查看 Value"（见 B1-20），没有真的新建一个（不想在你租户里留测试凭据） |
| 3 | **API permissions + Grant admin consent** | 权限已存在，没重新操作。间接证据：登录请求里确实带着 `User.Read / Mail.Read / Calendars.Read / Files.Read` 四个 scope |
| 4 | **VS Community 图形界面发布**（第五步方式 A / B） | 实际用的是**命令行 MSBuild**（方式 C）。**本环境无法自动化 VS 这种桌面程序**，所以这条我无法验证 —— 需要你自己在 VS 里点一次 |
| 5 | "超额**不收费**" | 免费套餐没有计费项；实测配额耗尽后只是被停用，账单里没有对应费用项。未做"超额后观察一整期账单"的严格验证 |

### B3. 你可以自己怎么复验

```powershell
$base = "https://myapp-abc123.australiaeast-01.azurewebsites.net"

# ① 应用是否活着
(Invoke-WebRequest "$base/health" -UseBasicParsing).StatusCode     # 期望 200

# ② http 是否被强制跳转（验证 HTTPS Only）
$r = Invoke-WebRequest "http://$($base -replace '^https://')/health" -MaximumRedirection 0 -UseBasicParsing -SkipHttpErrorCheck
$r.StatusCode; $r.Headers.Location                                  # 期望 301 + https 地址

# ③ 登录跳转里的 redirect_uri 是不是 https
$r2 = Invoke-WebRequest "$base/" -MaximumRedirection 0 -UseBasicParsing -SkipHttpErrorCheck
[System.Uri]::UnescapeDataString([regex]::Match([string]$r2.Headers.Location,'redirect_uri=([^&]+)').Groups[1].Value)
```

---

## 附录 C：备选方案 —— 不下载发布配置文件也能发布

当 "Download publish profile" 按钮是灰的（因为 FTP Basic Auth 关闭）、或者你不想把密码文件到处放时，
可以自己设一组**用户级部署凭据**，然后手写 pubxml。

### C1. 步骤

1. App Service → **Configuration → General settings**：
   * `SCM Basic Auth Publishing Credentials` = **On**
   * `FTP Basic Auth Publishing Credentials` = **On**（想用门户里的下载按钮才需要）
   * → Apply → 弹窗点 **Confirm**

2. App Service → **Deployment Center** → **FTPS Credentials** 标签 → **User-scope** 区块：
   * Username 填一个你记得住的名字，例如 `your-deploy-user`
   * Password / Confirm password 填同一个强密码
   * 点工具栏 **Save**（弹窗要点 Confirm）

3. 写 `Properties/PublishProfiles/AzureAppService.pubxml`，关键三行：

```xml
<UserName>your-deploy-user</UserName>                 <!-- 只写用户名，不要写 应用名\用户名 -->
<Password>你设的密码</Password>
<PublishUrl>https://你的SCM主机:443</PublishUrl>
```

4. 用 MSBuild 发布（同第五步方式 C）。

### C2. ⚠️ 实测踩到的坑

```
用户级凭据的 UserName 必须写【纯用户名】 your-deploy-user
   → 可以（实测 200 OK）

写成【应用名\用户名】 myapp\your-deploy-user
   → 401 Unauthorized（实测失败）
```

这和 FTP 的规则不同（FTP 反而要求 `应用名\用户名` 格式），别搞混。

### C3. 怎么确认凭据能用

```powershell
$scm  = "https://你的应用-xxxx.scm.australiaeast-01.azurewebsites.net"
$b64  = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("your-deploy-user:你的密码"))
Invoke-WebRequest "$scm/api/settings" -Headers @{ Authorization = "Basic $b64" } -UseBasicParsing
# 期望 200；如果是 401，说明用户名格式或密码不对
```
