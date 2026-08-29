namespace CloudDiskServer;

public record LoginResponse(string Message, Permission Permission = Permission.Unknown);
public record RegisterResponse(string Message, Permission Permission = Permission.Unknown);