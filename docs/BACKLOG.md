# Backlog

## Готово в коде — итерация 0.7

- календарь визитов, reschedule/arrival/result и безопасный test-drive check-out/check-in;
- PostgreSQL-защита от пересечения активных слотов автомобиля и ответственного менеджера;
- серверный preview Offer из публичной цены, прозрачных строк, скидки и подтверждённой себестоимости;
- контроль валюты, лимита скидки, минимальной маржи и запрет self-approval;
- auto approval в пределах полномочий и отдельные idempotent manager decisions;
- неизменяемый Approved Offer snapshot, история и новая ревизия;
- permissions, audit, optimistic concurrency, tenant/branch isolation, unit/integration/component tests.

Следующий scope: Reservation/Deal, оплата, договоры, выдача и итоговая экономика продажи. Эти функции намеренно не включены в 0.7.

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

## Готово в коде — итерация 0.6

- tenant-aware Customer, нормализация контактов, consent evidence и поиск дублей;
- privileged preview/merge с обязательной причиной и безопасным аудитом;
- Lead source/vehicle-or-criteria/manager/status и неизменяемый activity timeline;
- ручное и детерминированное round-robin назначение;
- настраиваемый first-response SLA, очередь просрочек и первое осмысленное действие;
- idempotency, optimistic concurrency, permissions и cross-tenant negative tests.

## Следующая продуктовая граница

1. Временная бронь с конкурентной защитой и истечением.
2. Deal snapshot, оплата, договоры и выдача.
3. Итоговая экономика plan/fact по завершённой продаже.

## Позже по MVP

Подготовка и бюджет -> публикация -> лид/SLA -> тест-драйв -> предложение/скидка -> бронь с DB concurrency -> сделка/выдача -> plan/fact прибыль -> PDF документа.

## Открытые продуктовые вопросы (не блокировали 0.1)

- допустимый процесс для автомобиля без VIN и кто утверждает исключение;
- кто и на каком основании исправляет VIN;
- момент признания фактической цены закупки и источник из учётной системы;
- правила повторного поступления/возврата ранее проданного автомобиля;
- обязательный набор документов и комплектности по типу поступления.
