# HS Comps Plugin for Hearthstone Deck Tracker (Battlegrounds)

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://microsoft.com)
[![HDT Plugin](https://img.shields.io/badge/HDT%20Plugin-Compatible-orange.svg)](https://hsreplay.net/downloads/)

Интеллектуальный плагин-ассистент для **Hearthstone Deck Tracker (HDT)** в режиме **«Поля сражений» (Battlegrounds)**. Плагин анализирует текущий стол игрока, доступные и забаненные типы существ (расы), выбранного героя и в реальном времени рассчитывает наиболее перспективные победные композиции на основе базы из более чем 2 600 эталонных топ-сборок игроков высокого MMR.

---

## 🌟 Ключевые возможности

1. **Компактный внутриигровой Mini-HUD:**
   - Отображается поверх клиента Hearthstone прямо во время матча.
   - Показывает топ-1 рекомендуемый архетип, ключевых существ и процент схожести/соответствия.
   - Быстрое переключение между альтернативными сборками стрелками.
   - Клик по оверлею открывает полноразмерное окно детального анализа.

2. **Полноразмерное окно композиций (1560×960):**
   - Аутентичный деревянный интерфейс в стиле Hearthstone.
   - Три основные вкладки рекомендаций: **[ТОП-1 РЕКОМЕНДАЦИЯ]**, **[АЛЬТЕРНАТИВА 2]**, **[АЛЬТЕРНАТИВА 3]**.
   - Динамическая панель фильтрации по 10 типам существ (включая Аномалии / Aberrations).
   - Кнопка фиксации стола (**Lock**): возможность закрепить желаемую финальную сборку на весь матч.
   - Авторские кнопки управления окном («Свернуть» и «Закрыть») с плавной подсветкой при наведении.

3. **Умный статус существ (Smart Status Highlighting):**
   - 🟢 **На столе (On Table):** существо уже выставлено на поле боя.
   - 🟡 **В руке (In Hand):** карта находится в руке игрока.
   - ⚪ **Уже собрано (Already Have):** дубликат или триплет уже имеется.
   - 🔵 **Нужно найти (Need to Find):** ключевая фигура, которую необходимо купить в таверне.
   - Карточки отображаются в аутентичных статусных рамках с динамическими масками.

4. **Векторный поисковый движок (`VectorSearchEngine`):**
   - Расчет сходства на основе косинусного расстояния, пересечения множеств (Jaccard) и взвешенных коэффициентов ключевых существ.
   - Учет синергии героя (hero-specific benchmarks).
   - Строгая фильтрация недоступных и забаненных рас лобби.

5. **Автономное обновление мета-базы:**
   - Встроенный инструмент `tools/build_vector_db.py` обращается к API Firestone (ZeroToHeroes) и собирает актуальные «безупречные игры» (Perfect Games).
   - Автоматическая догрузка недостающих официальных артов существ через `tools/download_missing_full_cards.py`.

---

## 📂 Структура проекта

```text
HS_Comps_Plugin/
├── .gitignore
├── GEMINI.md                    # Инструкции и инварианты проекта для AI-ассистентов
├── README.md                    # Документация проекта
├── HSCompsPlugin.csproj         # Проект C# (.NET Framework 4.8 / WPF)
├── bg_cards_official.json       # Официальная база данных карт Hearthstone
├── comps_vector_db.json         # Векторная база эталонных победных столов
├── missing_meta_cards.json      # Отчет по картам меты
├── supplementary_cards.json     # Дополнительные маппинги новых/кастомных карт
│
├── assets/
│   ├── cards/
│   │   └── full/                # Кеш официальных артов карт
│   └── ui/                      # Пользовательский интерфейс плагина
│       ├── details_window_bg.png# Главный деревянный фон (1560x960)
│       ├── hud_overlay_bg.png   # Резервный фон HUD
│       ├── ring_*.png           # Кольца индикации статусов карт
│       ├── arrows/              # Спрайты стрелок навигации
│       ├── digits/              # Цифровой шрифт плагина (0-9, %, /, .)
│       ├── frames/              # Статусные рамки существ и маски обрезки
│       ├── hud/                 # Текстуры компактного Mini-HUD
│       ├── lock/                # Кнопка закрепления стола (Lock/Unlock)
│       ├── tabs/                # Вкладки выбора альтернатив (1, 2, 3)
│       ├── tribes/              # Иконки всех 10 типов существ
│       └── window/              # Авторские кнопки управления окном
│           ├── btn_min_normal.png
│           ├── btn_min_hover.png
│           ├── btn_close_normal.png
│           └── btn_close_hover.png
│
├── lib/                         # Зависимости сборки
│   ├── HearthDb.dll             # База данных карт и сущностей Hearthstone
│   └── HearthstoneDeckTracker.exe# Основной исполняемый файл HDT
│
├── src/                         # Исходный код C#
│   ├── HSCompsPlugin.cs         # Точка входа плагина, интеграция в меню HDT
│   ├── Models/                  # Модели данных (BoardSlot, WinningBoard, TribeFlags...)
│   ├── Services/                # Сервисы (GameStateReader, VectorSearchEngine, ImageCache...)
│   └── UI/                      # WPF окна и элементы управления (CompsDetailWindow, MiniOverlay...)
│
└── tools/                       # Python-инструменты обслуживания
    ├── build_vector_db.py       # Генератор векторной базы из live API
    ├── download_missing_full_cards.py # Догрузчик артов
    └── hearthdb_id_map.json     # Маппинг DbfId <-> CardId
```

---

## 🛠️ Требования

- **ОС:** Windows 10 / Windows 11 (x64)
- **Hearthstone Deck Tracker:** актуальная версия (установленная)
- **.NET Framework:** 4.8 Runtime
- **Python:** 3.10+ (требуется только для запуска скриптов обновления базы в `tools/`)

---

## 🚀 Установка плагина

1. Убедитесь, что Hearthstone Deck Tracker закрыт.
2. Скомпилируйте проект или скопируйте рабочие файлы в каталог плагинов HDT:
   ```text
   %APPDATA%\HearthstoneDeckTracker\Plugins\HSCompsPlugin
   ```
3. Структура в папке плагина должна содержать:
   - `HSCompsPlugin.dll`
   - `Newtonsoft.Json.dll`
   - `bg_cards_official.json`
   - `comps_vector_db.json`
   - `supplementary_cards.json`
   - Папку `assets/` со всеми подкаталогами (`cards`, `ui`)
4. Запустите Hearthstone Deck Tracker.
5. Перейдите в **Опции (Options) -> Плагины (Plugins)** и убедитесь, что плагин **HS Comps Plugin** включен.

---

## 💻 Сборка из исходного кода

Проект использует современный SDK-style формат файла проекта для таргета `net48`.

Для компиляции через .NET CLI:
```powershell
dotnet build -c Debug
```
Или для релизной сборки:
```powershell
dotnet build -c Release
```

> [!NOTE]
> При успешной сборке срабатывает таргет `PostBuild`, который автоматически копирует собранную сборку DLL и все графические ресурсы напрямую в рабочий каталог HDT (`%APPDATA%\HearthstoneDeckTracker\Plugins\HSCompsPlugin`).

---

## 🔄 Обновление векторной базы столов

Для актуализации победных столов под свежий патч метагейма:
```powershell
python tools/build_vector_db.py
```
Скрипт:
1. Загрузит свежую статистику архетипов и безупречные игры (MMR 5000+, 10+ ход) из API Firestone.
2. Сформирует обновленный `comps_vector_db.json`.
3. Автоматически синхронизирует базу с папкой сборки и рабочей папкой плагина HDT.
4. Проверит наличие всех необходимых артов карт и догрузит недостающие.

---

## 📄 Лицензия

Проект распространяется для личного и некоммерческого использования в рамках экосистемы Hearthstone Deck Tracker.
