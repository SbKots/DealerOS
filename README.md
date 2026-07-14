# DealerOS

> DealerOS 1.0 — локальный демонстрационный MVP полного пути автомобиля: от поступления до `Sold`, immutable ProfitSnapshot и управленческой plan/fact экономики.

DealerOS — операционная система среднего автосалона автомобилей с пробегом. Release train 0.8–1.0 закрывает коммерческий lifecycle: Approved Offer → конкурентно безопасная Reservation → Deal → оплата/private PDF → handover → Vehicle Sold → проверяемая прибыль и CSV.

## Быстрый запуск

Требуется Docker Desktop с Docker Compose.

```powershell
docker compose up --build
```

## GitHub Pages visual demo

The Pages build is a separate frontend-only mode with synthetic data. It does not call the .NET API and does not replace the default Docker/API mode.

```powershell
cd apps/web
npm ci
npm run build:pages
```

`build:pages` loads `apps/web/.env.pages`, which sets `VITE_DEMO_MODE=true` and `VITE_BASE_PATH=/DealerOS/`. The generated static site is written to `apps/web/dist`. UI changes made in this mode live only in browser memory and may reset after a refresh.

After the workflow is available on `master`, `.github/workflows/pages.yml` deploys the visual demo to `https://sbkots.github.io/DealerOS/`. GitHub Pages must use **GitHub Actions** as its build source.

The safety boundary and deployment contract are documented in [docs/GITHUB_PAGES_DEMO.md](docs/GITHUB_PAGES_DEMO.md).

Откройте [http://localhost:4173](http://localhost:4173). Демо-пользователь:

- email: `admin@volga-auto.demo`
- пароль: `DealerOS!2026`

Вторая организация для проверки изоляции: `admin@north-auto.demo`, пароль тот же. API доступен на `http://localhost:5080`, health check — `/health`, OpenAPI в Development — `/swagger/v1/swagger.json`.

Пользователь только для чтения: `viewer@volga-auto.demo`, пароль тот же. Его JWT не содержит permissions создания и приёмки автомобиля.

Демо-диагност: `inspector@volga-auto.demo`, пароль тот же. MinIO Console: `http://localhost:9001`, локальные credentials заданы только в `compose.yaml`.

Демо-подготовка и контент: `prep@volga-auto.demo`. Демо-руководитель и QC: `manager@volga-auto.demo`. Пароль тот же. Для них включено независимое согласование; исполнитель execution не может провести QC своей работы.

В CRM demo seed содержит клиента Ивана Петрова и назначенный просроченный лид «Кроссовер до 2 млн ₽». Руководитель может проверить очередь SLA, первый контакт и квалификацию; администратор — создание клиента и явное объединение найденных дублей.

Для быстрого показа 1.0 seed также создаёт три независимые точки входа: Approved Offer (`Passat Offer`), Active Reservation (`Passat Reservation`) и Draft Deal (`Passat Deal`). Seed идемпотентен; повторный запуск не создаёт копии.

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

API принимает необязательный `X-Correlation-ID`, возвращает его в каждом response и включает в structured request log. Если header не задан, ID генерируется сервером.

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

## Демонстрация 0.1–1.0

Точный сценарий ролей и экранов приведён в [docs/DEMO.md](docs/DEMO.md). Короткий завершающий путь: `admin@volga-auto.demo` открывает «Брони» → преобразует Approved Offer, затем «Сделки» → фиксирует оплату, формирует оба PDF, завершает handover и открывает «Экономика» → выбирает проданный автомобиль и проверяет source drill-down/ревизию/CSV. Полный путь от intake, осмотра, подготовки, QC, CRM и визита автоматизирован в Playwright.

## Локальный backup/restore drill

При запущенном Compose и после создания хотя бы одного private файла:

```powershell
$backup = .\scripts\backup-local.ps1
.\scripts\restore-verify.ps1 -BackupPath $backup
```

Backup сохраняется в ignored `.local-backups/`. Restore verification поднимает только случайно именованные временные контейнеры PostgreSQL/MinIO с `tmpfs`, проверяет manifest SHA-256, migrations, ключевые counts, read-only vehicle query и hash одного private object, затем удаляет только эти временные контейнеры. Постоянные Compose volumes не подключаются и не изменяются.

DealerOS 1.0 предназначен только для локальной демонстрации с синтетическими данными. Реальные персональные/финансовые данные и production deployment запрещены до внедрения production IdP/MFA, TLS, secret manager, encryption/retention, malware scanning, HA/monitoring и правовой проверки.

## Структура

- `apps/api` — composition root, HTTP API, EF Core, JWT, миграции и адаптеры;
- `apps/web` — React/TypeScript интерфейс и Playwright e2e;
- `modules` — границы SharedKernel, IdentityAccess, Organizations, Vehicles, Inspections, Reconditioning, Operations, CRM, Sales, Reservations, Deals и Finance;
- `tests` — backend unit и PostgreSQL integration tests;
- `docs` — продукт, архитектура, решения, безопасность, demo и backlog;
- `compose.yaml` — воспроизводимое локальное окружение;
- `.github/workflows/ci.yml` — build/test/container e2e pipeline.

Дальнейший порядок работ и риски: [docs/BACKLOG.md](docs/BACKLOG.md). Архитектурные границы: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
