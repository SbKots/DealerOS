# DealerOS — проверка фотогалереи автомобиля

## Зафиксированное состояние

- Feature branch: `codex/vehicle-photo-gallery`.
- Base branch: `codex/ux-ui-redesign-1.0`.
- Base commit: `51ee9ee620a60a4756fa0dd06eb7988a3a95f130`.
- Product commit: `df0afc6a260d44b2448cec9903de27d87477e022`.
- GitHub Actions: итоговый run указывается в Pull Request после проверки HEAD.

## Реализовано

- Галерея в карточке автомобиля: множественный выбор, drag-and-drop, очередь загрузки, прогресс, ошибка отдельного файла и повтор.
- Единая сетка 4:3 без физической обрезки оригинала, focal point, подпись, категории, порядок, обложка и подтверждаемое удаление.
- Полноэкранный `contain`-просмотр с навигацией мышью и клавишами `←`, `→`, `Esc`.
- Обложка в реестре, карточке автомобиля и блоке внимания на обзоре.
- Импорт фото завершённого осмотра без неконтролируемого копирования исходного бинарного объекта.
- Отбор фотографий для объявления с неизменяемым snapshot после публикации.
- Отдельные серверные права `vehicles.photos.view`, `vehicles.photos.upload`, `vehicles.photos.manage`.
- Tenant/branch-проверки, optimistic concurrency и аудит загрузки, удаления, обложки, порядка, metadata и отбора.
- Статическая Pages-демоверсия на синтетических данных и `public/404.html`, возвращающий вложенный Pages URL в `/DealerOS/`.

Основные пути:

- API: `apps/api/Endpoints/QualityListingEndpoints.cs`.
- Обработка изображений: `apps/api/Infrastructure/InspectionImageProcessor.cs`.
- Application service: `modules/Operations/Application/MediaListingService.cs`.
- Domain model: `modules/Operations/Domain/QualityAndListing.cs`.
- UI: `apps/web/src/VehiclePhotoGallery.tsx`.
- Стили: `apps/web/src/App.css`.
- Миграция: `apps/api/Infrastructure/Migrations/20260714155211_VehiclePhotoGallery11.cs`.
- ADR: `docs/adr/0005-vehicle-gallery-storage-and-inspection-links.md`.
- Пользовательская документация: `docs/VEHICLE_PHOTO_GALLERY.md`.

## Хранение и безопасность

Оригинал и производные версии находятся в приватном MinIO bucket под случайными server-generated object keys. Backend выполняет decode/re-encode, исправляет EXIF Orientation и тем самым удаляет EXIF/GPS metadata. Для каждого нового upload-attempt создаётся собственный уникальный набор ключей; откат удаляет только объекты этого attempt.

Создаются thumbnail 480 px, medium 1280 px и large 2560 px по длинной стороне без upscale. Метаданные галереи хранят ссылки на оригинал и варианты. Импорт из осмотра разделяет неизменяемый original object, но создаёт собственные производные версии; удаление галерейной metadata не удаляет оригинал завершённого осмотра.

Выдача оригинала и вариантов проходит только через авторизованный backend с повторной tenant, branch и permission-проверкой. Физическое удаление выполняется после DB-операции; сбой MinIO записывается в tenant-aware идемпотентную очередь reconciliation.

Ограничения текущей версии:

- JPEG, PNG и WebP;
- не более 8 МиБ и 25 мегапикселей на файл;
- не более 100 фотографий на автомобиль;
- HEIC/HEIF отклоняется с явным сообщением о поддерживаемых форматах.

## Автоматические проверки

| Проверка | Точная команда | Результат |
| --- | --- | --- |
| Форматирование | `dotnet format DealerOS.slnx --verify-no-changes --no-restore` | успешно |
| Backend build | `dotnet build DealerOS.slnx --no-restore --nologo` | 0 warnings, 0 errors |
| Unit | `dotnet test tests/DealerOS.UnitTests/DealerOS.UnitTests.csproj --no-build --logger "console;verbosity=minimal"` | 64/64 |
| PostgreSQL + MinIO integration | `dotnet test tests/DealerOS.IntegrationTests/DealerOS.IntegrationTests.csproj --no-build --logger "console;verbosity=minimal"` | 36/36, 3 min 18 s |
| Frontend lint | `npm run lint` из `apps/web` | успешно |
| Frontend components | `npm test -- --run` из `apps/web` | 36/36 в 13 файлах |
| Frontend production build | `npm run build` из `apps/web` | успешно |
| GitHub Pages production build | `npm run build:pages` из `apps/web` | успешно |
| Полный Playwright | `npm run test:e2e` из `apps/web` | 5/5 |
| Повтор intake + gallery | `npx playwright test e2e/intake.spec.ts --repeat-each=2` из `apps/web` | 4/4 |

Новые тесты галереи:

- Domain/unit: `tests/DealerOS.UnitTests/QualityListingDomainTests.cs`.
- PostgreSQL + MinIO integration: `tests/DealerOS.IntegrationTests/VehiclePhotoGalleryApiTests.cs` — 5 сценариев.
- Frontend: `apps/web/src/VehiclePhotoGallery.test.tsx` — 3 сценария.
- E2E: `apps/web/e2e/intake.spec.ts` — полный сценарий загрузки, metadata, обложки, lightbox и удаления.

Integration-проверки покрывают реальный JPEG/PNG/WebP decode, ложный MIME/extension, oversized и unsupported content, EXIF orientation, отсутствие upscale, варианты, конкурентный одинаковый media id, tenant/branch/permissions, stale version, удаление, аудит и сохранность фото завершённого осмотра.

## Миграции

- Upgrade существующего локального PostgreSQL с предыдущей схемы применил `20260714155211_VehiclePhotoGallery11`; 109 media rows, 0 пустых variant keys, 84 legacy rows переведены на новую модель, 0 документов ошибочно включены в объявление.
- Fresh database проверена запуском собранного API image против отдельной пустой PostgreSQL database; последняя применённая миграция — `20260714155211_VehiclePhotoGallery11`, новые object/source columns созданы.
- Временная база и контейнер проверки удалены; рабочие PostgreSQL и MinIO volumes не удалялись.

## Чистый клон и визуальная проверка

Product commit клонирован отдельно в `C:\Users\SberKot\AppData\Local\Temp\DealerOS-gallery-clean-df0afc6`. В чистом клоне выполнены `dotnet restore DealerOS.slnx`, format verify, backend build/unit, `npm ci`, lint, frontend tests, обычная и Pages production-сборки — успешно.

Проверены 1440 px, 1024 px и 390 px, сетка и lightbox. Локальные screenshots сохранены как воспроизводимые test artifacts и не добавлены в Git:

- `apps/web/TestResults/vehicle-gallery-1440.png`;
- `apps/web/TestResults/vehicle-gallery-1024.png`;
- `apps/web/TestResults/vehicle-gallery-390.png`;
- `apps/web/TestResults/vehicle-gallery-lightbox.png`;
- `apps/web/TestResults/pages-gallery-desktop.png`;
- `apps/web/TestResults/pages-gallery-mobile.png`;
- `apps/web/TestResults/pages-gallery-lightbox.png`.

Pages build проверен под статическим mount `/DealerOS/`: root, favicon, JS/CSS и demo WebP возвращают HTTP 200; приложение не делает API requests; `404.html` перенаправляет ошибочный вложенный URL на `/DealerOS/`. До merge этой stacked-ветки опубликованный Pages сайт остаётся на версии родительского redesign PR.
