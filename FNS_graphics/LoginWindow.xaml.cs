using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FNS_graphics.Data;

namespace FNS_graphics
{
    public partial class LoginWindow : Window
    {
        private readonly Fns_database _database;
        private readonly FnsUser _currentAdmin;
        private readonly ObservableCollection<UserActivityRow> _users = [];
        private readonly ObservableCollection<RequestHistoryRow> _requestHistory = [];
        private readonly ObservableCollection<AdminActionRow> _adminActions = [];
        private bool _databaseAvailable;

        public LoginWindow(Fns_database database, FnsUser currentAdmin)
        {
            InitializeComponent();

            _database = database;
            _currentAdmin = currentAdmin;

            UsersDataGrid.ItemsSource = _users;
            RequestHistoryDataGrid.ItemsSource = _requestHistory;
            AdminActionsDataGrid.ItemsSource = _adminActions;
            AdminInfoTextBlock.Text = $"Администратор: {_currentAdmin.Login}";

            Loaded += LoginWindow_Loaded;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (Application.Current is not null)
            {
                base.OnClosed(e);
                TryCleanupBeforeExit();
                App.ForceProcessExit();
                return;
            }

            try
            {
                _database.AddAuthEventAsync(_currentAdmin.Id, _currentAdmin.Login, "logout", true)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Ошибка записи logout не должна мешать закрытию приложения.
            }

            _database.DisposeAsync().AsTask().GetAwaiter().GetResult();
            base.OnClosed(e);
            Application.Current?.Shutdown();
        }

        private void TryCleanupBeforeExit()
        {
            try
            {
                Task cleanupTask = Task.Run(async () =>
                {
                    try
                    {
                        await _database.AddAuthEventAsync(_currentAdmin.Id, _currentAdmin.Login, "logout", true);
                    }
                    catch
                    {
                        // Logout logging must not prevent the process from exiting.
                    }

                    await _database.DisposeAsync();
                });

                cleanupTask.Wait(TimeSpan.FromMilliseconds(800));
            }
            catch
            {
                // Cleanup is best-effort on application exit.
            }
        }

        private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAllAsync();
        }

