# Inspections module

`Inspections` владеет жизненным циклом технического осмотра: `InspectionTemplate`, `Inspection`, пунктами, дефектами и метаданными фото. Бизнес-правила находятся в `Domain`, orchestration и ports — в `Application`; EF, MinIO и HTTP adapters находятся в `apps/api`.

Публичные команды: start, save item, add defect/photo, remove draft defect, complete, cancel и start correction. Универсального PATCH status нет. Каждая команда требует exact permission и branch access, а все stores требуют OrganizationId.

Важные инварианты:

- один Draft/InProgress на Vehicle;
- обязательные items заполнены до complete;
- Critical всегда задаёт repair required и blocks sale;
- Completed неизменяем; исправление создаёт revision;
- template fields копируются в inspection items;
- photo object key никогда не формируется из client filename и уникален для каждой upload-попытки;
- retry `photoId` идемпотентен только в рамках того же inspection/defect;
- correction photo metadata хранит `SourcePhotoId` и разделяет неизменяемый object без копирования бинарного файла;
- удаление последней ссылки атомарно ставит tenant-aware запись в `object_deletion_queue`; MinIO failure не сообщает ложный DB rollback.
