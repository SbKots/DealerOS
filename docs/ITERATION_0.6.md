# Итерация 0.6 — Customer, Lead и first-response SLA

## Сквозной сценарий

Сотрудник находит либо создаёт клиента, видит предупреждение о совпадающих нормализованных контактах и при необходимости выполняет отдельное привилегированное merge-решение с причиной. Затем создаёт лид на конкретный автомобиль или критерии поиска. Руководитель назначает менеджера вручную либо детерминированным round-robin. Inbox показывает дедлайн и просрочку первого ответа. Менеджер фиксирует осмысленный контакт, следующее действие, квалифицирует либо закрывает лид; полный timeline остаётся доступен.

## Инварианты

- organization берётся только из проверенной сессии, а branch повторно проверяется application service;
- phone/email нормализуются сервером, совпадение лишь предупреждает и никогда не запускает auto-merge;
- merge требует permission, preview, причины и optimistic version; источник сохраняется как merged record;
- аудит merge и lead-команд не содержит contact values и activity summary;
- лид требует vehicle или search criteria и один неизменяемый customer в том же tenant;
- SLA берётся из настройки organization и сохраняется абсолютным `FirstResponseDueAt`;
- `FirstResponseAt` устанавливается только первой meaningful activity и больше не меняется;
- round-robin учитывает только активных пользователей с branch access и CRM permission, затем сортирует по активной нагрузке и UserId;
- lifecycle меняют отдельные команды, universal status PATCH отсутствует;
- command ID обеспечивает идемпотентность, version/DB constraint — конкурентную защиту;
- закрытые activity и финальные лиды не редактируются.

## Проверяемость

Unit tests покрывают нормализацию, consent evidence, merge/idempotency, SLA и lifecycle guards. PostgreSQL integration test проходит создание дубля, preview/merge, перенос лида, просрочку SLA, повтор round-robin и meaningful contact, qualification, permissions, cross-tenant search и отсутствие PII в audit. Component tests проверяют duplicate warning и работу overdue inbox до первого контакта.

## Ограничения

Телефония, email/messenger integrations и автоматическое определение результата отсутствуют: activity вводит сотрудник. Номер/email не шифруются прикладным ключом, поэтому production требует отдельной модели key management, retention и доступа к PII. Версия готова только для локальной демонстрации и не предназначена для production.
