# Runbook

## Проверка состояния

- Web: `http://host:4173/`
- API/DB health: `GET http://host:5080/health`
- Liveness: `GET /health/live`; readiness PostgreSQL: `GET /health/ready`.
- Correlation ID: response header `X-Correlation-ID`, он же пишется в audit для бизнес-команд.

## Миграция

Production не запускает миграции автоматически. Сделать backup, проверить migration script на staging, выполнить `dotnet tool restore` и `dotnet tool run dotnet-ef database update --project apps/api --startup-project apps/api`, затем smoke test. Initial migration обратима до `0`; если уже есть бизнес-данные, rollback приложения предпочтительнее удаления схемы.

## Backup/restore (локальный пример)

Backup: `docker compose exec -T postgres pg_dump -U dealeros -Fc dealeros > dealeros.dump`.

Restore проверяется в отдельной пустой БД: создать БД, выполнить `pg_restore --clean --if-exists`, запустить API и пройти `/health` + demo read path. Не проверять restore поверх production.

## Типовые инциденты

- `health` unhealthy: проверить доступность PostgreSQL и connection string, затем логи API.
- HTTP 409: ожидаемый duplicate/concurrency conflict; не повторять команду вслепую, обновить объект.
- HTTP 403: проверить permission и branch access пользователя; не подменять `org_id` вручную.
- Ошибка по correlation ID: найти структурированный request log и audit event, не запрашивая у пользователя токен.
