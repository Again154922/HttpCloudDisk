namespace CloudDisk.References;

/// <summary>用户账户信息,密码以 SHA-256 哈希值形式保存,不存明文</summary>
public record User(string Username, string Password, Permission Permission = Permission.Unknown);
