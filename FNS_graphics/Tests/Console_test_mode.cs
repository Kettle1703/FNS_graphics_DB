using System;
using System.IO;
using System.Runtime.InteropServices;
using FNS_rebuild;

namespace FNS_graphics.Tests
{
    internal static class Console_test_mode
    {
        private const string StochasticTestsArgument = "--stochastic-tests";
        private const uint AttachParentProcess = 0xFFFFFFFF;

        internal static bool Is_requested(string[] args)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, StochasticTestsArgument, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static int Run()
        {
            Ensure_console();
            return Run(new Test_options());
        }

        internal static int Run(string[] args)
        {
            Ensure_console();
            return Run(Test_options.Parse(args));
        }

        private static int Run(Test_options options)
        {
            Console.WriteLine("Стохастическое тестирование ядра FNS запущено.");
            Console.WriteLine("Источник статических данных: MongoDB.");
            Console.WriteLine($"Диапазон длин: {options.Min_length}..{options.Max_length}");
            Console.WriteLine($"Шаг по длине: {options.Length_step}");
            Console.WriteLine($"Повторов на длину: {options.Tests_per_length}");
            Console.WriteLine();

            int exitCode;
            try
            {
                Strategy_wrapper wrapper = new(new Factorial_strategy());
                bool success = Stochastic_tests_encryption.Run_round_trip_tests(
                    wrapper,
                    options.Min_length,
                    options.Max_length,
                    options.Tests_per_length,
                    options.Progress_step,
                    options.Length_step);

                Console.WriteLine();
                Console.WriteLine(success
                    ? "Итог: стохастическое тестирование пройдено."
                    : "Итог: стохастическое тестирование завершилось ошибкой.");

                exitCode = success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Итог: стохастическое тестирование упало с исключением.");
                Console.WriteLine(ex);
                exitCode = 1;
            }

            if (options.Pause_on_exit && !Console.IsInputRedirected)
            {
                Console.WriteLine();
                Console.WriteLine("Нажмите Enter, чтобы закрыть консоль.");
                Console.ReadLine();
            }

            return exitCode;
        }

        private static void Ensure_console()
        {
            if (Console.IsOutputRedirected)
                return;

            if (!AttachConsole(AttachParentProcess))
                AllocConsole();

            StreamWriter output = new(Console.OpenStandardOutput()) { AutoFlush = true };
            StreamWriter error = new(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetOut(output);
            Console.SetError(error);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        private sealed class Test_options
        {
            internal int Min_length { get; private init; } = Stochastic_tests_encryption.Fast_min_length;
            internal int Max_length { get; private init; } = Stochastic_tests_encryption.Fast_max_length_with_blocks;
            internal int Tests_per_length { get; private init; } = Stochastic_tests_encryption.Fast_tests_per_length;
            internal int Progress_step { get; private init; } = Stochastic_tests_encryption.Fast_progress_step;
            internal int Length_step { get; private init; } = 1;
            internal bool Pause_on_exit { get; private init; }

            internal static Test_options Parse(string[] args)
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
                    if (string.Equals(arg, "--stochastic-tests", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.Equals(arg, "--pause-on-exit", StringComparison.OrdinalIgnoreCase))
                    {
                        pauseOnExit = true;
                        continue;
                    }

                    if (Try_read_int(args, ref i, "--min-length", arg, out int min))
                    {
                        minLength = min;
                        continue;
                    }

                    if (Try_read_int(args, ref i, "--max-length", arg, out int max))
                    {
                        maxLength = max;
                        continue;
                    }

                    if (Try_read_int(args, ref i, "--tests-per-length", arg, out int tests))
                    {
                        testsPerLength = tests;
                        continue;
                    }

                    if (Try_read_int(args, ref i, "--progress-step", arg, out int progress))
                    {
                        progressStep = progress;
                        continue;
                    }

                    if (Try_read_int(args, ref i, "--length-step", arg, out int step))
                    {
                        lengthStep = step;
                        continue;
                    }

                    throw new ArgumentException($"Неизвестный аргумент тестового режима: {arg}");
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

                return new Test_options
                {
                    Min_length = minLength,
                    Max_length = maxLength,
                    Tests_per_length = testsPerLength,
                    Progress_step = progressStep,
                    Length_step = lengthStep,
                    Pause_on_exit = pauseOnExit
                };
            }

            private static bool Try_read_int(string[] args, ref int index, string option, string currentArg, out int value)
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
}
