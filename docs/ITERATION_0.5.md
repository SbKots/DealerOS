# Итерация 0.5 — QC, ReadyForSale и listing content

## Сквозной сценарий

Руководитель открывает завершённый execution как отдельную попытку QC. Блокирующее замечание возвращает конкретную работу без изменения завершённой истории. После исправления создаётся следующая QC revision; Pass переводит автомобиль в `ReadyForSale`. Контент-специалист загружает обязательные приватные кадры, выбирает cover и порядок, заполняет прозрачное описание и цену, фиксирует Listing Ready snapshot, выгружает JSON и отдельно подтверждает фактическую публикацию.

## Инварианты

- QC доступен только для `Completed` execution и не может выполняться его автором;
- Pass запрещён при Major/Critical/rework observations;
- rework всегда ссылается на конкретный work order из того же tenant/execution;
- предыдущие QC attempts, execution cost history и approved overrun не переписываются;
- `ReadyForSale` устанавливается только QC Pass и означает полную готовность после подготовки;
- media проходит server-side decode/re-encode, остаётся приватным и scoped по organization/vehicle;
- Listing Ready требует Exterior, Interior, DamageHistory и одну не-internal cover;
- snapshot исключает `DocumentsInternal` и не меняется вслед за карточкой автомобиля;
- export не устанавливает Published; подтверждение, ошибка и снятие выполняются отдельными командами;
- price/content/publication history, audit и optimistic concurrency обязательны.

## Ограничения

Формат export — документированный JSON snapshot. Реальных marketplace API, scraping, CDN и video transcoding нет. Версия готова только для локальной демонстрации и не предназначена для production.
