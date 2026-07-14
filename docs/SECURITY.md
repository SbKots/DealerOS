# Security baseline

- Reservation не принимает tenant/branch/customer/vehicle из доверенного client context: они выводятся из exact Approved Offer snapshot. Активную бронь атомарно защищает tenant-aware partial unique index; race loser получает контролируемый 409. Manual deposit хранит только безопасную ссылку без карты/банковских реквизитов. Expiration worker освобождает Vehicle транзакционно и не может снять конкурентно преобразованную бронь.

- Sales API не принимает tenant из запроса и повторно проверяет branch, Customer, Qualified Lead, Vehicle, Listing, execution и ответственного сотрудника. Composite tenant FK не позволяют связать записи разных организаций прямой записью в БД.
- Для test drive хранится только boolean подтверждения проверки водительских документов. Сканы и полный номер документа отсутствуют в HTTP-контрактах, домене, БД, audit и логах.
- Offer принимает только положительные прозрачные строки и неотрицательную скидку; итог, себестоимость и маржа рассчитываются сервером. Утверждённый snapshot неизменяем, self-approval запрещён, решения защищены ID и optimistic concurrency.

- OrganizationId/UserId не принимаются от клиента; tenant определяется валидированным JWT.
- RBAC реализован permission claims, не проверкой строкового имени роли.
- Branch access проверяется endpoint/application/query слоями.
- Выданный JWT на каждом защищённом запросе сверяется с активностью пользователя, текущими permissions и филиалами; блокировка или отзыв прав требует нового login.
- Tenant-aware составные внешние ключи не позволяют связать пользователя, филиал, автомобиль, историю и аудит разных организаций даже прямой записью в БД.
- Inspection FK включают OrganizationId; чтение и команды фильтруются по tenant и разрешённым филиалам. Попытка передать чужой `organizationId` игнорируется контрактом и покрыта integration test.
- Reconditioning FK к plan/vehicle/branch/inspection/defect/user включают OrganizationId. Permissions разделены на view/create/edit/submit/approve/cancel; approval дополнительно проверяет настройку независимого согласования. Decision ID, concurrency token и DB constraints не позволяют конкурентным запросам записать два решения или два активных плана.
- Bucket фото не публичен. Object key создаёт сервер и включает tenant/inspection/defect/photo UUID и уникальный upload-attempt ID; исходное имя не участвует в пути. Проигравшая конкурентная команда может удалить только свой ключ. Скачивание только через JWT + permission API.
- Upload ограничен 8 МБ/25 MP и JPEG/PNG/WebP, проверяется реальным декодированием и перекодируется без metadata. Поддельное расширение, обрезанный файл, oversized upload, сбой MinIO и cross-tenant download покрыты негативными тестами.
- Correction photo metadata ссылается через tenant-aware `SourcePhotoId` на исходное доказательство и переиспользует неизменяемый object. Отложенное удаление tenant-aware и физически удаляет object только без оставшихся metadata-ссылок.
- Login ограничен по IP фиксированным окном; malformed/invalid requests возвращают Problem Details без внутренних деталей.
- Пароли demo seed хешируются стандартным `PasswordHasher`; реальные среды должны использовать IdP, MFA для privileged users и secret manager.
- JWT key в `appsettings.json` и Compose только локальный. Production обязан переопределить его секретом, отключить demo seed и использовать TLS.
- Audit table не имеет API изменения/удаления. Логи не содержат пароль/токен/полный request body.
- CRM contact values, consent source, merge/close reason и activity summary не копируются в audit payload или application logs. Tenant-aware customer/lead FK, branch predicates и отдельный merge permission защищают PII от межорганизационного и непривилегированного доступа.
- Dependency restore проверяется NuGet/npm audit в release hardening; известная уязвимая OpenAPI dependency шаблона удалена.
- До production нужны полноценная rotation/revocation модель с security stamp или централизованным IdP, MFA, CSP, secure headers, malware scanning/CDR для файлов, S3 encryption/lifecycle, backup/restore drill и правовая проверка персональных данных.

Сообщения об уязвимостях передавать приватно владельцам репозитория, не создавая публичный issue с эксплойтом или секретами.
