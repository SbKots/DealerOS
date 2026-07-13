# DealerOS 0.3 — verification report

Дата проверки: 2026-07-13 (Europe/Moscow).

Статус: **VERIFIED — готово к review как локальная демонстрационная версия, не готово к production**.

## Проверенная версия

- branch: `codex/reconditioning-plan-0.3`;
- base `origin/master`: `3a3ed8b2d98e743beec2d8ca819acbf9c7740b37`;
- product commit: `38aa9302da6649fd06294230540b8f5bbab37e62`;
- test-only CI fix: `e3affb819660584304f7cb1fce16be1272084549`;
- проверенный code HEAD: `e3affb819660584304f7cb1fce16be1272084549`;
- product commit присутствует в ancestry проверенного HEAD;
- успешный GitHub Actions run: [29253860290](https://github.com/SbKots/DealerOS/actions/runs/29253860290).

## Scope вертикального сценария

Проверен сценарий `ReconditioningRequired vehicle → Draft plan → mandatory RepairRequired works → изменение состава и бюджета → Submitted → manager approval with limit → immutable Approved snapshot → новая revision`.

Проверены отдельные server commands для submit, approve, reject, request changes, cancel и revision; универсального status PATCH нет. Проверены tenant/branch isolation, permissions, independent approval, optimistic concurrency, idempotent decision ID, один активный план, audit, mixed-currency guard и PostgreSQL constraints.

## Количество тестов

| Набор | Результат |
| --- | --- |
| Backend unit | 26/26 passed |
| PostgreSQL + MinIO integration | 21/21 passed |
| Frontend component | 8/8 passed |
| Playwright e2e | 4/4 passed |
| Reconditioning e2e stability | 3/3 независимых запуска passed |

## Точные команды локальной проверки

### Backend

```powershell
dotnet restore DealerOS.slnx
dotnet list DealerOS.slnx package --vulnerable --include-transitive
dotnet format DealerOS.slnx --verify-no-changes --no-restore
dotnet build DealerOS.slnx -c Release --no-restore
dotnet test DealerOS.slnx -c Release --no-build --logger trx --results-directory TestResults
```

Результат: Release build завершён с 0 warnings и 0 errors; 26/26 unit и 21/21 integration passed; уязвимые NuGet packages не обнаружены.

### Два обязательных прогона integration после CI fix

```powershell
dotnet test tests\DealerOS.IntegrationTests\DealerOS.IntegrationTests.csproj -c Release --no-build --logger "trx;LogFileName=integration-fixture-run-1.trx" --results-directory TestResults\integration-fixture-run-1
dotnet test tests\DealerOS.IntegrationTests\DealerOS.IntegrationTests.csproj -c Release --no-build --logger "trx;LogFileName=integration-fixture-run-2.trx" --results-directory TestResults\integration-fixture-run-2
```

- run 1: 21/21 passed, 1 min 47 sec;
- run 2: 21/21 passed, 1 min 51 sec.

`ReconditioningPostgresFixture` создаёт один PostgreSQL Testcontainer на весь `ReconditioningApiTests`. Перед каждым test method fixture подключается к служебной БД `postgres`, выполняет `DROP DATABASE IF EXISTS "dealeros_reconditioning_tests" WITH (FORCE)`, затем `CREATE DATABASE "dealeros_reconditioning_tests"`. Поэтому application schemas и `__EFMigrationsHistory` не очищаются частично: они отсутствуют в новой БД и заново создаются штатными EF migrations. Каждый тест получает независимую БД и не зависит от порядка выполнения.

### Frontend

```powershell
cd apps\web
npm ci
npm audit --registry=https://registry.npmjs.org --audit-level=high
npm run lint
npm run test -- --reporter=default --reporter=junit --outputFile.junit=./test-results/frontend-junit.xml
npm run build
```

Результат: 0 npm vulnerabilities; lint passed; 8/8 component tests passed; TypeScript/Vite production build passed.

### Compose и Playwright

```powershell
docker compose up --build -d
cd apps\web
npx playwright install chromium
npm run test:e2e
```

Результат: PostgreSQL и MinIO healthy, web и API ready; 4/4 e2e passed. Новый `reconditioning.spec.ts` дополнительно выполнен три раза отдельными процессами с одним worker и перезапуском только API между прогонами:

```powershell
for ($run=1; $run -le 3; $run++) {
  docker compose restart api
  npx playwright test e2e/reconditioning.spec.ts --workers=1
}
```

Результат: 3/3 passed. PostgreSQL и MinIO volumes между этими прогонами не удалялись.

## Миграции

Integration test `ReconditioningApiTests.Migration_UpgradesFrom02AndFreshDatabaseStartsWithDemoQueue` проверяет оба пути:

- fresh database получает полный набор миграций и demo queue;
- БД сначала мигрируется до `20260713083112_InspectionPhotoReliability02`, затем обновляется до `20260713111154_ReconditioningPlan03`.

Тест входит в оба полных прогона 21/21. Tenant-aware FK, check constraints, unique `(OrganizationId, VehicleId, Revision)` и partial unique active-plan index проверены миграцией и integration-сценариями.

## Clean clone строго по README

Проверка выполнена на exact product commit:

```powershell
git clone --no-checkout https://github.com/SbKots/DealerOS.git C:\Users\SberKot\Documents\MEGA-PROJECT\.verification\clean-clone-0.3
git -C C:\Users\SberKot\Documents\MEGA-PROJECT\.verification\clean-clone-0.3 checkout --detach 38aa9302da6649fd06294230540b8f5bbab37e62
cd C:\Users\SberKot\Documents\MEGA-PROJECT\.verification\clean-clone-0.3
docker compose up --build
```

Команда запуска совпадает с README. Результат: `http://localhost:4173` вернул HTTP 200, `http://localhost:5080/health/ready` вернул HTTP 200, PostgreSQL и MinIO получили `healthy`. После проверки выполнен `docker compose down` без `-v`; пользовательские Docker volumes не удалялись. Временный clone удалён как воспроизводимый verification output.

## Найденные и исправленные проблемы

### MAJOR: несовместимые конкурентные approval-команды получали ложный success

- reproducer: `tests/DealerOS.IntegrationTests/ReconditioningApiTests.cs`, test `ConcurrentApproval_SameDecisionIdWithDifferentPayload_ReturnsConflictForLosingCommand`;
- unit contract: `tests/DealerOS.UnitTests/ReconditioningDomainTests.cs`, test `ApprovalDecisionId_CannotBeReusedForDifferentBudgetPayload`;
- исправление: `modules/Reconditioning/Domain/ReconditioningPlan.cs` и `modules/Reconditioning/Application/ReconditioningService.cs`;
- до исправления два конкурентных запроса с одинаковым `decisionId`, но разными limit/comment возвращали два HTTP 200;
- после исправления recovery повторно загружает aggregate и сравнивает нормализованные type, actor, reason, effective approved limit и currency; совместимый replay возвращает 200, другой payload — 409, в БД остаются одно решение и один snapshot.

### TEST: устаревшее e2e-ожидание номера версии

- reproducer и исправление: `apps/web/e2e/inspection.spec.ts`;
- ожидание текста `0.3` заменено проверкой пользовательского контракта `открыть раздел «Подготовка»`;
- production-код не менялся.

### CI: PostgreSQL connection reset после многократного container lifecycle

- неуспешный run: [29250990006](https://github.com/SbKots/DealerOS/actions/runs/29250990006);
- затронутые тесты: `tests/DealerOS.IntegrationTests/ReconditioningApiTests.cs`;
- исправление: `tests/DealerOS.IntegrationTests/ReconditioningPostgresFixture.cs` и `tests/DealerOS.IntegrationTests/ReconditioningApiTests.cs`;
- `tests/DealerOS.IntegrationTests/AssemblyInfo.cs` уже содержал assembly-level `DisableTestParallelization`, поэтому повторное добавление не требовалось;
- один container теперь используется всем классом, а новая БД создаётся перед каждым тестом;
- production-код, unit tests и другие integration-классы не изменены;
- fix commit: `e3affb819660584304f7cb1fce16be1272084549`;
- успешный повторный run: [29253860290](https://github.com/SbKots/DealerOS/actions/runs/29253860290).

Открытых BLOCKER, CRITICAL и MAJOR после исправлений нет.

## GitHub Actions и artifacts

Run [29253860290](https://github.com/SbKots/DealerOS/actions/runs/29253860290) на SHA `e3affb819660584304f7cb1fce16be1272084549`:

- backend — success: 26/26 unit, 21/21 integration;
- frontend — success: audit, lint, 8/8 tests, build;
- e2e — success: Compose stack и 4/4 Playwright;
- `backend-test-results`, artifact ID `8280427025`, 86,046 bytes;
- `frontend-test-results`, artifact ID `8280395024`, 844 bytes;
- `frontend-build`, artifact ID `8280395439`, 114,025 bytes;
- `playwright-report`, artifact ID `8280416622`, 209,118 bytes.

Локальные воспроизводимые результаты сохранены в игнорируемых Git каталогах:

- `TestResults/integration-fixture-run-1/integration-fixture-run-1.trx`;
- `TestResults/integration-fixture-run-2/integration-fixture-run-2.trx`;
- `apps/web/test-results/frontend-junit.xml`;
- `apps/web/playwright-report/index.html`;
- `apps/web/dist`.

## Diff hygiene и ограничения

- `.env`, keys, certificates, secrets, `bin`, `obj`, `dist`, `TestResults`, `test-results` и `playwright-report` в commit не включены;
- high-risk secret patterns в staged product diff не найдены;
- Docker volumes не удалялись при локальной проверке;
- GitHub Actions сообщает неблокирующее предупреждение о Node.js 20 внутри `actions/*@v4`, принудительно запускаемых runner на Node.js 24; это maintenance item, а не проблема продукта 0.3.

Версия предназначена только для локальной демонстрации. В 0.3 не реализованы фактическое выполнение ремонта, заказ-наряды, запчасти и склад, фактические расходы, QA, оплата подрядчиков, production secrets/observability/backup/HA и production deployment.

Report commit должен пройти отдельный CI на финальном PR HEAD до merge.
