-- Полный дамп структуры и тестовых данных для системы шифрования
-- Схема создается отдельно, чтобы не использовать public

DROP SCHEMA IF EXISTS "FNS_log" CASCADE;
CREATE SCHEMA "FNS_log";
SET search_path TO "FNS_log";

-- Таблица пользователей системы
CREATE TABLE users (
    id BIGSERIAL PRIMARY KEY,
    login VARCHAR(50) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    role VARCHAR(10) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP,
    last_login_at TIMESTAMP,
    blocked_at TIMESTAMP,

    -- Роль ограничена двумя значениями, так как в системе есть только пользователь и администратор
    CONSTRAINT chk_users_role
        CHECK (role IN ('user', 'admin'))
);

-- Таблица запросов на шифрование и дешифрование
CREATE TABLE encryption_requests (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    action VARCHAR(10) NOT NULL,
    status VARCHAR(20) NOT NULL,
    input_length INTEGER NOT NULL,
    output_length INTEGER,
    duration_ms INTEGER,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Храним только служебную статистику операции, без исходного текста и шифротекста
    CONSTRAINT chk_encryption_requests_action
        CHECK (action IN ('encrypt', 'decrypt')),

    CONSTRAINT chk_encryption_requests_status
        CHECK (status IN ('created', 'processing', 'success', 'failed', 'cancelled')),

    CONSTRAINT chk_encryption_requests_input_length
        CHECK (input_length >= 0),

    CONSTRAINT chk_encryption_requests_output_length
        CHECK (output_length IS NULL OR output_length >= 0),

    CONSTRAINT chk_encryption_requests_duration_ms
        CHECK (duration_ms IS NULL OR duration_ms >= 0)
);

