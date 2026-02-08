# UniCortex

Unity on-device unified search engine library.

Provides Dense vector search (HNSW) + Sparse vector search + BM25 full-text search + Hybrid search (RRF) in Pure C# with Burst/Jobs optimization, running on all platforms including WebGL.

## Features

| Feature | Description |
|---|---|
| **Dense Vector Search** | HNSW algorithm with SIMD-optimized distance functions |
| **Sparse Vector Search** | Inverted index based sparse vector similarity |
| **BM25 Full-Text Search** | Inverted index + tokenizer (ASCII, CJK, Hiragana, Katakana) |
| **Hybrid Search (RRF)** | Reciprocal Rank Fusion merging multiple search results |
| **Scalar Filter** | Metadata filtering (int, float, bool) |
| **Persistence** | Binary serialization with CRC32 integrity check |

## Requirements

- Unity 6000.0 or later
- Burst 1.8.0+
- Collections 2.1.0+
- Mathematics 1.3.0+

## Installation

Add via Unity Package Manager using git URL:

```
https://github.com/YOUR_ORG/UniCortex.git?path=Assets/UniCortex
```

Or add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unicortex.runtime": "https://github.com/YOUR_ORG/UniCortex.git?path=Assets/UniCortex"
  }
}
```

## Quick Start

```csharp
using UniCortex;
using Unity.Collections;

// Create database
var config = DatabaseConfig.Default;
var db = new UniCortexDatabase(config);

// Add documents
var vector = new NativeArray<float>(128, Allocator.Temp);
// ... fill vector data ...
db.Add(1, denseVector: vector);
vector.Dispose();

// Build index
db.Build();

// Search
var query = new NativeArray<float>(128, Allocator.Temp);
// ... fill query data ...
var results = db.SearchDense(query, new SearchParams { K = 10, EfSearch = 50 });

// Process results
for (int i = 0; i < results.Length; i++)
{
    var ext = db.GetExternalId(results[i].InternalId);
    // results[i].Score - lower is more relevant
}

results.Dispose();
query.Dispose();
db.Dispose();
```

## Distance Functions

| Type | Description |
|---|---|
| `EuclideanSq` | Squared Euclidean distance (default) |
| `Cosine` | Cosine distance (1 - cosine similarity), assumes normalized vectors |
| `DotProduct` | Negative dot product (-dot), higher similarity = lower score |

Configure via `DatabaseConfig.DistanceType`.

## Architecture

```
UniCortex
├── Core         - Shared data structures, memory management
├── Hnsw         - HNSW dense vector search
├── Sparse       - Sparse vector search
├── FullText     - BM25 full-text search with tokenizer
├── Hybrid       - RRF hybrid search orchestrator
├── Filter       - Scalar metadata filter
└── Persistence  - Binary save/load with CRC32
```

## License

Apache License 2.0 - see [LICENSE.md](LICENSE.md) for details.
