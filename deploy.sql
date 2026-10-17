-- ============================================================================
--  photo_organizer  ::  deployment script
--  Target: PostgreSQL 14.24+ (Ubuntu 22.04, x86_64)
--  Run as: psql -U postgres -f deploy.sql
-- ============================================================================

\set ON_ERROR_STOP on
-- ----------------------------------------------------------------------------
-- Part 0. Database recreation
-- ----------------------------------------------------------------------------
\c postgres

DROP DATABASE IF EXISTS photo_organizer WITH (FORCE);
CREATE DATABASE photo_organizer
         WITH ENCODING 'UTF8'
         LC_COLLATE = 'ru_RU.utf8'
         LC_CTYPE   = 'ru_RU.utf8'
         TEMPLATE   = template0;

\c photo_organizer
-- ----------------------------------------------------------------------------
-- Part 1. Extensions
-- ----------------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pgcrypto;   -- crypt(), gen_salt(), digest()


-- ----------------------------------------------------------------------------
-- Part 2. Roles (cluster-wide, create if missing)
-- ----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_owner') THEN
        CREATE ROLE app_owner NOLOGIN;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_admin') THEN
        CREATE ROLE app_admin LOGIN PASSWORD 'change_me_admin';
    ELSE
        ALTER ROLE app_admin WITH LOGIN PASSWORD 'change_me_admin';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN
        CREATE ROLE app_user LOGIN PASSWORD 'change_me_user';
    ELSE
        ALTER ROLE app_user WITH LOGIN PASSWORD 'change_me_user';
    END IF;
END $$;

ALTER ROLE app_owner NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;
ALTER ROLE app_admin NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;
ALTER ROLE app_user  NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;


-- ----------------------------------------------------------------------------
-- Part 3. Utility functions 
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;


-- ----------------------------------------------------------------------------
-- Part 4. Tables
-- ----------------------------------------------------------------------------

-- Пользователи приложения (кто логинится). Пароль хранится как bcrypt-хэш.
CREATE TABLE app_users (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    login               text   UNIQUE NOT NULL,
    password_hash       text,
    password_changed_at timestamptz,
    is_active           boolean NOT NULL DEFAULT true,
    created_at          timestamptz NOT NULL DEFAULT now()
);

-- Роли приложения (пакеты прав). Заполняются один раз при развёртывании.
CREATE TABLE app_roles (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        text   UNIQUE NOT NULL,
    description text
);

-- Назначение ролей пользователям.
CREATE TABLE user_roles (
    user_id bigint NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    role_id bigint NOT NULL REFERENCES app_roles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);

-- Фотографии: атрибуты S3-объекта.
CREATE TABLE photos (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_admin_id  bigint NOT NULL REFERENCES app_users(id),

    bucket          text   NOT NULL,
    object_key      text   NOT NULL,
    etag            text,
    content_type    text,
    size_bytes      bigint CHECK (size_bytes IS NULL OR size_bytes >= 0),
    checksum_sha256 text,

    title           text,
    taken_at        timestamptz,
    uploaded_at     timestamptz NOT NULL DEFAULT now(),

    is_public       boolean NOT NULL DEFAULT false,

    UNIQUE (bucket, object_key)
);

-- ACL: кому и на какую фотографию разрешено чтение.
CREATE TABLE photo_acl (
    photo_id     uuid    NOT NULL REFERENCES photos(id) ON DELETE CASCADE,
    subject_type char(1) NOT NULL CHECK (subject_type IN ('U','R')),
    subject_id   bigint  NOT NULL,
    can_read     boolean NOT NULL DEFAULT true,
    granted_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (photo_id, subject_type, subject_id)
);

-- Логическая взаимосвязь фотографий.
CREATE TABLE link (
    id_f    uuid NOT NULL,
    id_b    uuid NOT NULL,
    comment text,
    CONSTRAINT link_unique      UNIQUE (id_f, id_b),
    CONSTRAINT link_photos_fk   FOREIGN KEY (id_f) REFERENCES photos(id) ON DELETE CASCADE,
    CONSTRAINT link_photos_fk_1 FOREIGN KEY (id_b) REFERENCES photos(id) ON DELETE CASCADE,
    CONSTRAINT link_not_self    CHECK  (id_f <> id_b)
);

-- Атрибуты фотографии (1:1). Миниатюра хранится в самой таблице.
CREATE TABLE attributes (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    photo_id          uuid   NOT NULL UNIQUE REFERENCES photos(id) ON DELETE CASCADE,

    -- Миниатюра непосредственно в БД
    thumbnail_data    bytea,
    thumbnail_mime    text,
    thumbnail_width   int CHECK (thumbnail_width  IS NULL OR thumbnail_width  > 0),
    thumbnail_height  int CHECK (thumbnail_height IS NULL OR thumbnail_height > 0),
    thumbnail_bytes   int CHECK (thumbnail_bytes  IS NULL OR thumbnail_bytes  >= 0),
    thumbnail_sha256  text,

    -- Геолокация (PostGIS)
    geo               geometry(Point, 4326),
    address           text,

    -- Автор фотографии (имя, не FK)
    author_name       text,

    -- Расширяемое хранилище для будущих полей
    extra             jsonb NOT NULL DEFAULT '{}'::jsonb,

    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT attributes_extra_is_object
        CHECK (jsonb_typeof(extra) = 'object'),
    CONSTRAINT attributes_thumbnail_mime_check
        CHECK (thumbnail_mime IS NULL OR thumbnail_mime IN
               ('image/webp','image/jpeg','image/png'))
);

-- Шоу: административная сущность
CREATE TABLE shows (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name             text   UNIQUE NOT NULL,
    description      text,
    author_id        bigint NOT NULL REFERENCES app_users(id),
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),

    interval_seconds int NOT NULL DEFAULT 5
                     CHECK (interval_seconds BETWEEN 1 AND 600),
    loop             boolean NOT NULL DEFAULT true,
    random_order     boolean NOT NULL DEFAULT false,
    transition       text NOT NULL DEFAULT 'cut'
                     CHECK (transition IN ('cut','fade')),
    show_captions    boolean NOT NULL DEFAULT true,

    extra            jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT shows_extra_is_object CHECK (jsonb_typeof(extra) = 'object')
);

