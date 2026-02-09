# TelegramUserLinkParser

# Russian

**TelegramUserLinkParser** — это быстрая, **Zero-Allocation** утилита на C# для поиска и получения ссылок пользователей, каналы и чаты Telegram из текста.

Библиотека создана для высоконагруженных проектов, где важна скорость и отсутствие нагрузки на GC.

## Особенности

* **Zero Allocation:** Работает исключительно с `ReadOnlySpan<char>` и `ref struct`. Не создает лишних строк (`String`) в куче.
* **SIMD Optimized:** Использует векторные инструкции (AVX2) и `SearchValues` из .NET 8 для мгновенного поиска.
* **No Regex:** Полностью ручной парсинг на указателях (`Unsafe`), что быстрее регулярок.

## Форматы

Парсер возвращает два типа сущностей: `Username` (строка) и `Id` (число `long`).

| Формат в тексте | Тип | Результат
| :--- | :--- | :--- | :--- |
| `@username` | `Username` | `username` |
| `t.me/username` | `Username` | `username` |
| `https://t.me/user` | `Username` | `user` |
| `t.me/@id123456789` | **`Id`** | `123456789` |
| `tg://resolve?domain=durov` | `Username` | `durov` |
| `tg://user?id=123456` | `Id` | `123456` |
| `tg://openmessage?user_id=42`| `Id` | `42` |
| `@42`| `Id` | `42` |


## Использование

```cmd
cd your_directory && git clone https://github.com/Puxxalwl/TelegramUserLinkParser
```

```csharp
using YourDirectory;

// Исходный текст с ссылкаи
var text = "Пишите @durov или переходите на [https://t.me/telegram](https://t.me/telegram). Мой id: t.me/@id123456789";

// Создаем парсер (это ref struct, живет только на стеке)
TelegramUserLinkParser parser = new(text);

// Проходим по всем найденным ссылкам
while (parser.MoveNext())
{
    LinkResult link = parser.Current;

    if (link.Type == LinkType.Username)
    {
        // link.Value это Span
        Console.WriteLine($"Найдено имя: {link.Value.ToString()}");
    }
    else if (link.Type == LinkType.Id)
    {
        // ID сразу парсится в long
        Console.WriteLine($"Найден ID: {link.IdValue}");
    }
}
```

## Требования

- .NET 8.0 или выше.
- Процессор с поддержкой AVX2 (рекомендуется для максимальной скорости).
- Указать свою директорию в Parser.cs (namespace)
- Разрешить unsafe-код


# English

**TelegramUserLinkParser** is a fast, **Zero-Allocation** C# util for finding and retrieving Telegram user links, channels, and chats from text.

The library is designed for high-load projects where speed and zero GC pressure are critical.

## Features

* **Zero Allocation:** Works exclusively with `ReadOnlySpan<char>` and `ref struct`. Does not create unnecessary strings (`String`) on the heap.
* **SIMD Optimized:** Uses vector instructions (AVX2) and `SearchValues` from .NET 8 for instant searching.
* **No Regex:** Fully manual parsing using pointers (`Unsafe`), which is faster than regex.

## Formats

The parser returns two types of entities: `Username` (string) and `Id` (`long` number).

| Format in text | Type | Result |
| :--- | :--- | :--- |
| `@username` | `Username` | `username` |
| `t.me/username` | `Username` | `username` |
| `https://t.me/user` | `Username` | `user` |
| `t.me/@id123456789` | **`Id`** | `123456789` |
| `tg://resolve?domain=durov` | `Username` | `durov` |
| `tg://user?id=123456` | `Id` | `123456` |
| `tg://openmessage?user_id=42`| `Id` | `42` |
| `@42`| `Id` | `42` |


## Usage

```cmd
cd your_directory && git clone [https://github.com/Puxxalwl/TelegramUserLinkParser](https://github.com/Puxxalwl/TelegramUserLinkParser)
```

```csharp
using YourDirectory;

// Source text with links
var text = "Write to @durov or go to [https://t.me/telegram](https://t.me/telegram). My id: t.me/@id123456789";

// Create the parser (it is a ref struct, lives only on the stack)
TelegramUserLinkParser parser = new(text);

// Iterate through all found links
while (parser.MoveNext())
{
    LinkResult link = parser.Current;

    if (link.Type == LinkType.Username)
    {
        // link.Value is a Span
        Console.WriteLine($"Found name: {link.Value.ToString()}");
    }
    else if (link.Type == LinkType.Id)
    {
        // ID is parsed directly into long
        Console.WriteLine($"Found ID: {link.IdValue}");
    }
}
```

## Requirements

- .NET 8.0 or higher.
- CPU with AVX2 support (recommended for maximum speed).
- Specify your directory in Parser.cs (namespace).
- Allow unsafe code.

# Dev in TG:
- @puxalwl (or id 6984952764)