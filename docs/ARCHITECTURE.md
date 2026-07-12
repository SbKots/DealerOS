# Архитектура

## Стиль

Модульный монолит в monorepo. Модули владеют доменом и публичными application-контрактами; `apps/api` — composition root и инфраструктурные адаптеры. PostgreSQL атомарно сохраняет доменное состояние, историю и аудит; MinIO добавлен как S3-compatible object storage для фото. Redis, брокер и Kubernetes не добавлены: текущему срезу они не дают измеримой пользы.

```text
React/Vite -> ASP.NET Core command endpoints -> application services -> aggregates
                       |                              |                 |
                       |                              +-> ports --------+
                       |                                    |
                       +-> authenticated photo stream    EF/PostgreSQL + private MinIO
```

## Модули и владение

- `IdentityAccess`: пользователи, permissions, branch access; в дальнейшем роли/сессии/MFA.
- `Organizations`: организации и филиалы.
- `Vehicles`: VIN, Money usage, агрегат Vehicle, переходы и use cases поступления.
- `Inspections`: версионные шаблоны, агрегат Inspection, пункты, дефекты, метаданные фото и команды lifecycle.
- `SharedKernel`: только стабильные малые понятия и типы ошибок.
- `apps/api/Infrastructure`: EF mappings по схемам `identity`, `organizations`, `vehicles`, `audit`; это адаптер, а не место бизнес-правил.

Прямое изменение чужих таблиц из модулей запрещено. Проекции чтения могут соединять таблицы через инфраструктурный query adapter. Новые внешние эффекты будут публиковаться через transactional outbox; в текущем срезе внешних эффектов нет.

## Tenant isolation и безопасность

JWT содержит `org_id`, `branch_id`, `permission`; request body не содержит OrganizationId/UserId. При каждом защищённом запросе JWT сверяется с активностью пользователя и актуальными permissions/branch access в БД, поэтому блокировка и отзыв доступа прекращают старую сессию. Endpoint policy даёт первый барьер, application service повторно проверяет permission и branch scope, store добавляет OrganizationId/branch predicates. Составные tenant-aware FK запрещают межорганизационные связи, а unique index предотвращает дубли VIN между конкурентными запросами внутри tenant и не конфликтует с другим tenant.

## Состояния первого среза

```text
IntakeDraft -- accept-to-stock (обязательные данные + право + доступ к филиалу) --> InStock
InStock -- start inspection --> InspectionInProgress
InspectionInProgress -- complete(no blocking defects) --> ReadyForSale
InspectionInProgress -- complete(repair/blocking defects) --> ReconditioningRequired
InspectionInProgress -- cancel --> InStock
```

Повторный переход запрещён доменом. Универсального PATCH статуса нет. Каждое создание/принятие создаёт status history и audit event в одном `SaveChanges`.

## Осмотры и файлы

Partial unique index в PostgreSQL разрешает один Draft/InProgress на автомобиль. `Version` защищает команды; повтор complete идемпотентен. Завершённая ревизия не изменяется; correction создаёт новый Inspection со ссылкой на источник.

Файл до 8 МБ декодируется SkiaSharp, ограничивается 25 MP, перекодируется в JPEG/PNG/WebP и теряет исходные metadata. Ключ генерирует сервер с tenant prefix. Bucket приватен; скачивание идёт через permission-checked API, без public/presigned URL. Если запись метаданных не удалась, object удаляется компенсирующей операцией; нависшие multipart uploads не используются.

## Данные

Денежная сумма — `decimal(19,2)` + ISO code с банковским округлением. Время — `DateTimeOffset`/`timestamptz`. Check constraints защищают VIN, положительную закупочную цену, допустимые статусы и согласованность состояния приёмки. Значимые записи не имеют delete endpoint. `Version` — optimistic concurrency token. Запросы реестра используют server-side projection и tenant/branch index; пагинация будет добавлена до объёма пилотных данных.

## Миграции

Development/Compose может применять миграции при старте. Production: backup -> отдельный `dotnet ef database update` job -> запуск совместимой версии приложения -> smoke check `/health`. Initial migration только создаёт схемы/таблицы и откатывается `database update 0`; будущие destructive migrations требуют expand/contract ADR.
