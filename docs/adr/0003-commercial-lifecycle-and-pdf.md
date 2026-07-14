# ADR 0003: коммерческий lifecycle, payment ledger и PDF

## Статус

Принято для release train 0.8–1.0.

## Решение

Оставить `Sales` владельцем Visit/Offer, вынести временную блокировку в `Reservations`, а Deal/Payment/Document/Handover — в модуль `Deals`. Границы остаются проектами модульного монолита и используют одну PostgreSQL transaction через инфраструктурные stores. Финансовая аналитика 1.0 будет отдельным модулем `Finance`, читающим authoritative snapshots без создания второй бухгалтерии.

Payment и Refund — append-only записи с положительной суммой; направление задаёт kind. Deposit из Reservation переносится ровно одной записью. Deal, документы и handover содержат неизменяемые snapshots; correction документа создаёт revision со ссылкой на источник.

Для настоящих cross-platform PDF выбран `PDFsharp` 6.2.4. Официальная документация и NuGet указывают MIT license, поддержку .NET 10, Windows и Linux. Runtime image устанавливает DejaVu Sans. PDF хранится только в private MinIO; API проверяет permission, сохраняет SHA-256 и пишет отдельный audit download. Формы явно демонстрационные и не являются юридически проверенными документами или электронной подписью.

## Последствия

- бронь, конвертация, выдача и `Sold` атомарны в общей БД;
- PostgreSQL constraints дополняют optimistic concurrency для race-critical инвариантов;
- нет банка, кассы, acquiring, ЭДО и хранения карточных реквизитов;
- PDFsharp добавляет одну MIT dependency и системный Unicode-шрифт; NuGet vulnerability scan входит в release gate;
- до реальных данных нужны юридическая проверка шаблонов, IdP/MFA, TLS/secrets, privacy и production object-storage controls.
