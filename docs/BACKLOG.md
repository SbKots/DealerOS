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

## Следующая итерация 0.5 — контроль качества и публикационная готовность

1. QC gate, замечания и повторные работы.
2. Переход в полную `ReadyForSale` только после успешного QC.
3. Media/content completeness и immutable listing snapshot.
4. Ручной export/publication journal без внешней интеграции.

## Позже по MVP

Подготовка и бюджет -> публикация -> лид/SLA -> тест-драйв -> предложение/скидка -> бронь с DB concurrency -> сделка/выдача -> plan/fact прибыль -> PDF документа.

## Открытые продуктовые вопросы (не блокировали 0.1)

- допустимый процесс для автомобиля без VIN и кто утверждает исключение;
- кто и на каком основании исправляет VIN;
- момент признания фактической цены закупки и источник из учётной системы;
- правила повторного поступления/возврата ранее проданного автомобиля;
- обязательный набор документов и комплектности по типу поступления.
