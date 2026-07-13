# Reconditioning

Модуль владеет планом предпродажной подготовки, работами, явными исключениями обязательных дефектов, решениями руководителя, статусной историей и одобренными бюджетными snapshots.

Инварианты находятся в `Domain/ReconditioningPlan.cs`; application service повторно проверяет permission и branch scope. EF adapter и HTTP endpoints размещены в `apps/api`. Модуль не выполняет ремонт и не хранит фактические расходы — это scope 0.4.
