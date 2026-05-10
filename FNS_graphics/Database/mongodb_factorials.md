# MongoDB factorial table

The factorial table is stored in local MongoDB as base-256 digits.

Default connection:

```text
mongodb://localhost:27017
```

Default database and collections:

```text
database: fns_factorials
factorials collection: factorials_base256
length index collection: factorial_length_index
metadata collection: metadata
```

Factorial document format:

```json
{
  "_id": 1023,
  "base": 256,
  "digitsOrder": "little-endian",
  "digits": "<BSON binary byte[]>",
  "digitsCount": 1095,
  "sha256": "<hex hash>",
  "schemaVersion": 1,
  "updatedAtUtc": "<UTC timestamp>"
}
```

`digits` stores the same order used by the long arithmetic code: the least significant digit goes first.

Length index document format:

```json
{
  "_id": 1096,
  "base": 256,
  "sourceLength": 1096,
  "factorialCoefficientsCount": 1023,
  "schemaVersion": 1,
  "updatedAtUtc": "<UTC timestamp>"
}
```

`factorialCoefficientsCount` is K(L): the required count of FNS coefficients for a source block of length L.

Seeder command:

```powershell
dotnet run --project .\FNS_factorial_mongo_seed\FNS_factorial_mongo_seed.csproj
```

Optional arguments:

```powershell
dotnet run --project .\FNS_factorial_mongo_seed\FNS_factorial_mongo_seed.csproj -- "mongodb://localhost:27017" "fns_factorials"
```

The WPF application reads the same MongoDB table on demand. Optional environment variables:

```text
FNS_MONGO_CONNECTION_STRING=mongodb://localhost:27017
FNS_FACTORIALS_DATABASE=fns_factorials
```

The legacy C# factorial-table and K(L) calculations are kept in code for history, but the active path loads the precomputed data from MongoDB.
