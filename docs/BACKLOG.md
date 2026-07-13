# Backlog

## Готово — итерация 0.1

- discovery, scope, KPI, допущения и риски;
- modular monolith skeleton, Compose и CI;
- JWT demo auth, permissions, organization/branch scope;
- цифровой паспорт и `IntakeDraft -> InStock`;
- PostgreSQL migration, VIN constraint, status history и audit;
- React форма/карточка/реестр;
- unit, integration и e2e сценарии; demo data и документация.

## Готово в коде — итерация 0.2

- очередь, версионный чек-лист и один активный осмотр;
- дефекты, Critical-блокировка, MinIO-фото и неизменяемый результат;
- permission/tenant/branch isolation, optimistic concurrency, audit, integration и Playwright e2e.

## Готово в коде — итерация 0.3

- очередь `ReconditioningRequired` и автоматический перенос обязательных дефектов;
- редактор работ, labor/parts/currency/deadline/executor и групповой бюджет;
- submit, approval queue, approve/reject/request-changes/cancel отдельными командами;
- независимое согласование, idempotent/concurrent decision, immutable budget snapshot;
- новая ревизия утверждённого плана, сравнение и read-only история;
- tenant-aware constraints, optimistic concurrency, audit, PostgreSQL/component/Playwright tests.

## Готово в коде — итерация 0.4

- execution из неизменяемого утверждённого snapshot и заказ-работы;
- фактические labor/material/external расходы и возвраты материалов;
- блокировка, сроки, уведомления, подрядчики и settlement status;
- план/лимит/факт/variance и отдельное идемпотентное решение о перерасходе;
- tenant-aware constraints, optimistic concurrency, аудит и PostgreSQL/component tests.

## Готово в коде — итерация 0.5

- QC gate, замечания конкретным работам и неизменяемые повторные попытки;
- полная `ReadyForSale` только после успешного независимого QC;
- безопасные media, обязательные категории, cover/order и Content Pack;
- immutable Listing Ready snapshot и manual JSON export;
- ручной journal Draft/Exported/Published/Failed/Unpublished без ложной интеграции.

## Следующая итерация 0.6 — Customer и Lead

1. Tenant-aware Customer и дедупликация контактов.
2. Lead source/vehicle/channel/owner/status и activity timeline.
3. First-response SLA, очередь и просрочки.
4. Безопасное merge дублей с аудитом.

## Позже по MVP

Подготовка и бюджет -> публикация -> лид/SLA -> тест-драйв -> предложение/скидка -> бронь с DB concurrency -> сделка/выдача -> plan/fact прибыль -> PDF документа.

## Открытые продуктовые вопросы (не блокировали 0.1)

- допустимый процесс для автомобиля без VIN и кто утверждает исключение;
- кто и на каком основании исправляет VIN;
- момент признания фактической цены закупки и источник из учётной системы;
- правила повторного поступления/возврата ранее проданного автомобиля;
- обязательный набор документов и комплектности по типу поступления.
