# DealerOS 0.8–1.0 verification и локальный MVP-аудит

## 1. Verdict

**READY — только локальный демонстрационный MVP с синтетическими данными.** Локальные обязательные проверки 0.1–1.0 зелёные. Clean clone и GitHub Actions являются оставшимися внешними merge gates и будут заполнены одним evidence-only commit. Вердикт не разрешает реальные персональные/финансовые данные и не означает production readiness.

## 2. Git baseline и checkpoints

- branch: `codex/release-train-0.8-1.0`;
- base `origin/master`: `0b9048537f4197368164cbd77d68c2b4c2c60b71`;
- 0.8: `19a05606c3e34d4e2484d4732e1435bef9b362d3` — `DealerOS 0.8: concurrent reservation and expiration workflow`;
- 0.9: `bf5da00bd4894d0879dd0195e4176972eadb99e6` — `DealerOS 0.9: deal payments documents and vehicle handover`;
- 1.0: `6560afbdbed9fcf6328cbf6f61f1f337fbaf6050` — `DealerOS 1.0: auditable vehicle profit and local MVP readiness`;
- verification fix: `942150c754e7a2c1eb5e5b83d9551bf4ce96659d` — Reservation test assertions scoped to their scenario vehicle;
- verification candidate: будет зафиксирован commit, добавляющим этот первоначальный report;
- final evidence-only PR HEAD: pending candidate CI/clean clone.

Все checkpoints являются ancestors текущего HEAD. На момент отчёта diff к base: 68 файлов; generated EF designers входят в миграции, runtime/test artifacts не входят.

## 3. Реализованный scope и ограничения

0.8: атомарная tenant-aware Reservation из exact Approved Offer, manual deposit status, extend/cancel/expire, partial unique DB guard и expiration worker. 0.9: Deal snapshot, append-only Payment/Refund, private versioned PDF, handover checklist, `Sold` и listing unpublish. 1.0: immutable/revised ProfitSnapshot, authoritative plan/fact формула, manual cost/correction, dashboard/drill-down/CSV, correlation ID, idempotent commercial seed и isolated local backup/restore.

В scope не входят acquiring/касса, юридически утверждённые формы, ЭДО/КЭП, marketplace/telephony integrations, FX, production IdP/MFA/TLS/secrets, HA/DR SLA и production deployment. PDF имеет demo/non-legal маркировку. Реальные данные запрещены.

## 4. Lifecycle 0.1–1.0 и evidence map

| Этап | Основное evidence |
|---|---|
| Intake → InStock | `VehicleIntakeDomainTests`, `VehicleIntakeApiTests`, `e2e/intake.spec.ts` |
| Inspection → required/pass | `InspectionDomainTests`, `InspectionApiTests`, `e2e/inspection.spec.ts` |
| Plan approval/revision | `ReconditioningDomainTests`, `ReconditioningApiTests`, `e2e/reconditioning.spec.ts` |
| Execution/QC/Listing/CRM/Sales | `ReleaseTrainApiTests`, component suites, `e2e/release-train.spec.ts` |
| Approved Offer → Reservation | `ReservationDomainTests`, `ReservationApiTests`, release-train e2e |
| Reservation → Deal/payment/PDF/handover/Sold | `DealDomainTests`, `DealApiTests`, release-train e2e |
| Sold → Profit/dashboard sources | `FinanceDomainTests`, расширенный `DealApiTests`, `Finance.test.tsx`, release-train e2e |
| tenant/branch/permission negatives | API integration tests всех вертикалей; Finance cross-tenant/403 assertions |
| repeat/concurrency/idempotency | PostgreSQL Reservation race, concurrent Deal create/complete, workers, payment/cost replays |

## 5. Матрица 0.8–1.0

| Requirement | Реализация/путь |
|---|---|
| Reservation concurrency/expiry | `modules/Reservations`, `apps/api/Infrastructure/ReservationStore.cs`, `ReservationExpirationWorker.cs`, migration `20260714065204_Reservation08` |
| Deal/payment/refund/handover | `modules/Deals`, `apps/api/Infrastructure/DealStore.cs`, migration `20260714071856_DealPaymentsDocuments09` |
| PDF validity/private storage | `PdfSharpDealPdfGenerator.cs`, `MinioDealDocumentStorage.cs`; PDFsharp parse/SHA/private/cross-tenant integration assertions |
| Profit formula/revisions | `modules/Finance/Domain/Finance.cs`, `FinanceStore.cs`, migration `20260714074002_VehicleProfitDashboard10` |
| Dashboard/drill-down/CSV | `FinanceService.cs`, `FinanceEndpoints.cs`, `apps/web/src/Finance.tsx` |
| Local operability | `scripts/backup-local.ps1`, `scripts/restore-verify.ps1`, correlation middleware, health checks, README/RUNBOOK |

