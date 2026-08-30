using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using CloudDisk.References;

// =====================================================================
//  CloudDisk Server · 云盘服务端
//
//  功能:
//    · 浏览器端:首页、文件浏览、文件下载(仅允许访问 D: 盘)
//    · 客户端 API:健康检查、注册、登录、目录列表、磁盘列表、目录切换
//
//  服务默认监听 HTTP 80 端口,用户数据保存在 UserInfo.json
// =====================================================================

namespace CloudDisk.Server;

internal static class Program
{
    // 保护 UserInfo.json 读写操作的锁,避免并发读写冲突
    static Lock _fileLock = new();
    // 服务器对外暴露的磁盘列表(管理员可用)
    internal static readonly string[] Disks = new[] { "C:", "D:", "E:", "F:" };

    static void Main(string[] args)
    {
        // 创建并初始化 Web 应用
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        // GET / : 返回首页(读取 templates/index.html 模板)
        app.MapGet("/", () => Results.Content(File.ReadAllText(@".\templates\index.html"), "text/html"));

        // GET /browse?dir=xxx : 浏览器文件浏览(仅允许 D: 盘),返回动态生成的 HTML 文件列表
        app.MapGet("/browse", (string dir) =>
        {
            Console.WriteLine($"浏览器访客访问{dir}");

            // 参数校验:目录参数不能为空
            if (string.IsNullOrWhiteSpace(dir))
            {
                return Results.BadRequest("缺少目录参数");
            }

            // 权限校验:只允许访问 D: 盘(不区分大小写)
            if (!dir.StartsWith("D:", StringComparison.CurrentCultureIgnoreCase))
            {
                return Results.Text($"你无权访问{dir}", statusCode: StatusCodes.Status403Forbidden);
            }

            // 目录不存在时返回 404
            if (!Directory.Exists(dir))
            {
                return Results.Text($"目录不存在:{dir}", statusCode: StatusCodes.Status404NotFound);
            }

            // 页面头尾模板:静态 HTML/CSS/JS 写死在 head/end 中,中间插入动态列表
            const string head = """
                                <!DOCTYPE html>
                                <html lang="zh-CN">
                                <head>
                                <meta charset="utf-8">
                                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                                <title>Cloud Disk · 文件浏览</title>
                                <style>
                                :root {
                                    --primary: #4f8cff;
                                    --primary-2: #7b5cff;
                                    --accent: #35d0ba;
                                    --panel: rgba(255, 255, 255, .06);
                                    --panel-hover: rgba(255, 255, 255, .12);
                                    --line: rgba(255, 255, 255, .10);
                                    --text: #ffffff;
                                    --text-dim: rgba(255, 255, 255, .72);
                                    --text-faint: rgba(255, 255, 255, .45);
                                }
                                * {
                                    margin: 0;
                                    padding: 0;
                                    box-sizing: border-box;
                                }
                                body {
                                    min-height: 100vh;
                                    font-family: "Segoe UI", "PingFang SC", "Microsoft YaHei", "Helvetica Neue", Arial, sans-serif;
                                    color: var(--text);
                                    background:
                                        radial-gradient(1200px 800px at 15% 10%, rgba(79, 140, 255, .20), transparent 60%),
                                        radial-gradient(1000px 700px at 85% 85%, rgba(123, 92, 255, .22), transparent 60%),
                                        linear-gradient(135deg, #0b1026 0%, #101a3c 55%, #0d0f24 100%);
                                    background-attachment: fixed;
                                    -webkit-font-smoothing: antialiased;
                                }
                                .topbar {
                                    position: sticky;
                                    top: 0;
                                    z-index: 10;
                                    display: grid;
                                    grid-template-columns: 1fr auto 1fr;
                                    align-items: center;
                                    gap: 18px;
                                    padding: 14px 26px;
                                    background: rgba(11, 16, 38, .78);
                                    border-bottom: 1px solid var(--line);
                                    backdrop-filter: blur(14px);
                                    -webkit-backdrop-filter: blur(14px);
                                }
                                .brand {
                                    grid-column: 2;
                                    justify-self: center;
                                    display: inline-flex;
                                    align-items: center;
                                    gap: 9px;
                                    color: var(--text);
                                    text-decoration: none;
                                    font-size: 16px;
                                    font-weight: 600;
                                    letter-spacing: 1px;
                                    white-space: nowrap;
                                }
                                .brand svg {
                                    width: 26px;
                                    height: 26px;
                                    color: var(--accent);
                                }
                                .path {
                                    grid-column: 3;
                                    justify-self: end;
                                    min-width: 0;
                                    text-align: right;
                                    font-size: 13px;
                                    color: var(--text-dim);
                                    overflow: hidden;
                                    text-overflow: ellipsis;
                                    white-space: nowrap;
                                    direction: rtl;
                                    unicode-bidi: plaintext;
                                }
                                .list {
                                    max-width: 880px;
                                    margin: 30px auto 90px;
                                    padding: 0 20px;
                                    display: flex;
                                    flex-direction: column;
                                    gap: 10px;
                                }
                                .item {
                                    display: flex;
                                    align-items: center;
                                    gap: 14px;
                                    padding: 14px 18px;
                                    border-radius: 14px;
                                    background: var(--panel);
                                    border: 1px solid var(--line);
                                    color: var(--text);
                                    text-decoration: none;
                                    animation: rise .4s ease both;
                                    transition: transform .2s ease, background .2s ease, border-color .2s ease, box-shadow .2s ease;
                                }
                                .item:nth-child(1) { animation-delay: .02s; }
                                .item:nth-child(2) { animation-delay: .06s; }
                                .item:nth-child(3) { animation-delay: .10s; }
                                .item:nth-child(4) { animation-delay: .14s; }
                                .item:nth-child(5) { animation-delay: .18s; }
                                .item:nth-child(6) { animation-delay: .22s; }
                                .item:nth-child(7) { animation-delay: .26s; }
                                .item:nth-child(8) { animation-delay: .30s; }
                                .item:nth-child(9) { animation-delay: .34s; }
                                .item:nth-child(10) { animation-delay: .38s; }
                                .item:hover {
                                    transform: translateX(6px);
                                    background: var(--panel-hover);
                                    border-color: rgba(79, 140, 255, .55);
                                    box-shadow: 0 10px 26px rgba(0, 0, 0, .28);
                                }
                                .icon {
                                    width: 30px;
                                    height: 30px;
                                    flex: none;
                                    display: inline-flex;
                                    align-items: center;
                                    justify-content: center;
                                    border-radius: 9px;
                                    font-size: 17px;
                                    background: rgba(255, 255, 255, .08);
                                }
                                .item.up .icon {
                                    background: rgba(53, 208, 186, .14);
                                    color: var(--accent);
                                }
                                .item.folder .icon {
                                    color: #ffc46b;
                                }
                                .item.file .icon {
                                    color: #9cc3ff;
                                }
                                .name {
                                    flex: 1;
                                    min-width: 0;
                                    font-size: 15px;
                                    overflow: hidden;
                                    text-overflow: ellipsis;
                                    white-space: nowrap;
                                }
                                .item.up .name {
                                    color: var(--text-dim);
                                    font-size: 14px;
                                }
                                .arrow {
                                    color: var(--text-faint);
                                    font-size: 22px;
                                    line-height: 1;
                                }
                                .empty {
                                    padding: 70px 20px;
                                    text-align: center;
                                    color: var(--text-faint);
                                    font-size: 15px;
                                    letter-spacing: 1px;
                                }
                                .footer {
                                    position: fixed;
                                    left: 0;
                                    right: 0;
                                    bottom: 0;
                                    padding: 12px 0;
                                    text-align: center;
                                    font-size: 13px;
                                    letter-spacing: 1px;
                                    color: var(--text-faint);
                                    background: rgba(11, 16, 38, .72);
                                    border-top: 1px solid var(--line);
                                    backdrop-filter: blur(14px);
                                    -webkit-backdrop-filter: blur(14px);
                                }
                                @keyframes rise {
                                    from {
                                        opacity: 0;
                                        transform: translateY(12px);
                                    }
                                    to {
                                        opacity: 1;
                                        transform: translateY(0);
                                    }
                                }
                                @media (max-width: 520px) {
                                    .topbar {
                                        padding: 12px 16px;
                                        gap: 10px;
                                    }
                                    .path {
                                        font-size: 12px;
                                    }
                                    .list {
                                        padding: 0 14px;
                                        margin-top: 20px;
                                    }
                                    .icon {
                                        width: 26px;
                                        height: 26px;
                                        font-size: 15px;
                                    }
                                    .name {
                                        font-size: 14px;
                                    }
                                }
                                </style>
                                </head>
                                <body>
                                <header class="topbar">
                                <a class="brand" href="/">
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                                <path d="M17.5 19a4.5 4.5 0 0 0 .42-8.98 6 6 0 0 0-11.7 1.6A4 4 0 0 0 6.5 19h11z"/>
                                </svg>
                                <span>云端存储</span>
                                </a>
                                <div class="path" id="pathLabel">/</div>
                                </header>
                                <main class="list" id="fileList">
                                """;
            const string end = """
                               </main>
                               <footer class="footer">
                               <span id="countLabel"></span><span> · 云端存储</span>
                               </footer>
                               <script>
                               (function () {
                                   var list = document.getElementById("fileList");
                                   var items = Array.prototype.slice.call(list.children);
                                   var params = new URLSearchParams(location.search);
                                   var dir = params.get("dir") || "";
                                   document.getElementById("pathLabel").textContent = dir;

                                   var up = null;
                                   for (var i = 0; i < items.length; i++) {
                                       if (items[i].classList.contains("up")) {
                                           up = items.splice(i, 1)[0];
                                           break;
                                       }
                                   }

                                   items.sort(function (a, b) {
                                       var fa = a.classList.contains("folder") ? 0 : 1;
                                       var fb = b.classList.contains("folder") ? 0 : 1;
                                       if (fa !== fb) return fa - fb;
                                       return a.textContent.localeCompare(b.textContent, "zh-CN");
                                   });

                                   if (up) list.appendChild(up);
                                   items.forEach(function (el) {
                                       list.appendChild(el);
                                   });

                                   if (items.length === 0) {
                                       var empty = document.createElement("div");
                                       empty.className = "empty";
                                       empty.textContent = "此文件夹为空";
                                       list.appendChild(empty);
                                   }

                                   document.getElementById("countLabel").textContent = "共 " + items.length + " 项";
                               })();
                               </script>
                               </body>
                               </html>
                               """;
            string html = "";

            string[] files;
            List<string> filesTemp = new();

            // 读取目录下所有条目(文件与文件夹)
            string[] dirs = Directory.GetFileSystemEntries(dir);
            foreach (var file in dirs)
            {
                filesTemp.Add(Path.GetFileName(file));
            }

            files = filesTemp.ToArray();

            // 非根目录时,在列表顶部追加“返回上一级”入口
            if (!string.Equals(dir, "D:", StringComparison.CurrentCultureIgnoreCase) &&
                !string.Equals(dir, @"D:\", StringComparison.CurrentCultureIgnoreCase))
            {
                html +=
                    "<a class=\"item up\" href=\"/browse?dir=" + Uri.EscapeDataString(Path.GetDirectoryName(dir)?.TrimEnd('\\')!) +
                    "\"><span class=\"icon\">↩</span><span class=\"name\">返回上一级</span><span class=\"arrow\">›</span></a>";
            }

            // 生成文件/文件夹列表项:文件夹跳转 /browse,文件跳转 /browse/download
            foreach (var file in files)
            {
                html +=
                    $"<a class=\"item {(File.Exists(dir + @"\" + file) ? "file" : "folder")}\" href=\"/{(File.Exists(dir + @"\" + file) ? "browse/download" : "browse")}?dir={Uri.EscapeDataString(dir + @"\" + file)}\">" +
                    $"<span class=\"icon\">{(File.Exists(dir + @"\" + file) ? "📄" : "📁")}</span><span class=\"name\">{file}</span><span class=\"arrow\">›</span></a>";
            }

            return Results.Content(head + html + end, "text/html");
        });

        // GET /browse/download?dir=xxx : 浏览器下载文件(仅允许 D: 盘)
        app.MapGet("/browse/download", (string dir) =>
        {
            // 参数校验:文件路径不能为空
            if (string.IsNullOrWhiteSpace(dir))
            {
                return Results.BadRequest("缺少文件路径");
            }

            // 权限校验:只允许下载 D: 盘的文件
            if (!dir.StartsWith("D:", StringComparison.CurrentCultureIgnoreCase))
            {
                return Results.Text($"你无权下载文件{dir}", statusCode: 403);
            }
            
            Console.WriteLine($"浏览器访客下载{dir}");

            // 文件不存在时返回 404
            if (!File.Exists(dir))
            {
                return Results.Text($"文件不存在:{dir}", statusCode: StatusCodes.Status404NotFound);
            }

            // 以附件形式返回文件流(该流由 Results.File 在响应结束后自动释放,无需手动 using)
            var stream = File.OpenRead(dir);
            string fileName = Path.GetFileName(dir);
            return Results.File(stream, "application/octet-stream", fileName);
        });

        // GET /client : 客户端连接健康检查(客户端启动时探测用)
        app.MapGet("/client", () =>
        {
            Console.WriteLine("客户端连接成功");
            return Results.Ok();
        });
        
        // POST /client/register : 注册新用户,新用户默认分配 Guest 权限
        app.MapPost("/client/register", (RegisterRequest req) =>
        {
            // 参数校验:用户名和密码不能为空
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new RegisterResponse("用户名和密码不能为空"));
            
            // 密码只保存 SHA-256 哈希值,不保存明文
            User currentUser = new(req.Username, HashPassword(req.Password), Permission.Guest);
            List<User> users = LoadUsers();
            // 用户名查重(不区分大小写),重复时返回 409
            foreach (var user in users)
            {
                if (string.Equals(user.Username, currentUser.Username, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(new RegisterResponse("用户名已存在"), statusCode: StatusCodes.Status409Conflict);
                }
            }
            // 查重通过后写入用户列表并持久化
            users.Add(currentUser);
            SaveUsers(users);
            Console.WriteLine("客户端注册,已分配权限Guest");
            // 创建成功返回 201 Created
            return Results.Json(new RegisterResponse("注册成功", Permission.Guest), statusCode: StatusCodes.Status201Created);
        });

        // POST /client/login : 登录校验,用户名不区分大小写,密码比对哈希值
        app.MapPost("/client/login", (LoginRequest req) =>
        {
            // Console.WriteLine(req);
            
            // 参数校验:用户名和密码不能为空
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new LoginResponse("用户名和密码不能为空"));
            
            List<User> users = LoadUsers();
            foreach (var user in users)
            {
                if (string.Equals(req.Username, user.Username, StringComparison.OrdinalIgnoreCase) &&
                    HashPassword(req.Password) == user.Password)
                {
                    User currentUser = new(req.Username, HashPassword(req.Password), user.Permission);
                    Console.WriteLine($"客户端用户{req.Username}登录,权限{currentUser.Permission}");
                    return Results.Ok(new LoginResponse("登录成功", currentUser.Permission));
                }
            }

            // 未找到匹配用户或密码错误,统一返回 401
            return Results.Json(new LoginResponse("登录失败,用户名或密码错误"), statusCode: 401);
        });

        // GET /client/dir?dir=xxx : 客户端获取指定目录下的文件/文件夹名列表(JSON)
        app.MapGet("/client/dir", (string dir) =>
        {
            // 参数校验:目录参数不能为空
            if (string.IsNullOrWhiteSpace(dir))
            {
                return Results.BadRequest("缺少目录参数");
            }

            // 目录不存在时返回 404
            if (!Directory.Exists(dir))
            {
                return Results.NotFound($"目录不存在:{dir}");
            }

            // 读取目录条目并返回文件名列表
            List<string> files = new();
            string[] filesTemp = Directory.GetFileSystemEntries(dir);
            // Console.WriteLine(filesTemp);
            foreach (var file in filesTemp)
            {
                files.Add(Path.GetFileName(file));
            }

            Console.WriteLine($"客户端访问路径{dir}下文件");
            return Results.Json(files.ToArray());
        });

        // GET /client/get_disk : 客户端获取可访问的磁盘列表(JSON)
        app.MapGet("/client/get_disk", () =>
        {
            Console.WriteLine("客户端访问磁盘列表");
            return Results.Json(Disks);
        });

        // GET /client/cd?dir=xxx : 客户端切换目录前的存在性检查
        app.MapGet("/client/cd", (string dir) =>
        {
            // Console.WriteLine($"'{dir}'");
            if (Directory.Exists(dir)) return Results.Ok();
            return Results.NotFound();
        });

        // GET /client/download?dir=xxx : 客户端下载文件
        app.MapGet("/client/download", (string dir) =>
        {
            // 参数校验:文件路径不能为空
            if (string.IsNullOrWhiteSpace(dir))
            {
                return Results.BadRequest("缺少文件路径");
            }
            
            // 文件不存在时返回 404
            if (!File.Exists(dir))
            {
                return Results.Text($"文件不存在:{dir}", statusCode: StatusCodes.Status404NotFound);
            }

            // 以附件形式返回文件流(该流由 Results.File 在响应结束后自动释放,无需手动 using)
            var stream = File.OpenRead(dir);
            string fileName = Path.GetFileName(dir);
            return Results.File(stream, "application/octet-stream", fileName);
        });

        // 监听所有网卡地址的 HTTP 80 端口(HTTP 默认端口)
        app.Run("http://*:80");
    }

    /// <summary>从 UserInfo.json 读取全部用户,文件不存在时返回空列表</summary>
    static List<User> LoadUsers()
    {
        lock (_fileLock)
        {
            if (!File.Exists(@".\UserInfo.json")) return new List<User>();
            string json = File.ReadAllText(@".\UserInfo.json");
            return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
        }
    }

    /// <summary>将用户列表以缩进 JSON 格式写回 UserInfo.json</summary>
    static void SaveUsers(List<User> users)
    {
        lock (_fileLock)
        {
            string json = JsonSerializer.Serialize(users, new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText(@".\UserInfo.json", json);
        }
    }

    /// <summary>计算密码的 SHA-256 哈希并以 Base64 返回,用于安全存储与比对</summary>
    static string HashPassword(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        byte[] hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
