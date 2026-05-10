using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FNS_graphics.Data;
using FNS_rebuild;

namespace FNS_graphics
{
    public partial class MainWindow : Window
    {
        public static readonly RoutedUICommand EncryptCommand = new(
            "Encrypt",
            nameof(EncryptCommand),
            typeof(MainWindow));

        public static readonly RoutedUICommand DecryptCommand = new(
            "Decrypt",
            nameof(DecryptCommand),
            typeof(MainWindow));

        private static readonly string ReceiverPrivateKeyPath = Path.Combine(AppContext.BaseDirectory, "receiver_ecdh_private.pk8.b64");
        private static readonly string ReceiverPublicKeyPath = Path.Combine(AppContext.BaseDirectory, "receiver_ecdh_public.spki.b64");
        private const int DefaultBlockLength = 1096;

        private readonly Strategy_wrapper _wrapper;
        private readonly Hybrid_fns_cryptosystem _hybrid;
        private readonly ECDiffieHellman _receiverPrivateKey;
        private readonly byte[] _receiverPublicKeySpki;
        private readonly Fns_database _database;
        private readonly FnsUser _currentUser;
        private readonly List<TextBox> _highlightedTextBoxes = [];
        private const string AutoPlaceholder = "<заполняется автоматически>";

        public MainWindow(Fns_database database, FnsUser currentUser)
        {
            InitializeComponent();

            _wrapper = new Strategy_wrapper(new Factorial_strategy());
            _hybrid = new Hybrid_fns_cryptosystem(_wrapper);
            _receiverPrivateKey = LoadOrCreateReceiverPrivateKey();
            _receiverPublicKeySpki = _receiverPrivateKey.ExportSubjectPublicKeyInfo();
            _database = database;
            _currentUser = currentUser;

            SharedSenderPublicKeyTextBox.Text = AutoPlaceholder;
            SharedReceiverPublicKeyTextBox.Text = Convert.ToBase64String(_receiverPublicKeySpki);
            SharedSessionKeyTextBox.Text = AutoPlaceholder;
            TransferCipherTextTextBox.Text = string.Empty;

            EncryptMetricsTextBlock.Text = "Время: - | Длина: -";
            DecryptMetricsTextBlock.Text = "Время: - | Длина: -";
            StatusTextBlock.Text = $"Пользователь: {_currentUser.Login}. Нажмите «Шифровать» для автозаполнения полей передачи.";
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            try
            {
                _receiverPrivateKey.Dispose();
            }
            catch
            {
            }

            TryCleanupBeforeExit();
            App.ForceProcessExit();
        }

