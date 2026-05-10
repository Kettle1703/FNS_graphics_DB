using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Digit = System.UInt16;

namespace FNS_rebuild
{
    internal static class Factorial_table_storage
    {
        private const string DefaultConnectionString = "mongodb://localhost:27017";
        private const string DefaultDatabaseName = "fns_factorials";
        private const string FactorialsCollectionName = "factorials_base256";
        private const string LengthIndexCollectionName = "factorial_length_index";
        private const string DigitsOrder = "little-endian";
        private const int StoredBase = 256;

        private static readonly object sync = new();
        private static bool factorialTableLoaded;
        private static bool lengthIndexLoaded;

        internal static void Ensure_loaded(int expectedBase, int maxFactorial)
        {
            if (factorialTableLoaded && Factorial_encoding.factorial_base == expectedBase && Factorial_encoding.factorial_table.Count >= maxFactorial)
                return;

            lock (sync)
            {
                if (factorialTableLoaded && Factorial_encoding.factorial_base == expectedBase && Factorial_encoding.factorial_table.Count >= maxFactorial)
                    return;

                if (expectedBase != StoredBase)
                    throw new InvalidOperationException($"Таблица факториалов в MongoDB рассчитана для основания {StoredBase}, текущее основание: {expectedBase}.");

                List<FactorialDocument> documents = Load_documents(expectedBase, maxFactorial);
                if (documents.Count != maxFactorial)
                    throw new InvalidOperationException($"В MongoDB найдено {documents.Count} факториалов, ожидалось {maxFactorial}.");

                Factorial_encoding.factorial_table.Clear();
                Factorial_encoding.factorial_length_to_index.Clear();
                Factorial_encoding.factorial_base = (Digit)expectedBase;

                for (int i = 0; i < documents.Count; i++)
                {
                    FactorialDocument document = documents[i];
                    int expectedId = i + 1;
                    if (document.Id != expectedId)
                        throw new InvalidOperationException($"Нарушена последовательность таблицы факториалов: ожидался {expectedId}!, найден {document.Id}!.");

                    if (document.Base != expectedBase || document.DigitsOrder != DigitsOrder || document.Digits.Length != document.DigitsCount)
                        throw new InvalidOperationException($"Некорректная запись факториала {document.Id}! в MongoDB.");

                    Factorial_encoding.factorial_table.Add(To_digits(document.Digits));
                }

                factorialTableLoaded = true;
            }
        }

        internal static void Ensure_length_index_loaded(int expectedBase, int maxSourceLength)
        {
            if (lengthIndexLoaded && Factorial_strategy.source_length_to_factorial_coefficients_count.Count >= maxSourceLength)
                return;

            lock (sync)
            {
                if (lengthIndexLoaded && Factorial_strategy.source_length_to_factorial_coefficients_count.Count >= maxSourceLength)
                    return;

                if (expectedBase != StoredBase)
                    throw new InvalidOperationException($"Таблица K(L) в MongoDB рассчитана для основания {StoredBase}, текущее основание: {expectedBase}.");

                List<LengthIndexDocument> documents = Load_length_index_documents(expectedBase, maxSourceLength);
                if (documents.Count != maxSourceLength)
                    throw new InvalidOperationException($"В MongoDB найдено {documents.Count} записей K(L), ожидалось {maxSourceLength}.");

                Factorial_strategy.source_length_to_factorial_coefficients_count.Clear();

                for (int i = 0; i < documents.Count; i++)
                {
                    LengthIndexDocument document = documents[i];
                    int expectedLength = i + 1;
                    if (document.Id != expectedLength || document.SourceLength != expectedLength)
                        throw new InvalidOperationException($"Нарушена последовательность таблицы K(L): ожидалась длина {expectedLength}, найдена {document.SourceLength}.");

                    if (document.Base != expectedBase || document.FactorialCoefficientsCount < 1)
                        throw new InvalidOperationException($"Некорректная запись K(L) для длины {document.SourceLength} в MongoDB.");

                    Factorial_strategy.source_length_to_factorial_coefficients_count[document.SourceLength] = document.FactorialCoefficientsCount;
                }

                lengthIndexLoaded = true;
            }
        }

        internal static void Reset_loaded_state()
        {
            lock (sync)
            {
                factorialTableLoaded = false;
                lengthIndexLoaded = false;
            }
        }

        private static List<FactorialDocument> Load_documents(int expectedBase, int maxFactorial)
        {
            string connectionString = Get_config_value("FNS_MONGO_CONNECTION_STRING", DefaultConnectionString);
            string databaseName = Get_config_value("FNS_FACTORIALS_DATABASE", DefaultDatabaseName);

            MongoClient client = new(connectionString);
            IMongoDatabase database = client.GetDatabase(databaseName);
            IMongoCollection<FactorialDocument> collection = database.GetCollection<FactorialDocument>(FactorialsCollectionName);

            FilterDefinition<FactorialDocument> filter =
                Builders<FactorialDocument>.Filter.Eq(item => item.Base, expectedBase) &
                Builders<FactorialDocument>.Filter.Gte(item => item.Id, 1) &
                Builders<FactorialDocument>.Filter.Lte(item => item.Id, maxFactorial);

            return collection
                .Find(filter)
                .Sort(Builders<FactorialDocument>.Sort.Ascending(item => item.Id))
                .ToList();
        }

        private static List<LengthIndexDocument> Load_length_index_documents(int expectedBase, int maxSourceLength)
        {
            string connectionString = Get_config_value("FNS_MONGO_CONNECTION_STRING", DefaultConnectionString);
            string databaseName = Get_config_value("FNS_FACTORIALS_DATABASE", DefaultDatabaseName);

            MongoClient client = new(connectionString);
            IMongoDatabase database = client.GetDatabase(databaseName);
            IMongoCollection<LengthIndexDocument> collection = database.GetCollection<LengthIndexDocument>(LengthIndexCollectionName);

            FilterDefinition<LengthIndexDocument> filter =
                Builders<LengthIndexDocument>.Filter.Eq(item => item.Base, expectedBase) &
                Builders<LengthIndexDocument>.Filter.Gte(item => item.Id, 1) &
                Builders<LengthIndexDocument>.Filter.Lte(item => item.Id, maxSourceLength);

            return collection
                .Find(filter)
                .Sort(Builders<LengthIndexDocument>.Sort.Ascending(item => item.Id))
                .ToList();
        }

        private static Digit[] To_digits(byte[] bytes)
        {
            Digit[] result = new Digit[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                result[i] = bytes[i];

            return result;
        }

        private static string Get_config_value(string environmentVariable, string defaultValue)
        {
            string? value = Environment.GetEnvironmentVariable(environmentVariable);
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        [BsonIgnoreExtraElements]
        private sealed class FactorialDocument
        {
            [BsonId]
            public int Id { get; set; }

            [BsonElement("base")]
            public int Base { get; set; }

            [BsonElement("digitsOrder")]
            public string DigitsOrder { get; set; } = "";

            [BsonElement("digits")]
            public byte[] Digits { get; set; } = [];

            [BsonElement("digitsCount")]
            public int DigitsCount { get; set; }
        }

        [BsonIgnoreExtraElements]
        private sealed class LengthIndexDocument
        {
            [BsonId]
            public int Id { get; set; }

            [BsonElement("base")]
            public int Base { get; set; }

            [BsonElement("sourceLength")]
            public int SourceLength { get; set; }

            [BsonElement("factorialCoefficientsCount")]
            public int FactorialCoefficientsCount { get; set; }
        }
    }
}
