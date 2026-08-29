using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CloudDisk.References;

namespace CloudDisk.Client;

internal static class Program
{
    static readonly HttpClient HttpClient = new();
    private static JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    static string _url = "";
    static string _currentDir = "";
    static Permission _currentPermission = Permission.Unknown;
    
    static async Task Main(string[] args)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 开始连接服务器");
        
        _url = "http://[2409:8a1e:2321:70d0:9744:c5c0:5e93:b9a3]:5000/client";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await HttpClient.GetAsync(_url, cts.Token);
            if (!response.IsSuccessStatusCode) throw new Exception();
        }
        catch (Exception)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ipv6连接失败,自动尝试ipv4");
            _url = "http://192.168.1.2:5000/client";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await HttpClient.GetAsync(_url, cts.Token);
                if (!response.IsSuccessStatusCode) throw new Exception();
            }
            catch (Exception)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ipv4连接失败,客户端退出");
                Exit();
            }
        }
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已连接到远程api {_url}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 您可以开始使用了,键入help以获取命令帮助");
        while (true)
        {
            Console.Write($"{(_currentPermission == Permission.Unknown ? "请先登录/注册" : _currentDir)} >>> ");
            string? input = Console.ReadLine();
            await HandleFunc(input);
        }
    }

    static void Exit()
    {
        Console.Write("按任意键退出...");
        Console.ReadKey(true);
        Environment.Exit(0);
    }
    
    static async Task HandleFunc(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        string[] parts = input.Split(' ');
        // 命令:
        // help 获取帮助
        // login [usrname] [pwd] 登录
        // register [usrname] [pwd] 注册
        // dir 输出当前目录下文件(夹)
        // download [path] 下载文件
        // cd [path] 进入目录
        // del [filepath] 删除文件
        // upload [localpath] [remotepath] 上传文件
        // rd [path] 删除文件夹
        // md [path] 新建文件夹
        // exit 退出
        
        switch (parts[0])
        {
            case "help":
                Console.Write("""
                              help 获取帮助
                              login [usrname] [pwd] 登录
                              register [usrname] [pwd] 注册
                              dir 输出当前目录下文件(夹)
                              download [path] 下载文件
                              cd [path] 进入目录
                              del [filepath] 删除文件
                              upload [localpath] [remotepath] 上传文件
                              rd [path] 删除文件夹
                              md [path] 新建文件夹
                              
                              """);
                break;
            
            case "login":
                var loginUserInfo = new { Username = parts[1], Password = parts[2] };

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
                        case Permission.Guest or Permission.User:
                            var userLoginDirResponse = await HttpClient.GetAsync(_url + @"/dir?dir=D:\");
                            if (userLoginDirResponse.IsSuccessStatusCode)
                            {
                                var userLoginDirJson =
                                    JsonSerializer.Deserialize<string[]>(
                                        await userLoginDirResponse.Content.ReadAsStringAsync(), _options)!;
                                _currentDir = @"D:\";
                                foreach (var file in userLoginDirJson)
                                {
                                    Console.WriteLine(file);
                                }
                            }

                            break;
                        
                        case Permission.Admin:
                            var diskResponse = await HttpClient.GetAsync(_url + "/get_disk");
                            if (diskResponse.IsSuccessStatusCode)
                            {
                                var diskJson =
                                    JsonSerializer.Deserialize<string[]>(await diskResponse.Content.ReadAsStringAsync(),
                                        _options)!;
                                Console.WriteLine("远程计算机有下列盘符:");
                                foreach (var disk in diskJson)
                                {
                                    Console.Write(disk + (disk != diskJson[^1] ? " " : "\n"));
                                }

                                string? diskInput;
                                while (true)
                                {
                                    Console.Write("请输入要进入的盘符(输入单个字母) >>> ");
                                    diskInput = Console.ReadLine();
                                    if (diskJson.Contains(diskInput?.ToUpper() + ":")) break;
                                    Console.WriteLine($"非法输入\'{diskInput}\'");
                                }

                                var adminLoginDirResponse =
                                    await HttpClient.GetAsync(_url + $@"/dir?dir={diskInput?.ToUpper()}:\");
                                if (adminLoginDirResponse.IsSuccessStatusCode)
                                {
                                    var adminLoginDirJson =
                                        JsonSerializer.Deserialize<string[]>(
                                            await adminLoginDirResponse.Content.ReadAsStringAsync(), _options)!;
                                    _currentDir = diskInput?.ToUpper() + @":\";
                                    foreach (var file in adminLoginDirJson)
                                    {
                                        Console.WriteLine(file);
                                    }
                                }
                            }

                            break;
                    }
                    
                }
                // Console.WriteLine(loginResponse);
                // Console.WriteLine(response);
                break;
            
            case "register":
                var registerUserInfo = new { Username = parts[1], Password = parts[2] };
                var registerResponse = await HttpClient.PostAsJsonAsync(_url + "/register", registerUserInfo);

                if (registerResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest)
                {
                    var registerJson =
                        JsonSerializer.Deserialize<RegisterResponse>(await registerResponse.Content.ReadAsStringAsync(),
                            _options)!;
                    Console.WriteLine(registerJson.Message);
                    if (!registerResponse.IsSuccessStatusCode) return;
                    
                    Console.WriteLine($"你的权限是{registerJson.Permission}");
                    _currentPermission = registerJson.Permission;
                    var registerDirResponse = await HttpClient.GetAsync(_url + @"/dir?dir=D:\");
                    _currentDir = @"D:\";
                    if (registerDirResponse.IsSuccessStatusCode)
                    {
                        var registerDirJson =
                            JsonSerializer.Deserialize<string[]>(await registerDirResponse.Content.ReadAsStringAsync(),
                                _options)!;
                        foreach (var file in registerDirJson)
                        {
                            Console.WriteLine(file);
                        }
                    }
                }
                
                break;
            
            case "dir":
                var dirResponse = await HttpClient.GetAsync(_url + $"/dir?dir={_currentDir}");
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
    }
}