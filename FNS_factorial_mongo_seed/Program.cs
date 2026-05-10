using System.Security.Cryptography;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace FNS_factorial_mongo_seed;

internal static class Program
{
    private const int FactorialBase = 256;
    private const int MaxFactorial = 1023;
    private const int MaxSourceLengthWithoutBlocks = 1096;
    private const int SchemaVersion = 1;
    private const string DefaultConnectionString = "mongodb://localhost:27017";
    private const string DefaultDatabaseName = "fns_factorials";
    private const string FactorialsCollectionName = "factorials_base256";
    private const string LengthIndexCollectionName = "factorial_length_index";
    private const string MetadataCollectionName = "metadata";
    private const string DigitsOrder = "little-endian";
    private const string FactorialsMetadataId = "factorials_base256";
    private const string LengthIndexMetadataId = "factorial_length_index";

    private static async Task Main(string[] args)
    {
        string connectionString = GetValue(args, 0, "FNS_MONGO_CONNECTION_STRING", DefaultConnectionString);
        string databaseName = GetValue(args, 1, "FNS_FACTORIALS_DATABASE", DefaultDatabaseName);

        MongoClient client = new(connectionString);
        IMongoDatabase adminDatabase = client.GetDatabase("admin");
        await adminDatabase.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));

        IMongoDatabase database = client.GetDatabase(databaseName);
        IMongoCollection<FactorialDocument> factorials = database.GetCollection<FactorialDocument>(FactorialsCollectionName);
        IMongoCollection<LengthIndexDocument> lengthIndex = database.GetCollection<LengthIndexDocument>(LengthIndexCollectionName);
        IMongoCollection<BsonDocument> metadata = database.GetCollection<BsonDocument>(MetadataCollectionName);

        await CreateIndexesAsync(factorials, lengthIndex);

        DateTime now = DateTime.UtcNow;
        byte[] current = [1];
        List<byte[]> factorialDigits = new(MaxFactorial);
        long totalDigits = 0;
        int maxDigits = 0;

        for (int n = 1; n <= MaxFactorial; n++)
        {
            current = MultiplyByIntBase256(current, n);
            byte[] digits = current.ToArray();

            FactorialDocument document = new()
            {
                Id = n,
                Base = FactorialBase,
                DigitsOrder = DigitsOrder,
                Digits = digits,
                DigitsCount = digits.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(digits)),
                SchemaVersion = SchemaVersion,
                UpdatedAtUtc = now
            };

            await factorials.ReplaceOneAsync(
                item => item.Id == n,
                document,
                new ReplaceOptions { IsUpsert = true });

            totalDigits += digits.Length;
            if (digits.Length > maxDigits)
                maxDigits = digits.Length;

            factorialDigits.Add(digits);

            if (n % 100 == 0)
                Console.WriteLine($"Saved {n}! ({digits.Length} base-256 digits).");
        }

        for (int length = 1; length <= MaxSourceLengthWithoutBlocks; length++)
        {
            byte[] worstNumber = BuildWorstNumber(length);
            int coefficientsCount = FindRequiredFactorialCoefficientsCount(factorialDigits, worstNumber);

            LengthIndexDocument document = new()
            {
                Id = length,
                Base = FactorialBase,
                SourceLength = length,
                FactorialCoefficientsCount = coefficientsCount,
                SchemaVersion = SchemaVersion,
                UpdatedAtUtc = now
            };

            await lengthIndex.ReplaceOneAsync(
                item => item.Id == length,
                document,
                new ReplaceOptions { IsUpsert = true });
        }

        BsonDocument metadataDocument = new()
        {
            ["_id"] = FactorialsMetadataId,
            ["base"] = FactorialBase,
            ["maxFactorial"] = MaxFactorial,
            ["count"] = MaxFactorial,
            ["digitsOrder"] = DigitsOrder,
            ["collectionName"] = FactorialsCollectionName,
            ["totalDigits"] = totalDigits,
            ["maxDigits"] = maxDigits,
            ["schemaVersion"] = SchemaVersion,
            ["updatedAtUtc"] = now
        };

        await metadata.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", FactorialsMetadataId),
            metadataDocument,
            new ReplaceOptions { IsUpsert = true });

        BsonDocument lengthIndexMetadata = new()
        {
            ["_id"] = LengthIndexMetadataId,
            ["base"] = FactorialBase,
            ["maxSourceLength"] = MaxSourceLengthWithoutBlocks,
            ["count"] = MaxSourceLengthWithoutBlocks,
            ["collectionName"] = LengthIndexCollectionName,
            ["schemaVersion"] = SchemaVersion,
            ["updatedAtUtc"] = now
        };

        await metadata.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", LengthIndexMetadataId),
            lengthIndexMetadata,
            new ReplaceOptions { IsUpsert = true });

        long storedCount = await factorials.CountDocumentsAsync(item => item.Base == FactorialBase);
        long lengthIndexCount = await lengthIndex.CountDocumentsAsync(item => item.Base == FactorialBase);

        Console.WriteLine($"MongoDB: {connectionString}");
        Console.WriteLine($"Database: {databaseName}");
        Console.WriteLine($"Collection: {FactorialsCollectionName}");
        Console.WriteLine($"Stored factorials: {storedCount}");
        Console.WriteLine($"Length index collection: {LengthIndexCollectionName}");
        Console.WriteLine($"Stored K(L) rows: {lengthIndexCount}");
        Console.WriteLine($"Max factorial: {MaxFactorial}!");
        Console.WriteLine($"Max base-256 digits: {maxDigits}");
        Console.WriteLine($"Total base-256 digits: {totalDigits}");
    }

    private static async Task CreateIndexesAsync(
        IMongoCollection<FactorialDocument> factorials,
        IMongoCollection<LengthIndexDocument> lengthIndex)
    {
        CreateIndexModel<FactorialDocument> baseIndex = new(
            Builders<FactorialDocument>.IndexKeys.Ascending(item => item.Base),
            new CreateIndexOptions { Name = "idx_base" });

        CreateIndexModel<LengthIndexDocument> lengthBaseIndex = new(
            Builders<LengthIndexDocument>.IndexKeys.Ascending(item => item.Base),
            new CreateIndexOptions { Name = "idx_base" });

        await factorials.Indexes.CreateOneAsync(baseIndex);
        await lengthIndex.Indexes.CreateOneAsync(lengthBaseIndex);
    }

    private static byte[] MultiplyByIntBase256(byte[] number, int multiplier)
    {
        if (multiplier < 0)
            throw new ArgumentOutOfRangeException(nameof(multiplier));

        if (multiplier == 0 || number.Length == 0)
            return [0];

        List<byte> result = new(number.Length + 2);
        int carry = 0;

        foreach (byte digit in number)
        {
            int product = digit * multiplier + carry;
            result.Add((byte)(product & 255));
            carry = product >> 8;
        }

        while (carry > 0)
        {
            result.Add((byte)(carry & 255));
            carry >>= 8;
        }

        return result.ToArray();
    }

    private static byte[] BuildWorstNumber(int sourceLength)
    {
        byte[] result = new byte[sourceLength];
        Array.Fill(result, byte.MaxValue);
        return result;
    }

    private static int FindRequiredFactorialCoefficientsCount(List<byte[]> factorials, byte[] value)
    {
        int left = 0;
        int right = factorials.Count - 1;
        int best = -1;

        while (left <= right)
        {
            int middle = (left + right) / 2;
            if (CompareBase256(factorials[middle], value) <= 0)
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

    private static int CompareBase256(byte[] left, byte[] right)
    {
        int leftLast = TrimLastNonZero(left);
        int rightLast = TrimLastNonZero(right);

        if (leftLast != rightLast)
            return leftLast < rightLast ? -1 : 1;

        for (int i = leftLast; i >= 0; i--)
        {
            if (left[i] == right[i])
                continue;

            return left[i] < right[i] ? -1 : 1;
        }

        return 0;
    }

    private static int TrimLastNonZero(byte[] value)
    {
        int index = value.Length - 1;
        while (index > 0 && value[index] == 0)
            index--;

        return index;
    }

    private static string GetValue(string[] args, int index, string environmentVariable, string defaultValue)
    {
        if (args.Length > index && !string.IsNullOrWhiteSpace(args[index]))
            return args[index].Trim();

        string? environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue.Trim();

        return defaultValue;
    }
}

internal sealed class FactorialDocument
{
    [BsonId]
    public int Id { get; init; }

    [BsonElement("base")]
    public int Base { get; init; }

    [BsonElement("digitsOrder")]
    public string DigitsOrder { get; init; } = "";

    [BsonElement("digits")]
    public byte[] Digits { get; init; } = [];

    [BsonElement("digitsCount")]
    public int DigitsCount { get; init; }

    [BsonElement("sha256")]
    public string Sha256 { get; init; } = "";

    [BsonElement("schemaVersion")]
    public int SchemaVersion { get; init; }

    [BsonElement("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; init; }
}

internal sealed class LengthIndexDocument
{
    [BsonId]
    public int Id { get; init; }

    [BsonElement("base")]
    public int Base { get; init; }

    [BsonElement("sourceLength")]
    public int SourceLength { get; init; }

    [BsonElement("factorialCoefficientsCount")]
    public int FactorialCoefficientsCount { get; init; }

    [BsonElement("schemaVersion")]
    public int SchemaVersion { get; init; }

    [BsonElement("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; init; }
}