        private void TryCleanupBeforeExit()
        {
            try
            {
                Task cleanupTask = Task.Run(async () =>
                {
                    try
                    {
                        await _database.AddAuthEventAsync(_currentUser.Id, _currentUser.Login, "logout", true);
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

        private async Task TryLogOperationAsync(
            string action,
            string status,
            int inputLength,
            int? outputLength,
            int? durationMs,
            string? errorCode = null,
            string? errorMessage = null)
        {
            try
            {
                long requestId = await _database.AddEncryptionRequestAsync(
                    _currentUser.Id,
                    action,
                    status,
                    inputLength,
                    outputLength,
                    durationMs);

                if (!string.IsNullOrWhiteSpace(errorCode) && !string.IsNullOrWhiteSpace(errorMessage))
                    await _database.AddRequestErrorAsync(requestId, errorCode, errorMessage);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Операция выполнена, но запись в БД не сохранена: {ex.Message}";
            }
        }

        private void CopyField_Click(object sender, RoutedEventArgs e)
        {
            ClearPersistentHighlights();

            if (sender is not Button { Tag: TextBox source })
                return;

            string text = source.Text ?? string.Empty;
            if (text.Length == 0)
            {
                StatusTextBlock.Text = "Поле пустое, копировать нечего.";
                return;
            }

            Clipboard.SetText(text);
            StatusTextBlock.Text = "Содержимое поля скопировано в буфер обмена.";
        }

        private void EncryptCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Encrypt_Click(sender, new RoutedEventArgs());
        }

        private void DecryptCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Decrypt_Click(sender, new RoutedEventArgs());
        }

        private async void Encrypt_Click(object sender, RoutedEventArgs e)
        {
            ClearPersistentHighlights();
            string source = SourceTextBox.Text ?? string.Empty;

            try
            {
                if (string.IsNullOrEmpty(source))
                {
                    string message = "Введите исходный текст для шифрования.";
                    StatusTextBlock.Text = message;
                    await TryLogOperationAsync("encrypt", "failed", source.Length, null, 0, "empty_input", message);
                    return;
                }

                string receiverPublicBase64 = SharedReceiverPublicKeyTextBox.Text?.Trim() ?? string.Empty;
                byte[] receiverPublicSpki = string.IsNullOrEmpty(receiverPublicBase64) || receiverPublicBase64 == AutoPlaceholder
                    ? _receiverPublicKeySpki
                    : Convert.FromBase64String(receiverPublicBase64);
                Cipher_options options = new()
                {
                    Block_plain_text_length = DefaultBlockLength,
                    Key = string.Empty
                };

                Stopwatch watch = Stopwatch.StartNew();
                Hybrid_cipher_package packet = _hybrid.Encrypt(source, receiverPublicSpki, options);
                watch.Stop();
                int durationMs = ToDurationMilliseconds(watch);

                TransferCipherTextTextBox.Text = packet.Ciphertext;
                SharedSenderPublicKeyTextBox.Text = packet.Ephemeral_public_key;
                SharedSessionKeyTextBox.Text = packet.Encrypted_symmetric_key;

                EncryptMetricsTextBlock.Text = $"Время: {watch.Elapsed.TotalMilliseconds:F2} мс | Длина: {source.Length}";
                MarkPersistentHighlights(
                    TransferCipherTextTextBox,
                    SharedSenderPublicKeyTextBox,
                    SharedSessionKeyTextBox);

                StatusTextBlock.Text = "Шифрование выполнено. Данные для передачи заполнены в общем блоке.";
                await TryLogOperationAsync("encrypt", "success", source.Length, packet.Ciphertext.Length, durationMs);
            }
            catch (FormatException)
            {
                string message = "Публичный ключ получателя должен быть в формате Base64.";
                StatusTextBlock.Text = message;
                await TryLogOperationAsync("encrypt", "failed", source.Length, null, null, "invalid_receiver_key", message);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Ошибка шифрования: {ex.Message}";
                await TryLogOperationAsync("encrypt", "failed", source.Length, null, null, ErrorCodeFromException(ex), ex.Message);
            }
        }

        private async void Decrypt_Click(object sender, RoutedEventArgs e)
        {
            ClearPersistentHighlights();
            string ciphertext = TransferCipherTextTextBox.Text ?? string.Empty;
            string senderPublicKey = SharedSenderPublicKeyTextBox.Text?.Trim() ?? string.Empty;
            string encryptedSymmetricKey = SharedSessionKeyTextBox.Text?.Trim() ?? string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(ciphertext) || ciphertext.StartsWith("<", StringComparison.Ordinal))
                {
                    string message = "Поле шифротекста не заполнено.";
                    StatusTextBlock.Text = message;
                    await TryLogOperationAsync("decrypt", "failed", ciphertext.Length, null, 0, "empty_ciphertext", message);
                    return;
                }

                if (string.IsNullOrWhiteSpace(senderPublicKey) || senderPublicKey == AutoPlaceholder)
                {
                    string message = "Поле ключа отправителя не заполнено.";
                    StatusTextBlock.Text = message;
                    await TryLogOperationAsync("decrypt", "failed", ciphertext.Length, null, 0, "missing_sender_key", message);
                    return;
                }

                if (string.IsNullOrWhiteSpace(encryptedSymmetricKey) || encryptedSymmetricKey == AutoPlaceholder)
                {
                    string message = "Поле защищённого сеансового ключа не заполнено.";
                    StatusTextBlock.Text = message;
                    await TryLogOperationAsync("decrypt", "failed", ciphertext.Length, null, 0, "missing_session_key", message);
                    return;
                }

                Hybrid_cipher_package packet = new()
                {
                    Ciphertext = ciphertext,
                    Ephemeral_public_key = senderPublicKey,
                    Encrypted_symmetric_key = encryptedSymmetricKey,
                    Block_plain_text_length = DefaultBlockLength,
                    Curve_id = Hybrid_fns_cryptosystem.Curve_id_nist_p256
                };

                Stopwatch watch = Stopwatch.StartNew();
                string decrypted = _hybrid.Decrypt(packet, _receiverPrivateKey);
                watch.Stop();
                int durationMs = ToDurationMilliseconds(watch);

                SourceTextBox.Text = decrypted;
                DecryptMetricsTextBlock.Text = $"Время: {watch.Elapsed.TotalMilliseconds:F2} мс | Длина: {ciphertext.Length}";
                MarkPersistentHighlights(SourceTextBox);
                StatusTextBlock.Text = "Дешифрование выполнено. Текст записан в поле «Исходный текст».";
                await TryLogOperationAsync("decrypt", "success", ciphertext.Length, decrypted.Length, durationMs);
            }
            catch (FormatException)
            {
                string message = "Ключи пакета должны быть в формате Base64.";
                StatusTextBlock.Text = message;
                await TryLogOperationAsync("decrypt", "failed", ciphertext.Length, null, null, "invalid_package_key", message);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Ошибка дешифрования: {ex.Message}";
                await TryLogOperationAsync("decrypt", "failed", ciphertext.Length, null, null, ErrorCodeFromException(ex), ex.Message);
            }
        }

        private static int ToDurationMilliseconds(Stopwatch watch)
        {
            double rounded = Math.Round(watch.Elapsed.TotalMilliseconds);
            if (rounded > int.MaxValue)
                return int.MaxValue;

            return Math.Max(0, (int)rounded);
        }

        private static string ErrorCodeFromException(Exception exception)
        {
            return exception.GetType().Name;
        }

        private void MarkPersistentHighlights(params TextBox[] textBoxes)
        {
            Brush highlightBackground = new SolidColorBrush(Color.FromRgb(236, 249, 234));
            Brush highlightBorder = new SolidColorBrush(Color.FromRgb(92, 151, 112));

            foreach (TextBox box in textBoxes)
            {
                box.Background = highlightBackground;
                box.BorderBrush = highlightBorder;
                box.BorderThickness = new Thickness(2);
                _highlightedTextBoxes.Add(box);
            }
        }

        private void ClearPersistentHighlights()
        {
            if (_highlightedTextBoxes.Count == 0)
                return;

            Brush normalBackground = Brushes.White;
            Brush normalBorder = new SolidColorBrush(Color.FromRgb(182, 204, 184));

            foreach (TextBox box in _highlightedTextBoxes)
            {
                box.Background = normalBackground;
                box.BorderBrush = normalBorder;
                box.BorderThickness = new Thickness(1);
            }

            _highlightedTextBoxes.Clear();
        }

        private static ECDiffieHellman LoadOrCreateReceiverPrivateKey()
        {
            if (File.Exists(ReceiverPrivateKeyPath))
            {
                string privateB64 = File.ReadAllText(ReceiverPrivateKeyPath).Trim();
                byte[] privateBytes = Convert.FromBase64String(privateB64);

                ECDiffieHellman imported = ECDiffieHellman.Create();
                imported.ImportPkcs8PrivateKey(privateBytes, out int read);
                if (read != privateBytes.Length)
                    throw new CryptographicException("Не удалось полностью прочитать приватный ECDH-ключ получателя.");

                if (!File.Exists(ReceiverPublicKeyPath))
                {
                    byte[] publicBytes = imported.ExportSubjectPublicKeyInfo();
                    File.WriteAllText(ReceiverPublicKeyPath, Convert.ToBase64String(publicBytes));
                }

                return imported;
            }

            ECDiffieHellman created = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            byte[] privateKey = created.ExportPkcs8PrivateKey();
            byte[] publicKey = created.ExportSubjectPublicKeyInfo();

            File.WriteAllText(ReceiverPrivateKeyPath, Convert.ToBase64String(privateKey));
            File.WriteAllText(ReceiverPublicKeyPath, Convert.ToBase64String(publicKey));

            return created;
        }
    }
}
