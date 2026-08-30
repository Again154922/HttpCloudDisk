namespace CloudDisk.References;

/// <summary>注册请求:用户名 + 密码</summary>
public record RegisterRequest(string Username, string Password);

/// <summary>登录请求:用户名 + 密码</summary>
public record LoginRequest(string Username, string Password);
