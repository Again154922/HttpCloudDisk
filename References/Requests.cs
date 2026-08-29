namespace CloudDisk.References;

public record RegisterRequest(string Username, string Password);
public record LoginRequest(string Username, string Password);