CREATE INDEX idx_shows_author ON shows(author_id);

-- Фотографии шоу с порядком
CREATE TABLE show_photos (
    show_id  bigint NOT NULL REFERENCES shows(id)  ON DELETE CASCADE,
    photo_id uuid   NOT NULL REFERENCES photos(id) ON DELETE CASCADE,
    position int    NOT NULL,
    PRIMARY KEY (show_id, photo_id),
    CONSTRAINT show_photos_position_unique UNIQUE (show_id, position)
                                           DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT show_photos_position_positive CHECK (position >= 0)
);

CREATE INDEX idx_show_photos_show  ON show_photos(show_id, position);
CREATE INDEX idx_show_photos_photo ON show_photos(photo_id);


-- RLS и права
ALTER TABLE shows       ENABLE ROW LEVEL SECURITY;
ALTER TABLE show_photos ENABLE ROW LEVEL SECURITY;

CREATE POLICY shows_admin_all ON shows
FOR ALL TO app_admin USING (true) WITH CHECK (true);

CREATE POLICY show_photos_admin_all ON show_photos
FOR ALL TO app_admin USING (true) WITH CHECK (true);

REVOKE ALL ON shows, show_photos FROM app_user;

ALTER TABLE shows       OWNER TO app_owner;
ALTER TABLE show_photos OWNER TO app_owner;

-- Владелец должен иметь USAGE на схему (уже есть, но на всякий)
GRANT USAGE ON SCHEMA public TO app_owner;

-- Права администратора
GRANT SELECT, INSERT, UPDATE, DELETE ON shows       TO app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON show_photos TO app_admin;
GRANT USAGE, SELECT ON SEQUENCE shows_id_seq        TO app_admin;

-- Очередь компенсирующих действий для S3-объектов.
--Таблица - "Последний рубеж": если компенсирующее удаление из S3 не удалось,
--задача сохраняется здесь для фонового обработчика или ручного разбора.
CREATE TABLE cleanup_queue (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    action_type   text   NOT NULL CHECK (action_type IN ('delete_s3_object')),
    payload       jsonb  NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    processed_at  timestamptz,
    error_message text
);

-- ----------------------------------------------------------------------------
-- Part 5. Indexes
-- ----------------------------------------------------------------------------
CREATE INDEX idx_photo_acl_photo_read ON photo_acl(photo_id) WHERE can_read;
CREATE INDEX idx_photo_acl_subject    ON photo_acl(subject_type, subject_id);
CREATE INDEX idx_photos_owner         ON photos(owner_admin_id);
CREATE INDEX idx_photos_public        ON photos(id) WHERE is_public;
CREATE INDEX idx_link_f               ON link(id_f);
CREATE INDEX idx_link_b               ON link(id_b);
CREATE INDEX idx_attributes_geo       ON attributes USING GIST (geo);
CREATE INDEX idx_attributes_extra     ON attributes USING GIN  (extra jsonb_path_ops);
CREATE INDEX idx_attributes_author    ON attributes(author_name);
CREATE INDEX idx_cleanup_queue_unprocessed
    ON cleanup_queue(created_at)
    WHERE processed_at IS NULL;

-- ----------------------------------------------------------------------------
-- Part 6. Triggers
-- ----------------------------------------------------------------------------
CREATE TRIGGER trg_attributes_updated_at
BEFORE UPDATE ON attributes
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Триггер на updated_at
CREATE TRIGGER trg_shows_updated_at
BEFORE UPDATE ON shows
FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- ----------------------------------------------------------------------------
-- Part 7. Session context functions
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION app_current_user_id() RETURNS bigint
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT NULLIF(current_setting('app.user_id', true), '')::bigint
$$;

CREATE OR REPLACE FUNCTION app_current_role_id() RETURNS bigint
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT NULLIF(current_setting('app.role_id', true), '')::bigint
$$;

-- ----------------------------------------------------------------------------
-- Part 8. Access-check functions
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION app_can_read(p_photo_id uuid)
RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM photo_acl a
        WHERE a.photo_id     = p_photo_id
          AND a.can_read
          AND (
                (a.subject_type = 'U' AND a.subject_id = app_current_user_id())
             OR (a.subject_type = 'R' AND a.subject_id = app_current_role_id())
          )
    );
$$;

CREATE OR REPLACE FUNCTION app_can_view(p_photo_id uuid)
RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM photos p
        WHERE p.id = p_photo_id
          AND (p.is_public OR app_can_read(p.id))
    );
$$;

CREATE OR REPLACE FUNCTION authenticate_user(
    p_login    text,
    p_password text
)
RETURNS TABLE (user_id bigint, login text, role_id bigint)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_user_id bigint;
    v_hash    text;
    v_role_id bigint;
BEGIN
    SELECT u.id, u.password_hash
    INTO   v_user_id, v_hash
    FROM   app_users u
    WHERE  u.login = btrim(p_login) AND u.is_active = true;

    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'user not found' USING ERRCODE = 'P0002';
    END IF;

    IF v_hash IS NULL OR crypt(p_password, v_hash) <> v_hash THEN
        RAISE EXCEPTION 'invalid password' USING ERRCODE = '28P01';
    END IF;

    SELECT ur.role_id INTO v_role_id
    FROM   user_roles ur
    WHERE  ur.user_id = v_user_id
    LIMIT  1;

    RETURN QUERY SELECT v_user_id, btrim(p_login), v_role_id;
END;
$$;

