namespace CloudDiskServer;

public record User(string Username, string Password, Permission Permission = Permission.Unknown);