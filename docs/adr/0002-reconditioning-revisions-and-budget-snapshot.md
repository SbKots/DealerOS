# ADR 0002: ревизии плана и immutable budget snapshot

## Статус

Принято для итерации 0.3.

## Решение

`ReconditioningPlan` — aggregate root в отдельном модуле и PostgreSQL schema. Редактируются только Draft/ChangesRequested. Approval создаёт append-only decision, status history и один immutable budget snapshot. Изменение Approved создаёт новый Draft с `RevisesPlanId`; старый plan и snapshot не обновляются.

Один активный plan на автомобиль обеспечивается partial unique index по `(OrganizationId, VehicleId)` для статусов 1/2/3. Все связи с Vehicle, Branch, Inspection, Defect и User используют tenant-aware составные FK. Решение руководителя имеет client-generated decision ID; aggregate и DB constraint обеспечивают идемпотентность, а `Version` разрешает только один результат разных конкурентных команд.

## Последствия

- аудит и сравнение ревизий не зависят от реконструкции изменяемых строк;
- approved budget пригоден для будущего plan/fact 0.4;
- storage растёт append-only, но объём планов салона невелик;
- correction исходного inspection после создания plan не меняет уже сохранённые work snapshots;
- удаления исторических планов в API нет.
