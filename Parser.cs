using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace YourDirectory;



/// <summary>
/// EN: Defines the type of the extracted Telegram link entity. 
/// RU: Определяет тип извлечённой сущности ссылки Telegram.
/// </summary>
public enum LinkType
{
    Username,
    Id
}

/// <summary>
/// EN: Represents a parsed Telegram link result containing either a Username or an ID.
/// RU: Представляет результат разбора ссылки Telegram, содержащий юзернейм, либо ид.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct LinkResult
{
    public readonly LinkType Type;
    public readonly ReadOnlySpan<char> Value;
    public readonly long IdValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LinkResult(ReadOnlySpan<char> username)
    {
        Type = LinkType.Username;
        Value = username;
        IdValue = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LinkResult(long id, ReadOnlySpan<char> rawSpan)
    {
        Type = LinkType.Id;
        Value = rawSpan;
        IdValue = id;
    }
}

/// <summary>
/// EN: High-performance Telegram link parser for user links with minimal memory usage.
/// RU: Высокопроизводительный парсер Telegram ссылок пользователей с минимальным употреблением памяти.
/// Supports formats: @ID, @username, t.me/username, t.me/@id123, tg://resolve, tg://user.
/// </summary>
[SkipLocalsInit]
public ref struct TelegramUserLinkParser
{
    private ReadOnlySpan<char> _remaining;
    
    // EN: Search only for @ (username/ID) and t/T (t.me or tg://). 
    // RU: Ищем только @ (юз/ид) и t/T (t.me или tg://).
    private static readonly SearchValues<char> _markers = SearchValues.Create("@tT");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TelegramUserLinkParser(ReadOnlySpan<char> text)
    {
        _remaining = text;
        Current = default;
    }

    public LinkResult Current { get; private set; }

    /// <summary>
    /// EN: Advances the parser to the next valid Telegram link.
    /// RU: Перемещает парсер к следующей поддерживаемой ссылке.
    /// </summary>
    /// <returns>
    /// EN: true if a link was found; otherwise, false.
    /// RU: true если ссылка найдена, иначе false.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe bool MoveNext()
    {
        // EN: Cache references to avoid repeated bounds checks.
        // RU: Кэш ссылки для избежания повторных проверок границ.
        ref char searchSpaceStart = ref MemoryMarshal.GetReference(_remaining);
        int length = _remaining.Length;
        int offset = 0;

        while (length > 0)
        {
            // EN: 1. SIMD skip to next potential marker.
            // RU: 1. SIMD переход к следующему возможному маркеру.
            int foundIdx = _remaining.Slice(offset).IndexOfAny(_markers);
            if (foundIdx <0) goto Fail;

            offset += foundIdx;
            length -= foundIdx;

            ref char ptr = ref Unsafe.Add(ref searchSpaceStart, offset);
            char c = ptr;
            
            int advanced = 1;
            bool matched = false;

            // EN: 2. Branchless-style dispatch based on char.
            // RU: 2. Диспетчеризация без ветвлений на основе символа.
            if (c== '@')
            {
                if (length > 1)
                {
                    ref char nextPtr = ref Unsafe.Add(ref ptr, 1);
                    
                    // EN: Check for digit to distinguish @id from @username.
                    // RU: Быстрая проверка цифр для различения @id от @username.
                    if (char.IsAsciiDigit(nextPtr))
                    {
                        if (TryParseId(ref nextPtr, length - 1, out var id, out int idLen))
                        { 
                            Current = new(id, MemoryMarshal.CreateReadOnlySpan(ref nextPtr, idLen)); 
                            advanced = 1 + idLen; 
                            matched = true; 
                        }
                    }
                    else 
                    {
                        int uLen = CountUsernameChars(ref nextPtr, length - 1);
                        if (uLen >= 4) // EN: Min username length. RU: Минимальная длина юза.
                        { 
                            Current = new(MemoryMarshal.CreateReadOnlySpan(ref nextPtr, uLen)); 
                            advanced = 1 + uLen; 
                            matched = true; 
                        }
                    }
                }
            }
            else 
            {
                if (length >=5)
                {
                    // EN: Reading 8 bytes (4 chars) to validate the prefix.
                    // RU: Чтение 8 байт (4 символа) для проверки префикса.
                    ulong prefix = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref ptr));
                    
                    // EN: Case-insensitive mask for 't' (0x74), 'm' (0x6d), 'e' (0x65), '.' (0x2e).
                    // EN: We mask the 0x20 bit on letters.
                    // RU: Маска игнорирования регистра для 't' (0x74), 'm' (0x6d), 'e' (0x65), '.' (0x2e).
                    // EN: Маскируем 0x20 бит в буквах.
                    const ulong maskLower = 0x0020002000200020;
                    ulong lowerPrefix = prefix|maskLower;

                    // EN: Check for "t.me" (0x0065006D002E0074 is "em.t" little endian).
                    // RU: Проверка на наличие "t.me" (0x0065006D002E0074 это "em.t" в малом порядке байтов).
                    if (lowerPrefix == 0x0065006D002E0074) 
                    {
                        // EN: Check for slash at offset 4.
                        // RU: Проверка на слэш со смещением 4.
                        if (Unsafe.Add(ref ptr, 4) =='/')
                        {
                            // EN: Found "t.me/".
                            // RU: Найдено "t.me/".
                            ref char payload = ref Unsafe.Add(ref ptr, 5);
                            int remLen = length - 5;

                            if (remLen > 3 && payload == '@')
                            {
                                ref char afterAt = ref Unsafe.Add(ref payload, 1);
                                ulong idPrefix = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref afterAt));
                                
                                // EN: Check for id followed by digit. "id" is 0x00640069.
                                // RU: Проверка на id, за которым идёт цифра. "id" будет 0x00640069.
                                if ((idPrefix | 0x0000000000200020) == 0x0030000000640069 || // "id0" (EN: partial check | RU: частичная проверка)
                                    (idPrefix | 0x0000000000200020) == 0x0031000000640069)   // "id1" (EN: checking common starts | RU: проверка частых начал)
                                {
                                     // EN: More robust: check 'i', 'd' then digits.
                                     // RU: Надежнее проверяем 'i', 'd', потом цифры.
                                     if ((afterAt | 0x20) == 'i' && 
                                         (Unsafe.Add(ref afterAt, 1) | 0x20) == 'd')
                                     {
                                         ref char digitStart = ref Unsafe.Add(ref afterAt, 2);
                                         if (TryParseId(ref digitStart, remLen - 3, out var id, out int idLen))
                                         {
                                             Current = new(id, MemoryMarshal.CreateReadOnlySpan(ref digitStart, idLen));
                                             // advanced = 5 (t.me/)+ 1 (@)+2 (id)+idLen
                                             advanced = 8 + idLen;
                                             matched = true;
                                             goto Found;
                                         }
                                     }
                                }
                            }

                            // EN: Fallback to standard username.
                            // RU: Фаллбэк к стандартному юзернейму.
                            int uLen = CountUsernameChars(ref payload, remLen);
                            if (uLen >= 4)
                            {
                                Current = new(MemoryMarshal.CreateReadOnlySpan(ref payload, uLen)); 
                                advanced = 5 + uLen;
                                matched = true; 
                            }
                        }
                    }
                    else
                    {
                        // EN: Check for "tg://". tg:/ is 0x002F003A00670074.
                        // RU: Проверка "tg://". tg:/ это 0x002F003A00670074.
                        const ulong tgSchemePart =0x002F003A00670074;
                        
                        if (lowerPrefix == tgSchemePart && Unsafe.Add(ref ptr, 4) == '/')
                        {
                            ref char afterScheme =ref Unsafe.Add(ref ptr, 5);
                            int remLen = length - 5;
                            if (TryProcessTgScheme(ref afterScheme, remLen, out var res,out int used))
                            { 
                                Current = res;advanced = 5 + used; matched = true; 
                            }
                        }
                    }
                }
            }

            Found:
            if (matched)
            {
                int nextStart = offset + advanced;
                _remaining = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref searchSpaceStart,nextStart), _remaining.Length - nextStart);
                return true;
            }

            offset++; length--;
        }

        Fail:
        _remaining = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool TryProcessTgScheme(ref char start, int length, out LinkResult result, out int consumed)
    {
        result = default;
        consumed = 0;

        // tg://resolve?domain=
        if (length >= 15 && IsResolveDomain(ref start))
        {
            ref char payload = ref Unsafe.Add(ref start, 15);
            int uLen = CountUsernameChars(ref payload, length - 15);
            if (uLen >= 4)
            { 
                result = new(MemoryMarshal.CreateReadOnlySpan(ref payload, uLen));
                consumed = 15 + uLen;
                return true;
            }
        }
        // tg://user?id=
        else if (length >= 8 && IsTgUser(ref start))
        {
            if (TryParseId(ref Unsafe.Add(ref start, 8), length - 8, out var id, out int idLen))
            {
                result = new(id, MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref start, 8), idLen));
                consumed = 8 + idLen;
                return true;
            }
        }
        // tg://openmessage?user_id=
        else if (length >= 20 && IsOpenMessage(ref start))
        {
            if (TryParseId(ref Unsafe.Add(ref start, 20), length - 20, out var id, out int idLen))
            {
                result = new(id, MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref start, 20), idLen));
                consumed = 20 + idLen;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// EN: username length counter. Valid chars: A-Z, a-z, 0-9, _.
    /// RU: Счетчик длины юзернейма. Валидные символы: A-Z, a-z, 0-9, _.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int CountUsernameChars(ref char start, int maxLength)
    {
        int i = 0;

        // EN: Use AVX2 if available for processing 16 chars at once.
        // RU: Используем AVX2 если есть для обработки 16 символов за раз.
        if (Avx2.IsSupported && maxLength >= 16)
        {
            var vOne = Vector256.Create((short)1);
            var vZero = Vector256.Create((short)'0');
            var vNine = Vector256.Create((short)'9');
            var vA = Vector256.Create((short)'A');
            var vZ = Vector256.Create((short)'Z');
            var va = Vector256.Create((short)'a');
            var vz = Vector256.Create((short)'z');
            var vUnderscore = Vector256.Create((short)'_');
            
            while (i <= maxLength - 16)
            {
                var vData = Vector256.LoadUnsafe(ref start, (nuint)i).AsInt16();

                // EN: Determine valid characters using vector comparisons.
                // RU: Определение валидных символов с векторными сравнениями.
                var maskDigit = Avx2.And(Avx2.CompareGreaterThan(vData, Vector256.Subtract(vZero, vOne)), Avx2.CompareGreaterThan(Vector256.Add(vNine, vOne), vData));
                var maskUpper = Avx2.And(Avx2.CompareGreaterThan(vData, Vector256.Subtract(vA, vOne)),Avx2.CompareGreaterThan(Vector256.Add(vZ, vOne), vData));
                var maskLower = Avx2.And(Avx2.CompareGreaterThan(vData, Vector256.Subtract(va, vOne)),Avx2.CompareGreaterThan(Vector256.Add(vz, vOne), vData));
                var maskUnderscore = Avx2.CompareEqual(vData, vUnderscore);

                var isValid = Avx2.Or(Avx2.Or(maskDigit, maskUpper), Avx2.Or(maskLower, maskUnderscore));
                
                // EN: MoveMask creates an int where each bit corresponds to a byte.
                // RU: MoveMask создает int, где каждый бит соответствует байту.
                int mask = Avx2.MoveMask(isValid.AsByte());
                
                // EN: If all chars are valid, mask should be all 1s (-1).
                // RU: Если все символы валидны, маска должна быть всеми единицами (-1).
                if (mask == -1) { i += 16; continue; }

                // EN: Identify the first invalid character.
                // RU: Идентификация первого невалидного символа.
                int invMask = ~mask;
                int badByteIdx = int.TrailingZeroCount(invMask);
                return i + (badByteIdx / 2);
            }
        }

        // EN: Fallback or tail processing.
        // RU: Фаллбэк обработка или обработка хвоста.
        while (i < maxLength)
        {
            char c = Unsafe.Add(ref start, i);
            bool isLetter = (uint)((c | 0x20) - 'a') <= 25u;
            bool isDigit = (uint)(c - '0') <= 9u;
            
            if (!isLetter && !isDigit && c != '_') break;
            i++;
        }
        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool TryParseId(ref char start, int maxLength, out long id, out int length)
    {
        long val = 0; 
        int i = 0;
        
        // EN: Unrolled loop for small ids.
        // RU: Развернутый цикл для коротких id.
        if (maxLength >= 4)
        {
            uint d0 = (uint)(Unsafe.Add(ref start, 0) - '0');
            uint d1 = (uint)(Unsafe.Add(ref start, 1) - '0');
            uint d2 = (uint)(Unsafe.Add(ref start, 2) - '0');
            uint d3 = (uint)(Unsafe.Add(ref start, 3) - '0');
            
            if ((d0 | d1 | d2 | d3) > 9) goto Slow;
            val = d0 * 1000 + d1 * 100 + d2 * 10 + d3;
            i = 4;
        }

        Slow:
        while (i < maxLength)
        {
            uint digit = (uint)(Unsafe.Add(ref start, i) - '0');
            if (digit > 9) break;
            val = val * 10 + digit; 
            i++;
        }
        length = i; 
        id = val;
        return i > 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool IsResolveDomain(ref char ptr)
    {
        // "resolve?domain=" 15 chars
        
        const ulong p1 = 0x006F007300650072; // "reso"
        if ((Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref ptr)) | 0x0020002000200020) != p1) return false;
        const ulong p2 = 0x003F00650076006C; // "lve?"
        if ((Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref ptr, 4))) | 0x0000002000200020) != p2) return false;
        const ulong p3 = 0x0061006D006F0064; // "doma"
        if ((Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref ptr, 8))) | 0x0020002000200020) != p3) return false;

        ref char tail = ref Unsafe.Add(ref ptr, 12);
        // 'i', 'n'
        if (((tail | 0x20) != 'i') || ((Unsafe.Add(ref tail, 1) | 0x20) != 'n')) return false;
        return Unsafe.Add(ref tail, 2) == '=';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool IsTgUser(ref char ptr)
    {
        // "user?id="
        const ulong user = 0x0072006500730075; 
        const ulong q_id = 0x003D00640069003F; 
        ulong v1 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref ptr));
        ulong v2 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref ptr, 4)));
        
        return ((v1 | 0x0020002000200020) == user) &&
               ((v2 | 0x0000002000200000) == q_id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool IsOpenMessage(ref char ptr)
    {
        // "openmessage?user_id="
        // EN: Check "open". RU: Проверка "open".
        const ulong open = 0x006E00650070006F;
        if ((Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref ptr)) | 0x0020002000200020) != open) return false;
        // EN: Check "mess". RU: Проверка "mess".
        const ulong mess = 0x007300730065006D;
        if ((Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref ptr, 4))) | 0x0020002000200020) != mess) return false;
        
        if (Unsafe.Add(ref ptr, 11) != '?') return false;
        if (Unsafe.Add(ref ptr, 16) != '_') return false;
        if (Unsafe.Add(ref ptr, 19) != '=') return false;
        return true; 
    }
}