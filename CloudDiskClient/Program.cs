using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CloudDisk.References;

// =====================================================================
//  CloudDisk Client · 云盘命令行客户端
//
//  功能:
//    · 注册、登录(按权限自动进入对应目录)
//    · 目录浏览、切换目录、获取磁盘列表
//    · 下载文件到本地
//
//  连接方式:优先通过 IPv6 连接服务器,失败后自动回退到 IPv4
// =====================================================================

namespace CloudDisk.Client;

internal static class Program
{
    // 全局复用的 HTTP 客户端
    static readonly HttpClient HttpClient = new();
    // JSON 反序列化选项:属性名不区分大小写,便于匹配服务器字段
    private static JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    // 当前连接的服务端地址
    static string _url = "";
    // 客户端当前所在目录(登录后初始化为 D:)
    static string _currentDir = "";
    // 当前登录用户的权限(Unknown 表示未登录)
    static Permission _currentPermission = Permission.Unknown;
    // 当前用户可访问的磁盘列表
    static List<string> _currentDisk = new();
    
    static async Task Main(string[] args)
    {
        // Console.WriteLine(new string[1]);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 开始连接服务器");
        
        // 第一次连接尝试:IPv6
        _url = "http://[2409:8a1e:2321:70d0:9744:c5c0:5e93:b9a3]:80/client";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await HttpClient.GetAsync(_url, cts.Token);
            if (!response.IsSuccessStatusCode) throw new Exception();
        }
        catch (Exception)
        {
            // IPv6 连接失败,回退到 IPv4 再试一次
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ipv6连接失败,自动尝试ipv4");
            _url = "http://192.168.1.2:80/client";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var response = await HttpClient.GetAsync(_url, cts.Token);
                if (!response.IsSuccessStatusCode) throw new Exception();
            }
            catch (Exception)
            {
                // IPv4 也连接失败,退出程序
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ipv4连接失败,客户端退出");
                Exit();
            }
        }
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已连接到远程api {_url}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 您可以开始使用了,键入help以获取帮助");
        // 主循环:逐行读取用户输入并交给 HandleFunc 处理
        while (true)
        {
            Console.Write($"{(_currentPermission == Permission.Unknown ? "请先登录/注册" : _currentDir)} >>> ");
            string? input = Console.ReadLine();
            // 输入流结束(管道/EOF)时正常退出
            if (input is null) break;
            await HandleFunc(input);
        }
        
        Exit();
    }

    /// <summary>退出程序;输入被重定向(管道/自动化)时跳过“按任意键”等待</summary>
    static void Exit()
    {
        Console.Write("按任意键退出...");
        if (!Console.IsInputRedirected)
        {
            Console.ReadKey(true);
        }
        Environment.Exit(0);
    }
    
    static async Task HandleFunc(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        string[] parts = input.Split(' ');
        // 支持的命令:
        //   help                         获取帮助                        guest
        //   login [用户名] [密码]        登录                            guest
        //   register [用户名] [密码]     注册                            guest
        //   dir                          输出当前目录下的文件/文件夹        guest
        //   download [路径]              下载文件                        guest
        //   cd [路径]                    进入目录(.. / 绝对路径 / 相对路径)  guest
        //   del [路径]                   删除文件                        admin
        //   upload [本地路径] [远程路径] 上传文件                        user, admin
        //   rd [路径]                    删除文件夹                      admin
        //   md [路径]                    新建文件夹                      user, admin
        //   disk                         获取可访问的磁盘列表              guest
        //   admin [用户名]               设置权限为管理员                 admin
        //   user [用户名]                设置权限为用户                   admin
        //   guest [用户名]               设置权限为访客                   admin
        //   exit                         退出                            guest
        // 注:del / upload / rd / md / admin / user / guest 目前尚未实现
        
        switch (parts[0])
        {
            // help:按当前权限打印可用命令
            case "help":
                switch (_currentPermission)
                {
                    case Permission.Admin:
                        Console.Write("""
                                      help                          获取帮助
                                      dir                           输出当前目录下的文件/文件夹
                                      download [路径]               下载文件
                                      cd [路径]                     进入目录(.. / 绝对路径 / 相对路径)
                                      del [路径]                    删除文件
                                      upload [本地路径] [远程路径]  上传文件
                                      rd [路径]                     删除文件夹
                                      md [路径]                     新建文件夹
                                      disk                          获取可访问的磁盘列表(全部磁盘)
                                      admin [用户名]                设置权限为管理员
                                      user [用户名]                 设置权限为用户
                                      guest [用户名]                设置权限为访客
                                      exit                          退出
                                      
                                      """);
                        break;
                    
                    case Permission.User:
                        Console.Write("""
                                      help                          获取帮助
                                      dir                           输出当前目录下的文件/文件夹
                                      download [路径]               下载文件
                                      cd [路径]                     进入目录(.. / 绝对路径 / 相对路径)
                                      upload [本地路径] [远程路径]  上传文件
                                      md [路径]                     新建文件夹
                                      disk                          获取可访问的磁盘列表(仅 D:)
                                      exit                          退出
                                      
                                      """);
                        break;
                    
                    case Permission.Guest:
                        Console.Write("""
                                      help                          获取帮助
                                      dir                           输出当前目录下的文件/文件夹
                                      download [路径]               下载文件
                                      cd [路径]                     进入目录(.. / 绝对路径 / 相对路径)
                                      disk                          获取可访问的磁盘列表(仅 D:)
                                      exit                          退出
                                      
                                      """);
                        break;
                    
                    default:
                        // 未登录时只显示登录/注册相关命令
                        Console.Write("""
                                      help                          获取帮助
                                      login [用户名] [密码]         登录
                                      register [用户名] [密码]      注册
                                      exit                          退出
                                      
                                      """);
                        break;
                }
                break;
            
            // exit:退出程序
            case "exit":
                Exit();
                break;

            // login:登录;成功后按权限自动进入对应目录
            case "login":
            {
                var loginUserInfo = new { Username = "", Password = "" };
                try
                {
                    loginUserInfo = new { Username = parts[1], Password = parts[2] };
                }
                catch (Exception)
                {
                    Console.WriteLine("非法输入,键入help以获取帮助");
                    break;
                }

                // 向服务器发送登录请求
                using var loginResponse = await HttpClient.PostAsJsonAsync(_url + "/login", loginUserInfo);
                // Console.WriteLine(_url + "/login");
                if (loginResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized)
                {
                    var loginJson =
                        JsonSerializer.Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync(),
                            _options)!;
                    Console.WriteLine(loginJson.Message);
                    if (!loginResponse.IsSuccessStatusCode) return;
                    
                    Console.WriteLine($"你的权限是{loginJson.Permission}");
                    _currentPermission = loginJson.Permission;
                    switch (loginJson.Permission)
                    {
                        // 访客/普通用户只能访问 D:,自动进入并列出目录
                        case Permission.Guest or Permission.User:
                            await HandleFunc("cd d:");
                            await HandleFunc("dir");
                            break;
                        
                        // 管理员登录后先拉取磁盘列表,再让用户选择要进入的盘
                        case Permission.Admin:
                            await HandleFunc("disk");
                            string? diskInput;
                            while (true)
                            {
                                Console.Write("请输入要进入的盘符(输入单个字母) >>> ");
                                diskInput = Console.ReadLine();
                                if (_currentDisk.Contains(diskInput?.ToUpper() + ":")) break;
                                Console.WriteLine($"非法输入\'{diskInput}\'");
                            }

                            await HandleFunc($"cd {diskInput!.ToUpper() + ":"}");
                            await HandleFunc("dir");

                            break;
                    }
                    
                }
                // Console.WriteLine(loginResponse);
                // Console.WriteLine(response);
                break;
            }
            
            // register:注册新用户;成功后自动进入 D: 盘
            case "register":
            {
                var registerUserInfo = new { Username = "", Password = "" };
                try
                {
                    registerUserInfo = new { Username = parts[1], Password = parts[2] };
                }
                catch (Exception)
                {
                    Console.WriteLine("非法输入,键入help以获取帮助");
                    break;
                }
                
                using var registerResponse = await HttpClient.PostAsJsonAsync(_url + "/register", registerUserInfo);
                if (registerResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
                {
                    var registerJson =
                        JsonSerializer.Deserialize<RegisterResponse>(await registerResponse.Content.ReadAsStringAsync(),
                            _options)!;
                    Console.WriteLine(registerJson.Message);
                    if (!registerResponse.IsSuccessStatusCode) return;
                    
                    Console.WriteLine($"你的权限是{registerJson.Permission}");
                    _currentPermission = registerJson.Permission;

                    // 注册成功自动进入 D: 并列出目录
                    await HandleFunc("cd d:");
                    await HandleFunc("dir");
                }
                
                break;
            }
            
            // dir:请求服务器列出当前目录内容
            case "dir":
            {
                if (_currentPermission == Permission.Unknown) break;
                
                using var dirResponse = await HttpClient.GetAsync(_url + $"/dir?dir={Uri.EscapeDataString(_currentDir)}");
                if (dirResponse.IsSuccessStatusCode)
                {
                    var dirJson =
                        JsonSerializer.Deserialize<string[]>(await dirResponse.Content.ReadAsStringAsync(), _options)!;
                    foreach (var file in dirJson)
                    {
                        Console.WriteLine(file);
                    }
                }
                break;
            }
            
            // cd:切换目录(.. / 绝对路径 / 相对路径)
            case "cd":
                if (_currentPermission == Permission.Unknown) break;
                
                try
                {
                    if (parts.Length == 1) throw new Exception();
                }
                catch (Exception)
                {
                    Console.WriteLine("非法输入,键入help以获取帮助");
                    break;
                }

                string cdDir = "";
                foreach (var part in parts[1..])
                {
                    cdDir += part + " ";
                }
                cdDir = cdDir.TrimEnd(' ');

                if (cdDir == "..")  // 上级
                {
                    if (_currentDir[^1] != ':')
                    {
                        _currentDir = Path.GetDirectoryName(_currentDir)!.TrimEnd('\\');
                    }
                }
                else if (cdDir[1] == ':')  // 绝对路径
                {
                    if (!cdDir[0].ToString().Equals("D", StringComparison.CurrentCultureIgnoreCase) && _currentPermission != Permission.Admin) break;
                    
                    cdDir = cdDir[0].ToString().ToUpper() + cdDir[1..] + "\\";
                    // Console.WriteLine(result);
                    using var cdResponse = await HttpClient.GetAsync(_url + $"/cd?dir={Uri.EscapeDataString(cdDir)}");
                    // Console.WriteLine(Uri.EscapeDataString(parts[1]));
                    if (cdResponse.IsSuccessStatusCode) _currentDir = cdDir.TrimEnd('\\');
                    else Console.WriteLine("目标目录不存在");
                }
                else  // 相对路径
                {
                    var targetDir = _currentDir + "\\" + cdDir;
                    // Console.WriteLine(targetDir);
                    using var cdResponse = await HttpClient.GetAsync(_url + $"/cd?dir={Uri.EscapeDataString(targetDir + "\\")}");
                    if (cdResponse.IsSuccessStatusCode) _currentDir = targetDir;
                }
                
                break;
            
            // disk:获取并打印可访问的磁盘列表
            case "disk":
            {
                if (_currentPermission == Permission.Unknown) break;
                
                Console.WriteLine("你可访问的远程计算机盘符有:");
                switch (_currentPermission)
                {
                    // 非管理员固定只有 D: 盘
                    case not Permission.Admin and not Permission.Unknown:
                        Console.WriteLine("D:");
                        _currentDisk = new List<string> { "D:" };
                        break;
                    
                    // 管理员向服务器请求全部磁盘列表
                    case Permission.Admin:
                    {
                        using var diskResponse = await HttpClient.GetAsync(_url + "/get_disk");
                        if (diskResponse.IsSuccessStatusCode)
                        {
                            var diskJson =
                                JsonSerializer.Deserialize<string[]>(await diskResponse.Content.ReadAsStringAsync(),
                                    _options)!;
                            _currentDisk = diskJson.ToList();
                            foreach (var disk in diskJson)
                            {
                                Console.Write(disk + (disk == diskJson[^1] ? "" : " "));
                            }
                            Console.WriteLine();
                        }
                        
                        break;
                    }
                }

                break;
            }
            
            // download:从服务器下载文件到本地
            case "download":
            {
                try
                {
                    if (parts.Length == 1) throw new Exception();
                }
                catch (Exception)
                {
                    Console.WriteLine("非法输入,键入help以获取帮助");
                    break;
                }

                string downloadDir = "";
                foreach (var part in parts[1..])
                {
                    downloadDir += part + " ";
                }
                downloadDir = downloadDir.TrimEnd(' ');

                using var downloadResponse =
                    await HttpClient.GetAsync(_url + "/download", HttpCompletionOption.ResponseHeadersRead);
                if (!downloadResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("下载失败");
                    break;
                }
                string fileName = downloadResponse.Content.Headers.ContentDisposition?.FileName?.Trim('\"') ??
                                   Path.GetFileName(downloadDir);
                const string saveDir = @"C:\james\Download";

                await using var stream = await downloadResponse.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(saveDir);
                await stream.CopyToAsync(fileStream);
                
                Console.WriteLine("下载成功,已保存到Downloads文件夹");
                
                break;
            }
        }
    }
}
