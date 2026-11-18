# DeepSeek 对话 for Windows 98

> 本 README 由 DeepSeek 编写。程序也为 DeepSeek 编写。Owner 不对此 README 负责。

一句话版本：**双击一个 32 位 exe，输入 API Key，然后看着 DeepSeek 在 1998 年的操作系统上一个字一个字地把回答吐出来。**

不是远程桌面，不是虚拟机里套浏览器，也不是"在 Win98 上开个 putty 连 Linux"。就是原生的 WinForms 窗口，跑在一台真正意义上有 Start 按钮的机器上。

---

## 先说清楚：奇迹不是我造的

本项目 99% 的含金量来自这个开源项目：

### [11EJDE11/dotnet48-win98](https://github.com/11EJDE11/dotnet48-win98)

它把 **.NET Framework 4.8** 原生搬到了 **Windows 98 SE** 上：改写导入表让 CLR 去找自家的 `DNKERNEL.DLL` / `DNADVAPI.DLL` 包装层、二进制补丁掉 `clr.dll` 和 `clrjit.dll` 里那些读取 NT TEB（`fs:[TEB]`）的内联 TLS 快路径、自己接管 `_CorExeMain`，让托管 exe 可以像正常 Windows 程序一样双击启动。

**其中最关键的，是本项目的生死线：HTTPS。**

没有它，此程序就只是一个"提示 HTTPS 不受支持"的漂亮窗口。

---

## 为什么说 HTTPS 才是真正的难点

Windows 98 的问题不在于"老"，而在于 **它压根没有能用的现代 TLS 协议栈**：

- 系统里的 `secur32.dll` 没有 SChannel 包，`SslStream` 无路可走；
- `crypt32` 不认识 SHA-256 证书签名，现代证书在它眼里约等于一张涂鸦；
- 而 `api.deepseek.com` 只认 **TLS 1.2**。Win98 时代流行的 SSL 3.0 / TLS 1.0，在大模型面前属于"上个世纪的话题"。