## 6. Точные команды и результаты

Backend:

```powershell
dotnet restore DealerOS.slnx
dotnet list DealerOS.slnx package --vulnerable --include-transitive
dotnet format DealerOS.slnx --verify-no-changes --no-restore
dotnet build DealerOS.slnx -c Release --no-restore --nologo --verbosity:minimal
dotnet test tests/DealerOS.UnitTests/DealerOS.UnitTests.csproj -c Release --no-build --nologo --logger "trx;LogFileName=unit.trx" --results-directory TestResults/final-unit
dotnet test tests/DealerOS.IntegrationTests/DealerOS.IntegrationTests.csproj -c Release --no-build --nologo --logger "trx;LogFileName=integration.trx" --results-directory TestResults/final-integration
```

Результат: restore/format/Release build зелёные, 0 warnings; NuGet vulnerable transitive packages — 0; unit **62/62**; real PostgreSQL+MinIO integration **31/31**. TRX сохранены локально в ignored `TestResults/final-*` и публикуются CI artifact `backend-test-results`.

Frontend:

```powershell
cd apps/web
npm ci --registry=https://registry.npmjs.org
npm audit --audit-level=high --registry=https://registry.npmjs.org
npm run lint
npm test -- --run --reporter=default --reporter=junit --outputFile.junit=./test-results/frontend-junit.xml
npm run build
```

Результат: npm vulnerabilities — 0; lint/build зелёные; component **31/31**. JUnit и `dist` находятся только в ignored outputs и публикуются CI artifacts.

Runtime/E2E:

```powershell
docker compose up --build -d
npx playwright test
docker compose restart api
npx playwright test e2e/release-train.spec.ts --workers=1
docker compose restart api
npx playwright test e2e/release-train.spec.ts --workers=1
```

Результат: Compose build, PostgreSQL/MinIO health, `/health/live`, `/health/ready`, web `/health` — success/HTTP 200; полный Playwright **5/5**; два дополнительных независимых critical runs **1/1 + 1/1**. API restart сопровождается readiness polling, не arbitrary sleep. Browser console/pageerror assertion чистая.

## 7. Migrations/data evidence

- Fresh empty PostgreSQL 17 tmpfs: все **13** migrations, включая 0.8/0.9/1.0, применились штатным `dotnet-ef database update`.
- Upgrade: `dotnet-ef database update 20260713183257_SalesVisitsOffers07` дал точную схему 0.7 и 10 migrations; затем latest применил только 0.8–1.0 и дал 13.
- В fresh и upgraded схеме: 7 Finance check constraints и 56 indexes в `reservations/deals/finance`; composite tenant FK и numeric precision присутствуют в generated migration/model snapshot.
- Demo seed после последовательных API restarts сохранил одинаковые counts `Vehicle,Offer,Reservation,Deal = 3,3,2,1` для VIN prefix `WVWZZZ1JZXW70000`; дублей нет.
- Миграции 0.1–0.7 не изменялись.

## 8. Concurrency/idempotency/isolation

`ReservationApiTests.ConcurrentReservation_*`: один HTTP 200, один 409, одна бронь сценарного Vehicle. `DealApiTests.ReservationToSold_*`: concurrent create и complete не дают HTTP 500, имеют один success/один 409. Command/payment/document/manual-cost IDs не создают второй effect; conflicting payload получает 409. Worker expire повторно возвращает 0. North tenant получает 404, viewer/export получает 403, branch scope проверяется application/store и composite FK.

## 9. Payment reconciliation и profit drill-down

`GrossRevenue = completed Deal total`; `NetRevenue = GrossRevenue - RefundedTotal`; `TotalCost = Vehicle purchase + completed Operations actual + active non-duplicated manual cost`; `ActualProfit = NetRevenue - TotalCost`. Payment ledger не прибавляется к revenue, discount не вычитается второй раз. Unit tests покрывают formula/bankers rounding/no-double-counting/mixed currency/negative profit. Integration example: initial 400000 RUB profit; corrected manual cost 20000 и completed refund 50000 создали четыре immutable revisions и итог 330000. Snapshot JSON перечисляет Deal/approved snapshot, payment/refund IDs, work/material totals и active manual-cost IDs; dashboard total совпал с drill-down.

