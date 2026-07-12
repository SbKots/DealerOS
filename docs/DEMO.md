# Демонстрация итерации 0.2

1. Запустить `docker compose up --build`, открыть `http://localhost:4173`.
2. Войти `admin@volga-auto.demo` / `DealerOS!2026`.
3. Заполнить уникальный валидный VIN, марку, модель, год, пробег и цену.
4. Создать поступление: карточка показывает `Черновик`; повтор того же VIN даст понятный conflict.
5. Нажать «Принять на склад»: появятся `На складе` и номер `MSK-YYYY-XXXXXXXX`.
6. Обновить страницу: запись сохранена в PostgreSQL.
7. Выйти и войти как `admin@north-auto.demo`: автомобиль первой организации не виден и прямой URL возвращает 404.

Проверяемые эффекты: status history содержит два перехода, audit — `vehicle.intake_created` и `vehicle.accepted_to_stock`, оба события имеют actor, tenant и correlation ID.

## Сценарий осмотра

1. Войти `inspector@volga-auto.demo` / `DealerOS!2026` и открыть «Осмотры».
2. В очереди выбрать demo Lada Vesta с VIN `XTA210990Y0200001`, начать осмотр.
3. Заполнить 11 пунктов; прогресс должен достичь 100%.
4. Добавить Critical-дефект тормозов, выключив client checkbox блокировки. Сервер всё равно включит `RepairRequired` и `BlocksSale`.
5. Прикрепить PNG/JPEG/WebP до 8 МБ, увидеть preview. MinIO bucket остаётся приватным.
6. Завершить осмотр с подтверждением: автомобиль перейдёт в `ReconditioningRequired`.
7. Обновить страницу, в карточке автомобиля открыть вкладку «Осмотры»: ревизия, дефект и фото доступны read-only.

Для tenant negative check войти как `admin@north-auto.demo`: осмотр и фото «Волга Авто» недоступны и прямые API URL возвращают 404.