ALTER FUNCTION authenticate_user(text, text) OWNER TO app_owner;
REVOKE ALL   ON FUNCTION authenticate_user(text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION authenticate_user(text, text) TO app_user, app_admin;
-- ----------------------------------------------------------------------------
-- Part 8. Row-Level Security
-- ----------------------------------------------------------------------------
ALTER TABLE photos     ENABLE ROW LEVEL SECURITY;
ALTER TABLE photo_acl  ENABLE ROW LEVEL SECURITY;
ALTER TABLE link       ENABLE ROW LEVEL SECURITY;
ALTER TABLE attributes ENABLE ROW LEVEL SECURITY;

-- FORCE RLS НЕ включаем: владелец app_owner должен обходить RLS,
-- чтобы SECURITY DEFINER-функции работали корректно.

-- photos
CREATE POLICY photos_admin_all ON photos
FOR ALL TO app_admin
USING (true) WITH CHECK (true);

CREATE POLICY photos_user_select ON photos
FOR SELECT TO app_user
USING ( app_can_view(id) );

-- link: связь видна, только если видны оба конца
CREATE POLICY link_admin_all ON link
FOR ALL TO app_admin
USING (true) WITH CHECK (true);

CREATE POLICY link_user_select ON link
FOR SELECT TO app_user
USING ( app_can_view(id_f) AND app_can_view(id_b) );

-- attributes: атрибут виден, если видна фотография
CREATE POLICY attributes_admin_all ON attributes
FOR ALL TO app_admin
USING (true) WITH CHECK (true);

CREATE POLICY attributes_user_select ON attributes
FOR SELECT TO app_user
USING ( app_can_view(photo_id) );

-- photo_acl: доступ только у администратора
CREATE POLICY photo_acl_admin_all ON photo_acl
FOR ALL TO app_admin
USING (true) WITH CHECK (true);

-- ----------------------------------------------------------------------------
-- Part 9. Functions and procedures
--
-- ВАЖНО: во всех процедурах внутренняя проверка делается через session_user,
-- а не current_user. Причина: SECURITY DEFINER подменяет current_user на
-- владельца функции (app_owner), и проверка pg_has_role(current_user, ...)
-- ломается. session_user всегда указывает на того, кто установил соединение.
-- ----------------------------------------------------------------------------

-- register_user: регистрация нового пользователя приложения
CREATE OR REPLACE FUNCTION register_user(
    p_login    text,
    p_password text,
    p_role_id  bigint DEFAULT NULL
) RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_user_id bigint;
BEGIN
    IF p_login IS NULL OR length(btrim(p_login)) = 0 THEN
        RAISE EXCEPTION 'login must not be empty' USING ERRCODE = '22023';
    END IF;
    IF p_password IS NULL OR length(p_password) < 8 THEN
        RAISE EXCEPTION 'password too short (min 8 chars)' USING ERRCODE = '22023';
    END IF;

    INSERT INTO app_users(login, password_hash, password_changed_at)
    VALUES (
        btrim(p_login),
        crypt(p_password, gen_salt('bf', 10)),
        now()
    )
    RETURNING id INTO v_user_id;

    IF p_role_id IS NOT NULL THEN
        INSERT INTO user_roles(user_id, role_id) VALUES (v_user_id, p_role_id);
    END IF;

    RETURN v_user_id;
EXCEPTION
    WHEN unique_violation THEN
        RAISE EXCEPTION 'user with login % already exists', p_login
            USING ERRCODE = '23505';
    WHEN foreign_key_violation THEN
        RAISE EXCEPTION 'role % does not exist', p_role_id
            USING ERRCODE = '23503';
END;
$$;

-- change_user_password: смена пароля по login + old_password.
-- Аутентификация до входа в систему, поэтому app.user_id НЕ используется.
CREATE OR REPLACE PROCEDURE change_user_password(
    p_login        text,
    p_old_password text,
    p_new_password text
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_user_id      bigint;
    v_current_hash text;
BEGIN
    IF p_login IS NULL OR p_old_password IS NULL OR p_new_password IS NULL THEN
        RAISE EXCEPTION 'login, old_password and new_password must not be null'
            USING ERRCODE = '22023';
    END IF;
    IF length(p_new_password) < 8 THEN
        RAISE EXCEPTION 'new password too short (min 8 chars)'
            USING ERRCODE = '22023';
    END IF;

    SELECT id, password_hash
    INTO   v_user_id, v_current_hash
    FROM   app_users
    WHERE  login = btrim(p_login);

    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'user not found' USING ERRCODE = 'P0002';
    END IF;

    IF v_current_hash IS NULL
       OR crypt(p_old_password, v_current_hash) <> v_current_hash THEN
        RAISE EXCEPTION 'invalid old password' USING ERRCODE = '28P01';
    END IF;

    UPDATE app_users
       SET password_hash       = crypt(p_new_password, gen_salt('bf', 10)),
           password_changed_at = now()
     WHERE id = v_user_id;
END;
$$;

-- delete_user: три режима обработки фотографий удаляемого пользователя
CREATE OR REPLACE PROCEDURE delete_user(
    p_user_id        bigint,
    p_mode           text,          -- 'delete_photos' | 'transfer' | 'unrestrict'
    p_target_user_id bigint DEFAULT NULL
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_photo_ids uuid[];
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may delete users' USING ERRCODE = '42501';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM app_users WHERE id = p_user_id) THEN
        RAISE EXCEPTION 'user % not found', p_user_id USING ERRCODE = 'P0002';
    END IF;

    SELECT array_agg(DISTINCT photo_id)
    INTO   v_photo_ids
    FROM   photo_acl
    WHERE  subject_type = 'U' AND subject_id = p_user_id;

    IF p_mode = 'delete_photos' THEN
        IF v_photo_ids IS NOT NULL THEN
            DELETE FROM photos WHERE id = ANY(v_photo_ids);
        END IF;

    ELSIF p_mode = 'transfer' THEN
        IF p_target_user_id IS NULL THEN
            RAISE EXCEPTION 'p_target_user_id is required for mode=transfer'
                USING ERRCODE = '22023';
        END IF;
        IF p_target_user_id = p_user_id THEN
            RAISE EXCEPTION 'target user must differ from the deleted user'
                USING ERRCODE = '22023';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM app_users WHERE id = p_target_user_id) THEN
            RAISE EXCEPTION 'target user % not found', p_target_user_id
                USING ERRCODE = 'P0002';
        END IF;

        IF v_photo_ids IS NOT NULL THEN
            INSERT INTO photo_acl(photo_id, subject_type, subject_id, can_read)
            SELECT DISTINCT photo_id, 'U', p_target_user_id, true
            FROM   photo_acl
            WHERE  subject_type = 'U' AND subject_id = p_user_id
            ON CONFLICT (photo_id, subject_type, subject_id) DO NOTHING;

            DELETE FROM photo_acl
            WHERE  subject_type = 'U' AND subject_id = p_user_id;
        END IF;

    ELSIF p_mode = 'unrestrict' THEN
        IF v_photo_ids IS NOT NULL THEN
            UPDATE photos SET is_public = true WHERE id = ANY(v_photo_ids);
            DELETE FROM photo_acl
            WHERE  subject_type = 'U' AND subject_id = p_user_id;
        END IF;

    ELSE
        RAISE EXCEPTION
            'unknown mode: %. Allowed: delete_photos, transfer, unrestrict', p_mode
            USING ERRCODE = '22023';
    END IF;

    DELETE FROM app_users WHERE id = p_user_id;

EXCEPTION
    WHEN foreign_key_violation THEN
        RAISE EXCEPTION
            'cannot delete user %: still owner of photos. Reassign ownership first.',
            p_user_id
            USING ERRCODE = '23503';
END;
$$;

-- reassign_photos_owner: переназначение владельца фотографий
CREATE OR REPLACE PROCEDURE reassign_photos_owner(
    p_from_user_id bigint,
    p_to_user_id   bigint
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may reassign ownership' USING ERRCODE = '42501';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM app_users WHERE id = p_to_user_id) THEN
        RAISE EXCEPTION 'target user % not found', p_to_user_id USING ERRCODE = 'P0002';
    END IF;

    UPDATE photos SET owner_admin_id = p_to_user_id
    WHERE owner_admin_id = p_from_user_id;
END;
$$;

-- grant_photo_read / revoke_photo_read
CREATE OR REPLACE PROCEDURE grant_photo_read(
    p_photo_id     uuid,
    p_subject_type char(1),
    p_subject_id   bigint
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may grant access' USING ERRCODE = '42501';
    END IF;

    IF p_subject_type NOT IN ('U','R') THEN
        RAISE EXCEPTION 'subject_type must be U or R' USING ERRCODE = '22023';
    END IF;

    IF p_subject_type = 'U'
       AND NOT EXISTS (SELECT 1 FROM app_users WHERE id = p_subject_id) THEN
        RAISE EXCEPTION 'user % not found', p_subject_id USING ERRCODE = 'P0002';
    END IF;
    IF p_subject_type = 'R'
       AND NOT EXISTS (SELECT 1 FROM app_roles WHERE id = p_subject_id) THEN
        RAISE EXCEPTION 'role % not found', p_subject_id USING ERRCODE = 'P0002';
    END IF;

    INSERT INTO photo_acl(photo_id, subject_type, subject_id, can_read)
    VALUES (p_photo_id, p_subject_type, p_subject_id, true)
    ON CONFLICT (photo_id, subject_type, subject_id)
    DO UPDATE SET can_read = true, granted_at = now();
END;
$$;

CREATE OR REPLACE PROCEDURE revoke_photo_read(
    p_photo_id     uuid,
    p_subject_type char(1),
    p_subject_id   bigint
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may revoke access' USING ERRCODE = '42501';
    END IF;

    DELETE FROM photo_acl
    WHERE photo_id = p_photo_id
      AND subject_type = p_subject_type
      AND subject_id   = p_subject_id;
END;
$$;

-- link_add / link_remove
CREATE OR REPLACE PROCEDURE link_add(
    p_id_f    uuid,
    p_id_b    uuid,
    p_comment text DEFAULT NULL
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may modify links' USING ERRCODE = '42501';
    END IF;

    INSERT INTO link(id_f, id_b, comment)
    VALUES (p_id_f, p_id_b, p_comment)
    ON CONFLICT (id_f, id_b) DO UPDATE SET comment = EXCLUDED.comment;
END;
$$;

CREATE OR REPLACE PROCEDURE link_remove(p_id_f uuid, p_id_b uuid)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may modify links' USING ERRCODE = '42501';
    END IF;
    DELETE FROM link WHERE id_f = p_id_f AND id_b = p_id_b;
END;
$$;

-- attribute_upsert: создание или обновление атрибутов фотографии
CREATE OR REPLACE PROCEDURE attribute_upsert(
    p_photo_id         uuid,
    p_thumbnail_data   bytea  DEFAULT NULL,
    p_thumbnail_mime   text   DEFAULT NULL,
    p_thumbnail_width  int    DEFAULT NULL,
    p_thumbnail_height int    DEFAULT NULL,
    p_lon              double precision DEFAULT NULL,
    p_lat              double precision DEFAULT NULL,
    p_address          text   DEFAULT NULL,
    p_author_name      text   DEFAULT NULL,
    p_extra            jsonb  DEFAULT NULL
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_geo geometry(Point, 4326);
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may modify attributes' USING ERRCODE = '42501';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM photos WHERE id = p_photo_id) THEN
        RAISE EXCEPTION 'photo % not found', p_photo_id USING ERRCODE = 'P0002';
    END IF;

    IF (p_lon IS NULL) <> (p_lat IS NULL) THEN
        RAISE EXCEPTION 'both lon and lat must be provided together'
            USING ERRCODE = '22023';
    END IF;

    IF p_lon IS NOT NULL THEN
        v_geo := ST_SetSRID(ST_MakePoint(p_lon, p_lat), 4326);
    END IF;

    INSERT INTO attributes(
        photo_id,
        thumbnail_data, thumbnail_mime, thumbnail_width, thumbnail_height,
        thumbnail_bytes, thumbnail_sha256,
        geo, address, author_name, extra
    ) VALUES (
        p_photo_id,
        p_thumbnail_data, p_thumbnail_mime, p_thumbnail_width, p_thumbnail_height,
        CASE WHEN p_thumbnail_data IS NULL THEN NULL
             ELSE octet_length(p_thumbnail_data) END,
        CASE WHEN p_thumbnail_data IS NULL THEN NULL
             ELSE encode(digest(p_thumbnail_data, 'sha256'), 'hex') END,
        v_geo, p_address, p_author_name, COALESCE(p_extra, '{}'::jsonb)
    )
    ON CONFLICT (photo_id) DO UPDATE SET
        thumbnail_data   = COALESCE(EXCLUDED.thumbnail_data,   attributes.thumbnail_data),
        thumbnail_mime   = COALESCE(EXCLUDED.thumbnail_mime,   attributes.thumbnail_mime),
        thumbnail_width  = COALESCE(EXCLUDED.thumbnail_width,  attributes.thumbnail_width),
        thumbnail_height = COALESCE(EXCLUDED.thumbnail_height, attributes.thumbnail_height),
        thumbnail_bytes  = COALESCE(EXCLUDED.thumbnail_bytes,  attributes.thumbnail_bytes),
        thumbnail_sha256 = COALESCE(EXCLUDED.thumbnail_sha256, attributes.thumbnail_sha256),
        geo              = COALESCE(EXCLUDED.geo,              attributes.geo),
        address          = COALESCE(EXCLUDED.address,          attributes.address),
        author_name      = COALESCE(EXCLUDED.author_name,      attributes.author_name),
        extra            = attributes.extra || EXCLUDED.extra;
END;
$$;

-- attribute_clear_field: точечное обнуление поля
CREATE OR REPLACE PROCEDURE attribute_clear_field(
    p_photo_id uuid,
    p_field    text
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may modify attributes' USING ERRCODE = '42501';
    END IF;

    CASE p_field
        WHEN 'thumbnail' THEN
            UPDATE attributes
               SET thumbnail_data = NULL, thumbnail_mime = NULL,
                   thumbnail_width = NULL, thumbnail_height = NULL,
                   thumbnail_bytes = NULL, thumbnail_sha256 = NULL
             WHERE photo_id = p_photo_id;
        WHEN 'geo'     THEN UPDATE attributes SET geo = NULL WHERE photo_id = p_photo_id;
        WHEN 'address' THEN UPDATE attributes SET address = NULL WHERE photo_id = p_photo_id;
        WHEN 'author'  THEN UPDATE attributes SET author_name = NULL WHERE photo_id = p_photo_id;
        WHEN 'extra'   THEN UPDATE attributes SET extra = '{}'::jsonb WHERE photo_id = p_photo_id;
        ELSE
            RAISE EXCEPTION 'unknown field: %', p_field USING ERRCODE = '22023';
    END CASE;
END;
$$;

-- attribute_set_extra / attribute_remove_extra_keys
CREATE OR REPLACE PROCEDURE attribute_set_extra(
    p_photo_id uuid,
    p_extra    jsonb
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may modify attributes' USING ERRCODE = '42501';
    END IF;
    UPDATE attributes SET extra = extra || p_extra WHERE photo_id = p_photo_id;
END;
$$;

CREATE OR REPLACE PROCEDURE attribute_remove_extra_keys(
    p_photo_id uuid,
    p_keys     text[]
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may modify attributes' USING ERRCODE = '42501';
    END IF;
    UPDATE attributes SET extra = extra - p_keys WHERE photo_id = p_photo_id;
END;
$$;

--процедура перемещения фотографий вверх/вниз
CREATE OR REPLACE PROCEDURE move_show_photo(
    p_show_id    bigint,
    p_photo_id   uuid,
    p_direction  int    -- -1 = вверх, +1 = вниз
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_current_pos  int;
    v_neighbor_id  uuid;
    v_neighbor_pos int;
BEGIN
    IF p_direction NOT IN (-1, 1) THEN
        RAISE EXCEPTION 'direction must be -1 or +1' USING ERRCODE = '22023';
    END IF;

    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may modify shows' USING ERRCODE = '42501';
    END IF;

    SELECT position INTO v_current_pos
    FROM   show_photos
    WHERE  show_id = p_show_id AND photo_id = p_photo_id;

    IF v_current_pos IS NULL THEN
        RAISE EXCEPTION 'photo % not in show %', p_photo_id, p_show_id
            USING ERRCODE = 'P0002';
    END IF;

    SELECT photo_id, position
    INTO   v_neighbor_id, v_neighbor_pos
    FROM   show_photos
    WHERE  show_id = p_show_id
      AND  CASE WHEN p_direction = 1 THEN position > v_current_pos
                ELSE position < v_current_pos END
    ORDER BY CASE WHEN p_direction = 1 THEN position END ASC,
             CASE WHEN p_direction = -1 THEN position END DESC
    LIMIT 1;

    IF v_neighbor_id IS NULL THEN
        RETURN;   -- уже на краю
    END IF;

    -- Обмен через временную позицию -1
    UPDATE show_photos SET position = -1
     WHERE show_id = p_show_id AND photo_id = p_photo_id;

    UPDATE show_photos SET position = v_current_pos
     WHERE show_id = p_show_id AND photo_id = v_neighbor_id;

    UPDATE show_photos SET position = v_neighbor_pos
     WHERE show_id = p_show_id AND photo_id = p_photo_id;
END;
$$;

ALTER PROCEDURE move_show_photo(bigint, uuid, int) OWNER TO app_owner;
REVOKE ALL   ON PROCEDURE move_show_photo(bigint, uuid, int) FROM PUBLIC;
GRANT EXECUTE ON PROCEDURE move_show_photo(bigint, uuid, int) TO app_admin;

-- get_photo_links: связи заданной фотографии, доступные пользователю
CREATE OR REPLACE FUNCTION get_photo_links(p_photo_id uuid)
RETURNS TABLE (id_f uuid, id_b uuid, comment text)
LANGUAGE sql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
    SELECT l.id_f, l.id_b, l.comment
    FROM   link l
    WHERE  (l.id_f = p_photo_id OR l.id_b = p_photo_id)
      AND  app_can_view(l.id_f)
      AND  app_can_view(l.id_b);
$$;

-- Процедура добавления фотографии в шоу с авто-позицией
CREATE OR REPLACE PROCEDURE add_photo_to_show(
    p_show_id  bigint,
    p_photo_id uuid
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_next_pos int;
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may modify shows' USING ERRCODE = '42501';
    END IF;

    SELECT COALESCE(MAX(position), 0) + 10 INTO v_next_pos
    FROM   show_photos WHERE show_id = p_show_id;

    INSERT INTO show_photos(show_id, photo_id, position)
    VALUES (p_show_id, p_photo_id, v_next_pos)
    ON CONFLICT (show_id, photo_id) DO NOTHING;
END;
$$;

ALTER PROCEDURE add_photo_to_show(bigint, uuid) OWNER TO app_owner;
REVOKE ALL   ON PROCEDURE add_photo_to_show(bigint, uuid) FROM PUBLIC;
GRANT EXECUTE ON PROCEDURE add_photo_to_show(bigint, uuid) TO app_admin;



-- get_photo_thumbnail: миниатюра, если фотография видна пользователю
CREATE OR REPLACE FUNCTION get_photo_thumbnail(p_photo_id uuid)
RETURNS TABLE (mime text, data bytea, width int, height int, sha256 text)
LANGUAGE sql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
    SELECT a.thumbnail_mime, a.thumbnail_data,
           a.thumbnail_width, a.thumbnail_height, a.thumbnail_sha256
    FROM   attributes a
    WHERE  a.photo_id = p_photo_id
      AND  app_can_view(a.photo_id);
$$;

-- ----------------------------------------------------------------------------
-- Part 10. Ownership
-- ----------------------------------------------------------------------------
ALTER TABLE app_users   OWNER TO app_owner;
ALTER TABLE app_roles   OWNER TO app_owner;
ALTER TABLE user_roles  OWNER TO app_owner;
ALTER TABLE photos      OWNER TO app_owner;
ALTER TABLE photo_acl   OWNER TO app_owner;
ALTER TABLE link        OWNER TO app_owner;
ALTER TABLE attributes  OWNER TO app_owner;
ALTER TABLE cleanup_queue OWNER TO app_owner;

ALTER FUNCTION set_updated_at()                     OWNER TO app_owner;
ALTER FUNCTION app_current_user_id()                OWNER TO app_owner;
ALTER FUNCTION app_current_role_id()                OWNER TO app_owner;
ALTER FUNCTION app_can_read(uuid)                   OWNER TO app_owner;
ALTER FUNCTION app_can_view(uuid)                   OWNER TO app_owner;
ALTER FUNCTION register_user(text, text, bigint)    OWNER TO app_owner;
ALTER FUNCTION get_photo_links(uuid)                OWNER TO app_owner;
ALTER FUNCTION get_photo_thumbnail(uuid)            OWNER TO app_owner;

ALTER PROCEDURE change_user_password(text, text, text)        OWNER TO app_owner;
ALTER PROCEDURE delete_user(bigint, text, bigint)             OWNER TO app_owner;
ALTER PROCEDURE reassign_photos_owner(bigint, bigint)         OWNER TO app_owner;
ALTER PROCEDURE grant_photo_read(uuid, char, bigint)          OWNER TO app_owner;
ALTER PROCEDURE revoke_photo_read(uuid, char, bigint)         OWNER TO app_owner;
ALTER PROCEDURE link_add(uuid, uuid, text)                    OWNER TO app_owner;
ALTER PROCEDURE link_remove(uuid, uuid)                       OWNER TO app_owner;
ALTER PROCEDURE attribute_upsert(
    uuid, bytea, text, integer, integer,
    double precision, double precision, text, text, jsonb
) OWNER TO app_owner;
ALTER PROCEDURE attribute_clear_field(uuid, text)             OWNER TO app_owner;
ALTER PROCEDURE attribute_set_extra(uuid, jsonb)              OWNER TO app_owner;
ALTER PROCEDURE attribute_remove_extra_keys(uuid, text[])     OWNER TO app_owner;

-- ----------------------------------------------------------------------------
-- Part 11. Permissions
-- ----------------------------------------------------------------------------

-- КРИТИЧНО: app_owner должен иметь USAGE на public, иначе SECURITY DEFINER
-- функции не смогут найти таблицы, и вы получите "relation does not exist".
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO app_owner, app_admin, app_user;

-- Administrator: полный доступ ко всем прикладным таблицам
GRANT SELECT, INSERT, UPDATE, DELETE ON photos     TO app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON photo_acl  TO app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON link       TO app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON attributes TO app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON app_users  TO app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON app_roles  TO app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON user_roles TO app_admin;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON cleanup_queue TO app_admin;
GRANT USAGE, SELECT ON SEQUENCE cleanup_queue_id_seq TO app_admin;

-- User: только чтение photos/link/attributes. Никакого доступа к ACL и справочникам.
GRANT SELECT ON photos     TO app_user;
GRANT SELECT ON link       TO app_user;
GRANT SELECT ON attributes TO app_user;

REVOKE ALL ON photo_acl  FROM app_user;
REVOKE ALL ON app_users  FROM app_user;
REVOKE ALL ON app_roles  FROM app_user;
REVOKE ALL ON user_roles FROM app_user;
REVOKE ALL ON cleanup_queue FROM app_user;
REVOKE ALL ON SEQUENCE cleanup_queue_id_seq FROM app_user;

-- Session context + access checks
REVOKE ALL   ON FUNCTION app_current_user_id() FROM PUBLIC;
REVOKE ALL   ON FUNCTION app_current_role_id() FROM PUBLIC;
REVOKE ALL   ON FUNCTION app_can_read(uuid)    FROM PUBLIC;
REVOKE ALL   ON FUNCTION app_can_view(uuid)    FROM PUBLIC;

GRANT EXECUTE ON FUNCTION app_current_user_id()     TO app_admin, app_user;
GRANT EXECUTE ON FUNCTION app_current_role_id()     TO app_admin, app_user;
GRANT EXECUTE ON FUNCTION app_can_read(uuid)        TO app_admin, app_user;
GRANT EXECUTE ON FUNCTION app_can_view(uuid)        TO app_admin, app_user;
GRANT EXECUTE ON FUNCTION get_photo_links(uuid)     TO app_admin, app_user;
GRANT EXECUTE ON FUNCTION get_photo_thumbnail(uuid) TO app_admin, app_user;

-- register_user: только администратор
REVOKE ALL   ON FUNCTION register_user(text, text, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION register_user(text, text, bigint) TO app_admin;

-- change_user_password: и app_user (свой пароль), и app_admin (сброс по знанию старого)
REVOKE ALL   ON PROCEDURE change_user_password(text, text, text) FROM PUBLIC;
GRANT EXECUTE ON PROCEDURE change_user_password(text, text, text) TO app_admin, app_user;

-- Управляющие процедуры: только администратор
REVOKE ALL ON PROCEDURE delete_user(bigint, text, bigint)      FROM PUBLIC;
REVOKE ALL ON PROCEDURE reassign_photos_owner(bigint, bigint)  FROM PUBLIC;
REVOKE ALL ON PROCEDURE grant_photo_read(uuid, char, bigint)   FROM PUBLIC;
REVOKE ALL ON PROCEDURE revoke_photo_read(uuid, char, bigint)  FROM PUBLIC;
REVOKE ALL ON PROCEDURE link_add(uuid, uuid, text)             FROM PUBLIC;
REVOKE ALL ON PROCEDURE link_remove(uuid, uuid)                FROM PUBLIC;
REVOKE ALL ON PROCEDURE attribute_upsert(
    uuid, bytea, text, integer, integer,
    double precision, double precision, text, text, jsonb
) FROM PUBLIC;
REVOKE ALL ON PROCEDURE attribute_clear_field(uuid, text)         FROM PUBLIC;
REVOKE ALL ON PROCEDURE attribute_set_extra(uuid, jsonb)          FROM PUBLIC;
REVOKE ALL ON PROCEDURE attribute_remove_extra_keys(uuid, text[]) FROM PUBLIC;

GRANT EXECUTE ON PROCEDURE delete_user(bigint, text, bigint)     TO app_admin;
GRANT EXECUTE ON PROCEDURE reassign_photos_owner(bigint, bigint) TO app_admin;
GRANT EXECUTE ON PROCEDURE grant_photo_read(uuid, char, bigint)  TO app_admin;
GRANT EXECUTE ON PROCEDURE revoke_photo_read(uuid, char, bigint) TO app_admin;
GRANT EXECUTE ON PROCEDURE link_add(uuid, uuid, text)            TO app_admin;
GRANT EXECUTE ON PROCEDURE link_remove(uuid, uuid)               TO app_admin;
GRANT EXECUTE ON PROCEDURE attribute_upsert(
    uuid, bytea, text, integer, integer,
    double precision, double precision, text, text, jsonb
) TO app_admin;
GRANT EXECUTE ON PROCEDURE attribute_clear_field(uuid, text)         TO app_admin;
GRANT EXECUTE ON PROCEDURE attribute_set_extra(uuid, jsonb)          TO app_admin;
GRANT EXECUTE ON PROCEDURE attribute_remove_extra_keys(uuid, text[]) TO app_admin;

-- ----------------------------------------------------------------------------
-- Part 12. Reference data
-- ----------------------------------------------------------------------------
INSERT INTO app_roles(name, description) VALUES
    ('auditor', 'Аудитор'),
    ('analyst', 'Аналитик'),
    ('manager', 'Менеджер'),
    ('viewer',  'Наблюдатель')
ON CONFLICT (name) DO NOTHING;

GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO app_admin;

-- ----------------------------------------------------------------------------
-- 
-- ----------------------------------------------------------------------------

-- Таблица аудита
CREATE TABLE audit_log (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    changed_at   timestamptz NOT NULL DEFAULT now(),
    changed_by   bigint,                 -- app_users.id или NULL
    operation    text NOT NULL CHECK (operation IN ('INSERT','UPDATE','DELETE')),
    table_name   text NOT NULL,
    record_id    text NOT NULL,          -- uuid или bigint в текстовом виде
    before_data  jsonb,
    after_data   jsonb,
    change_diff  jsonb
);

CREATE INDEX idx_audit_log_time   ON audit_log(changed_at DESC);
CREATE INDEX idx_audit_log_table  ON audit_log(table_name, changed_at DESC);

-- Функция для вычисления разницы между old и new (только изменённые ключи)
CREATE OR REPLACE FUNCTION fn_jsonb_diff(p_old jsonb, p_new jsonb)
RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
    SELECT COALESCE(jsonb_object_agg(key, value), '{}'::jsonb)
    FROM   jsonb_each(p_new)
    WHERE  p_old->>key IS DISTINCT FROM p_new->>key;
$$;

-- Универсальная триггерная функция
CREATE OR REPLACE FUNCTION audit_trigger()
RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public AS $$
DECLARE
    v_user_id bigint;
    v_record_id text;
BEGIN
    -- Пытаемся получить app.user_id из сессии, иначе NULL
    BEGIN
        v_user_id := NULLIF(current_setting('app.user_id', true), '')::bigint;
    EXCEPTION WHEN OTHERS THEN
        v_user_id := NULL;
    END;

    -- Определяем идентификатор записи в зависимости от таблицы
    IF TG_TABLE_NAME IN ('photos','attributes','link','show_photos') THEN
        v_record_id := COALESCE(NEW.id::text, OLD.id::text);
    ELSIF TG_TABLE_NAME IN ('shows') THEN
        v_record_id := COALESCE(NEW.id::text, OLD.id::text);
    ELSE
        v_record_id := 'unknown';
    END IF;

    INSERT INTO audit_log(changed_by, operation, table_name, record_id,
                          before_data, after_data, change_diff)
    VALUES (
        v_user_id,
        TG_OP,
        TG_TABLE_NAME,
        v_record_id,
        CASE WHEN TG_OP <> 'INSERT' THEN to_jsonb(OLD) END,
        CASE WHEN TG_OP <> 'DELETE' THEN to_jsonb(NEW) END,
        CASE WHEN TG_OP = 'UPDATE'
             THEN fn_jsonb_diff(to_jsonb(OLD), to_jsonb(NEW)) END
    );

    RETURN COALESCE(NEW, OLD);
END;
$$;

-- Навешиваем триггеры на контролируемые таблицы
CREATE TRIGGER trg_audit_photos      AFTER INSERT OR UPDATE OR DELETE ON photos      FOR EACH ROW EXECUTE FUNCTION audit_trigger();
CREATE TRIGGER trg_audit_attributes  AFTER INSERT OR UPDATE OR DELETE ON attributes  FOR EACH ROW EXECUTE FUNCTION audit_trigger();
CREATE TRIGGER trg_audit_shows       AFTER INSERT OR UPDATE OR DELETE ON shows       FOR EACH ROW EXECUTE FUNCTION audit_trigger();
CREATE TRIGGER trg_audit_show_photos AFTER INSERT OR UPDATE OR DELETE ON show_photos FOR EACH ROW EXECUTE FUNCTION audit_trigger();
CREATE TRIGGER trg_audit_link        AFTER INSERT OR UPDATE OR DELETE ON link        FOR EACH ROW EXECUTE FUNCTION audit_trigger();

-- Управление аудитом через настройку
CREATE TABLE app_settings (
    key   text PRIMARY KEY,
    value text NOT NULL
);
INSERT INTO app_settings(key, value) VALUES ('audit_enabled', 'true')
ON CONFLICT (key) DO NOTHING;

-- Процедура включения/отключения аудита (администратор)
CREATE OR REPLACE PROCEDURE set_audit_enabled(p_enabled boolean)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public AS $$
BEGIN
    IF NOT pg_has_role(session_user, 'app_admin', 'MEMBER') THEN
        RAISE EXCEPTION 'only app_admin may toggle audit' USING ERRCODE = '42501';
    END IF;

    IF p_enabled THEN
        ALTER TABLE photos      ENABLE TRIGGER trg_audit_photos;
        ALTER TABLE attributes  ENABLE TRIGGER trg_audit_attributes;
        ALTER TABLE shows       ENABLE TRIGGER trg_audit_shows;
        ALTER TABLE show_photos ENABLE TRIGGER trg_audit_show_photos;
        ALTER TABLE link        ENABLE TRIGGER trg_audit_link;
        UPDATE app_settings SET value = 'true' WHERE key = 'audit_enabled';
    ELSE
        ALTER TABLE photos      DISABLE TRIGGER trg_audit_photos;
        ALTER TABLE attributes  DISABLE TRIGGER trg_audit_attributes;
        ALTER TABLE shows       DISABLE TRIGGER trg_audit_shows;
        ALTER TABLE show_photos DISABLE TRIGGER trg_audit_show_photos;
        ALTER TABLE link        DISABLE TRIGGER trg_audit_link;
        UPDATE app_settings SET value = 'false' WHERE key = 'audit_enabled';
    END IF;
END;
$$;

GRANT EXECUTE ON PROCEDURE set_audit_enabled(boolean) TO app_admin;
GRANT EXECUTE ON FUNCTION fn_jsonb_diff(jsonb, jsonb) TO app_admin;
-- ----------------------------------------------------------------------------
-- Part 13. Sanity checks
-- ----------------------------------------------------------------------------
DO $$
DECLARE
    v_roles_count       int;
    v_tables_count      int;
    v_owner_has_usage   boolean;
    v_funcs_missing     int;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'postgis') THEN
        RAISE EXCEPTION 'postgis extension not installed';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pgcrypto') THEN
        RAISE EXCEPTION 'pgcrypto extension not installed';
    END IF;

    SELECT count(*) INTO v_roles_count FROM app_roles;
    IF v_roles_count = 0 THEN
        RAISE EXCEPTION 'app_roles is empty';
    END IF;

    SELECT count(*) INTO v_tables_count
    FROM   information_schema.tables
    WHERE  table_schema = 'public'
      AND  table_name IN ('app_users','app_roles','user_roles',
                          'photos','photo_acl','link','attributes');
    IF v_tables_count <> 7 THEN
        RAISE EXCEPTION 'expected 7 application tables, found %', v_tables_count;
    END IF;

    -- Проверяем, что app_owner имеет USAGE на public
    SELECT has_schema_privilege('app_owner', 'public', 'USAGE')
    INTO   v_owner_has_usage;
    IF NOT v_owner_has_usage THEN
        RAISE EXCEPTION 'app_owner lacks USAGE on schema public';
    END IF;

    -- Проверяем, что все нужные функции созданы и принадлежат app_owner
    SELECT count(*) INTO v_funcs_missing
    FROM   (VALUES
        ('register_user'),
        ('change_user_password'),
        ('delete_user'),
        ('reassign_photos_owner'),
        ('grant_photo_read'),
        ('revoke_photo_read'),
        ('link_add'),
        ('link_remove'),
        ('attribute_upsert'),
        ('attribute_clear_field'),
        ('attribute_set_extra'),
        ('attribute_remove_extra_keys'),
        ('app_can_read'),
        ('app_can_view'),
        ('get_photo_links'),
        ('get_photo_thumbnail')
    ) AS expected(name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_proc p
        WHERE  p.proname = expected.name
          AND  p.proowner = (SELECT oid FROM pg_roles WHERE rolname = 'app_owner')
    );
    IF v_funcs_missing > 0 THEN
        RAISE EXCEPTION 'some functions are missing or not owned by app_owner: %',
            v_funcs_missing;
    END IF;
    IF NOT has_table_privilege('app_admin', 'cleanup_queue', 'SELECT') THEN
        RAISE EXCEPTION 'app_admin lacks SELECT on cleanup_queue';
    END IF;

    RAISE NOTICE 'Deployment complete: % roles seeded, % application tables created',
        v_roles_count, v_tables_count;
END $$;


/*Пример правильной последовательности
1. Extensions
2. Roles
3. Utility functions (set_updated_at и другие)
4. Tables
5. Indexes
6. Triggers
7. Session context functions
8. Access-check functions
9. RLS policies
10. Business functions/procedures
11. ALTER ... OWNER
12. GRANT/REVOKE
13. Reference data
14. Sanity checks*/