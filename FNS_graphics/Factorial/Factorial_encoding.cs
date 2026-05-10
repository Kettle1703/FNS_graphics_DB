using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static System.Console;
using Digit = System.UInt16;
namespace FNS_rebuild
{
    internal class Factorial_encoding
    {
        public static Dictionary<char, Digit> char_to_number = [];  // связь символа из алфавита и его порядкового номера в алфавите

        internal const int Precomputed_factorials_count = 1023;  // таблица хранит 1!..1023! в MongoDB

        public static List<Digit[]> factorial_table = [];  // вектор предрасчитанных факториалов из MongoDB
        public static Digit factorial_base = 0;  // основание системы счисления, в которой посчитана таблица факториалов
        public static Dictionary<int, int> factorial_length_to_index = [];  // [LEGACY] заменён MongoDB-коллекцией K(L)

        public static void Create_factorial_table(int max_length)
        {
            // Историческая точка входа оставлена для совместимости.
            // Раньше метод считал факториалы в C# через последовательное умножение.
            // Сейчас рабочий путь не пересчитывает таблицу, а загружает предрасчитанные 1!..1023! из MongoDB.

            Ensure_factorial_table_loaded();
        }

        [Obsolete("Рабочий путь использует MongoDB-таблицу. Метод оставлен только как legacy-реализация расчёта факториалов в C#.")]
        static void Create_factorial_table_legacy_calculated(int max_length)
        {
            // ОТКЛЮЧЁННЫЙ LEGACY-КОД:
            // создаёт или продолжает таблицу факториалов до длины max_length разрядов через умножение в C#.
            // Не вызывается рабочим шифрованием, потому что таблица берётся из MongoDB.
            if (max_length < 1)
                return;

            Reset_factorial_table_cache();
            factorial_base = Factorial_strategy.power;
            Digit[] current;
            int n;

            if (factorial_table.Count == 0)
            {
                current = [1];
                factorial_table.Add(current);
                n = 2;
            }
            else
            {
                current = factorial_table[^1];
                n = factorial_table.Count + 1;
            }

            if (current.Length > max_length)
                return;

            while (n <= Digit.MaxValue)
            {
                Digit[] next = Long_math.Multiply_by_digit(current, (Digit)n, 0);
                if (next.Length > max_length)
                    break;

                factorial_table.Add(next);
                current = next;
                n++;
            }

            Rebuild_length_index();
        }

        internal static void Rebuild_length_index()
        {
            // LEGACY: производный индекс по длинам факториалов.
            // Рабочий путь использует MongoDB-коллекцию factorial_length_index с готовыми K(L).

            // строит словарь: длина -> индекс последнего факториала с такой длиной
            factorial_length_to_index.Clear();
            if (factorial_table.Count == 0)
                return;

            int max_len = factorial_table[^1].Length;
            int index = 0;

            for (int length = 1; length <= max_len; length++)
            {
                while (index < factorial_table.Count && factorial_table[index].Length <= length)
                    index++;

                int value = index - 1;
                if (value < 0)
                    value = 0;

                factorial_length_to_index[length] = value;
            }
        }

        // [НЕ ИСПОЛЬЗУЕТСЯ В ТЕКУЩЕЙ ВЕРСИИ] см. блок-комментарий выше
        static int Convert_to_int(Digit[] digits)
        {
            // переводит массив разрядов в int (обычная система), порядок разрядов инвертированный
            // нужен был старой реализации для получения коэффициента factoradic
            int base_value = Factorial_strategy.power;
            int value = 0;
            for (int i = digits.Length - 1; i >= 0; i--)
                value = value * base_value + digits[i];

            return value;
        }

        internal static void Ensure_factorial_table_loaded()
        {
            Factorial_table_storage.Ensure_loaded(Factorial_strategy.power, Precomputed_factorials_count);
        }

        internal static void Reset_factorial_table_cache()
        {
            factorial_table.Clear();
            factorial_length_to_index.Clear();
            factorial_base = 0;
            Factorial_table_storage.Reset_loaded_state();
        }

        [Obsolete("K(L) хранится в MongoDB-коллекции factorial_length_index. Метод оставлен только для проверки старой схемы расчёта.")]
        internal static int Get_required_factorial_coefficients_count(Digit[] value)
        {
            // Возвращает K: минимальное количество коэффициентов ФСС для числа value.
            // LEGACY: раньше это считалось сравнением с таблицей факториалов.
            // Сейчас рабочий путь читает готовое K(L) из MongoDB.

            if (value.Length == 0)
                return 1;
            if (value.Length == 1 && value[0] == 0)
                return 1;

            Ensure_factorial_table_loaded();

            int left = 0;
            int right = factorial_table.Count - 1;
            int best = -1;

            while (left <= right)
            {
                int middle = (left + right) / 2;
                Digit[] factorial = factorial_table[middle];

                if (Less_or_equal(factorial, value))
                {
                    best = middle;
                    left = middle + 1;
                }
                else
                {
                    right = middle - 1;
                }
            }

            return best < 0 ? 1 : best + 1;
        }

        static bool Less_or_equal(Digit[] left, Digit[] right)
        {
            return !Long_math.Less_than(right, left);
        }

        public static Digit[] Convert_to_factorial_system(Digit[] value)
        {
            // переводит число в факториальную систему счисления, результат в инвертированном порядке
            // (result[0] = c_1, result[1] = c_2, ..., result[M-1] = c_M)
            //
            // алгоритм "снизу вверх": последовательно делим число на 2, 3, 4, ...
            // остаток на каждом шаге — это коэффициент c_i при i!.
            // Правило 0 <= c_i <= i соблюдается автоматически: c_i — это остаток от деления на (i+1).
            // Сложность: O(M^2 log M) digit-операций; таблица факториалов не требуется.
            if (value.Length == 0)
                return [0];
            if (value.Length == 1 && value[0] == 0)
                return [0];

            List<Digit> result = [];
            Digit[] current = value;
            int divisor = 2;

            while (current.Length > 1 || current[0] != 0)
            {
                current = Long_math.Divide_by_int(current, divisor, out int remainder);
                result.Add((Digit)remainder);
                divisor++;
            }

            return result.ToArray();
        }


    }
}
