# DealerOS Verification Report 0.2

## 1. Итог

- Вердикт: **READY WITH NOTES**
- Проверяемый режим: **A — итерация 0.2 «Осмотр автомобиля и фиксация дефектов»**
- Ветка: `codex/vehicle-inspection-0.2`
- Проверенный product commit: `dbc714b4c0690f00a2819c2e63f0ea723e3aec76`
- Исходный product commit итерации: `96a8d52f8014dc1aa4234b20a3638301047bb8a3`
- Дата: 2026-07-13, Europe/Moscow
- CI: [GitHub Actions run 29236346853](https://github.com/SbKots/DealerOS/actions/runs/29236346853), exact product SHA, `backend`, `frontend`, `e2e` — success.

Обязательный сценарий от PostgreSQL до React UI фактически пройден. Дополнительно проверены конкурентный upload одного `photoId`, photo evidence в correction, предсказуемое удаление при недоступном MinIO и точная семантика `InspectionPassed`. Проверены tenant/branch isolation, permissions, миграции на пустой БД и upgrade с 0.1, гонки start/complete, идемпотентность, аудит и read-only история. Проект воспроизведён из чистого clone на точном product SHA. Все найденные BLOCKER/CRITICAL/MAJOR закрыты. Версия готова для локальной демонстрации и product discovery, но **не готова для production**.

## 2. Проверенный scope

Обязательные функции: очередь `InStock`, start inspection, snapshot версионного шаблона, чек-лист, дефекты и Critical-блокировка, защищённые фото, complete/cancel/correction, новый статус Vehicle, история, аудит, tenant/branch isolation, optimistic concurrency, React desktop/tablet UI и Problem Details.

Сознательно отложены: заказ-наряды, факт ремонта, склад запчастей, бюджетирование, публикация, CRM, брони, сделки, AI-анализ и production hardening. Они не оценивались как обязательные для 0.2.

Источники: мастер-промпт, запрос 0.2, контрольный промпт, `README.md`, `docs/PRODUCT.md`, `docs/ARCHITECTURE.md`, `docs/ASSUMPTIONS.md`, `docs/ITERATION_0.2.md`, `docs/SECURITY.md` и фактический diff.

## 3. Матрица требований

| ID | Требование | Статус | Доказательство | Примечание |
|---|---|---|---|---|
| R01 | InStock виден в очереди доступного филиала | PASS | `InspectionApiTests`, Playwright | Query фильтрует tenant, branch и status. |
| R02 | Один активный Inspection | PASS | Concurrent integration test, `ux_inspections_active_vehicle` | Защита кодом и PostgreSQL partial unique index. |
| R03 | Версионный template snapshot | PASS | Integration с созданием v2 после complete | Старый item label и version не меняются. |
| R04 | 11 категорий и обязательные items | PASS | Unit, integration, e2e | Complete отклоняет Pending required items. |
| R05 | Дефекты и серверные Critical rules | PASS | Unit + integration | Critical принудительно даёт `RepairRequired` и `BlocksSale`. |
| R06 | Private photo upload/download | PASS | Real MinIO Testcontainers + Compose + e2e | JPEG/PNG/WebP, 8 MiB, 25 MP, decode/re-encode, authenticated stream; уникальный key на upload-attempt. |
| R07 | Неизменяемый completed result | PASS | Unit, integration, reload e2e | Изменения/удаления запрещены. |
| R08 | Correction создаёт новую ревизию | PASS | Unit + PostgreSQL/MinIO integration | Source остаётся неизменяемым Completed; новая photo metadata хранит `SourcePhotoId` и разделяет immutable object. |
| R09 | Корректный Vehicle status | PASS | Integration + e2e | `InspectionPassed` означает технически пройденный осмотр; `ReconditioningRequired` — нужна подготовка; cancel возвращает InStock. |
| R10 | Exact permissions и branch scope | PASS | 403/branch integration tests | Endpoint policy и application demand. |
| R11 | Tenant isolation API + DB + object key | PASS | Две организации, cross-tenant 404/photo 404, composite FK | `organizationId` из body не управляет scope. |
| R12 | Audit критических команд | PASS | Integration query audit table | Actor, tenant, correlation ID и event data. |
| R13 | Optimistic concurrency и safe repeat | PASS | Concurrent start/complete/photo integration | Нет двух результатов; repeat complete и повтор `photoId` для того же defect идемпотентны; другое назначение даёт 409. |
| R14 | PostgreSQL migration fresh + upgrade 0.1 | PASS | Testcontainers migration до previous release, затем latest; clean Compose | Все 5 migration применены, включая `20260713083112_InspectionPhotoReliability02`. |
| R15 | React desktop/tablet states | PASS | 5 component + 3 e2e | Loading, empty, validation, conflict, Critical, completion, read-only, next action. |
| R16 | CI и артефакты | PASS | Run 29236346853 | 3/3 jobs success; 4 artifacts. |
| R17 | ReconditioningPlan, ремонт, бронь, сделка | NOT APPLICABLE | `docs/BACKLOG.md` | Явно scope 0.3 и более поздних итераций. |
| R18 | Предсказуемое физическое удаление | PASS | PostgreSQL/MinIO integration | DB commit не маскируется 503; tenant-aware idempotent cleanup record остаётся для reconciliation. |

## 4. Результаты автоматических проверок

Чистый clone: `C:\Users\SberKot\AppData\Local\Temp\DealerOS-verify-dbc714b`, branch checkout на точном `dbc714b4c0690f00a2819c2e63f0ea723e3aec76`. После restore/build/test/Compose/e2e дерево осталось чистым.

| Проверка | Точная команда | Результат | Длительность/детали |
|---|---|---|---|
| Clone | `git clone --branch codex/vehicle-inspection-0.2 --single-branch https://github.com/SbKots/DealerOS.git C:\Users\SberKot\AppData\Local\Temp\DealerOS-verify-dbc714b` | PASS | `git rev-parse HEAD` = exact product SHA; `git status -sb` чистый. |
| Backend restore | `dotnet restore DealerOS.slnx` | PASS | 8 projects restored. |
| Format | `dotnet format DealerOS.slnx --verify-no-changes --no-restore` | PASS | Exit 0. |
| Backend build | `dotnet build DealerOS.slnx --no-restore` | PASS | 0 warnings, 0 errors; clean clone 5.48 s. |
| Backend tests | `dotnet test DealerOS.slnx --no-build` | PASS | 19 unit + 16 integration, 0 failed/skipped; clean clone integration 2 m 41 s. |
| Targeted reliability tests | `dotnet test tests\DealerOS.IntegrationTests\DealerOS.IntegrationTests.csproj --filter "FullyQualifiedName~ConcurrentPhotoUpload|FullyQualifiedName~Correction_PreservesSharedPhoto|FullyQualifiedName~RemoveDraftDefect_WhenMinioUnavailable" --logger "console;verbosity=minimal"` | PASS | 3/3 на реальных PostgreSQL и MinIO. |
| Frontend install | `npm ci` | PASS | 127 packages from lockfile; clean clone 10 s. |
| Frontend lint | `npm run lint` | PASS | oxlint exit 0. |
| Component tests | `npm run test` | PASS | 5/5, 0 skipped; clean clone 3.03 s. |
| Frontend build | `npm run build` | PASS | TypeScript + Vite production; 350.15 kB JS, 14.09 kB CSS. |
| NuGet audit | `dotnet list DealerOS.slnx package --vulnerable --include-transitive` | PASS | 0 known vulnerable packages. |
| npm audit | `npm audit --audit-level=high --registry=https://registry.npmjs.org` | PASS | 0 vulnerabilities. |
| Clean Compose | `docker compose up --build -d` | PASS | Fresh PostgreSQL/MinIO volumes; 4 services up, PostgreSQL/MinIO healthy. |
| Health | `Invoke-WebRequest -UseBasicParsing http://localhost:4173/health` | PASS | HTTP 200 `Healthy`. |
| Readiness | `Invoke-WebRequest -UseBasicParsing http://localhost:5080/health/ready` | PASS | HTTP 200 `Healthy` for PostgreSQL + object storage. |
| OpenAPI | `$spec=Invoke-RestMethod http://localhost:5080/swagger/v1/swagger.json; ($spec.paths.PSObject.Properties.Name | Where-Object { $_ -match 'inspection' }).Count` | PASS | 14 inspection/template paths. |
| Playwright | `npm run test:e2e` из `apps/web` | PASS | 3/3, 6.3 s, Chromium, desktop + tablet в clean clone. |
| Parallel e2e repeat | `docker compose restart api; npx playwright test --repeat-each=3` из `apps/web` | PASS | 9/9, 11.5 s, 6 workers в clean clone. |
| Static markers | `rg -n '^(<<<<<<<|=======|>>>>>>>)' . --glob '!apps/web/package-lock.json'` | PASS | Маркеров конфликта нет. |
| Scope debt markers | `rg -n 'TODO|FIXME|HACK' apps modules tests docs` | PASS | Маркеров нет. |
| CI | `C:\Program Files\GitHub CLI\gh.exe run view 29236346853 --repo SbKots/DealerOS --json databaseId,headSha,status,conclusion,url,jobs,createdAt,updatedAt` | PASS | backend/frontend/e2e success на exact product SHA. |

Локально сохранены результаты: `C:\Users\SberKot\Documents\MEGA-PROJECT\TestResults\unit\unit-results.trx`, `C:\Users\SberKot\Documents\MEGA-PROJECT\TestResults\integration\integration-results.trx`, `C:\Users\SberKot\Documents\MEGA-PROJECT\apps\web\TestResults\frontend-results.xml`, `C:\Users\SberKot\Documents\MEGA-PROJECT\apps\web\playwright-report\index.html` и `C:\Users\SberKot\Documents\MEGA-PROJECT\apps\web\dist`. Они исключены из Git и дополнительно опубликованы CI-артефактами.

## 5. Сквозные сценарии

### E2E-01 — полный осмотр

Предусловия: чистый Compose, demo organization/branch/user. Шаги: login → выбор филиала → создание и приёмка Vehicle → очередь → start → 11 items → Critical дефект → PNG upload/preview → complete → `ReconditioningRequired` → reload → Vehicle «Осмотры» → read-only ревизия с фото. Результат: PASS. Доказательство: `apps/web/e2e/inspection.spec.ts`, local clean clone и CI Playwright report.

### E2E-02 — приёмка 0.1 не сломана

Шаги: login → валидный Vehicle → reload → выбор строки точного VIN → accept. Результат: PASS. Доказательство: `apps/web/e2e/intake.spec.ts`, включая parallel repeat 3x.

### API-NEG-01 — изоляция, files и concurrency

Реальные PostgreSQL и MinIO: viewer 403, другой branch 403, другой tenant inspection/photo 404, body `organizationId` не влияет, поддельный/обрезанный/oversized файл отклонён, MinIO upload failure даёт 503 без photo metadata, concurrent start/complete не дублирует result. Два конкурентных upload одного `photoId` дают одну DB-запись и доступный object; retry того же defect безопасен, другой defect получает 409. Correction видит исходное photo evidence, а удаление её дефекта не удаляет object исходного Completed. MinIO delete failure после DB commit возвращает success и оставляет tenant-aware cleanup record. Результат: PASS. Доказательство: `tests/DealerOS.IntegrationTests/InspectionApiTests.cs`.

## 6. Найденные проблемы

| Severity | Найдено | Исправлено | Осталось |
|---|---:|---:|---:|
| BLOCKER | 0 | 0 | 0 |
| CRITICAL | 0 | 0 | 0 |
| MAJOR | 4 | 4 | 0 |
| MINOR | 4 | 4 | 0 |
| NOTE | 4 | 0 | 4 |

### F-0.2-001 — MAJOR — native image decoder не загружался в integration host — исправлено

- Места: `apps/api/Infrastructure/InspectionImageProcessor.cs`, `apps/api/DealerOS.Api.csproj`, `tests/DealerOS.IntegrationTests/DealerOS.IntegrationTests.csproj`, `tests/DealerOS.IntegrationTests/InspectionApiTests.cs`.
- Воспроизведение: targeted `InspectionApiTests` при photo decode в Windows testhost.
- Факт: `DllNotFoundException: libSkiaSharp`, 2 из 3 required integration scenarios не могли завершиться.
- Риск: невозможно доказать required file path; CI Linux также мог упасть.
- Исправление: explicit Win32 и Linux native assets в test host, Linux asset в API image.
- Повтор: targeted 3/3, current full integration 16/16, Windows clean clone и Ubuntu CI success.

### F-0.2-002 — MINOR — numeric undefined enum доходил до DB constraint — исправлено

- Места: `modules/Inspections/Domain/Inspection.cs`, `modules/Inspections/Domain/InspectionTemplate.cs`, `tests/DealerOS.UnitTests/InspectionDomainTests.cs`.
- Воспроизведение: `(InspectionCategory)999` и `(DefectSeverity)999` в domain command.
- Факт: первый reproducing unit test упал: exception не выброшен.
- Риск: PostgreSQL check violation вместо stable domain Problem Details.
- Исправление: `Enum.IsDefined` до мутации.
- Повтор: reproducing test PASS, current full unit 19/19.

### F-0.2-003 — MINOR — component tests не очищали DOM — исправлено

- Места: `apps/web/src/test/setup.ts`, `apps/web/src/Inspections.test.tsx`.
- Факт: после добавления component scenarios два теста видели дубли кнопок из предыдущего render.
- Риск: order-dependent false failures/positives.
- Исправление: Testing Library `cleanup()` в global `afterEach`.
- Повтор: 5/5 component tests локально, в clone и CI.

### F-0.2-004 — MINOR — intake e2e выбирал первую карточку после reload — исправлено

- Место: `apps/web/e2e/intake.spec.ts`.
- Воспроизведение: clean clone, 2 Playwright workers, inspection e2e параллельно создаёт Vehicle.
- Факт: 1/3 e2e failure, trace указал на отсутствие success для целевого VIN.
- Риск: flaky acceptance gate.
- Исправление: после reload тест явно выбирает row своего VIN.
- Повтор: 3/3, затем 9/9 на exact SHA, CI e2e success.

### F-0.2-005 — MAJOR — проигравший concurrent upload мог удалить object победителя — исправлено

- Места: `modules/Inspections/Application/InspectionService.cs`, `apps/api/Infrastructure/InspectionStore.cs`, `modules/Inspections/Application/Contracts.cs`, `tests/DealerOS.IntegrationTests/InspectionApiTests.cs`.
- Тест-воспроизводитель: `InspectionApiTests.ConcurrentPhotoUpload_IsIdempotentAndKeepsWinningObject` с реальными PostgreSQL и MinIO.
- Факт: обе попытки с одним `photoId` использовали детерминированный `objectKey`; DB loser удалял общий key, на который уже могла ссылаться успешная запись.
- Риск: тихая потеря photo evidence и PostgreSQL metadata, указывающая на отсутствующий object.
- Исправление: каждый upload-attempt получает случайный attempt ID в key; loser удаляет только свой key, сбрасывает stale tracking, перечитывает регистрацию и возвращает идемпотентный success только для того же inspection/defect. Другое назначение того же `photoId` возвращает `409 inspection_photo.id_conflict`.
- Повтор: targeted 3/3, full integration 16/16, clean clone и CI success; фотография скачивается после гонки и повторной команды.

### F-0.2-006 — MAJOR — correction теряла photo evidence исходного осмотра — исправлено

- Места: `modules/Inspections/Domain/Inspection.cs`, `modules/Inspections/Application/Contracts.cs`, `apps/api/Infrastructure/DealerOsDbContext.cs`, `apps/api/Infrastructure/InspectionStore.cs`, `apps/api/Infrastructure/Migrations/20260713083112_InspectionPhotoReliability02.cs`, `tests/DealerOS.UnitTests/InspectionDomainTests.cs`, `tests/DealerOS.IntegrationTests/InspectionApiTests.cs`.
- Тесты-воспроизводители: `InspectionDomainTests.Correction_CopiesPhotoMetadataAndReferencesImmutableSourcePhoto`, `InspectionApiTests.Correction_PreservesSharedPhotoAndSourceCompletedInspection`.
- Факт: `InspectionDefect.CopyTo` копировал поля дефекта, но оставлял collection `Photos` пустой.
- Риск: новая ревизия теряла доказательства, а пользователь видел неполный результат correction.
- Исправление: correction создаёт новую metadata с новым `Id`, tenant-aware `SourcePhotoId` и тем же immutable `ObjectKey`; бинарный файл не копируется. FK `Restrict` защищает source metadata, а физическое удаление выполняется только при отсутствии других metadata-ссылок.
- Повтор: source Completed сохраняет version/status/photo после удаления correction-дефекта; обе ревизии корректны после reload и correction complete.

### F-0.2-007 — MAJOR — MinIO delete failure возвращал ложный rollback после DB commit — исправлено

- Места: `modules/Inspections/Application/InspectionService.cs`, `modules/Inspections/Domain/InspectionObjectDeletion.cs`, `modules/Inspections/Application/Contracts.cs`, `apps/api/Infrastructure/InspectionStore.cs`, `apps/api/Infrastructure/MinioInspectionPhotoStorage.cs`, `apps/api/Infrastructure/DealerOsDbContext.cs`, `apps/api/Infrastructure/Migrations/20260713083112_InspectionPhotoReliability02.cs`, `tests/DealerOS.IntegrationTests/InspectionApiTests.cs`.
- Тест-воспроизводитель: `InspectionApiTests.RemoveDraftDefect_WhenMinioUnavailable_CommitsDbAndQueuesTenantCleanup` с остановленным реальным MinIO.
- Факт: metadata/defect уже удалялись в PostgreSQL, затем `DeleteAsync` выбрасывал 503, создавая впечатление полного отката.
- Риск: повторные действия пользователя при фактически изменённой DB и неучтённые orphan objects.
- Исправление: удаление metadata и запись `(OrganizationId,ObjectKey)` в `inspections.object_deletion_queue` сохраняются одной транзакцией; запись идемпотентна по composite PK. Storage failure логируется адаптером, API возвращает сохранённый DB-результат, cleanup record остаётся для reconciliation.
- Повтор: HTTP 200, defect/photo metadata отсутствуют, tenant cleanup row существует; full integration и CI success.

### F-0.2-008 — MINOR — `ReadyForSale` преждевременно означал полную готовность после одного осмотра — исправлено

- Места: `modules/Vehicles/Domain/VehicleStatus.cs`, `modules/Vehicles/Domain/Vehicle.cs`, `apps/web/src/App.tsx`, `docs/PRODUCT.md`, `docs/ARCHITECTURE.md`, `docs/ASSUMPTIONS.md`, `docs/ITERATION_0.2.md`, `tests/DealerOS.IntegrationTests/InspectionApiTests.cs`.
- Воспроизведение: завершение осмотра без blocking defects переводило Vehicle в `ReadyForSale`, хотя подготовка и quality gate относятся к будущему процессу.
- Риск: неверная семантика статуса блокировала бы корректный workflow 0.3.
- Исправление: значение 5 переименовано в `InspectionPassed`; это только технически пройденный осмотр. `ReadyForSale` зарезервирован за подготовкой и контролем качества.
- Повтор: integration test проверяет `InspectionPassed`; frontend types/build и документация согласованы.

## 7. Выполненные исправления

Все найденные проблемы current scope исправлены без добавления новых продуктовых функций. Тесты не ослаблялись: файлы проходят реальный decode, PostgreSQL и MinIO настоящие, Critical rules проверяются на сервере, e2e остался parallel. Новые тесты сначала падали на product predecessor `7410efb209aaa350551c7e8f9715ea8c73f0e30a`, затем прошли на `dbc714b4c0690f00a2819c2e63f0ea723e3aec76`. Каждое исправление подтверждено targeted, полным локальным, clean-clone и CI-прогоном.

## 8. Безопасность и изоляция данных

- JWT проверяется, а состояние user/permissions/branches перепроверяется в БД на каждом запросе.
- Exact permission есть в endpoint policy и application service. Viewer получает 403 прямым API запросом.
- Organization/User не берутся из client payload; branch проверяется. Store queries содержат tenant predicates, БД — composite tenant-aware FK.
- Object key формируется сервером и содержит tenant/inspection/defect/photo UUID и уникальный upload-attempt ID. Client filename не участвует в пути. Проигравшая команда не может удалить object победителя. Bucket не публичен; public/presigned URL нет.
- Correction metadata имеет tenant-aware self-FK `SourcePhotoId` и разделяет immutable object. Удаление последней ссылки ставит tenant-aware idempotent cleanup record до внешнего эффекта.
- Файл проверяется декодером, ограничен по байтам/пикселям/форматам и перекодируется без исходных metadata.
- Аудит содержит actor, tenant, event, entity, before/after и correlation ID. Токены/пароли/файлы не логируются.
- Demo secrets в Compose явно local-only; production ключ с local prefix отклоняется.

## 9. Данные и миграции

Миграция `20260712215235_VehicleInspection02` создаёт schema `inspections`, templates/items/inspections/defects/photos, индексы и check constraints. Миграция `20260713083112_InspectionPhotoReliability02` добавляет `SourcePhotoId`, tenant-aware self-FK, разрешает нескольким revision metadata ссылаться на один object и создаёт `inspections.object_deletion_queue` с composite PK `(OrganizationId,ObjectKey)`. Fresh path проверен clean Compose. Upgrade path проверен тестом: migration до `20260712181329_TenantIntegrityAndValidation`, затем старт API до latest. Модель и snapshot совпадают; `dotnet format` и build clean.

Деньги дефекта — `numeric(19,2)` + ISO currency, отрицательная сумма запрещена. Это только estimate, не plan/fact экономика. Время — `timestamptz`. `Version` — EF concurrency token; unique constraints защищают active inspection и version template. Delete cascade ограничен до internal children; tenant/Vehicle/User/Branch/Template relations — restrict.

## 10. Архитектура и качество кода

Сохранён модульный монолит. `modules/Inspections` содержит domain/application и ports; `apps/api` — EF, HTTP, MinIO и image adapters. EF entities не выходят в API DTO. Generic PATCH status нет. Vehicle transition вызывается application service в одном scoped DbContext/SaveChanges с Inspection и audit. Upload использует compensation только над уникальным attempt object. Remove использует transactional cleanup record до внешнего эффекта, поэтому DB truth не маскируется storage error.

Технический долг: автоматический worker/операторская команда reconciliation для `object_deletion_queue` в 0.2 не реализованы. Очередь tenant-aware, идемпотентна и сохраняет orphan для следующего безопасного прохода; до production нужны retry policy, метрики, alert и runbook.

## 11. Frontend и UX

Проверены login, навигация «Приёмка/Осмотры», очередь, вкладка Vehicle, прогресс, чек-лист, дефект, Critical banner, photo preview, confirm complete, read-only result и next action. Component tests покрывают loading/conflict/immutability; Playwright проходит real API. Tablet viewport 820×1180 проверен; responsive CSS также имеет mobile breakpoint, но реальное mobile device/accessibility audit не проводились.

## 12. Эксплуатационная готовность

- Docker Compose воспроизводит PostgreSQL 17, pinned MinIO, API и nginx/React. Старт из чистого clone пройден без ручной правки.
- `/health/live` и `/health/ready` есть; readiness включает PostgreSQL и object storage.
- CI выполняет audits, format, Release build, 35 backend tests, 5 frontend tests, production build, fresh Compose и 3 Playwright tests.
- Артефакты [run 29236346853](https://github.com/SbKots/DealerOS/actions/runs/29236346853#artifacts):
  - `backend-test-results`, artifact ID `8273620438`, 57,474 bytes, digest `sha256:056d437a06712cc3c7ec9082cda07297dc121574fbda763d16343da6ab1d1428`;
  - `frontend-test-results`, artifact ID `8273570370`, 661 bytes, digest `sha256:b1602a20cea38dd015d9b4e2deab57dbaeb836d68650913b3df084cbe19dc3fb`;
  - `frontend-build`, artifact ID `8273570558`, 109,260 bytes, digest `sha256:f25f52f8c667e23698f5ec9fb8e87be17e4b05cafc675ec59e8a744849ea5f5b`;
  - `playwright-report`, artifact ID `8273596849`, 202,372 bytes, digest `sha256:6e33e5bc73f62b0cfbe51f579e5fb21ec939cf9d7395edcea415539a64a28c17`.
- Production migration должна идти отдельным release step; auto-migrate и demo seed не предназначены для production.

## 13. Непроверенные пункты

1. Реальные интервью с диагностом/приёмщиком/руководителем. Риск: неверная обязательность items/фото/блокировок. Шаг: провести интервью по вопросам `docs/ASSUMPTIONS.md` до пилота.
2. Production security: IdP/MFA/TLS/secret manager, malware scanning/CDR, S3 encryption/lifecycle, penetration test. Риск: нельзя загружать реальные чувствительные файлы. Шаг: production threat model и security acceptance.
3. Backup/restore, disaster recovery, нагрузка, длительная soak и metrics/alerts. Риск: эксплуатационная неготовность. Шаг: release-candidate runbook/drills и SLO.
4. Safari/Firefox, real tablet, keyboard/screen-reader audit. Риск: UX/accessibility вне Chromium не доказаны. Шаг: cross-browser и accessibility acceptance до пилота.

## 14. Следующие действия

1. До пилота: провести discovery-интервью и утвердить шаблон, NotApplicable, Critical photo и correction permissions.
2. До пилота на реальных данных: заменить demo auth/secrets, добавить TLS, malware scanning, политику хранения и правовую проверку фото.
3. До production: backup/restore drill PostgreSQL + S3, monitoring/SLO, load/soak, HA object storage, secret rotation, deploy/rollback drill и penetration test.
4. После product acceptance 0.2 начать 0.3: черновик `ReconditioningPlan` из `RepairRequired` defects, не реализуя факт ремонта в том же изменении.

## 15. Финальное заключение

Проверяемый scope 0.2 можно считать завершённым на уровне локальной демонстрации. Его можно показывать сотрудникам автосалона на обезличенных/demo данных для проверки workflow и KPI. Реальные персональные/коммерческие данные без production security мер использовать нельзя. В production выпускать нельзя до закрытия security/operations пунктов и последующих product итераций.
