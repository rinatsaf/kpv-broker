# Contributing

## Branching
- `main` — стабильная ветка
- `feature/<ticket>-short-name` — новая функциональность
- `bugfix/<ticket>-short-name` — исправления
- `chore/<ticket>-short-name` — техдолг/настройки
- `release/x.y` — если нужен релизный поток (опционально)

Примеры:
- `feature/BROK-12-prefetch`
- `bugfix/BROK-31-ack-timeout`
- `chore/BROK-5-ci-dotnet9`

---

## Commit rules (Conventional Commits)
Формат коммита:

`<type>(<scope>): <summary>`

### type
- `feat` — новая фича
- `fix` — багфикс
- `refactor` — рефакторинг без изменения поведения
- `perf` — оптимизация
- `test` — тесты
- `docs` — документация
- `build` — сборка/пакеты
- `chore` — инфраструктура/настройки

### scope
- `contracts` — контракты (code-first DTO + интерфейсы)
- `core` — Broker.Core
- `engine` — Broker.Engine
- `client` — SDK
- `mgmt` — Management API
- `ui` — UI (если будет)
- `infra` — docker/compose/ci
- `samples` — demo проекты
- `tests` — тестовые проекты

### Примеры
- `feat(engine): add consume streaming handler`
- `feat(core): implement inflight store`
- `fix(client): reconnect on stream reset`
- `docs: add architecture overview`
- `chore(infra): add docker compose`

### Запрещено
- коммиты вида `fix`, `update`, `wip` без деталей
- огромные коммиты на 50 файлов без описания

---

## Pull Request rules

### Перед созданием PR
- Локально прошло:
  - `dotnet build`
  - `dotnet test`
- Нет секретов/ключей в коде
- Обновлена документация, если менялся публичный API/контракт

### Требования к PR
- PR маленькие и понятные (желательно до ~400 строк изменений)
- 1 PR = 1 логическая задача
- Минимум 1 апрув (для core/engine/contract — 2 апрува)
- CI должен быть зелёным

### Что обязательно в описании PR
1) **Что сделано** (2–6 пунктов)
2) **Почему** (контекст/проблема)
3) **Как проверить** (шаги)
4) (если нужно) **Риски** / **Breaking changes**

---

## PR Template
Скопируйте это в `.github/pull_request_template.md` или вставляйте в описание вручную:

### What
- [ ] ...

### Why
- ...

### How to test
1. ...
2. ...

### Notes / Risks
- ...

### Checklist
- [ ] `dotnet test` passed
- [ ] No breaking changes (or described)
- [ ] Updated docs if needed