## 10. PDF evidence

Два типа PDF генерируются PDFsharp 6.2.4, начинаются `%PDF-`, реально открываются PDFsharp reader, имеют page count > 0 и совпадающий SHA-256. Binary хранится в private MinIO; anonymous download — 401, другой tenant — 404. Metadata содержит template/revision/source link; повторная ревизия требует reason. NuGet license/compatibility решение закреплено ADR 0003.

## 11. Backup/restore/correlation/health

Точная README-команда `$backup = .\scripts\backup-local.ps1; .\scripts\restore-verify.ps1 -BackupPath $backup` прошла. Isolated randomly named PostgreSQL/MinIO containers и temporary volume использовали tmpfs, постоянные Compose volumes не монтировались. Restore: migrations 13, vehicles 59, deals 3, ProfitSnapshots 1; private object `organizations/.../20d3c672009e49c5a40f0e40fa296260.png` совпал по SHA-256 `7ce78e46a0340f336b19b46c5ae401650bd9ef87073dc096b22d8f523c8e9ca3`. Temporary containers/volume удалены.

Safe `X-Correlation-ID=finance-integration-correlation-001` вернулся без изменения, вошёл в audit/request scope. Небезопасный/длинный ID заменяется server GUID. Live/ready проверяют process и PostgreSQL/MinIO.

## 12. Найденные defects

| Severity | Reproducer | Причина и fix | Пути | Retest |
|---|---|---|---|---|
| MAJOR | конкурентные Reservation/Deal integration tests | DB unique/concurrency обработка и idempotent reload реализованы в 0.8/0.9 | `ReservationStore.cs`, `ReservationService.cs`, `DealStore.cs`, `DealService.cs`, соответствующие tests | green |
| MAJOR | completed refund/cost correction должны менять прибыль, не старый snapshot | добавлены immutable revision link/hash и atomic capture | `FinanceStore.cs`, `FinanceService.cs`, `DealService.cs`, `DealApiTests.cs` | green |
| MAJOR | initial Windows backup копировал длинные object paths | MinIO objects архивируются внутри temporary Docker volume; host получает tar + manifest | `scripts/backup-local.ps1`, `scripts/restore-verify.ps1` | full drill green |
| MINOR/test reliability | full integration: expected Reservation total 0/1, demo seed дал 2/3 | assertion ограничен Vehicle конкретного сценария; production не менялся | `tests/DealerOS.IntegrationTests/ReservationApiTests.cs`, fix `942150c…` | targeted 2/2; full 31/31 |
| EXPECTED control | immediate `repeat-each=2` после full e2e получил 429 | login limiter 10/IP/minute оставлен неизменным; независимые runs выполнялись после controlled API restart/readiness | `apps/api/Program.cs`, `apps/web/e2e/release-train.spec.ts` | 1/1 + 1/1 |

Открытых BLOCKER/CRITICAL/относящихся к scope MAJOR нет.

## 13. Clean clone и evidence-only rule

Pending на verification candidate. Будут зафиксированы exact candidate SHA, temp clone path/удаление, команды строго README и proof, что `candidate..final` меняет только этот report.

## 14. GitHub Actions/artifacts

Pending push/PR. Workflow обязан дать backend/frontend/e2e SUCCESS. Ожидаемые artifacts: `backend-test-results`, `frontend-test-results`, `frontend-build`, `playwright-report`. Candidate и final evidence-only run будут указаны ссылками.

## 15. Hygiene/security review

`git diff --check` clean. Проверяются tracked+untracked paths и high-risk patterns. В diff нет `.env`, secrets, certificates/keys, `bin`, `obj`, `dist`, `TestResults`, `test-results`, Playwright report, dump/tar/backup. Audit/log payload не содержит customer contact, document/payment body или token. Локальные credentials в Compose явно demo-only и существовали как воспроизводимая конфигурация.

## 16. Однозначные ответы

- Локальный MVP 1.0 завершён: **да, при зелёных pending external gates**.
- Можно показывать сотрудникам автосалона: **да, локально и только на синтетических demo data**.
- Можно использовать реальные данные: **нет**.
- Production ready: **нет**.
- До pilot/production: production IdP/MFA, TLS/secure headers, secret manager/rotation, encryption/retention, malware scanning, off-site backup с RPO/RTO, monitoring/alerting/HA, юридическая и privacy review, реальные accounting/bank/document integrations и нагрузочная проверка.
