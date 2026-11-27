#!/bin/sh
# Run in a disposable PostgreSQL container as the postgres OS user.
set -eu
umask 077
work=$(mktemp -d)
repo=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
cleanup() {
  for instance in restored source; do
    if [ -f "$work/$instance/postmaster.pid" ]; then
      pg_ctl -D "$work/$instance" -m fast -w stop >/dev/null
    fi
  done
  rm -rf -- "$work"
}
trap cleanup EXIT
export PGHOST="$work" PGPORT=55431 PGUSER=postgres PGDATABASE=postgres
initdb -D "$work/source" -U "$PGUSER" --auth-local=trust --auth-host=scram-sha-256 --no-locale -E UTF8 > /dev/null
pg_ctl -D "$work/source" -o "-k $work -p $PGPORT -h 127.0.0.1" -l "$work/source.log" -w start >/dev/null
sql() { psql -XqAtw -v ON_ERROR_STOP=1 "$@"; }
library=Editing_Projects
createdb "$library"
sql -d "$library" -c "CREATE TABLE project_fixture (edit text); INSERT INTO project_fixture VALUES ('before backup')"
sql -f "$repo/sql/control.sql" >/dev/null
test "$(sql -c "SELECT count(*) FROM pg_roles WHERE rolname = 'resolve_panel_agent'")" = 0
test "$(sql -f "$repo/sql/registry-layout.sql")" = resolve
sql -d "$library" -f "$repo/sql/library.sql" >/dev/null
sql -v library="$library" -f "$repo/sql/register.sql"
sql -f "$repo/sql/groups.sql" >/dev/null
password=$(od -An -N24 -tx1 /dev/urandom | tr -d ' \n')
sql -v password="$password" -v library="$library" <<'SQL' >/dev/null
CREATE ROLE recovery_seed NOLOGIN PASSWORD :'password';
SELECT resolve_access.apply_change('create','recovery_editor','Editor',rolpassword,NULL)
FROM pg_authid WHERE rolname='recovery_seed';
DROP ROLE recovery_seed;
SELECT resolve_access.change_group(NULL,NULL,'Editors',ARRAY['recovery_editor'],ARRAY[:'library']);
SQL
group=$(sql -c "SELECT access_role FROM resolve_access.available_libraries" )
# Administrative traffic stays on the private socket. Verify member logins over TCP.
{
  printf 'host "%s" +%s 127.0.0.1/32 scram-sha-256\n' "$library" "$group"
  printf 'host all +resolve_panel_members 127.0.0.1/32 reject\n'
  cat "$work/source/pg_hba.conf"
} > "$work/hba"
mv "$work/hba" "$work/source/pg_hba.conf"
sql -c 'SELECT pg_reload_conf()' >/dev/null
pg_basebackup -D "$work/restored" -Fp -X stream --checkpoint=fast -w
pg_verifybackup "$work/restored"
export PGPORT=55432
pg_ctl -D "$work/restored" -o "-k $work -p $PGPORT -h 127.0.0.1" -l "$work/restored.log" -w start >/dev/null
test "$(sql -c "SELECT name FROM resolve_access.available_libraries")" = "$library"
test "$(sql -c "SELECT label FROM resolve_access.group_directory WHERE 'recovery_editor' = ANY(logins) AND 'Editing_Projects' = ANY(libraries)")" = Editors
editor() { PGHOST=127.0.0.1 PGUSER=recovery_editor PGPASSWORD="$password" psql -XqAtw -v ON_ERROR_STOP=1 "$@"; }
test "$(editor -d "$library" -c 'TABLE project_fixture')" = 'before backup'
editor -d "$library" -c "INSERT INTO project_fixture VALUES ('after recovery')"
test "$(editor -d "$library" -c 'SELECT count(*) FROM project_fixture')" = 2
if editor -d postgres -c 'SELECT 1' >/dev/null 2>&1; then exit 1; fi
test "$(PGPORT=55431 sql -d "$library" -c 'SELECT count(*) FROM project_fixture')" = 1
printf '%s\n' 'Verified the recovered library, login, group grant, write and refusal on the maintenance database.'

# A desktop group install can follow an old web panel, a named panel or no panel.
sql -c 'CREATE ROLE resolve_panel_agent LOGIN; CREATE ROLE studio_service LOGIN'
group_access() {
  sql -v role="$1" <<'SQL'
SELECT has_table_privilege(:'role', 'resolve_access.group_directory', 'SELECT')
   AND has_table_privilege(:'role', 'resolve_access.member_direct_access', 'SELECT')
   AND has_function_privilege(:'role', 'resolve_access.change_group(text,bigint,text,text[],text[])', 'EXECUTE');
SQL
}
sql -f "$repo/sql/groups.sql" >/dev/null
test "$(group_access resolve_panel_agent)" = f
sql -c 'GRANT EXECUTE ON FUNCTION resolve_access.panel_administrator(text) TO resolve_panel_agent'
sql -f "$repo/sql/groups.sql" >/dev/null
test "$(group_access resolve_panel_agent)" = f
sql -c 'GRANT EXECUTE ON FUNCTION resolve_access.change_member(text,text,text,text,text,text,text,bytea,boolean) TO resolve_panel_agent'
sql -f "$repo/sql/groups.sql" >/dev/null
test "$(group_access resolve_panel_agent)" = t
sql -c "CREATE TABLE resolve_access.panel_service_account (username name);
        INSERT INTO resolve_access.panel_service_account VALUES ('studio_service');
        GRANT EXECUTE ON FUNCTION resolve_access.panel_administrator(text),
          resolve_access.change_member(text,text,text,text,text,text,text,bytea,boolean) TO studio_service"
sql -f "$repo/sql/control.sql" >/dev/null
sql -f "$repo/sql/groups.sql" >/dev/null
test "$(group_access studio_service)" = t

createdb previous_registry
sed 's/resolve_/previous_/g' "$repo/sql/control.sql" | sql -d previous_registry >/dev/null
test "$(sql -d previous_registry -f "$repo/sql/registry-layout.sql")" = previous
