# MSSQLPlanViewer テストレポート

- **対象リポジトリ:** MSSQLPlanViewer
- **テストプロジェクト:** `tests/MSSQLPlanViewer.Core.Tests`
- **実行日:** 2026-06-01
- **フレームワーク:** xUnit 2.9.3 / Microsoft.NET.Test.Sdk 17.14.1 / coverlet.collector 6.0.4（.NET 10）

## 1. 結果サマリ

| 指標 | 値 |
| --- | --- |
| Total | 81 |
| Passed | 81 |
| Failed | 0 |
| Skipped | 0 |
| Outcome | Completed |

すべてのテストが成功しています。

## 2. カバレッジサマリ

| 指標 | カバレッジ |
| --- | --- |
| Line rate | 94.9% |
| Branch rate | 84.8% |

> 計測ツール: coverlet（Cobertura 形式）。

### 主要クラスのカバレッジ（ビジネスロジック）

| クラス | Line | Branch |
| --- | --- | --- |
| PlanDisplayFormatter | 100% | 100% |
| PlanComparisonService | 100% | 100% |
| PlanTreeNavigator | 100% | 100% |
| PlanTableProjector | 100% | 92% |
| PlanTableCsvExporter | 100% | 100% |
| PlanTableMarkdownExporter | 100% | 100% |
| PlanTableColumns | 100% | 92% |
| PlanGraphLayoutService | 91% | 91% |
| ShowplanParser | 96% | 78% |

中核となる解析・整形・投影・比較・エクスポートのロジックは、ほぼ全行・全分岐を網羅しています。

## 3. テストカテゴリ別の内訳

| テストクラス | 件数 | カバー範囲 |
| --- | ---: | --- |
| `ShowplanParserTests` | 5 | Showplan XML の解析（2022 / 2012 スキーマ）、不正 XML のエラー、XXE/DTD 拒否、入力サイズ上限。 |
| `PlanProjectionTests` | 18 | パーサーの詳細プロパティ抽出（述語・シーク/結合条件・Order By/Top・Defined Values/Group By・QueryPlan メタデータ・QueryTimeStats・Optimizer 統計・Missing Index/Wait Stats・プランレベル警告・XML 属性の取捨）、テーブル投影の階層構造、グラフレイアウト座標。 |
| `PlanComparisonServiceTests` | 4 | 2 プラン比較の差分・百分率、複数ステートメントのコスト合算、コスト未取得時の null、基準値ゼロ時の差分%。 |
| `PlanDisplayFormatterTests` | 35 | `FormatCost` / `FormatPercent` / `FormatNumber` / `FormatObjectName` / `FormatWarningSummary` / `TryGetSafeHttpUrl`（http/https 許可、javascript/data/vbscript/file/相対 URL 拒否）。 |
| `PlanTableCsvExporterTests` | 5 | ヘッダー/行出力、RFC4180 エスケープ、InvariantCulture と null、ヘッダーのみ、数式インジェクション無害化。 |
| `PlanTableMarkdownExporterTests` | 4 | ヘッダー/区切り/行、パイプエスケープと改行変換、InvariantCulture と null、ヘッダー+区切りのみ。 |
| `PlanRenderingEdgeCaseTests` | 10 | 投影・レイアウトの堅牢性（ルート未指定、循環グラフ、孤立ノード、コスト情報なし、未知ノード参照エッジ、空ステートメント）。 |

## 4. 本セッションで追加したテスト

- `TestPlanFactory.cs` — 合成プランモデル（ノード/エッジ/ステートメント）を簡潔に構築するテストヘルパー。エッジケース用の異常トポロジー生成に使用。
- `PlanDisplayFormatterTests.cs` — 未カバーだった整形メソッド 5 種に対し 24 ケースを追加（`FormatCost`/`FormatPercent`/`FormatNumber`/`FormatObjectName`/`FormatWarningSummary`）。
- `PlanRenderingEdgeCaseTests.cs` — `PlanTableProjector` と `PlanGraphLayoutService` の堅牢性テスト 10 件を追加。これにより `PlanTreeNavigator` のルート解決フォールバック分岐（ルート未指定→エッジ推定、ルートなし→先頭ノード）も網羅。

この追加によりカバレッジは **Line 92.9% → 94.9%、Branch 80.4% → 84.8%** に向上しました。

## 5. 残存ギャップと方針

- **レコード型（`GraphEdgeLayout` / `GraphNodeLayout` / `ShowplanMetadata` / `StatementPlanSummary` / `OptimizerStatsUsageEntry` / `StatementPlan` 等）** の未カバー行は、コンパイラ生成の値等価比較・`ToString`・`Deconstruct` などであり、振る舞い上の意味を持たないため意図的にテスト対象外としています。
- **`ShowplanParser` の分岐（78%）** は、現状 2 つのサンプルプランに含まれないプラン形状向けの防御的分岐です。サンプルプランを増やすことで段階的に向上可能です。

## 6. 再現手順

```powershell
# テスト実行
dotnet test

# カバレッジ収集（Cobertura）
dotnet test --collect:"XPlat Code Coverage"

# TRX 形式のテスト結果出力
dotnet test --logger "trx;LogFileName=test-results.trx"
```

生成物（`TestResults/` 配下の `*.trx` / `coverage.cobertura.xml`）は `.gitignore` 対象であり、リポジトリには含めません。
