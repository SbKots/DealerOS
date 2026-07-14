# ADR 0004: immutable ProfitSnapshot и изолированный local restore

Статус: принято, 2026-07-14.

## Решение

Finance является read/derivation boundary над authoritative Vehicle, Deal и Operations, а не второй бухгалтерией. Completed Deal создаёт `DealerOS.Profit.v1` snapshot; refund и manual-cost correction добавляют ревизию со ссылкой на предыдущую. Payments не признаются revenue второй раз, manual costs не включают Purchase/Reconditioning, mixed currency блокируется до появления явного FX.

Dashboard читает последнюю ревизию каждой сделки и группирует currency. Drill-down сохраняет source IDs/amounts JSON и SHA-256. Это делает цифру воспроизводимой и оставляет исходные закрытые записи неизменяемыми.

Local recovery состоит из PostgreSQL custom dump, private MinIO object copy и hash manifest. Verification выполняется только в случайно именованных temporary containers на `tmpfs`; постоянные Compose volumes не монтируются. Это доказательство локальной восстанавливаемости, но не production backup policy.

## Последствия

Корректировки требуют append-only revision и немного увеличивают объём данных. Snapshot фиксирует известную на момент события картину; новые типы authoritative costs потребуют новой версии формулы. До production необходимы encrypted off-site backups, RPO/RTO, secret management, мониторинг и регулярный controlled restore.
