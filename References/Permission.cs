namespace CloudDisk.References;

/// <summary>用户权限等级,决定客户端可访问的磁盘范围</summary>
public enum Permission
{
    /// <summary>访客:仅能访问 D: 盘,也是注册用户的默认权限</summary>
    Guest = 0,

    /// <summary>普通用户:与访客相同的访问范围</summary>
    User = 1,

    /// <summary>管理员:可访问服务器上的全部磁盘</summary>
    Admin = 2,

    /// <summary>未知/未登录状态</summary>
    Unknown = -1
}
