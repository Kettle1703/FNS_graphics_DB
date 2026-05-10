CREATE OR REPLACE FUNCTION "FNS_log".fn_set_users_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION "FNS_log".fn_update_last_login_at()
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

CREATE OR REPLACE PROCEDURE "FNS_log".sp_block_user(
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
        RAISE EXCEPTION 'Admin id % not found', p_admin_id;
    END IF;

    IF v_admin_role <> 'admin' THEN
        RAISE EXCEPTION 'User id % is not admin', p_admin_id;
    END IF;

    SELECT CASE WHEN is_active THEN 'active' ELSE 'blocked' END
    INTO v_old_value
    FROM "FNS_log".users
    WHERE id = p_target_user_id;

    IF v_old_value IS NULL THEN
        RAISE EXCEPTION 'Target user id % not found', p_target_user_id;
    END IF;

    UPDATE "FNS_log".users
    SET is_active = FALSE,
        blocked_at = CURRENT_TIMESTAMP
    WHERE id = p_target_user_id;

    INSERT INTO "FNS_log".admin_actions (admin_id, target_user_id, action, old_value, new_value, description)
    VALUES (p_admin_id, p_target_user_id, 'block_user', v_old_value, 'blocked', p_description);
END;
$$;

CREATE OR REPLACE PROCEDURE "FNS_log".sp_unblock_user(
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
        RAISE EXCEPTION 'Admin id % not found', p_admin_id;
    END IF;

    IF v_admin_role <> 'admin' THEN
        RAISE EXCEPTION 'User id % is not admin', p_admin_id;
    END IF;

    SELECT CASE WHEN is_active THEN 'active' ELSE 'blocked' END
    INTO v_old_value
    FROM "FNS_log".users
    WHERE id = p_target_user_id;

    IF v_old_value IS NULL THEN
        RAISE EXCEPTION 'Target user id % not found', p_target_user_id;
    END IF;

    UPDATE "FNS_log".users
    SET is_active = TRUE,
        blocked_at = NULL
    WHERE id = p_target_user_id;

    INSERT INTO "FNS_log".admin_actions (admin_id, target_user_id, action, old_value, new_value, description)
    VALUES (p_admin_id, p_target_user_id, 'unblock_user', v_old_value, 'active', p_description);
END;
$$;
