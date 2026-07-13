# Security baseline

- OrganizationId/UserId не принимаются от клиента; tenant определяется валидированным JWT.
- RBAC реализован permission claims, не проверкой строкового имени роли.
- Branch access проверяется endpoint/application/query слоями.
- Выданный JWT на каждом защищённом запросе сверяется с активностью пользователя, текущими permissions и филиалами; блокировка или отзыв прав требует нового login.
- Tenant-aware составные внешние ключи не позволяют связать пользователя, филиал, автомобиль, историю и аудит разных организаций даже прямой записью в БД.
- Login ограничен по IP фиксированным окном; malformed/invalid requests возвращают Problem Details без внутренних деталей.
- Пароли demo seed хешируются стандартным `PasswordHasher`; реальные среды должны использовать IdP, MFA для privileged users и secret manager.
- JWT key в `appsettings.json` и Compose только локальный. Production обязан переопределить его секретом, отключить demo seed и использовать TLS.
- Audit table не имеет API изменения/удаления. Логи не содержат пароль/токен/полный request body.
- Dependency restore проверяется NuGet/npm audit в release hardening; известная уязвимая OpenAPI dependency шаблона удалена.
- До production нужны полноценная rotation/revocation модель с security stamp или централизованным IdP, MFA, CSP, secure headers, backup/restore drill и правовая проверка персональных данных.

Сообщения об уязвимостях передавать приватно владельцам репозитория, не создавая публичный issue с эксплойтом или секретами.
