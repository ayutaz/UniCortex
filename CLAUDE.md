# UniCortex

Unity 向けオンデバイス統合検索エンジンライブラリ。
Dense ベクトル検索 (HNSW) + Sparse ベクトル検索 + BM25 全文検索 + ハイブリッド検索 (RRF) を
Pure C# (Burst/Jobs 最適化) で提供し、WebGL を含む全プラットフォームで動作する。

## 技術スタック

- **Unity**: 6000.3.6f1 (Unity 6 LTS)
- **言語**: C# + Burst Compiler + Jobs System + Unity.Mathematics
- **メモリ**: NativeArray / NativeContainer (Allocator.Persistent)
- **永続化**: System.IO.MemoryMappedFiles
- **配布**: UPM パッケージ
- **ターゲット規模**: ~50,000 ベクトル

## アーキテクチャ

```
UniCortex
├── Dense ベクトル検索 (HNSW アルゴリズム, Burst/SIMD 最適化)
├── Sparse ベクトル検索
├── BM25 全文検索 (転置インデックス + トークナイザ)
├── ハイブリッド検索 (RRF ReRanker で結果統合)
├── スカラーフィルタ
├── 永続化 (MemoryMappedFile)
└── API: Add / Search / Update / Delete / Filter
```

### コンポーネント概要

| コンポーネント | 説明 |
|---|---|
| **HNSW Index** | Hierarchical Navigable Small World グラフによる近似最近傍探索 |
| **Sparse Index** | キーワード特徴量ベースのスパースベクトル検索 |
| **BM25 Engine** | 転置インデックス + トークナイザによる全文検索 |
| **RRF ReRanker** | Reciprocal Rank Fusion で複数検索結果を統合 |
| **Scalar Filter** | `price >= 1000 AND category == "weapon"` 等のフィルタ付き検索 |
| **Persistence** | MemoryMappedFile による永続化・遅延読み込み |

## 開発上の制約

### Burst Compiler 制約
- マネージドメモリの動的割り当て (GC 対象) 不可
- 仮想メソッド呼び出し不可
- マネージド例外処理不可
- 代替: NativeContainer, 関数ポインタ, 値型に制限

### IL2CPP (AOT) 制約
- Reflection.Emit 不可 (動的コード生成不可)
- 複雑なジェネリクスはコード膨張を招く → `where T : struct` 等で制約
- コードストリッピング対策 → link.xml で保護

### メモリレイアウト
- **ベクトルデータ**: SoA (Structure of Arrays) — SIMD 効率最大化
- **グラフ構造**: AoS (Array of Structures) — キャッシュ局所性優先

### プラットフォーム対応
- WebGL 対応のため Pure C# 必須 (ネイティブプラグイン不可)
- 距離計算は `Unity.Mathematics` の `float4` で SIMD auto-vectorize
- Burst がプラットフォーム別に SSE/AVX (x86) および NEON (ARM) を自動生成

## プロジェクト構成

```
Assets/
├── UniCortex/
│   ├── Runtime/           # ランタイムコード
│   │   ├── UniCortex.Runtime.asmdef
│   │   ├── Core/          # 共通データ構造・メモリ管理
│   │   ├── Hnsw/          # HNSW Dense ベクトル検索
│   │   ├── Sparse/        # Sparse ベクトル検索
│   │   ├── FullText/      # BM25 全文検索・転置インデックス
│   │   ├── Hybrid/        # RRF ReRanker・ハイブリッド検索
│   │   ├── Filter/        # スカラーフィルタ
│   │   └── Persistence/   # MemoryMappedFile 永続化
│   ├── Editor/            # エディタ拡張
│   │   └── UniCortex.Editor.asmdef
│   └── Tests/
│       ├── Runtime/       # ランタイムテスト
│       │   └── UniCortex.Tests.Runtime.asmdef
│       └── Editor/        # エディタテスト
│           └── UniCortex.Tests.Editor.asmdef
```

### Assembly Definition 方針
- Runtime asmdef は `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`, `Unity.Jobs` を参照
- Editor asmdef は Runtime asmdef + `UnityEditor` を参照
- Tests asmdef は対応する Runtime/Editor asmdef + `UnityEngine.TestRunner`, `UnityEditor.TestRunner` を参照

## 開発コマンド

### テスト実行
```bash
# Unity Test Runner (EditMode + PlayMode) をコマンドラインで実行
"C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe" -runTests -batchmode -projectPath . -testResults ./TestResults.xml -testPlatform EditMode

# PlayMode テスト
"C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe" -runTests -batchmode -projectPath . -testResults ./TestResults.xml -testPlatform PlayMode
```

### ビルド
```bash
# スタンドアロンビルド (Windows)
"C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe" -batchmode -quit -projectPath . -buildTarget Win64 -executeMethod BuildScript.Build
```

## 競合との差別化

既存の Unity 向け検索ライブラリ (RAGSearchUnity/USearch, hnsw-sharp 等) は Dense ベクトル検索のみを提供。
UniCortex は以下の機能で差別化する:

| 機能 | RAGSearchUnity | hnsw-sharp | **UniCortex** |
|---|---|---|---|
| Dense ベクトル検索 | ✅ | ✅ | ✅ |
| Sparse ベクトル検索 | ❌ | ❌ | **✅** |
| BM25 全文検索 | ❌ | ❌ | **✅** |
| ハイブリッド検索 (RRF) | ❌ | ❌ | **✅** |
| スカラーフィルタ | ❌ | ❌ | **✅** |
| WebGL 対応 | △ | ✅ | **✅** |
| Pure C# (Burst) | ❌ (USearch C++) | ✅ | **✅** |

## コーディング規約

- Burst 互換コードでは `[BurstCompile]` 属性を付与し、マネージド型を使用しない
- NativeContainer は必ず `Dispose()` する (IDisposable パターン)
- 距離計算は `Unity.Mathematics.float4` で実装し、Burst の auto-vectorize に委ねる
- パブリック API には XML ドキュメントコメントを付与する
- テストは NUnit (Unity Test Framework) で記述する
