namespace CloudDisk.References;

/// <summary>登录响应:提示消息 + 登录成功后的用户权限</summary>
public record LoginResponse(string Message, Permission Permission = Permission.Unknown);

/// <summary>注册响应:提示消息 + 注册成功后的用户权限</summary>
public record RegisterResponse(string Message, Permission Permission = Permission.Unknown);
