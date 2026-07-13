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
- `Reconditioning`: агрегат плана, работы, исключения обязательных дефектов, решения, бюджетные snapshots и ревизии.
- `Operations`: исполнение утверждённого snapshot, заказ-работы, материалы, фактические расходы, перерасход, сроки и состояние расчёта с подрядчиком.
- `SharedKernel`: только стабильные малые понятия и типы ошибок.
- `apps/api/Infrastructure`: EF mappings по схемам `identity`, `organizations`, `vehicles`, `audit`; это адаптер, а не место бизнес-правил.

Прямое изменение чужих таблиц из модулей запрещено. Проекции чтения могут соединять таблицы через инфраструктурный query adapter. Новые внешние эффекты будут публиковаться через transactional outbox; в текущем срезе внешних эффектов нет.

## Tenant isolation и безопасность

JWT содержит `org_id`, `branch_id`, `permission`; request body не содержит OrganizationId/UserId. При каждом защищённом запросе JWT сверяется с активностью пользователя и актуальными permissions/branch access в БД, поэтому блокировка и отзыв доступа прекращают старую сессию. Endpoint policy даёт первый барьер, application service повторно проверяет permission и branch scope, store добавляет OrganizationId/branch predicates. Составные tenant-aware FK запрещают межорганизационные связи, а unique index предотвращает дубли VIN между конкурентными запросами внутри tenant и не конфликтует с другим tenant.

## Состояния первого среза

```text
IntakeDraft -- accept-to-stock (обязательные данные + право + доступ к филиалу) --> InStock
InStock -- start inspection --> InspectionInProgress
InspectionInProgress -- complete(no blocking defects) --> InspectionPassed
InspectionInProgress -- complete(repair/blocking defects) --> ReconditioningRequired
InspectionInProgress -- cancel --> InStock

Reconditioning Draft -- submit --> Submitted
Submitted -- request-changes --> ChangesRequested -- submit --> Submitted
Submitted -- approve --> Approved
Submitted -- reject --> Rejected
Draft/Submitted/ChangesRequested -- cancel --> Cancelled
Approved -- create-revision --> Draft(revision + 1)

Execution Draft -- start --> InProgress -- complete(all mandatory work + budget decision) --> Completed
Work Scheduled -- start --> InProgress -- block/resume --> Blocked/InProgress
Work InProgress -- complete --> Completed -- return-for-rework --> ReturnedForRework
```

`InspectionPassed` означает только техническое прохождение осмотра без дефектов, требующих подготовки или блокирующих продажу. Это не полная готовность к продаже: будущий `ReadyForSale` может быть установлен только после подготовки и контроля качества в следующем процессе. Повторный переход запрещён доменом. Универсального PATCH статуса нет. Каждое создание/принятие создаёт status history и audit event в одном `SaveChanges`.

## Осмотры и файлы

Partial unique index в PostgreSQL разрешает один Draft/InProgress на автомобиль. `Version` защищает команды; повтор complete идемпотентен. Завершённая ревизия не изменяется; correction создаёт новый Inspection со ссылкой на источник.

Файл до 8 МБ декодируется SkiaSharp, ограничивается 25 MP, перекодируется в JPEG/PNG/WebP и теряет исходные metadata. Каждая попытка upload получает отдельный server-generated object key с tenant prefix и случайным attempt ID. При DB conflict проигравшая команда удаляет только свой object, повторно читает регистрацию `photoId` и возвращает идемпотентный результат только для того же inspection/defect; другое назначение даёт `409`.

Photo metadata correction-ревизии получает новый `Id`, сохраняет `SourcePhotoId` и ссылается на тот же неизменяемый object без копирования бинарного файла. Физическое удаление разрешено только при отсутствии metadata-ссылок. Удаление дефекта атомарно с PostgreSQL ставит tenant-aware идемпотентную запись в `inspections.object_deletion_queue`; сбой MinIO оставляет эту запись для reconciliation и не превращает уже сохранённое DB-удаление в ложный rollback. Bucket приватен; скачивание идёт через permission-checked API, без public/presigned URL.

## План подготовки и согласование

`ReconditioningPlan` создаётся только из Completed inspection автомобиля в `ReconditioningRequired`. Каждый `RepairRequired` defect превращается в обязательную работу со snapshot исходного описания и tenant-aware FK к дефекту. Partial unique index допускает один активный `Draft/Submitted/ChangesRequested` на автомобиль. Изменение состава, денег и статуса выполняется отдельными командами; универсального PATCH статуса нет.

Работы хранят labor и parts как `decimal(19,2) + currency`. Read model группирует разные валюты, а submit требует одну валюту, поэтому система никогда не складывает их молча. Удаление последней обязательной работы по дефекту требует причины и создаёт `defect_omission`. Approved создаёт отдельный неизменяемый `budget_snapshot` с плановой суммой и одобренным лимитом. Повтор decision ID идемпотентен; другой payload с тем же ID конфликтует. `Version` и составной unique constraint гарантируют один результат конкурентного согласования.

Approved не редактируется. Новая ревизия копирует работы/обоснованные исключения в новый Draft, сохраняет ссылку `RevisesPlanId` и проходит повторное согласование. Исходный план, решение и snapshot остаются неизменными. Настройка организации `RequireIndependentReconditioningApproval` запрещает автору согласовать собственный план.

## Исполнение подготовки

`ReconditioningExecution` создаётся только из точного immutable `ApprovedBudgetSnapshot`; tenant-aware unique constraint разрешает одно исполнение snapshot. Заказ-работы получают snapshot состава плана и дальше изменяются отдельными командами, защищёнными `Version`. Фактические labor, material и external суммы хранятся как `decimal(19,2) + currency`; расход и возврат материала — идемпотентные движения с отдельным command ID.

Завершение требует исполнения всех обязательных работ, урегулированного внешнего расчёта и отдельного решения при превышении лимита. Решение о перерасходе идемпотентно по decision ID, не может менять валюту и при включённом независимом согласовании недоступно автору execution. Фоновый worker с отложенным первым циклом создаёт tenant-aware дедуплицированные уведомления `DueSoon/Overdue`; ручная команда оставлена для демонстрации и детерминированных тестов.

## Данные

Денежная сумма — `decimal(19,2)` + ISO code с банковским округлением. Время — `DateTimeOffset`/`timestamptz`. Check constraints защищают VIN, положительную закупочную цену, допустимые статусы и согласованность состояния приёмки. Значимые записи не имеют delete endpoint. `Version` — optimistic concurrency token. Запросы реестра используют server-side projection и tenant/branch index; пагинация будет добавлена до объёма пилотных данных.

## Миграции

Development/Compose может применять миграции при старте. Production: backup -> отдельный `dotnet ef database update` job -> запуск совместимой версии приложения -> smoke check `/health`. Initial migration только создаёт схемы/таблицы и откатывается `database update 0`; будущие destructive migrations требуют expand/contract ADR.
