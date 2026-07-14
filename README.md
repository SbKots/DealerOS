# DealerOS

> Текущий сквозной релиз 0.8 продолжает неизменяемый Approved Offer конкурентно безопасной временной бронью, ручной demo-предоплатой и автоматическим освобождением автомобиля по сроку.

DealerOS — операционная система среднего автосалона автомобилей с пробегом. Итерация 0.8 не позволяет одновременно обещать один готовый автомобиль двум клиентам: активная бронь защищена PostgreSQL и переводит автомобиль в `Reserved`.

## Быстрый запуск

Требуется Docker Desktop с Docker Compose.

```powershell
docker compose up --build
```

Откройте [http://localhost:4173](http://localhost:4173). Демо-пользователь:

- email: `admin@volga-auto.demo`
- пароль: `DealerOS!2026`

Вторая организация для проверки изоляции: `admin@north-auto.demo`, пароль тот же. API доступен на `http://localhost:5080`, health check — `/health`, OpenAPI в Development — `/swagger/v1/swagger.json`.

Пользователь только для чтения: `viewer@volga-auto.demo`, пароль тот же. Его JWT не содержит permissions создания и приёмки автомобиля.

Демо-диагност: `inspector@volga-auto.demo`, пароль тот же. MinIO Console: `http://localhost:9001`, локальные credentials заданы только в `compose.yaml`.

Демо-подготовка и контент: `prep@volga-auto.demo`. Демо-руководитель и QC: `manager@volga-auto.demo`. Пароль тот же. Для них включено независимое согласование; исполнитель execution не может провести QC своей работы.

В CRM demo seed содержит клиента Ивана Петрова и назначенный просроченный лид «Кроссовер до 2 млн ₽». Руководитель может проверить очередь SLA, первый контакт и квалификацию; администратор — создание клиента и явное объединение найденных дублей.

Остановить окружение: `docker compose down`. Удалить только локальные демонстрационные данные: `docker compose down -v`.

## Локальная разработка

1. Поднять PostgreSQL и MinIO: `docker compose up postgres minio -d`.
2. В одном терминале: `dotnet run --project apps/api`.
3. В другом: `cd apps/web; npm ci; npm run dev`.

API в Development применяет миграции и идемпотентно создаёт demo data. В production автоматическое применение миграций выключено и должно выполняться отдельным release step.

Основные параметры окружения:

- `ConnectionStrings__DealerOS` — PostgreSQL connection string;
- `Jwt__Key` — ключ подписи JWT; production не запустится с локальным ключом;
- `Database__ApplyMigrations` — автоматические миграции, только для Development/тестов;
- `Database__SeedDemo` — демонстрационные организации и пользователи.
- `ObjectStorage__Endpoint`, `AccessKey`, `SecretKey`, `Bucket`, `UseSsl` — приватное S3-compatible хранилище фотографий;
- `ObjectStorage__EnsureBucket` — создание bucket при старте только для Development/тестов.
- `Operations__DeadlineWorkerEnabled`, `Operations__DeadlineWorkerIntervalMinutes` — фоновый tenant-aware контроль близких и просроченных сроков работ.
- `Reservations__ExpirationWorkerEnabled`, `Reservations__ExpirationWorkerIntervalSeconds` — идемпотентное освобождение истёкших броней.

Диагностика: `/health/live` проверяет процесс, `/health/ready` — PostgreSQL и object storage, `/health` сохранён как совмещённая проверка.

## Проверки

```powershell
dotnet restore DealerOS.slnx
dotnet format DealerOS.slnx --verify-no-changes --no-restore
dotnet build DealerOS.slnx --no-restore
dotnet test DealerOS.slnx --no-build

cd apps/web
npm ci
npm run lint
npm run test
npm run build
```

Integration tests используют настоящий PostgreSQL в Testcontainers и требуют запущенный Docker. E2E запускается против полного Compose-стека: `npm run test:e2e` после `docker compose up --build -d`.

## Структура

- `apps/api` — composition root, HTTP API, EF Core, JWT, миграции и адаптеры;
- `apps/web` — React/TypeScript интерфейс и Playwright e2e;
- `modules` — границы SharedKernel, IdentityAccess, Organizations, Vehicles, Inspections, Reconditioning, Operations, CRM и Sales;
- `tests` — backend unit и PostgreSQL integration tests;
- `docs` — продукт, архитектура, решения, безопасность, demo и backlog;
- `compose.yaml` — воспроизводимое локальное окружение;
- `.github/workflows/ci.yml` — build/test/container e2e pipeline.

Дальнейший порядок работ и риски: [docs/BACKLOG.md](docs/BACKLOG.md). Архитектурные границы: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
