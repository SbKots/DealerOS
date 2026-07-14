# Runbook

## Проверка состояния

- Web: `http://host:4173/`
- API/DB health: `GET http://host:5080/health`
- Liveness: `GET /health/live`; readiness PostgreSQL: `GET /health/ready`.
- Correlation ID: клиентский `X-Correlation-ID` принимается либо генерируется, возвращается тем же header, попадает в structured request log и audit бизнес-команд без body/PII.

## Миграция

Production не запускает миграции автоматически. Сделать backup, проверить migration script на staging, выполнить `dotnet tool restore` и `dotnet tool run dotnet-ef database update --project apps/api --startup-project apps/api`, затем smoke test. Initial migration обратима до `0`; если уже есть бизнес-данные, rollback приложения предпочтительнее удаления схемы.

## Backup/restore локального demo

Запустить `$backup = .\scripts\backup-local.ps1`, затем `.\scripts\restore-verify.ps1 -BackupPath $backup`. Первый скрипт получает consistent PostgreSQL custom dump и private MinIO objects, фиксирует длины/SHA-256 в manifest. Второй никогда не подключает постоянные volumes: восстанавливает в случайно именованные PostgreSQL/MinIO containers на `tmpfs`, проверяет `__EFMigrationsHistory`, counts Vehicle/Deal/ProfitSnapshot, read-only SQL и hash private object, затем удаляет только временные containers. Backup каталоги ignored и не должны попадать в Git.

Это локальный drill, не production backup policy. До реального использования нужны зашифрованные off-site backups, retention, key management, RPO/RTO, регулярный restore rehearsal и контролируемый доступ.

## Типовые инциденты

- `health` unhealthy: проверить доступность PostgreSQL и connection string, затем логи API.
- HTTP 409: ожидаемый duplicate/concurrency conflict; не повторять команду вслепую, обновить объект.
- HTTP 403: проверить permission и branch access пользователя; не подменять `org_id` вручную.
- Ошибка по correlation ID: найти структурированный request log и audit event, не запрашивая у пользователя токен.
