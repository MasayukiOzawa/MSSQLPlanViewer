# MSSQL Plan Viewer

SQL Server の Showplan XML を解析し、**グラフィカル実行プラン** と **テーブル表示** を同時に確認できる Blazor Web アプリです。

## 現在のスコープ

- XML は**ファイルアップロードなし**で、テキスト領域へ貼り付けて解析
- **単一の Showplan XML** を対象に表示
- Showplan の XML 名前空間差異を吸収して解析
- 演算子グラフは **inline SVG** で描画
- Graphical plan には **ズームボタン / Reset** と **演算子アイコン** を表示
- Graphical plan は **上から下** へ流れるレイアウトで、**ドラッグスクロール** に対応
- 演算子一覧は **階層化テーブル** として表示し、親子関係を維持
- 大きい XML 貼り付けに備えて、Blazor Server の受信メッセージ上限を拡張

## プロジェクト構成

- `src\MSSQLPlanViewer.Web` - Blazor Web App (`net10.0`)
- `src\MSSQLPlanViewer.Core` - Showplan モデル / パーサー / 表示変換 (`net8.0`)
- `tests\MSSQLPlanViewer.Core.Tests` - パーサーと表示変換のテスト (`net10.0`)

## ローカル実行

```powershell
dotnet run --project .\src\MSSQLPlanViewer.Web\MSSQLPlanViewer.Web.csproj
```

起動後、表示されたページのテキスト領域へ Showplan XML を貼り付けて解析します。

## テスト

```powershell
dotnet test .\MSSQLPlanViewer.sln
```

## 実装メモ

- パーサーは `XDocument` と `LocalName` ベースで実装し、Showplan スキーマ URI の違いを吸収しています。
- グラフ/テーブルはどちらも `MSSQLPlanViewer.Core` の変換サービスで生成し、UI から分離しています。
- 初版は SSMS 完全互換ではなく、主要演算子・コスト・警告・実行時情報の可視化を優先しています。
- 大きい実行プラン XML の貼り付けで回線断のように見える再接続ループが起きないよう、SignalR の `MaximumReceiveMessageSize` を `10 MB` に設定しています。
- Graphical plan は inline SVG でズーム可能で、主要演算子は SSMS を意識したアイコン付きノードとして描画します。
- Graphical plan は上から下へ読み進めやすいレイアウトとし、先頭ノードは左上から表示されます。ズーム後はドラッグで表示領域を移動でき、Reset で倍率と表示位置を初期化できます。
- Table view は DFS ベースの親子順で投影し、フィルタ時も親ノードを残して階層コンテキストを維持します。
