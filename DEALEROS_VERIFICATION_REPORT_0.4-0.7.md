# DealerOS 0.4–0.7 — verification report

## Verdict

**NOT READY — ожидаются clean-clone и GitHub Actions.** Локальные backend, frontend, PostgreSQL/MinIO integration, migration и Playwright gates зелёные. Открытых BLOCKER, CRITICAL и относящихся к scope MAJOR после локальной проверки нет. До подтверждения чистого клона и CI разрешён только Draft PR.

Версия предназначена только для локальной демонстрации и product discovery. Она не является production-ready и не должна использовать реальные персональные или финансовые данные.

## Идентичность проверяемой версии

| Поле | Значение |
|---|---|
| Branch | `codex/release-train-0.4-0.7` |
| Base `origin/master` | `ce4960eecb6ac7c92b5b9ad401336bf72dfd3208` |
| 0.4 checkpoint | `dd619bed9133dffcc1948d0a02bedc73134e0eab` |
| 0.5 checkpoint | `5393d265db831036b654cba0cf4cd7f37834c409` |
| 0.6 checkpoint | `4a26e07310de8fa8916ce48c5277ca42eab32432` |
| 0.7 checkpoint | `6f915b862844e55bac02db2e5e308ddbc479182f` |
| Verification fixes | `8bca805a84c844fb33e9db6823419dd07df68414` |
| Локально проверенный product HEAD | `8bca805a84c844fb33e9db6823419dd07df68414` |
| Final PR HEAD | Ожидается после evidence-only commit |

В ancestry также подтверждены обязательные product/fix commits `dbc714b4c0690f00a2819c2e63f0ea723e3aec76`, `38aa9302da6649fd06294230540b8f5bbab37e62` и `c9b2981b8ba026fbba37fa54d1d2661979ac4827`.

## Реализованный scope

### 0.4 — выполнение подготовки

- Execution создаётся только из Approved plan и неизменяемого budget snapshot.
- Отдельные команды запускают и завершают execution/work order, фиксируют труд, подрядчика и материалы.
- Плановые и фактические суммы разделены; превышение лимита требует отдельного решения.
- Есть deadline notifications, audit, permissions, tenant/branch scope и optimistic concurrency.

Основные файлы: `modules/Operations/Domain/ReconditioningExecution.cs`, `modules/Operations/Application/OperationsService.cs`, `apps/api/Endpoints/OperationsEndpoints.cs`, `apps/api/Infrastructure/OperationsStore.cs`, `apps/api/Infrastructure/Migrations/20260713164628_OperationsExecution04.cs`, `apps/web/src/Operations.tsx`.

### 0.5 — QC, медиа и Listing Ready

- ReadyForSale разрешается только после отдельного QC attempt; rework оставляет историю и не переводит автомобиль в продажу.
- Приватные оригиналы медиа хранятся в MinIO, скачиваются только через authenticated API.
- Content Pack и Listing Ready создают неизменяемый публичный snapshot; публикация пока фиксируется вручную журналом.

Основные файлы: `modules/Operations/Domain/QualityAndListing.cs`, `modules/Operations/Application/QualityControlService.cs`, `modules/Operations/Application/MediaListingService.cs`, `apps/api/Endpoints/QualityListingEndpoints.cs`, `apps/api/Infrastructure/Migrations/20260713171556_QualityListing05.cs`, `apps/web/src/QualityListings.tsx`.

### 0.6 — Customer, Lead и First Response SLA

- Customer и Lead имеют tenant/branch scope; дубли предупреждаются, объединение выполняется явной командой.
- Назначение менеджера, активности, квалификация и закрытие выражены отдельными серверными командами.
- First Response SLA рассчитывается и сохраняется сервером; очередь показывает overdue leads.

Основные файлы: `modules/Crm/Domain/Customer.cs`, `modules/Crm/Domain/Lead.cs`, `modules/Crm/Application/CrmService.cs`, `apps/api/Endpoints/CrmEndpoints.cs`, `apps/api/Infrastructure/Migrations/20260713174048_CustomerLead06.cs`, `apps/web/src/Crm.tsx`.

### 0.7 — Visit, test drive и Offer

- Visit/test drive защищён от пересечения автомобиля и ответственного менеджера PostgreSQL exclusion constraints.
- Offer preview и totals считаются сервером; строки, скидка, себестоимость и margin snapshot имеют явную валюту.
- Автор не согласует собственное предложение; конкурентное решение даёт один результат.
- Approved Offer и его snapshot неизменяемы; дальнейшее изменение создаёт revision.

Основные файлы: `modules/Sales/Domain/Visit.cs`, `modules/Sales/Domain/SalesOffer.cs`, `modules/Sales/Application/SalesService.cs`, `apps/api/Endpoints/SalesEndpoints.cs`, `apps/api/Infrastructure/Migrations/20260713183257_SalesVisitsOffers07.cs`, `apps/web/src/Sales.tsx`.

