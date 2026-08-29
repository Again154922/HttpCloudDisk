namespace CloudDiskClient;

public record LoginResponse(string Message, Permission Permission);
public record RegisterResponse(string Message, Permission Permission);