using System;
using System.Globalization;

namespace FNS_graphics.Data
{
    public sealed class FnsUser
    {
        public long Id { get; init; }
        public string Login { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public string DisplayName => $"{Login} ({FormatRole(Role)})";

        private static string FormatRole(string role)
        {
            return role == "admin" ? "админ" : "пользователь";
        }
    }

    public sealed class RequestHistoryRow
    {
        public long RequestId { get; init; }
        public string Login { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int InputLength { get; init; }
        public string OutputLength { get; init; } = string.Empty;
        public string DurationMs { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
    }

    public sealed class UserActivityRow
    {
        public long UserId { get; init; }
        public string Login { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public long TotalRequests { get; init; }
        public long EncryptRequests { get; init; }
        public long DecryptRequests { get; init; }
        public long SuccessfulRequests { get; init; }
        public long FailedRequests { get; init; }
        public DateTime? LastRequestAtValue { get; init; }
        public DateTime? LastLoginAtValue { get; init; }

        public string RoleDisplay => Role == "admin" ? "админ" : "пользователь";
        public string StateDisplay => IsActive ? "активен" : "заблокирован";
        public string LastRequestAt => FormatDate(LastRequestAtValue);
        public string LastLoginAt => FormatDate(LastLoginAtValue);

        private static string FormatDate(DateTime? value)
        {
            return value?.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";
        }
    }

    public sealed class AdminActionRow
    {
        public long ActionId { get; init; }
        public string AdminLogin { get; init; } = string.Empty;
        public string TargetLogin { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public DateTime CreatedAtValue { get; init; }

        public string ActionDisplay => Action switch
        {
            "create_user" => "создание",
            "block_user" => "блокировка",
            "unblock_user" => "разблокировка",
            "change_role" => "смена роли",
            "reset_password" => "сброс пароля",
            "delete_user" => "удаление",
            _ => Action
        };

        public string CreatedAt => CreatedAtValue.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
