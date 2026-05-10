using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace FNS_graphics.Data
{
    public sealed class Fns_database : IAsyncDisposable
    {
        private readonly string _connectionString;
        private NpgsqlDataSource? _dataSource;

        internal Fns_database(string connectionString)
        {
            _connectionString = connectionString;
        }

        private NpgsqlDataSource DataSource => _dataSource ??= NpgsqlDataSource.Create(_connectionString);

        internal async Task CheckConnectionAsync(CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
        }

        internal async Task<List<FnsUser>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT id, login, role, is_active
                FROM "FNS_log".users
                WHERE is_active = TRUE
                ORDER BY CASE WHEN role = 'user' THEN 0 ELSE 1 END, login;
                """;

            List<FnsUser> users = [];
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                users.Add(new FnsUser
                {
                    Id = reader.GetInt64(0),
                    Login = reader.GetString(1),
                    Role = reader.GetString(2),
                    IsActive = reader.GetBoolean(3)
                });
            }

            return users;
        }

        internal async Task<FnsUser?> GetUserByLoginAsync(string login, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT id, login, password_hash, role, is_active
                FROM "FNS_log".users
                WHERE login = @login;
                """;

            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            command.Parameters.Add("login", NpgsqlDbType.Varchar).Value = login;

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new FnsUser
            {
                Id = reader.GetInt64(0),
                Login = reader.GetString(1),
                PasswordHash = reader.GetString(2),
                Role = reader.GetString(3),
                IsActive = reader.GetBoolean(4)
            };
        }

        internal async Task<List<RequestHistoryRow>> GetRecentRequestsAsync(int limit = 20, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT request_id, login, action, status, input_length, output_length, duration_ms, created_at
                FROM "FNS_log".v_encryption_request_details
                ORDER BY created_at DESC, request_id DESC
                LIMIT @limit;
                """;

            List<RequestHistoryRow> rows = [];
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = limit;

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int? outputLength = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                int? durationMs = reader.IsDBNull(6) ? null : reader.GetInt32(6);
                DateTime createdAt = reader.GetDateTime(7);

                rows.Add(new RequestHistoryRow
                {
                    RequestId = reader.GetInt64(0),
                    Login = reader.GetString(1),
                    Action = FormatAction(reader.GetString(2)),
                    Status = FormatStatus(reader.GetString(3)),
                    InputLength = reader.GetInt32(4),
                    OutputLength = outputLength?.ToString(CultureInfo.InvariantCulture) ?? "-",
                    DurationMs = durationMs is null ? "-" : $"{durationMs.Value} мс",
                    CreatedAt = createdAt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                });
            }

            return rows;
        }

        internal async Task<List<UserActivityRow>> GetUserActivitySummaryAsync(CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT user_id, login, role, is_active, total_requests, encrypt_requests, decrypt_requests,
                       successful_requests, failed_requests, last_request_at, last_login_at
                FROM "FNS_log".v_user_activity_summary
                ORDER BY CASE WHEN role = 'admin' THEN 0 ELSE 1 END, login;
                """;

            List<UserActivityRow> rows = [];
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new UserActivityRow
                {
                    UserId = reader.GetInt64(0),
                    Login = reader.GetString(1),
                    Role = reader.GetString(2),
                    IsActive = reader.GetBoolean(3),
                    TotalRequests = reader.GetInt64(4),
                    EncryptRequests = reader.GetInt64(5),
                    DecryptRequests = reader.GetInt64(6),
                    SuccessfulRequests = reader.GetInt64(7),
                    FailedRequests = reader.GetInt64(8),
                    LastRequestAtValue = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                    LastLoginAtValue = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
                });
            }

            return rows;
        }

        internal async Task<List<AdminActionRow>> GetRecentAdminActionsAsync(int limit = 30, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT aa.id, admin_user.login, COALESCE(target_user.login, '-'), aa.action,
                       COALESCE(aa.description, '-'), aa.created_at
                FROM "FNS_log".admin_actions aa
                JOIN "FNS_log".users admin_user ON admin_user.id = aa.admin_id
                LEFT JOIN "FNS_log".users target_user ON target_user.id = aa.target_user_id
                ORDER BY aa.created_at DESC, aa.id DESC
                LIMIT @limit;
                """;

            List<AdminActionRow> rows = [];
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = limit;

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new AdminActionRow
                {
                    ActionId = reader.GetInt64(0),
                    AdminLogin = reader.GetString(1),
                    TargetLogin = reader.GetString(2),
                    Action = reader.GetString(3),
                    Description = reader.GetString(4),
                    CreatedAtValue = reader.GetDateTime(5)
                });
            }

            return rows;
        }

        internal async Task<long> AddEncryptionRequestAsync(
            long userId,
            string action,
            string status,
            int inputLength,
            int? outputLength,
            int? durationMs,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
                INSERT INTO "FNS_log".encryption_requests
                    (user_id, action, status, input_length, output_length, duration_ms)
                VALUES
                    (@user_id, @action, @status, @input_length, @output_length, @duration_ms)
                RETURNING id;
                """;

            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            command.Parameters.Add("user_id", NpgsqlDbType.Bigint).Value = userId;
            command.Parameters.Add("action", NpgsqlDbType.Varchar).Value = action;
            command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = status;
            command.Parameters.Add("input_length", NpgsqlDbType.Integer).Value = inputLength;
            command.Parameters.Add("output_length", NpgsqlDbType.Integer).Value = outputLength is null ? DBNull.Value : outputLength.Value;
            command.Parameters.Add("duration_ms", NpgsqlDbType.Integer).Value = durationMs is null ? DBNull.Value : durationMs.Value;

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        internal async Task AddRequestErrorAsync(
            long requestId,
            string errorCode,
            string errorMessage,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
                INSERT INTO "FNS_log".request_errors
                    (request_id, error_code, error_message)
                VALUES
                    (@request_id, @error_code, @error_message);
                """;

            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            command.Parameters.Add("request_id", NpgsqlDbType.Bigint).Value = requestId;
            command.Parameters.Add("error_code", NpgsqlDbType.Varchar).Value = Truncate(errorCode, 50);
            command.Parameters.Add("error_message", NpgsqlDbType.Text).Value = errorMessage;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        internal async Task AddAuthEventAsync(
            long? userId,
            string login,
            string eventType,
            bool success,
            string? ipAddress = null,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
                INSERT INTO "FNS_log".auth_events
                    (user_id, login, event_type, success, ip_address)
                VALUES
                    (@user_id, @login, @event_type, @success, @ip_address);
                """;

            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            command.Parameters.Add("user_id", NpgsqlDbType.Bigint).Value = userId is null ? DBNull.Value : userId.Value;
            command.Parameters.Add("login", NpgsqlDbType.Varchar).Value = Truncate(login, 50);
            command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = eventType;
            command.Parameters.Add("success", NpgsqlDbType.Boolean).Value = success;
            command.Parameters.Add("ip_address", NpgsqlDbType.Inet).Value = string.IsNullOrWhiteSpace(ipAddress) ? DBNull.Value : ipAddress;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        internal async Task SetUserActiveAsync(
            long adminId,
            long targetUserId,
            bool active,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            string procedure = active ? "sp_unblock_user" : "sp_block_user";
            string sql = "CALL \"FNS_log\"." + procedure + "(@admin_id, @target_user_id, @description);";

            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            command.Parameters.Add("admin_id", NpgsqlDbType.Bigint).Value = adminId;
            command.Parameters.Add("target_user_id", NpgsqlDbType.Bigint).Value = targetUserId;
            command.Parameters.Add("description", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(description) ? DBNull.Value : description;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        internal async Task ResetUserPasswordAsync(
            long adminId,
            long targetUserId,
            string newPassword,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("Новый пароль не может быть пустым.", nameof(newPassword));

            string passwordHash = Password_hasher.HashPassword(newPassword);

            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            await EnsureAdminAsync(connection, transaction, adminId, cancellationToken);

            const string targetSql = """
                SELECT password_hash
                FROM "FNS_log".users
                WHERE id = @target_user_id
                FOR UPDATE;
                """;

            await using (NpgsqlCommand targetCommand = new(targetSql, connection, transaction))
            {
                targetCommand.Parameters.Add("target_user_id", NpgsqlDbType.Bigint).Value = targetUserId;
                object? oldHash = await targetCommand.ExecuteScalarAsync(cancellationToken);
                if (oldHash is null || oldHash is DBNull)
                    throw new InvalidOperationException("Пользователь не найден.");
            }

            const string updateSql = """
                UPDATE "FNS_log".users
                SET password_hash = @password_hash
                WHERE id = @target_user_id;
                """;

            await using (NpgsqlCommand updateCommand = new(updateSql, connection, transaction))
            {
                updateCommand.Parameters.Add("password_hash", NpgsqlDbType.Varchar).Value = passwordHash;
                updateCommand.Parameters.Add("target_user_id", NpgsqlDbType.Bigint).Value = targetUserId;
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertAdminActionAsync(
                connection,
                transaction,
                adminId,
                targetUserId,
                "reset_password",
                "hidden",
                "hidden",
                description,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        internal async Task ChangeUserRoleAsync(
            long adminId,
            long targetUserId,
            string newRole,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            if (newRole != "admin" && newRole != "user")
                throw new ArgumentException("Роль должна быть admin или user.", nameof(newRole));

            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            await EnsureAdminAsync(connection, transaction, adminId, cancellationToken);

            const string readRoleSql = """
                SELECT role
                FROM "FNS_log".users
                WHERE id = @target_user_id
                FOR UPDATE;
                """;

            string oldRole;
            await using (NpgsqlCommand roleCommand = new(readRoleSql, connection, transaction))
            {
                roleCommand.Parameters.Add("target_user_id", NpgsqlDbType.Bigint).Value = targetUserId;
                object? result = await roleCommand.ExecuteScalarAsync(cancellationToken);
                if (result is null || result is DBNull)
                    throw new InvalidOperationException("Пользователь не найден.");

                oldRole = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (oldRole == newRole)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            const string updateRoleSql = """
                UPDATE "FNS_log".users
                SET role = @new_role
                WHERE id = @target_user_id;
                """;

            await using (NpgsqlCommand updateCommand = new(updateRoleSql, connection, transaction))
            {
                updateCommand.Parameters.Add("new_role", NpgsqlDbType.Varchar).Value = newRole;
                updateCommand.Parameters.Add("target_user_id", NpgsqlDbType.Bigint).Value = targetUserId;
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertAdminActionAsync(
                connection,
                transaction,
                adminId,
                targetUserId,
                "change_role",
                oldRole,
                newRole,
                description,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return _dataSource?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        private static async Task EnsureAdminAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            long adminId,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT role
                FROM "FNS_log".users
                WHERE id = @admin_id;
                """;

            await using NpgsqlCommand command = new(sql, connection, transaction);
            command.Parameters.Add("admin_id", NpgsqlDbType.Bigint).Value = adminId;

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null || result is DBNull)
                throw new InvalidOperationException("Администратор не найден.");

            string role = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
            if (role != "admin")
                throw new InvalidOperationException("Текущий пользователь не является администратором.");
        }

        private static async Task InsertAdminActionAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            long adminId,
            long targetUserId,
            string action,
            string oldValue,
            string newValue,
            string? description,
            CancellationToken cancellationToken)
        {
            const string sql = """
                INSERT INTO "FNS_log".admin_actions
                    (admin_id, target_user_id, action, old_value, new_value, description)
                VALUES
                    (@admin_id, @target_user_id, @action, @old_value, @new_value, @description);
                """;

            await using NpgsqlCommand command = new(sql, connection, transaction);
            command.Parameters.Add("admin_id", NpgsqlDbType.Bigint).Value = adminId;
            command.Parameters.Add("target_user_id", NpgsqlDbType.Bigint).Value = targetUserId;
            command.Parameters.Add("action", NpgsqlDbType.Varchar).Value = action;
            command.Parameters.Add("old_value", NpgsqlDbType.Text).Value = oldValue;
            command.Parameters.Add("new_value", NpgsqlDbType.Text).Value = newValue;
            command.Parameters.Add("description", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(description) ? DBNull.Value : description;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static string FormatAction(string action)
        {
            return action switch
            {
                "encrypt" => "шифрование",
                "decrypt" => "дешифрование",
                _ => action
            };
        }

        private static string FormatStatus(string status)
        {
            return status switch
            {
                "created" => "создано",
                "processing" => "в работе",
                "success" => "успех",
                "failed" => "ошибка",
                "cancelled" => "отменено",
                _ => status
            };
        }

        private static string Truncate(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
