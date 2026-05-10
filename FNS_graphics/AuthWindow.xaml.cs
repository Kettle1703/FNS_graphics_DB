using System;
using System.Threading.Tasks;
using System.Windows;
using FNS_graphics.Data;

namespace FNS_graphics
{
    public partial class AuthWindow : Window
    {
        private readonly Fns_database _database;
        private bool _databaseAvailable;
        private bool _handoffCompleted;

        public AuthWindow(Fns_database database)
        {
            InitializeComponent();

            _database = database;
            Loaded += AuthWindow_Loaded;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (!_handoffCompleted)
            {
                TryDisposeDatabase();
                base.OnClosed(e);
                App.ForceProcessExit();
                return;
            }

            base.OnClosed(e);
        }

        private void TryDisposeDatabase()
        {
            try
            {
                Task disposeTask = _database.DisposeAsync().AsTask();
                disposeTask.Wait(TimeSpan.FromMilliseconds(800));
            }
            catch
            {
                // Cleanup is best-effort on application exit.
            }
        }

        private async void AuthWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeDatabaseAsync();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            ClearLoginStatus();

            if (!_databaseAvailable)
            {
                ConnectionStatusTextBlock.Text = "База данных: недоступна. Проверьте PostgreSQL и повторите запуск.";
                return;
            }

            string login = LoginTextBox.Text.Trim().ToLowerInvariant();
            string password = PasswordBox.Password.Trim();

            if (login.Length == 0)
            {
                UserStatusTextBlock.Text = "Пользователь: введите логин.";
                return;
            }

            if (password.Length == 0)
            {
                PasswordStatusTextBlock.Text = "Пароль: введите пароль.";
                return;
            }

            SetLoginEnabled(false);

            try
            {
                FnsUser? user = await _database.GetUserByLoginAsync(login);
                if (user is null)
                {
                    await _database.AddAuthEventAsync(null, login, "failed_login", false);
                    UserStatusTextBlock.Text = $"Пользователь: нет пользователя с логином {login}.";
                    PasswordStatusTextBlock.Text = "Пароль: не проверялся.";
                    return;
                }

                UserStatusTextBlock.Text = $"Пользователь: найден ({FormatRole(user.Role)}).";

                if (!user.IsActive)
                {
                    await _database.AddAuthEventAsync(user.Id, user.Login, "failed_login", false);
                    UserStatusTextBlock.Text = $"Пользователь: {user.Login} заблокирован.";
                    PasswordStatusTextBlock.Text = "Пароль: не проверялся.";
                    return;
                }

                if (!Password_hasher.VerifyPassword(password, user.PasswordHash))
                {
                    await _database.AddAuthEventAsync(user.Id, user.Login, "failed_login", false);
                    PasswordStatusTextBlock.Text = "Пароль: неправильный.";
                    return;
                }

                PasswordStatusTextBlock.Text = "Пароль: верный.";
                await _database.AddAuthEventAsync(user.Id, user.Login, "login", true);
                OpenRoleWindow(user);
            }
            catch (Exception ex)
            {
                WindowStatusTextBlock.Text = $"Ошибка входа: {ex.Message}";
            }
            finally
            {
                if (!_handoffCompleted)
                    SetLoginEnabled(true);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async Task InitializeDatabaseAsync()
        {
            _databaseAvailable = false;
            SetLoginEnabled(false);
            ClearLoginStatus();
            ConnectionStatusTextBlock.Text = "База данных: подключение к FNS_rebuild...";

            try
            {
                await _database.CheckConnectionAsync();
                _databaseAvailable = true;
                SetLoginEnabled(true);
                ConnectionStatusTextBlock.Text = "База данных: подключено.";
                UserStatusTextBlock.Text = string.Empty;
                PasswordStatusTextBlock.Text = string.Empty;
                WindowStatusTextBlock.Text = "Введите логин и пароль.";
                LoginTextBox.Focus();
            }
            catch (Exception ex)
            {
                ConnectionStatusTextBlock.Text = $"База данных: недоступна. {ex.Message}";
                UserStatusTextBlock.Text = "Пользователь: проверка невозможна.";
                PasswordStatusTextBlock.Text = "Пароль: проверка невозможна.";
            }
        }

        private void OpenRoleWindow(FnsUser user)
        {
            WindowStatusTextBlock.Text = user.Role == "admin"
                ? "Окно: открытие панели администратора..."
                : "Окно: открытие панели шифрования...";

            try
            {
                Window roleWindow = user.Role == "admin"
                    ? new LoginWindow(_database, user)
                    : new MainWindow(_database, user);

                Application.Current.MainWindow = roleWindow;
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                roleWindow.Show();

                _handoffCompleted = true;
                Close();
            }
            catch (Exception ex)
            {
                WindowStatusTextBlock.Text = $"Окно: не удалось открыть. {ex.Message}";
            }
        }

        private void ClearLoginStatus()
        {
            UserStatusTextBlock.Text = string.Empty;
            PasswordStatusTextBlock.Text = string.Empty;
            WindowStatusTextBlock.Text = string.Empty;
        }

        private void SetLoginEnabled(bool enabled)
        {
            LoginTextBox.IsEnabled = enabled;
            PasswordBox.IsEnabled = enabled;
            LoginButton.IsEnabled = enabled;
        }

        private static string FormatRole(string role)
        {
            return role == "admin" ? "администратор" : "пользователь";
        }
    }
}