## Объединённый demo flow

`Approved Reconditioning Plan → Execution/work orders/actual costs → QC Passed → ReadyForSale → private media → Content Pack → Listing Ready/manual publication → Customer → Lead → manager assignment/first contact → Visit → test drive checkout/return → Offer → manager approval → immutable Approved Offer`.

Сценарий полностью проходит через UI без ручного изменения PostgreSQL: `apps/web/e2e/release-train.spec.ts`.

## Матрица требований

| Область | Результат | Доказательство |
|---|---|---|
| 0.4 actual costs и budget overrun | PASS | `ExecutionWorkflow_TracksActualCostsApprovesOverrunAndIsTenantIsolated` |
| 0.5 QC/rework/ReadyForSale/private media/listing snapshot | PASS | `QualityReworkThenPass_CreatesListingSnapshotAndManualPublicationJournal` |
| 0.6 duplicate warning/explicit merge/Lead/SLA | PASS | `CustomerLeadWorkflow_WarnsDuplicatesMergesExplicitlyAndMeasuresFirstResponseSla` |
| 0.7 overlap/test drive/Offer/concurrent approval/snapshot/revision | PASS | `VisitAndOfferWorkflow_PreventsOverlapAndCreatesOneImmutableApprovedSnapshot` |
| Tenant и branch isolation | PASS | negative API assertions во всех четырёх методах `tests/DealerOS.IntegrationTests/ReleaseTrainApiTests.cs` |
| Permissions | PASS | прямые forbidden/allowed API assertions и UI permission states |
| Money/currency | PASS | decimal columns, currency checks и запрет неявного смешивания валют |
| Idempotency/concurrency | PASS | command IDs, optimistic versions, unique/exclusion constraints, concurrent media/decision assertions |
| Audit и immutable snapshots | PASS | PostgreSQL reload assertions для history/decisions/snapshots |

## Количество тестов

| Suite | Результат |
|---|---:|
| Backend unit | 46/46 PASS |
| PostgreSQL + MinIO integration | 25/25 PASS |
| Frontend component | 22/22 PASS |
| Полный Playwright | 5/5 PASS |
| Дополнительные независимые запуски нового 0.4–0.7 E2E | 2/2 PASS |

## Точные команды и результаты

### Backend

```powershell
dotnet restore DealerOS.slnx
dotnet list DealerOS.slnx package --vulnerable --include-transitive
dotnet format DealerOS.slnx --verify-no-changes --no-restore
dotnet build DealerOS.slnx -c Release --no-restore
dotnet test DealerOS.slnx -c Release --no-build --logger "trx;LogFilePrefix=final" --results-directory TestResults
```

Результат: restore PASS; vulnerable packages 0; format PASS; Release build — 0 warnings, 0 errors; unit 46/46; integration 25/25 за 2 min 38 s.

### Frontend

```powershell
cd apps/web
npm ci
npm audit --audit-level=high
npm audit --audit-level=high --registry=https://registry.npmjs.org
npm run lint
npm run test -- --reporter=default --reporter=junit --outputFile.junit=./test-results/frontend-junit.xml
npm run build
```

`npm ci` PASS. Первый audit через локально настроенный `https://registry.npmmirror.com` вернул 404 `[NOT_IMPLEMENTED]`; повтор через официальный npm registry — 0 vulnerabilities. Lint PASS; 22/22 tests PASS; production build PASS (`425.73 kB`, gzip `119.64 kB` для основного JS bundle).

### Runtime и E2E

```powershell
docker compose build web
docker compose up -d --no-deps web
docker compose restart api
cd apps/web
npx playwright test --reporter=list,html
npx playwright test e2e/release-train.spec.ts --workers=1 --reporter=list
npx playwright test e2e/release-train.spec.ts --workers=1 --reporter=list
```

Результат: Compose PostgreSQL/MinIO healthy, API и web доступны; полный набор 5/5 PASS за 24.6 s; два дополнительных запуска главного сценария PASS за 11.3 s и 8.7 s. Для общего набора используется один Playwright worker, потому что production-like login limiter ограничивает один IP десятью входами в минуту.

### Fresh и upgrade migrations

Для каждого сценария создавался отдельный `postgres:17-alpine` без volume (`docker run -d --rm -e POSTGRES_DB=dealeros -e POSTGRES_USER=dealeros -e POSTGRES_PASSWORD=dealeros -P postgres:17-alpine`), connection string собирался из фактического `docker port`, а контейнер останавливался после read-only evidence queries.

```powershell
dotnet ef database update --project apps/api --startup-project apps/api --configuration Release --no-build --connection $freshConnection
$env:ConnectionStrings__DealerOS=$freshConnection
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:5091'
dotnet run --project apps/api -c Release --no-build --no-launch-profile

dotnet ef database update 20260713111154_ReconditioningPlan03 --project apps/api --startup-project apps/api --configuration Release --no-build --connection $upgradeConnection
dotnet ef database update --project apps/api --startup-project apps/api --configuration Release --no-build --connection $upgradeConnection
```

