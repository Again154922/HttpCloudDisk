namespace CloudDisk.References;

public record User(string Username, string Password, Permission Permission = Permission.Unknown);