using FNS_rebuild;

namespace FNS_stochastic_tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        TestOptions options;
        try
        {
            options = TestOptions.Parse(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }

        Console.WriteLine("Стохастическое тестирование ядра FNS запущено.");
        Console.WriteLine("Источник статических данных: MongoDB.");
        Console.WriteLine($"Диапазон длин: {options.MinLength}..{options.MaxLength}");
        Console.WriteLine($"Шаг по длине: {options.LengthStep}");
        Console.WriteLine($"Повторов на длину: {options.TestsPerLength}");
        Console.WriteLine();

        try
        {
            Strategy_wrapper wrapper = new(new Factorial_strategy());
            bool success = Stochastic_tests_encryption.Run_round_trip_tests(
                wrapper,
                options.MinLength,
                options.MaxLength,
                options.TestsPerLength,
                options.ProgressStep,
                options.LengthStep);

            Console.WriteLine();
            Console.WriteLine(success
                ? "Итог: стохастическое тестирование пройдено."
                : "Итог: стохастическое тестирование завершилось ошибкой.");

            return Exit(success ? 0 : 1, options.PauseOnExit);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("Итог: стохастическое тестирование упало с исключением.");
            Console.WriteLine(ex);
            return Exit(1, options.PauseOnExit);
        }
    }

    private static int Exit(int exitCode, bool pauseOnExit)
    {
        if (pauseOnExit)
        {
            Console.WriteLine();
            Console.WriteLine("Нажмите Enter для выхода...");
            Console.ReadLine();
        }

        return exitCode;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Пример:");
        Console.WriteLine("dotnet run --project .\\FNS_stochastic_tests\\FNS_stochastic_tests.csproj -- --tests-per-length 3 --length-step 3 --progress-step 100");
    }

    private sealed class TestOptions
    {
        internal int MinLength { get; private init; } = Stochastic_tests_encryption.Fast_min_length;
        internal int MaxLength { get; private init; } = Stochastic_tests_encryption.Fast_max_length_with_blocks;
        internal int TestsPerLength { get; private init; } = Stochastic_tests_encryption.Fast_tests_per_length;
        internal int ProgressStep { get; private init; } = Stochastic_tests_encryption.Fast_progress_step;
        internal int LengthStep { get; private init; } = 1;
        internal bool PauseOnExit { get; private init; }

        internal static TestOptions Parse(string[] args)
        {
            int minLength = Stochastic_tests_encryption.Fast_min_length;
            int maxLength = Stochastic_tests_encryption.Fast_max_length_with_blocks;
            int testsPerLength = Stochastic_tests_encryption.Fast_tests_per_length;
            int progressStep = Stochastic_tests_encryption.Fast_progress_step;
            int lengthStep = 1;
            bool pauseOnExit = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (string.Equals(arg, "--pause-on-exit", StringComparison.OrdinalIgnoreCase))
                {
                    pauseOnExit = true;
                    continue;
                }

                if (TryReadInt(args, ref i, "--min-length", arg, out int min))
                {
                    minLength = min;
                    continue;
                }

                if (TryReadInt(args, ref i, "--max-length", arg, out int max))
                {
                    maxLength = max;
                    continue;
                }

                if (TryReadInt(args, ref i, "--tests-per-length", arg, out int tests))
                {
                    testsPerLength = tests;
                    continue;
                }

                if (TryReadInt(args, ref i, "--progress-step", arg, out int progress))
                {
                    progressStep = progress;
                    continue;
                }

                if (TryReadInt(args, ref i, "--length-step", arg, out int step))
                {
                    lengthStep = step;
                    continue;
                }

                throw new ArgumentException($"Неизвестный аргумент: {arg}");
            }

            if (minLength < 1)
                throw new ArgumentOutOfRangeException(nameof(minLength), "Минимальная длина должна быть >= 1.");
            if (maxLength < minLength)
                throw new ArgumentOutOfRangeException(nameof(maxLength), "Максимальная длина должна быть >= минимальной.");
            if (testsPerLength < 1)
                throw new ArgumentOutOfRangeException(nameof(testsPerLength), "Повторов на длину должно быть >= 1.");
            if (progressStep < 1)
                throw new ArgumentOutOfRangeException(nameof(progressStep), "Шаг прогресса должен быть >= 1.");
            if (lengthStep < 1)
                throw new ArgumentOutOfRangeException(nameof(lengthStep), "Шаг длины должен быть >= 1.");

            return new TestOptions
            {
                MinLength = minLength,
                MaxLength = maxLength,
                TestsPerLength = testsPerLength,
                ProgressStep = progressStep,
                LengthStep = lengthStep,
                PauseOnExit = pauseOnExit
            };
        }

        private static bool TryReadInt(string[] args, ref int index, string option, string currentArg, out int value)
        {
            value = 0;

            string prefix = option + "=";
            if (currentArg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return int.TryParse(currentArg[prefix.Length..], out value);

            if (!string.Equals(currentArg, option, StringComparison.OrdinalIgnoreCase))
                return false;

            if (index + 1 >= args.Length)
                throw new ArgumentException($"Для {option} нужно указать число.");

            index++;
            if (!int.TryParse(args[index], out value))
                throw new ArgumentException($"Для {option} указано не число: {args[index]}");

            return true;
        }
    }
}
