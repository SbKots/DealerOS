# Демонстрация итерации 0.1

1. Запустить `docker compose up --build`, открыть `http://localhost:4173`.
2. Войти `admin@volga-auto.demo` / `DealerOS!2026`.
3. Заполнить уникальный валидный VIN, марку, модель, год, пробег и цену.
4. Создать поступление: карточка показывает `Черновик`; повтор того же VIN даст понятный conflict.
5. Нажать «Принять на склад»: появятся `На складе` и номер `MSK-YYYY-XXXXXXXX`.
6. Обновить страницу: запись сохранена в PostgreSQL.
7. Выйти и войти как `admin@north-auto.demo`: автомобиль первой организации не виден и прямой URL возвращает 404.

Проверяемые эффекты: status history содержит два перехода, audit — `vehicle.intake_created` и `vehicle.accepted_to_stock`, оба события имеют actor, tenant и correlation ID.
