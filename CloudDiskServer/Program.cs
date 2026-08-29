using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using CloudDisk.References;

namespace CloudDisk.Server;

internal static class Program
{
    static Lock _fileLock = new();
    internal static readonly string[] Disks = new[] { "C:", "D:", "E:", "F:" };

    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapGet("/", () => Results.Content(File.ReadAllText(@".\templates\index.html"), "text/html"));

        app.MapGet("/browse", (string dir) =>
        {
            Console.WriteLine($"浏览器访客访问{dir}");

            if (!dir.StartsWith("D:"))
            {
                return Results.BadRequest($"你无权访问{dir}");
            }

            const string head = """
                                <!DOCTYPE html>
                                <html>
                                <head>
                                <title>Cloud Disk</title>
                                <meta charset='utf-8'>
                                <style>
                                a:visited, a:link
                                {
                                    color: #00e;
                                }
                                </style>
                                </head>
                                <body>
                                <ul>
                                """;
            const string end = """
                               </ul>
                               </body>
                               </html>
                               """;
            string html = "";

            string[] files;
            List<string> filesTemp = new();

            string[] dirs = Directory.GetFileSystemEntries(dir);
            foreach (var file in dirs)
            {
                filesTemp.Add(Path.GetFileName(file));
            }

            files = filesTemp.ToArray();

            if (dir != "D:" && dir != @"D:\")
            {
                html +=
                    $"<li><a href=/browse?dir={Uri.EscapeDataString(Path.GetDirectoryName(dir)?.TrimEnd('\\')!)}>..</a></li>";
            }

            foreach (var file in files)
            {
                html +=
                    $"<li><a href=/{(File.Exists(dir + @"\" + file) ? "browse/download" : "browse")}?dir={Uri.EscapeDataString(dir + @"\" + file)}>{file}</a></li>";
            }

            return Results.Content(head + html + end, "text/html");
        });

        app.MapGet("/browse/download", (string dir) =>
        {
            Console.WriteLine($"浏览器访客下载{dir}");

            var stream = File.OpenRead(dir);
            string fileName = Path.GetFileName(dir);
            return Results.File(stream, "application/octet-stream", fileName);
        });

        app.MapGet("/client", () => Results.Ok());
        
        app.MapPost("/client/register", (RegisterRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new RegisterResponse("用户名和密码不能为空"));
            
            User currentUser = new(req.Username, HashPassword(req.Password), Permission.Guest);
            List<User> users = LoadUsers();
            foreach (var user in users)
            {
                if (user.Username == currentUser.Username)
                {
                    return Results.BadRequest(new RegisterResponse("用户名已存在"));
                }
            }
            users.Add(currentUser);
            SaveUsers(users);
            return Results.Ok(new RegisterResponse("注册成功", Permission.Guest));
        });

        app.MapPost("/client/login", (LoginRequest req) =>
        {
            // Console.WriteLine(req);
            
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new LoginResponse("用户名和密码不能为空"));
            
            List<User> users = LoadUsers();
            foreach (var user in users)
            {
                if (req.Username == user.Username && HashPassword(req.Password) == user.Password)
                {
                    User currentUser = new(req.Username, HashPassword(req.Password), user.Permission);
                    return Results.Ok(new LoginResponse("登录成功", currentUser.Permission));
                }
            }

            return Results.Json(new LoginResponse("登录失败,用户名或密码错误"), statusCode: 401);
        });

        app.MapGet("/client/dir", (string dir) =>
        {
            List<string> files = new();
            string[] filesTemp = Directory.GetFileSystemEntries(dir);
            Console.WriteLine(filesTemp);
            foreach (var file in filesTemp)
            {
                files.Add(Path.GetFileName(file));
            }

            return Results.Json(files.ToArray());
        });

        app.MapGet("/client/get_disk", () => Results.Json(Disks));

        app.Run("http://*:5000");
    }

    static List<User> LoadUsers()
    {
        lock (_fileLock)
        {
            if (!File.Exists(@".\UserInfo.json")) return new List<User>();
            string json = File.ReadAllText(@".\UserInfo.json");
            return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
        }
    }

    static void SaveUsers(List<User> users)
    {
        lock (_fileLock)
        {
            string json = JsonSerializer.Serialize(users, new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText(@".\UserInfo.json", json);
        }
    }

    static string HashPassword(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        byte[] hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}