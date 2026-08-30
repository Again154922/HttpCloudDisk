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
            var response = await HttpClient.GetAsync(_url, cts.Token);
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
                var response = await HttpClient.GetAsync(_url, cts.Token);
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
        //   help                      获取帮助
        //   login [用户名] [密码]     登录
        //   register [用户名] [密码]  注册
        //   dir                       输出当前目录下的文件/文件夹
        //   download [路径]           下载文件
        //   cd [路径]                 进入目录(.. / 绝对路径 / 相对路径)
        //   del [路径]                删除文件
        //   upload [本地路径] [远程路径] 上传文件
        //   rd [路径]                 删除文件夹
        //   md [路径]                 新建文件夹
        //   disk                      获取可访问的磁盘列表
        //   exit                      退出
        // 注:download / del / upload / rd / md 目前尚未实现
        
        switch (parts[0])
        {
            // help:打印帮助信息
            case "help":
                Console.Write("""
                              help 获取帮助
                              login [usrname] [pwd] 登录
                              register [usrname] [pwd] 注册
                              dir 输出当前目录下文件(夹)
                              download [path] 下载文件
                              cd [path] 进入目录 [path]: ..(上级)/绝对路径/向下相对路径
                              del [filepath] 删除文件
                              upload [localpath] [remotepath] 上传文件
                              rd [path] 删除文件夹
                              md [path] 新建文件夹
                              exit 退出
                              
                              """);
                break;
            
            // exit:退出程序
            case "exit":
                Exit();
                break;

            // login:登录;成功后按权限自动进入对应目录
            case "login":
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
                var loginResponse = await HttpClient.PostAsJsonAsync(_url + "/login", loginUserInfo);
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
            
            // register:注册新用户;成功后自动进入 D: 盘
            case "register":
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
                
                var registerResponse = await HttpClient.PostAsJsonAsync(_url + "/register", registerUserInfo);
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
            
            // dir:请求服务器列出当前目录内容
            case "dir":
                if (_currentPermission == Permission.Unknown) break;
                
                var dirResponse = await HttpClient.GetAsync(_url + $"/dir?dir={Uri.EscapeDataString(_currentDir)}");
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

                string result = "";
                foreach (var part in parts[1..])
                {
                    result += part + " ";
                }
                result = result.TrimEnd(' ');

                if (result == "..")  // 上级
                {
                    if (_currentDir[^1] != ':')
                    {
                        _currentDir = Path.GetDirectoryName(_currentDir)!.TrimEnd('\\');
                    }
                }
                else if (result[1] == ':')  // 绝对路径
                {
                    result = result[0].ToString().ToUpper() + result[1..] + "\\";
                    // Console.WriteLine(result);
                    var cdResponse = await HttpClient.GetAsync(_url + $"/cd?dir={Uri.EscapeDataString(result)}");
                    // Console.WriteLine(Uri.EscapeDataString(parts[1]));
                    if (cdResponse.IsSuccessStatusCode) _currentDir = result.TrimEnd('\\');
                    else Console.WriteLine("目标目录不存在");
                }
                else  // 相对路径
                {
                    var targetDir = _currentDir + "\\" + result;
                    // Console.WriteLine(targetDir);
                    var cdResponse = await HttpClient.GetAsync(_url + $"/cd?dir={Uri.EscapeDataString(targetDir + "\\")}");
                    if (cdResponse.IsSuccessStatusCode) _currentDir = targetDir;
                }
                
                break;
            
            // disk:获取并打印可访问的磁盘列表
            case "disk":
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
                        var diskResponse = await HttpClient.GetAsync(_url + "/get_disk");
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

                break;
        }
    }
}
