# Итерация 0.3 — план предпродажной подготовки и бюджет

## Вертикальная ценность

Сотрудник превращает обязательные дефекты завершённого осмотра в проверяемый план работ и бюджет. Руководитель принимает отдельное серверное решение, после которого состав и approved snapshot неизменяемы. Изменение оформляется новой ревизией.

## Реализовано

- очередь автомобилей `ReconditioningRequired` и последнего плана;
- автоматические mandatory works из `RepairRequired` defects;
- add/update/remove work, явная причина исключения обязательного дефекта;
- labor/parts, ISO currency, executor, priority, category, срок и комментарий;
- grouped budget и запрет mixed-currency submit;
- `Draft → Submitted → Approved/Rejected/ChangesRequested`, `Cancelled`;
- independent approval, idempotent decision ID и optimistic concurrency;
- immutable budget snapshot и revision chain;
- vehicle plan history, approval queue, read-only Approved и revision comparison;
- PostgreSQL migration, tenant-aware FK/check/partial unique constraints, audit и demo seed.

## Команды API

- `POST /api/vehicles/{vehicleId}/reconditioning-plans`;
- `POST|PUT /api/reconditioning-plans/{id}/works...` и отдельная `/remove`;
- `POST /submit`, `/approve`, `/reject`, `/request-changes`, `/cancel`;
- `POST /revisions`;
- GET очередей, detail и истории автомобиля.

Универсального status PATCH нет.

## Не входит

Факт ремонта, заказ-наряды, запчасти, склад, фактические расходы, QA, оплаты подрядчиков, объявления, CRM, бронирования и сделки относятся к 0.4+.