`dotnet48-win98` 给出的答案是 `TLS98.DLL`：用 [BearSSL](https://bearssl.org/) 在 Win98 上重新实现了 SChannel 的客户端侧 SSPI，再把 .NET 对 `secur32` 的 P/Invoke 悄悄路由过去。于是 `SslStream`、`HttpWebRequest`、`WebClient`、`HttpClient` 全部原地复活。

也就是说：

> **Win98 不是上不了网，它只是没听过 TLS 1.2 这个名字。**

本项目做的全部事情，就是站在这块已经铺好的地基上，用 `HttpWebRequest` 发一个普普通通的 POST，然后感慨一句：这玩意儿居然真的通了。

---

## 功能

- **API Key 输入框**：密码掩码显示，不落盘、不记忆、不传给别人（毕竟它只传给了 DeepSeek）
- **多行输入 + 发送按钮**：Ctrl 不用按，回车不用按，鼠标点一下就行
- **多轮对话**：请求会带上当前会话的完整历史（只回传 role/content，思考内容不回灌给模型）
- **会话管理**：左侧列表可新建（Ctrl+N）、删除（Delete），右键菜单重命名/删除，F2 重命名，拖动分隔条调宽度，`＜` 收起/展开；新建/删除为图标按钮并带悬停提示
- **自动标题**：首轮请求附带 `title.enabled`，标题随回答的最后一个分片返回（不计费、不额外发请求）；没返回时用用户消息前 12 个字兜底
- **本地存档**：每个会话以 JSON 保存在 exe 同目录的 `sessions\` 下，包含用户请求、思考内容和模型回复
- **固定使用思考模式**：模型名固定为 `deepseek-flash`，`reasoning_effort` 固定为 `high`
- **思考动画**：等待时状态栏转动 `|/-\`，右侧单行控件实时滚动显示最新一句思考内容（换行显示为字面量 `\r\n`，正文区不展示思考）
- **SSE 流式接收 + 打字机效果**：服务器分片到达后先入缓冲队列，再由 20ms 定时器每帧吐 3 个字符，约 150 字/秒，纯手工打字的仪式感
- **对话记录带时间**：每条消息头显示 `你  |  2026-11-18 17:18:37`，时间取自存档里的 `created_at`，重新打开历史会话时显示的是当时的时间
- **纯文本展示 Markdown**：`#`、`**`、`-` 原样输出，不做渲染，复古得很
- **错误可视化**：非 2xx 时从 `WebException.Response` 里捞出状态码和服务器原始 JSON，弹窗展示，方便你确认自己是不是 Key 填错了

## 界面

```
+------------------+-------------------------------------------+
| [新建] [删除]    | ＜  API Key [ ********************** ]    |
| +--------------+ | +---------------------------------------+ |
| | 记住用户30岁 | | | 在这里输入你想问的问题                | |
| | 新会话       | | +---------------------------------------+ |
| |              | | [ 发送 ]  思考中 \                        |
| +--------------+ | +---------------------------------------+ |
|                  | | 你  |  2026-11-18 17:18:37              | |
|                  | | 我今年 30 岁，请记住这个数字。        | |
|                  | |                                       | |
|                  | | AI  |  2026-11-18 17:18:38              | |
|                  | | 好的，我记住了……（逐字打出来）        | |
|                  | +---------------------------------------+ |
+------------------+-------------------------------------------+
```

<img width="908" height="667" alt="image" src="https://github.com/user-attachments/assets/07e446fd-0605-4d0f-a558-4152fc66648b" />

<img width="908" height="667" alt="image" src="https://github.com/user-attachments/assets/852057e7-4ffb-400b-b457-8ca6f3f1fb3e" />


左侧列表的宽度可以拖动中间的竖条调整，点 `＜` 收起后按钮变成 `＞`；新建/删除按钮是 24×24 的图标按钮，鼠标悬停会提示对应快捷键。

## 请求长这样

```json
{
  "model": "deepseek-flash",
  "stream": true,
  "reasoning_effort": "high",
  "title": { "enabled": true },
  "messages": [
    { "role": "user", "content": "你好，介绍一下你自己" }
  ]
}
```

`title` 只在会话第一轮请求时带上，标题会随最后一个分片（`delta.title`）一起回来，不额外收费；之后的轮次不再开启。

响应走 SSE，每行一个 `data:` 分片。思考内容（`reasoning_content`）不进正文，只把最新尾部滚动显示在“思考中”右边那一行里，完整内容写进 JSON；正文取 `delta.content`，先用 20ms 定时器缓冲，再像老式打字机一样一个字一个字吐出来。

## 编译

Win98 是纯 32 位系统，所以 `/platform:x86` 不是可选项，是保命符。

在任意一台装了 .NET Framework 4.8 的现代 Windows 上：

```powershell
& "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x86 `
  /codepage:65001 /optimize+ /win32icon:"app.ico" /out:"DeepSeekChat_x86.exe" `
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  /reference:System.Web.Extensions.dll /reference:System.Core.dll `
  /reference:Microsoft.VisualBasic.dll `
  "DeepSeekChat.cs"
```

编译产物：`DeepSeekChat_x86.exe`（约 20 KB，比一张 GIF 还小）。

- `/codepage:65001` 是为了让源码里的中文界面文字别变成火星文
- `/win32icon` 决定资源管理器里显示的 exe 图标；窗口图标和新建/删除按钮图标则是以 **base64 字符串写进源码**，运行时解码，所以跑起来不需要任何 `.ico` 文件
- `Microsoft.VisualBasic.dll` 只为了那个重命名用的 `InputBox`，Win98 的 .NET 4.8 里自带

## 在 Win98 上运行

1. Windows 98 SE，CPU 需支持 SSE2
2. 安装 [KernelEx 4.5.2+](http://kernelex.sourceforge.net/)
3. 按 [dotnet48-win98 的说明](https://github.com/11EJDE11/dotnet48-win98#quick-install) 装好 .NET Framework 4.8 运行时，直到记事本里出现 `HELLO48 OK`
  - .Net 环境所需的 `gdiplus.dll`、 KernelEx 环境所需的 `unicows` （后者提取到 `C:\Windows\SYSTEM`）可在 [Retro Stuff](https://retro.remotecpu.com/win98.php) 网站页面下载。
4. 确认 `C:\WINDOWS\SYSTEM\CACERT.PEM` 存在（根证书全靠它），系统时间正确
5. 把 `dschat.exe` 拷过去，双击，填 Key，发送

如果 HTTPS 报证书错误，设 `TLS98_LOG=1` 再跑一次，日志在 `C:\DOTNET48\TLS98.LOG`——该项目作者早就替你想到了这一天。

## 已知限制（诚实版）

- **思考行只给一行**：旁边那条单行控件只滚最新一轮的内容，想看完整思考过程请自行脑补（完整内容在 JSON 里）
- **不能取消请求**：点下去就等着，和当年拨号上网一个脾气
- **没有 TLS 1.3**：`TLS98.DLL` 只做到 TLS 1.2；哪天服务器强制 1.3，本程序当场退休
- **API Key 不保存**：每次重新输入，安全得像在输 BIOS 密码
- **需要网络**：这大概是整份 README 里最不幽默的一条

## 致谢

- [11EJDE11/dotnet48-win98](https://github.com/11EJDE11/dotnet48-win98)：让 .NET 4.8 在 Win98 上跑起来，也让 TLS 1.2 从"不可能"变成"能用"——**没有它，本仓库只是一个笑话**
- [BearSSL](https://bearssl.org/)：`TLS98.DLL` 里真正的 TLS 实现
- [KernelEx](http://kernelex.sourceforge.net/)：Win98 能撑到今天的原因之一
- DeepSeek API：愿意接受一个来自 1998 年的 User-Agent


整活项目，代码随便看随便改。真要用在什么严肃场合，请先给 Windows 98 磕一个。
