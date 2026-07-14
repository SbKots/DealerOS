# Архитектура

## Дополнение 0.8: Reservations

Модуль `Reservations` продолжает точный `ApprovedOfferSnapshot`, не копируя владение Offer из `Sales`. Создание брони и переход Vehicle `ReadyForSale → Reserved` сохраняются одной транзакцией. Partial unique index `(OrganizationId, VehicleId) WHERE Status IN (PendingDeposit, Active)` является окончательной защитой от двух клиентов; optimistic token обеспечивает согласованность extend/deposit/cancel/expire. Повтор create/command ID с тем же actor и payload идемпотентен, другой payload возвращает `409`.

Expiration worker использует существующий application-worker pattern и `TimeProvider`: он tenant-aware выбирает только просроченные активные брони, идемпотентно закрывает их и освобождает Vehicle только в той же транзакции. `Reserved` блокирует новые Visit/TestDrive/Offer, потому что Sales принимает только `ReadyForSale`. Предоплата 0.8 является явно ручным demo-фактом; неизменяемый Payment ledger принадлежит checkpoint 0.9.

## Дополнение 0.7: Sales

Модуль `Sales` владеет агрегатами `Visit` и `SalesOffer`, их историями, решениями и `ApprovedOfferSnapshot`. Application service читает проверенные проекции CRM, Vehicle, Listing и Operations через порт, но изменяет только таблицы схемы `sales`. PostgreSQL composite FK сохраняют tenant integrity, optimistic tokens защищают команды, а GiST exclusion constraints атомарно запрещают пересечение активных слотов менеджера и test-drive автомобиля. Публичная цена и подтверждённая себестоимость копируются в Offer как финансовый snapshot; последующие изменения источников не переписывают утверждённое предложение.

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
- `CRM`: клиент, согласия и дедупликация; лид, назначение, SLA первого ответа, activity timeline и явные lifecycle-команды.
- `Sales`: Visit, SalesOffer и ApprovedOfferSnapshot; серверные суммы, approval и неизменяемый коммерческий snapshot.
- `Reservations`: временная бронь Approved Offer, ручной статус предоплаты, истечение и конкурентная блокировка Vehicle.
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

Execution Completed -- QC Pass --> Vehicle ReadyForSale
Execution Completed -- QC ReworkRequired --> selected work ReturnedForRework --> new QC revision
Listing Draft -- ready(required media + cover + content) --> Listing Ready
Channel Draft -- manual export --> Exported -- explicit confirmation --> Published -- unpublish --> Unpublished

Lead New -- assign --> Assigned -- meaningful contact --> FirstContact -- qualify --> Qualified
Lead New/Assigned/FirstContact/Qualified -- close(reason) --> Lost/Spam/Duplicate/Deferred
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

## Контроль качества и listing

`QualityCheck` — отдельная ревизия над завершённым execution с actor/time и JSON checklist snapshot. Pass запрещён при блокирующих замечаниях; rework ссылается на конкретные work order/defect, переоткрывает только выбранные работы и не меняет предыдущую QC-ревизию. Только отдельная команда Pass независимого контролёра переводит `ReconditioningRequired -> ReadyForSale` и пишет status history/audit в одной транзакции.

`VehicleMedia` хранит tenant/branch/vehicle metadata приватного объекта. Pipeline повторно использует decode/re-encode и лимиты Inspections; каждая upload attempt имеет уникальный object key, а конфликт удаляет только собственный объект. Категории: `Exterior`, `Interior`, `DamageHistory`, `DocumentsInternal`; внутренние документы не могут быть cover и не входят в публичный snapshot.

`ListingContent` хранит snapshot характеристик, контента, цены, template/version и упорядоченных media. `Ready` неизменяем, требует Exterior/Interior/DamageHistory и ровно одну cover. Manual export создаёт только `Exported`; `Published` появляется исключительно отдельной командой после фактического подтверждения сотрудником. Command ID, optimistic concurrency, history и audit защищают историю цены, контента и публикаций.

## CRM и SLA первого ответа

`Customer` принадлежит organization и филиалу создания, хранит только нормализованные phone/email, канал и доказательства согласия. Поиск и duplicate warning всегда ограничены tenant и доступными филиалами. Merge не удаляет источник: он требует отдельного permission, предварительного просмотра и причины, сохраняет связь с целевой записью, переводит лиды и пишет аудит без контактных данных.

`Lead` фиксирует source, branch, customer, автомобиль либо критерии поиска и неизменяемый `FirstResponseDueAt`, вычисленный из tenant-настройки. Ручное и round-robin назначение — отдельные идемпотентные команды; round-robin исключает неактивных/недоступных пользователей и детерминированно выбирает минимальную активную нагрузку. `FirstResponseAt` устанавливается один раз только осмысленной activity. Повтор command ID с тем же payload безопасен, с другим — конфликт; optimistic concurrency и unique history constraint не допускают двойного результата.

Закрытые activity и финальные lead statuses неизменяемы. Сводка activity ограничена и не попадает в audit payload; application logs не содержат телефон/email. UI показывает SLA по `TimeProvider`-совместимой серверной проекции, но браузер не решает бизнес-правила.

## Данные

Денежная сумма — `decimal(19,2)` + ISO code с банковским округлением. Время — `DateTimeOffset`/`timestamptz`. Check constraints защищают VIN, положительную закупочную цену, допустимые статусы и согласованность состояния приёмки. Значимые записи не имеют delete endpoint. `Version` — optimistic concurrency token. Запросы реестра используют server-side projection и tenant/branch index; пагинация будет добавлена до объёма пилотных данных.

## Миграции

Development/Compose может применять миграции при старте. Production: backup -> отдельный `dotnet ef database update` job -> запуск совместимой версии приложения -> smoke check `/health`. Initial migration только создаёт схемы/таблицы и откатывается `database update 0`; будущие destructive migrations требуют expand/contract ADR.
