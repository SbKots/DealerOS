# ADR 0005: единая галерея автомобиля и ссылочные фото осмотра

Статус: принято, 2026-07-14.

## Контекст

В DealerOS уже существовали приватные MinIO-объекты фотографий осмотра и ограниченная `VehicleMedia` для объявления. Отдельная третья модель галереи привела бы к копированию бинарных файлов, разным правилам доступа и риску удаления доказательств завершённого осмотра.

## Решение

- Расширить `operations.vehicle_media` до единой gallery metadata, сохранив совместимые `/media` endpoints для текущего listing workspace.
- Хранить безопасно перекодированный original и варианты thumbnail/medium/large; все ключи одной upload-attempt содержат случайный attempt ID.
- Хранить category, caption, focal point, order, cover, listing selection и optimistic `Version` в PostgreSQL; каждое существенное изменение писать в audit.
- Для импорта из Completed inspection создавать новую gallery metadata и производные варианты, но ссылаться на существующий immutable original через `SourceInspectionPhotoId`. Такая запись имеет `OwnsOriginalObject=false`.
- Физически удалять только `OwnedObjectKeys`; сначала атомарно удалить metadata и создать tenant-aware cleanup records, затем пытаться удалить MinIO objects.
- Оставить bucket приватным и скачивать любой вариант только через JWT, permission и tenant/branch checks.

## Последствия

Плюсы: одно представление медиа для карточки и объявления, отсутствие неконтролируемых копий, безопасная конкуренция и сохранность доказательств. Минусы: четыре объекта на самостоятельную загрузку, дополнительная миграция и необходимость фоновой reconciliation очереди удалений. HEIC/HEIF отложен до появления проверенного decoder; клиент и сервер сообщают об ограничении явно.