Fresh: 10 migrations, 2 organizations, 6 users, 2 vehicles, 1 customer и 1 lead после идемпотентного demo seed. Upgrade: 6 migrations на уровне 0.3, затем 10 latest; extension `btree_gist` — 1; constraints `ex_sales_visit_vehicle_slot`, `ex_sales_visit_responsible_slot`, `ck_organizations_sales_policy` — 3/3.

## Найденные и исправленные проблемы

| Severity | Проблема и воспроизведение | Исправление и retest |
|---|---|---|
| MAJOR | Два конкурентных Visit могли вызвать PostgreSQL deadlock и HTTP 500. Reproducer: concurrent часть `VisitAndOfferWorkflow_PreventsOverlapAndCreatesOneImmutableApprovedSnapshot` в `tests/DealerOS.IntegrationTests/ReleaseTrainApiTests.cs`. | `apps/api/Infrastructure/SalesStore.cs` переводит exclusion/deadlock conflict в контролируемый 409; 25/25 integration PASS. |
| MAJOR | Draft Offer UI не позволял полноценно добавлять/удалять несколько прозрачных строк. Reproducer: `adds, removes and saves transparent lines in a draft offer` в `apps/web/src/Sales.test.tsx`. | Редактор реализован в `apps/web/src/Sales.tsx`; component и общий E2E PASS. |
| MAJOR | `<img src>` не передавал Bearer token и приватные фото Listing возвращали 401. Reproducer: `downloads private media with the authenticated API instead of an unprotected image request` в `apps/web/src/QualityListings.test.tsx` и главный E2E. | `PrivateImage` в `apps/web/src/QualityListings.tsx` получает Blob через `apiBlob`, создаёт/revokes object URL; 3 изображения доступны в E2E. |
| MAJOR | Optional Listing возвращал `200` с пустым body; безусловный `response.json()` создавал page error. Reproducer: `returns null for a successful 200 response with an empty body` в `apps/web/src/api.test.ts`. | `apps/web/src/api.ts` безопасно возвращает `null` для пустого успешного ответа; browser console/pageerror чисты. |
| MAJOR | Поля actual costs оставались доступны во время server command; refresh новой optimistic revision мог потерять введённое в старую форму значение. Reproducer: `locks work inputs while a server command is pending` в `apps/web/src/Operations.test.tsx`. | `apps/web/src/Operations.tsx` блокирует inputs при `busy`; component test и два повторных E2E PASS. |
| MINOR/test infrastructure | Параллельный полный Playwright создавал более 10 login requests с одного IP и получал ожидаемый 429 от login limiter. Reproducer: полный запуск с 4 workers. | `apps/web/playwright.config.ts` фиксирует 1 worker; production limiter не ослаблен; полный набор 5/5 PASS. |

Открытых BLOCKER, CRITICAL и относящихся к scope MAJOR нет.

## Артефакты локальной проверки

- `TestResults/final_net10.0_20260713222558.trx` — 46 unit tests.
- `TestResults/final_net10.0_20260713222848.trx` — 25 integration tests.
- `apps/web/test-results/frontend-junit.xml` — 22 frontend tests.
- `apps/web/dist` — production frontend build.
- `apps/web/playwright-report` — HTML Playwright report.
- `TestResults/fresh-seed.stdout.log` и `TestResults/fresh-seed.stderr.log` — запуск fresh demo seed.

Эти каталоги игнорируются Git и не входят в PR; GitHub Actions публикует соответствующие test/build/Playwright artifacts.

## Security и diff hygiene

- `git diff --check origin/master...HEAD` — PASS.
- Diff: 79 файлов и 5 commits до evidence-only report commit.
- В diff нет `.env`, private keys, certificates, access tokens, `bin`, `obj`, `dist`, `TestResults`, `test-results`, `playwright-report` или бинарных файлов.
- Наибольшие файлы — генерируемые EF migration designers/model snapshot; случайных дампов и крупных бинарников нет.
- PostgreSQL и MinIO demo volumes не удалялись.

## Clean clone

PENDING. Будет выполнен строго по `README.md` на evidence commit, включающем код, verification fixes и этот report.

## GitHub Actions

PENDING. Draft PR не будет переведён в Ready и не будет merged до зелёных backend, frontend и e2e jobs, опубликованных artifacts и отсутствия unresolved review threads.

## Сознательно отложено до 0.8–1.0

Бронь, сделка, платежи, договорные документы, выдача автомобиля, возвраты и финальная прибыль; автоматическая публикация на внешние площадки; production secrets/backup/observability/HA и deployment automation. Следующий train должен продолжить Approved Offer, а не обходить его snapshot.
