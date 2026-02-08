# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2025-01-01

### Added
- Dense vector search using HNSW (Hierarchical Navigable Small World) algorithm
- Sparse vector search with inverted index
- BM25 full-text search with UTF-8 tokenizer (ASCII + CJK/Hiragana/Katakana)
- Hybrid search with RRF (Reciprocal Rank Fusion) result merging
- Scalar metadata filtering (int, float, bool)
- Binary persistence with CRC32 integrity check (Save/Load)
- Burst/Jobs compatible data structures throughout
- Support for EuclideanSq, Cosine, and DotProduct distance functions
- Soft delete with batch rebuild pattern
- NaN/Inf input validation on all vector inputs
- UPM package configuration