-- Таблица ошибок, возникших при обработке запросов
CREATE TABLE request_errors (
    id BIGSERIAL PRIMARY KEY,
    request_id BIGINT NOT NULL,
    error_code VARCHAR(50) NOT NULL,
    error_message TEXT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Таблица событий авторизации
CREATE TABLE auth_events (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NULL,
    login VARCHAR(50) NOT NULL,
    event_type VARCHAR(20) NOT NULL,
    success BOOLEAN NOT NULL,
    ip_address INET,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT chk_auth_events_event_type
        CHECK (event_type IN ('login', 'logout', 'failed_login'))
);

-- Таблица действий администратора
CREATE TABLE admin_actions (
    id BIGSERIAL PRIMARY KEY,
    admin_id BIGINT NOT NULL,
    target_user_id BIGINT,
    action VARCHAR(30) NOT NULL,
    old_value TEXT,
    new_value TEXT,
    description TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT chk_admin_actions_action
        CHECK (action IN ('create_user', 'block_user', 'unblock_user', 'change_role', 'reset_password', 'delete_user'))
);

-- Связи между таблицами объявляются после создания всех таблиц
ALTER TABLE encryption_requests
    ADD CONSTRAINT fk_encryption_requests_user
    FOREIGN KEY (user_id)
    REFERENCES users (id)
    ON UPDATE CASCADE
    ON DELETE RESTRICT;

ALTER TABLE request_errors
    ADD CONSTRAINT fk_request_errors_request
    FOREIGN KEY (request_id)
    REFERENCES encryption_requests (id)
    ON UPDATE CASCADE
    ON DELETE CASCADE;

ALTER TABLE auth_events
    ADD CONSTRAINT fk_auth_events_user
    FOREIGN KEY (user_id)
    REFERENCES users (id)
    ON UPDATE CASCADE
    ON DELETE SET NULL;

ALTER TABLE admin_actions
    ADD CONSTRAINT fk_admin_actions_admin
    FOREIGN KEY (admin_id)
    REFERENCES users (id)
    ON UPDATE CASCADE
    ON DELETE RESTRICT;

ALTER TABLE admin_actions
    ADD CONSTRAINT fk_admin_actions_target_user
    FOREIGN KEY (target_user_id)
    REFERENCES users (id)
    ON UPDATE CASCADE
    ON DELETE SET NULL;

-- Представление для просмотра запросов на шифрование и дешифрование вместе с данными пользователя
CREATE OR REPLACE VIEW v_encryption_request_details AS
SELECT
    er.id AS request_id,
    er.user_id,
    u.login,
    u.role,
    er.action,
    er.status,
    er.input_length,
    er.output_length,
    er.duration_ms,
    er.created_at,
    COUNT(re.id) AS error_count,
    STRING_AGG(re.error_code, ', ' ORDER BY re.created_at) AS error_codes
FROM "FNS_log".encryption_requests er
JOIN "FNS_log".users u ON u.id = er.user_id
LEFT JOIN "FNS_log".request_errors re ON re.request_id = er.id
GROUP BY
    er.id,
    er.user_id,
    u.login,
    u.role,
    er.action,
    er.status,
    er.input_length,
    er.output_length,
    er.duration_ms,
    er.created_at;

-- Представление для общей статистики активности пользователей
CREATE OR REPLACE VIEW v_user_activity_summary AS
SELECT
    u.id AS user_id,
    u.login,
    u.role,
    u.is_active,
    COUNT(er.id) AS total_requests,
    COUNT(er.id) FILTER (WHERE er.action = 'encrypt') AS encrypt_requests,
    COUNT(er.id) FILTER (WHERE er.action = 'decrypt') AS decrypt_requests,
    COUNT(er.id) FILTER (WHERE er.status = 'success') AS successful_requests,
    COUNT(er.id) FILTER (WHERE er.status = 'failed') AS failed_requests,
    MAX(er.created_at) AS last_request_at,
    u.last_login_at
FROM "FNS_log".users u
LEFT JOIN "FNS_log".encryption_requests er ON er.user_id = u.id
GROUP BY
    u.id,
    u.login,
    u.role,
    u.is_active,
    u.last_login_at;

-- Триггерная функция для автоматического обновления даты изменения пользователя
CREATE OR REPLACE FUNCTION fn_set_users_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_set_users_updated_at
BEFORE UPDATE ON users
FOR EACH ROW
EXECUTE FUNCTION fn_set_users_updated_at();

-- Триггерная функция для обновления даты последнего успешного входа пользователя
CREATE OR REPLACE FUNCTION fn_update_last_login_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.event_type = 'login' AND NEW.success = TRUE AND NEW.user_id IS NOT NULL THEN
        UPDATE "FNS_log".users
        SET last_login_at = NEW.created_at
        WHERE id = NEW.user_id;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_update_last_login_at
AFTER INSERT ON auth_events
FOR EACH ROW
EXECUTE FUNCTION fn_update_last_login_at();

-- Процедура блокировки пользователя с записью действия администратора
CREATE PROCEDURE sp_block_user(
    IN p_admin_id BIGINT,
    IN p_target_user_id BIGINT,
    IN p_description TEXT DEFAULT NULL
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_admin_role VARCHAR(10);
    v_old_value TEXT;
BEGIN
    SELECT role INTO v_admin_role
    FROM "FNS_log".users
    WHERE id = p_admin_id;

    IF v_admin_role IS NULL THEN
        RAISE EXCEPTION 'Администратор с id % не найден', p_admin_id;
    END IF;

    IF v_admin_role <> 'admin' THEN
        RAISE EXCEPTION 'Пользователь с id % не является администратором', p_admin_id;
    END IF;

    SELECT
        CASE WHEN is_active THEN 'active' ELSE 'blocked' END
    INTO v_old_value
    FROM "FNS_log".users
    WHERE id = p_target_user_id;

    IF v_old_value IS NULL THEN
        RAISE EXCEPTION 'Пользователь с id % не найден', p_target_user_id;
    END IF;

    UPDATE "FNS_log".users
    SET is_active = FALSE,
        blocked_at = CURRENT_TIMESTAMP
    WHERE id = p_target_user_id;

    INSERT INTO "FNS_log".admin_actions (admin_id, target_user_id, action, old_value, new_value, description)
    VALUES (p_admin_id, p_target_user_id, 'block_user', v_old_value, 'blocked', p_description);
END;
$$;

-- Процедура разблокировки пользователя с записью действия администратора
CREATE PROCEDURE sp_unblock_user(
    IN p_admin_id BIGINT,
    IN p_target_user_id BIGINT,
    IN p_description TEXT DEFAULT NULL
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_admin_role VARCHAR(10);
    v_old_value TEXT;
BEGIN
    SELECT role INTO v_admin_role
    FROM "FNS_log".users
    WHERE id = p_admin_id;

    IF v_admin_role IS NULL THEN
        RAISE EXCEPTION 'Администратор с id % не найден', p_admin_id;
    END IF;

    IF v_admin_role <> 'admin' THEN
        RAISE EXCEPTION 'Пользователь с id % не является администратором', p_admin_id;
    END IF;

    SELECT
        CASE WHEN is_active THEN 'active' ELSE 'blocked' END
    INTO v_old_value
    FROM "FNS_log".users
    WHERE id = p_target_user_id;

    IF v_old_value IS NULL THEN
        RAISE EXCEPTION 'Пользователь с id % не найден', p_target_user_id;
    END IF;

    UPDATE "FNS_log".users
    SET is_active = TRUE,
        blocked_at = NULL
    WHERE id = p_target_user_id;

    INSERT INTO "FNS_log".admin_actions (admin_id, target_user_id, action, old_value, new_value, description)
    VALUES (p_admin_id, p_target_user_id, 'unblock_user', v_old_value, 'active', p_description);
END;
$$;

-- Пользователи системы
INSERT INTO users (login, password_hash, role, is_active, created_at, updated_at, last_login_at, blocked_at) VALUES
('admin_kirill', 'pbkdf2-sha256$100000$0csJi/uN26wL8pyxrIxE3A==$ppnqZWjAaqFfEWc4QU91KruwqT6tJVTS+LNdfktPgBs=', 'admin', TRUE, '2026-02-01 09:12:00', '2026-02-01 09:12:00', NULL, NULL),
('admin_marta', 'pbkdf2-sha256$100000$xmtMwQDBj6OcvGUnyJptkQ==$IBFgt3TOTnUXU9oNljFxZk9JHt0Tt8pbRyhguQ5IXO8=', 'admin', TRUE, '2026-02-03 14:35:00', '2026-03-15 11:20:00', NULL, NULL),
('ivan_solver', 'pbkdf2-sha256$100000$29H8AqMvvjmH0o3/nLvbcQ==$Q63n11VWb5D+eoZGVM+/hKMMpNK3jCYl/9pmwf2HD48=', 'user', TRUE, '2026-02-05 16:04:00', '2026-02-05 16:04:00', NULL, NULL),
('lena_crypto', 'pbkdf2-sha256$100000$AFDZOcQjFqX+mQvHPUK2MQ==$riaGVEq9dJCcyuoYTdSUDktxiTHN4g/MUD7njTcqydk=', 'user', TRUE, '2026-02-08 12:48:00', '2026-04-22 09:31:00', NULL, NULL),
('test_reader', 'pbkdf2-sha256$100000$q+QCK4gICb28spdOiuRTWA==$VN7fO6b/iFeLsWYj9nmauxk5PDUNkAXIXG1ulHoLUzo=', 'user', TRUE, '2026-02-12 18:19:00', '2026-02-12 18:19:00', NULL, NULL),
('blocked_nick', 'pbkdf2-sha256$100000$khl8qfrROdBr6hcYQ6czCA==$rRw/272UiV4BnSarWbwbcJ2cvU/FdVnoB7l+2RnpIu8=', 'user', FALSE, '2026-02-18 10:10:00', '2026-04-30 15:26:00', '2026-04-29 21:40:00', '2026-04-30 15:26:00'),
('olga_texts', 'pbkdf2-sha256$100000$SEj2+Pfcug3ToNmKpPDbFw==$SyW7RCkW5QoYVtUpjoEz8uTjFLdsDh2dCbMdA6usyvk=', 'user', TRUE, '2026-03-01 13:05:00', '2026-03-01 13:05:00', NULL, NULL),
('max_factorial', 'pbkdf2-sha256$100000$6VjvuvLY0otV+Sz9KMfBXQ==$ybtMlbB8uYwdH3csHe1cCZD5GGekQ6jDLzH5Zg5oDu0=', 'user', TRUE, '2026-03-09 07:50:00', '2026-03-19 19:44:00', NULL, NULL),
('vera_archive', 'pbkdf2-sha256$100000$d0dvLOHEYIEy1ZA+3KNsrg==$lyUiPbyZinBnQrBcelsb2NAQqC74rDwsxuiXWE6Zlqw=', 'user', TRUE, '2026-03-14 15:33:00', '2026-03-14 15:33:00', NULL, NULL),
('pavel_notes', 'pbkdf2-sha256$100000$DjhOTZa+/zVs6yKMhBqeZA==$/qBI+N3aXFIcMGyHJknITkOLLo88/g+6dA7RlRPGMx4=', 'user', TRUE, '2026-03-22 09:27:00', '2026-04-10 12:18:00', NULL, NULL),
('sasha_lab', 'pbkdf2-sha256$100000$OY3sdFaXmmR7D5M6rIM5hw==$ujhmRIbrI2zuKtlsvOSAiRjzRQt5Hw3kDfuPugXJ5mQ=', 'user', TRUE, '2026-04-01 17:55:00', '2026-04-01 17:55:00', NULL, NULL),
('guest_demo', 'pbkdf2-sha256$100000$j+viqSRiCZKLTvCO9JKYxQ==$ucMS5+ZmQVNQ7w/ZThRkmpdIz7rWxIQEEiwzj06ZSIU=', 'user', TRUE, '2026-04-15 08:05:00', '2026-04-15 08:05:00', NULL, NULL);

-- Запросы на шифрование и дешифрование
INSERT INTO encryption_requests (user_id, action, status, input_length, output_length, duration_ms, created_at) VALUES
(3, 'encrypt', 'success', 42, 50, 24, '2026-05-06 09:14:21'),
(3, 'decrypt', 'success', 50, 42, 22, '2026-05-06 09:15:03'),
(4, 'encrypt', 'success', 87, 104, 31, '2026-05-06 11:42:18'),
(5, 'encrypt', 'failed', 0, NULL, 20, '2026-05-06 12:05:47'),
(7, 'encrypt', 'success', 128, 154, 38, '2026-05-07 08:33:10'),
(8, 'decrypt', 'failed', 96, NULL, 27, '2026-05-07 10:26:59'),
(4, 'encrypt', 'success', 19, 23, 21, '2026-05-07 14:18:44'),
(3, 'encrypt', 'cancelled', 64, NULL, 20, '2026-05-08 09:01:12'),
(7, 'decrypt', 'success', 154, 128, 35, '2026-05-08 17:27:33'),
(5, 'encrypt', 'success', 33, 40, 23, '2026-05-09 07:45:29'),
(8, 'encrypt', 'success', 73, 88, 29, '2026-05-09 13:11:05'),
(4, 'decrypt', 'success', 104, 87, 30, '2026-05-10 08:22:14'),
(9, 'encrypt', 'success', 12, 14, 20, '2026-05-10 09:02:33'),
(9, 'encrypt', 'success', 205, 246, 40, '2026-05-10 09:04:10'),
(10, 'encrypt', 'success', 58, 70, 26, '2026-05-10 09:18:45'),
(10, 'decrypt', 'success', 70, 58, 25, '2026-05-10 09:19:21'),
(11, 'encrypt', 'success', 91, 109, 32, '2026-05-10 10:05:12'),
(11, 'encrypt', 'failed', 310, NULL, 39, '2026-05-10 10:07:48'),
(12, 'encrypt', 'success', 24, 29, 22, '2026-05-10 10:31:09'),
(12, 'decrypt', 'success', 29, 24, 21, '2026-05-10 10:32:02'),
(3, 'encrypt', 'success', 76, 91, 28, '2026-05-10 11:15:40'),
(4, 'decrypt', 'failed', 23, NULL, 24, '2026-05-10 11:42:19'),
(7, 'encrypt', 'processing', 37, NULL, NULL, '2026-05-10 12:00:00'),
(8, 'encrypt', 'created', 44, NULL, NULL, '2026-05-10 12:04:30'),
(9, 'decrypt', 'cancelled', 246, NULL, 20, '2026-05-10 12:10:17');

-- Ошибки обработки запросов
INSERT INTO request_errors (request_id, error_code, error_message, created_at) VALUES
(4, 'empty_input', 'Пользователь отправил пустую строку для шифрования.', '2026-05-06 12:05:47'),
(6, 'invalid_input', 'Шифротекст содержит недопустимые символы для расшифрования.', '2026-05-07 10:26:59'),
(18, 'input_too_long', 'Длина исходной строки превышает допустимое значение для тестового режима.', '2026-05-10 10:07:48'),
(22, 'invalid_input', 'Переданный шифротекст не соответствует ожидаемому формату.', '2026-05-10 11:42:19');

-- События авторизации; успешные login проверяют триггер обновления last_login_at
INSERT INTO auth_events (user_id, login, event_type, success, ip_address, created_at) VALUES
(1, 'admin_kirill', 'login', TRUE, '192.168.1.10', '2026-05-06 08:55:04'),
(3, 'ivan_solver', 'login', TRUE, '192.168.1.21', '2026-05-06 09:13:51'),
(3, 'ivan_solver', 'logout', TRUE, '192.168.1.21', '2026-05-06 09:19:30'),
(NULL, 'ghost_user', 'failed_login', FALSE, '192.168.1.77', '2026-05-06 10:02:11'),
(4, 'lena_crypto', 'login', TRUE, '192.168.1.34', '2026-05-06 11:40:02'),
(5, 'test_reader', 'failed_login', FALSE, '192.168.1.58', '2026-05-06 12:04:59'),
(7, 'olga_texts', 'login', TRUE, '192.168.1.62', '2026-05-07 08:31:48'),
(8, 'max_factorial', 'login', TRUE, '192.168.1.73', '2026-05-07 10:25:33'),
(6, 'blocked_nick', 'failed_login', FALSE, '192.168.1.44', '2026-05-08 16:12:09'),
(2, 'admin_marta', 'login', TRUE, '192.168.1.11', '2026-05-08 10:16:35'),
(4, 'lena_crypto', 'login', TRUE, '192.168.1.34', '2026-05-10 08:21:59'),
(7, 'olga_texts', 'logout', TRUE, '192.168.1.62', '2026-05-10 09:34:10'),
(9, 'vera_archive', 'login', TRUE, '192.168.1.80', '2026-05-10 09:01:50'),
(10, 'pavel_notes', 'login', TRUE, '192.168.1.81', '2026-05-10 09:17:10'),
(11, 'sasha_lab', 'login', TRUE, '192.168.1.82', '2026-05-10 10:04:11'),
(12, 'guest_demo', 'login', TRUE, '192.168.1.83', '2026-05-10 10:30:02'),
(NULL, 'admin_root', 'failed_login', FALSE, '192.168.1.91', '2026-05-10 10:41:00'),
(NULL, 'olga_text', 'failed_login', FALSE, '192.168.1.62', '2026-05-10 10:58:12'),
(1, 'admin_kirill', 'logout', TRUE, '192.168.1.10', '2026-05-10 11:02:55'),
(3, 'ivan_solver', 'login', TRUE, '192.168.1.21', '2026-05-10 11:14:59');

-- Действия администратора, созданные без процедур как исторические записи
INSERT INTO admin_actions (admin_id, target_user_id, action, old_value, new_value, description, created_at) VALUES
(1, 3, 'create_user', NULL, 'ivan_solver', 'Создан пользователь для тестирования шифрования коротких сообщений.', '2026-02-05 16:04:00'),
(1, 4, 'create_user', NULL, 'lena_crypto', 'Создан пользователь для проверки работы с текстовыми фразами.', '2026-02-08 12:48:00'),
(2, 5, 'reset_password', NULL, 'password_reset', 'Выполнен сброс пароля по запросу пользователя.', '2026-03-15 11:20:00'),
(1, 6, 'block_user', 'active', 'blocked', 'Пользователь заблокирован после повторных некорректных попыток входа.', '2026-04-30 15:26:00'),
(2, 8, 'change_role', 'user', 'user', 'Проверка административного журнала без изменения итоговой роли.', '2026-05-01 18:09:00'),
(1, 9, 'create_user', NULL, 'vera_archive', 'Создан пользователь для проверки повторных запусков шифрования.', '2026-03-14 15:33:00'),
(2, 10, 'create_user', NULL, 'pavel_notes', 'Создан пользователь для тестирования шифрования заметок.', '2026-03-22 09:27:00'),
(1, 12, 'reset_password', NULL, 'password_reset', 'Сброс пароля демонстрационного пользователя перед показом интерфейса.', '2026-05-05 13:13:00');

-- Вызовы процедур добавляют новые записи в admin_actions и проверяют триггер updated_at у users
CALL sp_unblock_user(2, 6, 'Проверка процедуры разблокировки пользователя после ручной блокировки.');
CALL sp_block_user(1, 11, 'Проверка процедуры блокировки пользователя во время тестирования.');

-- Дополнительное изменение пользователя для проверки триггера updated_at
UPDATE users
SET role = 'user'
WHERE login = 'guest_demo';

-- Примеры запросов для проверки после запуска файла
-- SELECT * FROM v_encryption_request_details ORDER BY request_id;
-- SELECT * FROM v_user_activity_summary ORDER BY total_requests DESC, login;
-- SELECT * FROM admin_actions ORDER BY id;
