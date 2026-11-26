BEGIN;
SET LOCAL search_path = pg_catalog, pg_temp;
SELECT pg_advisory_xact_lock(912780041);

CREATE TABLE IF NOT EXISTS resolve_access.user_groups (
    role_name name PRIMARY KEY,
    role_oid oid UNIQUE NOT NULL,
    label text NOT NULL CHECK (length(btrim(label)) BETWEEN 1 AND 80),
    revision bigint NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX IF NOT EXISTS user_groups_label ON resolve_access.user_groups (lower(label));

CREATE OR REPLACE VIEW resolve_access.group_directory AS
SELECT g.role_name::text AS group_key, g.label, g.revision,
       ARRAY(
           SELECT p.username::text
           FROM pg_auth_members a
           JOIN resolve_access.people p ON p.login_oid = a.member
           JOIN pg_roles r ON r.oid = p.login_oid AND r.rolname = p.username
           WHERE a.roleid = g.role_oid AND p.removed_at IS NULL
           ORDER BY p.username
       ) AS logins,
       ARRAY(
           SELECT l.name::text
           FROM resolve_access.available_libraries l
           JOIN pg_roles r ON r.rolname = l.access_role
           WHERE EXISTS (SELECT FROM pg_auth_members a WHERE a.roleid = r.oid AND a.member = g.role_oid)
           ORDER BY l.name
       ) AS libraries
FROM resolve_access.user_groups g
JOIN pg_roles r ON r.oid = g.role_oid AND r.rolname = g.role_name AND NOT r.rolcanlogin AND NOT r.rolsuper;

CREATE OR REPLACE VIEW resolve_access.member_direct_access AS
SELECT d.username,
       ARRAY(
           SELECT l.name::text
           FROM resolve_access.available_libraries l
           JOIN pg_roles r ON r.rolname = l.access_role
           WHERE EXISTS (SELECT FROM pg_auth_members a WHERE a.roleid = r.oid AND a.member = p.login_oid)
           ORDER BY l.name
       ) AS libraries
FROM resolve_access.directory d
JOIN resolve_access.people p ON p.username = d.username;

CREATE OR REPLACE FUNCTION resolve_access.change_group(
    requested_group text, expected_revision bigint, new_label text, chosen_logins text[], chosen_libraries text[]
) RETURNS text[]
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $body$
DECLARE
    previous resolve_access.group_directory%ROWTYPE;
    group_role name;
    account_name text;
    library_role name;
    inherit_only text := CASE WHEN current_setting('server_version_num')::int >= 160000
                              THEN ' WITH INHERIT TRUE, SET FALSE' ELSE '' END;
BEGIN
    PERFORM pg_advisory_xact_lock(912780041);
    IF requested_group IS NOT NULL THEN
        SELECT * INTO previous FROM resolve_access.group_directory WHERE group_key = requested_group;
        IF NOT FOUND OR previous.revision IS DISTINCT FROM expected_revision THEN
            RAISE EXCEPTION 'The group has changed. Open the group list again.' USING ERRCODE = 'P0001';
        END IF;
        group_role := previous.group_key;
    ELSE
        previous.logins := ARRAY[]::text[];
        previous.libraries := ARRAY[]::text[];
    END IF;

    IF new_label IS NULL THEN
        IF group_role IS NULL THEN
            RAISE EXCEPTION 'Select a group to delete.' USING ERRCODE = 'P0001';
        END IF;
        chosen_logins := ARRAY[]::text[];
        chosen_libraries := ARRAY[]::text[];
    ELSE
        new_label := btrim(new_label);
        IF length(new_label) NOT BETWEEN 1 AND 80 THEN
            RAISE EXCEPTION 'Enter a group name of 1–80 characters.' USING ERRCODE = 'P0001';
        END IF;
        IF EXISTS (SELECT FROM resolve_access.user_groups
                   WHERE lower(label) = lower(new_label) AND role_name IS DISTINCT FROM group_role) THEN
            RAISE EXCEPTION 'A group with this name already exists.' USING ERRCODE = 'P0001';
        END IF;
        IF chosen_logins IS NULL OR chosen_libraries IS NULL OR EXISTS (
            SELECT FROM unnest(chosen_libraries) AS selected(library)
            WHERE NOT EXISTS (SELECT FROM resolve_access.available_libraries l WHERE l.name = selected.library)
        ) THEN
            RAISE EXCEPTION 'A library is no longer available. Open the group list again.' USING ERRCODE = 'P0001';
        END IF;
    END IF;

    -- Serialize changes to these accounts against rename, disable and delete.
    PERFORM p.login_oid
    FROM resolve_access.people p
    WHERE p.username::text = ANY(previous.logins || chosen_logins)
    ORDER BY p.login_oid FOR UPDATE;
    IF requested_group IS NOT NULL AND previous.logins IS DISTINCT FROM
        (SELECT logins FROM resolve_access.group_directory WHERE group_key = requested_group) THEN
        RAISE EXCEPTION 'The group membership has changed. Open the group list again.' USING ERRCODE = 'P0001';
    END IF;
    IF EXISTS (
        SELECT FROM unnest(chosen_logins) AS selected(login)
        WHERE NOT EXISTS (
            SELECT FROM resolve_access.people p
            JOIN pg_roles r ON r.oid = p.login_oid AND r.rolname = p.username
            WHERE p.username = selected.login AND p.removed_at IS NULL
              AND NOT r.rolsuper AND NOT r.rolcreaterole AND pg_has_role(r.oid, 'resolve_panel_members', 'MEMBER')
        )
    ) THEN
        RAISE EXCEPTION 'An account is no longer available. Open the group list again.' USING ERRCODE = 'P0001';
    END IF;

    IF group_role IS NULL THEN
        group_role := 'resolve_group_' || replace(gen_random_uuid()::text, '-', '');
        EXECUTE format('CREATE ROLE %I NOLOGIN', group_role);
        INSERT INTO resolve_access.user_groups (role_name, role_oid, label)
        SELECT rolname, oid, new_label FROM pg_roles WHERE rolname = group_role;
    END IF;

    FOR account_name IN SELECT unnest(previous.logins) EXCEPT SELECT unnest(chosen_logins)
    LOOP
        EXECUTE format('REVOKE %I FROM %I', group_role, account_name);
    END LOOP;
    FOR library_role IN
        SELECT l.access_role FROM resolve_access.available_libraries l
        WHERE l.name::text = ANY(previous.libraries) AND NOT l.name::text = ANY(chosen_libraries)
    LOOP
        EXECUTE format('REVOKE %I FROM %I', library_role, group_role);
    END LOOP;
    FOR library_role IN
        SELECT l.access_role FROM resolve_access.available_libraries l
        WHERE l.name::text = ANY(chosen_libraries) AND NOT l.name::text = ANY(previous.libraries)
    LOOP
        EXECUTE format('GRANT %I TO %I%s', library_role, group_role, inherit_only);
    END LOOP;
    FOR account_name IN SELECT unnest(chosen_logins) EXCEPT SELECT unnest(previous.logins)
    LOOP
        EXECUTE format('GRANT %I TO %I%s', group_role, account_name, inherit_only);
    END LOOP;

    IF new_label IS NULL THEN
        DELETE FROM resolve_access.user_groups WHERE role_name = group_role;
        BEGIN
            EXECUTE format('DROP ROLE %I', group_role);
        EXCEPTION WHEN dependent_objects_still_exist THEN
            -- Retain ownership of objects created under the role on older PostgreSQL versions.
            NULL;
        END;
    ELSIF requested_group IS NOT NULL THEN
        UPDATE resolve_access.user_groups SET label = new_label, revision = revision + 1 WHERE role_name = group_role;
    END IF;

    IF NOT previous.libraries <@ chosen_libraries THEN
        RETURN previous.logins;
    END IF;
    RETURN ARRAY(SELECT unnest(previous.logins) EXCEPT SELECT unnest(chosen_logins));
END;
$body$;

CREATE OR REPLACE FUNCTION resolve_access.remove_group_memberships() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $body$
DECLARE
    group_role name;
BEGIN
    FOR group_role IN
        SELECT g.role_name FROM resolve_access.user_groups g
        JOIN pg_auth_members a ON a.roleid = g.role_oid
        JOIN pg_roles r ON r.oid = g.role_oid AND r.rolname = g.role_name
        WHERE a.member = NEW.login_oid
    LOOP
        EXECUTE format('REVOKE %I FROM %I', group_role, NEW.username);
    END LOOP;
    RETURN NEW;
END;
$body$;

DROP TRIGGER IF EXISTS remove_group_memberships ON resolve_access.people;
CREATE TRIGGER remove_group_memberships AFTER UPDATE OF removed_at ON resolve_access.people
FOR EACH ROW WHEN (OLD.removed_at IS NULL AND NEW.removed_at IS NOT NULL)
EXECUTE FUNCTION resolve_access.remove_group_memberships();

REVOKE ALL ON FUNCTION resolve_access.change_group(text,bigint,text,text[],text[]),
    resolve_access.remove_group_memberships() FROM PUBLIC;
GRANT SELECT ON resolve_access.group_directory, resolve_access.member_direct_access TO resolve_panel_agent;
GRANT EXECUTE ON FUNCTION resolve_access.change_group(text,bigint,text,text[],text[]) TO resolve_panel_agent;
COMMIT;