        private async void RefreshDatabase_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAllAsync();
        }

        private void UsersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedUserText();
        }

        private async void BlockUser_Click(object sender, RoutedEventArgs e)
        {
            await ChangeUserActiveStateAsync(false);
        }

        private async void UnblockUser_Click(object sender, RoutedEventArgs e)
        {
            await ChangeUserActiveStateAsync(true);
        }

        private async void MakeAdmin_Click(object sender, RoutedEventArgs e)
        {
            await ChangeUserRoleAsync("admin");
        }

        private async void MakeUser_Click(object sender, RoutedEventArgs e)
        {
            await ChangeUserRoleAsync("user");
        }

        private async void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            UserActivityRow? selectedUser = GetSelectedUser();
            if (selectedUser is null)
                return;

            string newPassword = ResetPasswordBox.Password;
            if (newPassword.Length < 6)
            {
                DatabaseStatusTextBlock.Text = "Новый пароль должен быть не короче 6 символов.";
                return;
            }

            SetActionControlsEnabled(false);
            DatabaseStatusTextBlock.Text = $"Сброс пароля для {selectedUser.Login}...";

            try
            {
                await _database.ResetUserPasswordAsync(
                    _currentAdmin.Id,
                    selectedUser.UserId,
                    newPassword,
                    $"Сброс пароля из панели администратора пользователем {_currentAdmin.Login}.");

                ResetPasswordBox.Clear();
                await RefreshAllAsync($"Пароль пользователя {selectedUser.Login} обновлен.");
            }
            catch (Exception ex)
            {
                DatabaseStatusTextBlock.Text = $"Не удалось сбросить пароль: {ex.Message}";
                SetActionControlsEnabled(_databaseAvailable);
            }
        }

        private async Task ChangeUserActiveStateAsync(bool active)
        {
            UserActivityRow? selectedUser = GetSelectedUser();
            if (selectedUser is null)
                return;

            if (selectedUser.UserId == _currentAdmin.Id && !active)
            {
                DatabaseStatusTextBlock.Text = "Нельзя заблокировать текущего администратора.";
                return;
            }

            if (selectedUser.IsActive == active)
            {
                DatabaseStatusTextBlock.Text = active
                    ? "Пользователь уже активен."
                    : "Пользователь уже заблокирован.";
                return;
            }

            SetActionControlsEnabled(false);
            DatabaseStatusTextBlock.Text = active
                ? $"Разблокировка {selectedUser.Login}..."
                : $"Блокировка {selectedUser.Login}...";

            try
            {
                await _database.SetUserActiveAsync(
                    _currentAdmin.Id,
                    selectedUser.UserId,
                    active,
                    $"Изменение статуса из панели администратора пользователем {_currentAdmin.Login}.");

                string result = active
                    ? $"Пользователь {selectedUser.Login} разблокирован."
                    : $"Пользователь {selectedUser.Login} заблокирован.";

                await RefreshAllAsync(result);
            }
            catch (Exception ex)
            {
                DatabaseStatusTextBlock.Text = $"Не удалось изменить статус: {ex.Message}";
                SetActionControlsEnabled(_databaseAvailable);
            }
        }

        private async Task ChangeUserRoleAsync(string newRole)
        {
            UserActivityRow? selectedUser = GetSelectedUser();
            if (selectedUser is null)
                return;

            if (selectedUser.UserId == _currentAdmin.Id && newRole == "user")
            {
                DatabaseStatusTextBlock.Text = "Нельзя снять роль администратора с текущего пользователя.";
                return;
            }

            if (selectedUser.Role == newRole)
            {
                DatabaseStatusTextBlock.Text = newRole == "admin"
                    ? "Пользователь уже является администратором."
                    : "Пользователь уже имеет обычную роль.";
                return;
            }

            SetActionControlsEnabled(false);
            DatabaseStatusTextBlock.Text = $"Смена роли для {selectedUser.Login}...";

            try
            {
                await _database.ChangeUserRoleAsync(
                    _currentAdmin.Id,
                    selectedUser.UserId,
                    newRole,
                    $"Смена роли из панели администратора пользователем {_currentAdmin.Login}.");

                string roleText = newRole == "admin" ? "администратором" : "обычным пользователем";
                await RefreshAllAsync($"Пользователь {selectedUser.Login} теперь является {roleText}.");
            }
            catch (Exception ex)
            {
                DatabaseStatusTextBlock.Text = $"Не удалось изменить роль: {ex.Message}";
                SetActionControlsEnabled(_databaseAvailable);
            }
        }

        private async Task RefreshAllAsync(string? finalStatus = null)
        {
            _databaseAvailable = false;
            RefreshDatabaseButton.IsEnabled = false;
            SetActionControlsEnabled(false);
            DatabaseStatusTextBlock.Text = "Подключение к FNS_rebuild...";

            try
            {
                await _database.CheckConnectionAsync();
                _databaseAvailable = true;

                await RefreshUsersAsync();
                await RefreshRequestHistoryAsync();
                await RefreshAdminActionsAsync();

                DatabaseStatusTextBlock.Text = finalStatus ?? "Подключено. Данные панели обновлены.";
            }
            catch (Exception ex)
            {
                _users.Clear();
                _requestHistory.Clear();
                _adminActions.Clear();
                DatabaseStatusTextBlock.Text = $"БД недоступна: {ex.Message}";
            }
            finally
            {
                RefreshDatabaseButton.IsEnabled = true;
                SetActionControlsEnabled(_databaseAvailable);
                UpdateSelectedUserText();
            }
        }

        private async Task RefreshUsersAsync()
        {
            List<UserActivityRow> rows = await _database.GetUserActivitySummaryAsync();
            _users.Clear();

            foreach (UserActivityRow row in rows)
                _users.Add(row);
        }

        private async Task RefreshRequestHistoryAsync()
        {
            List<RequestHistoryRow> rows = await _database.GetRecentRequestsAsync(50);
            _requestHistory.Clear();

            foreach (RequestHistoryRow row in rows)
                _requestHistory.Add(row);
        }

        private async Task RefreshAdminActionsAsync()
        {
            List<AdminActionRow> rows = await _database.GetRecentAdminActionsAsync(50);
            _adminActions.Clear();

            foreach (AdminActionRow row in rows)
                _adminActions.Add(row);
        }

        private UserActivityRow? GetSelectedUser()
        {
            if (UsersDataGrid.SelectedItem is UserActivityRow selectedUser)
                return selectedUser;

            DatabaseStatusTextBlock.Text = "Выберите пользователя в таблице.";
            return null;
        }

        private void UpdateSelectedUserText()
        {
            if (UsersDataGrid.SelectedItem is UserActivityRow selectedUser)
            {
                SelectedUserTextBlock.Text = $"Выбран: {selectedUser.Login}, {selectedUser.RoleDisplay}, {selectedUser.StateDisplay}";
                return;
            }

            SelectedUserTextBlock.Text = "Пользователь не выбран.";
        }

        private void SetActionControlsEnabled(bool enabled)
        {
            UsersDataGrid.IsEnabled = enabled;
            BlockUserButton.IsEnabled = enabled;
            UnblockUserButton.IsEnabled = enabled;
            MakeAdminButton.IsEnabled = enabled;
            MakeUserButton.IsEnabled = enabled;
            ResetPasswordBox.IsEnabled = enabled;
            ResetPasswordButton.IsEnabled = enabled;
        }
    }
}
