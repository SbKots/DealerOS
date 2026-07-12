# DealerOS 0.2 — осмотр автомобиля и фиксация дефектов

## Цель

Дать диагносту единый доказуемый процесс от очереди принятого автомобиля до неизменяемого результата осмотра, дефектов, закрытых фотографий и решения о необходимости предпродажной подготовки.

## Вертикальный срез

`InStock vehicle -> inspection queue -> start -> checklist -> defects -> private photos -> complete -> ReconditioningRequired/ReadyForSale -> read-only result`.

## Архитектурный план

- новый модуль `Inspections` с domain/application слоями;
- PostgreSQL schema `inspections` для templates, snapshots, inspections, defects и photo metadata;
- tenant-aware composite keys/FK и partial unique active-inspection constraint;
- private S3-compatible storage adapter и MinIO в local Compose/Testcontainers;
- command endpoints без generic PATCH;
- расширение vehicle state machine и истории статусов;
- React queue и inspection workspace для desktop/tablet;
- audit всех command operations и Problem Details для validation/conflict/storage failures.

## Критерии приёмки

- полный UI e2e с фотографией проходит после reload;
- completed inspection нельзя изменить/очистить;
- tenant/branch/permission IDOR закрыты на API и DB уровнях;
- invalid/oversized/spoofed image отклоняется до появления photo metadata;
- MinIO failure не оставляет подтверждённую фотографию;
- concurrent start/complete не создаёт дубли;
- fresh DB и upgrade с 0.1 проходят миграции;
- clean Compose и GitHub Actions зелёные;
- создан `DEALEROS_VERIFICATION_REPORT_0.2.md`.

## Не входит

Заказ-наряды, бюджетирование, выполнение ремонта, запчасти, публикация, CRM, бронирования, сделки и AI-анализ фотографий.
