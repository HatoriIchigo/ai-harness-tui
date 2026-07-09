# ai-harness-tui

> `ai-harness-main` の情報表示サブコマンドを叩いて描く、読み取り専用の TUI ダッシュボード。

左に対象（ハーネス実行体＋daemon のメモリ上のプロジェクト）、右上にプラグイン、右下にログを並べ、
2 秒ごとに取り直して同じ領域を再描画する。

```
┌ ai-harness ─────────────────────────────────────────────┐
│ daemon: running   memory: 2 project(s)                  │
│ C:\Users\project1                                       │
├──────────────────┬──────────────────────────────────────┤
│ targets          │ plugins                              │
│ (ai-harness-main)│  ai-harness-deny          true       │
│ project1         │  ai-harness-file-rules    false      │
│ project2         │                                      │
│                  ├──────────────────────────────────────┤
│                  │ logs (newest first, warn+)           │
│                  │  07-09 23:59:59  error  deny: …      │
└──────────────────┴──────────────────────────────────────┘
  ↑↓/jk 選択   f フィルタ: warn+   r 再取得   q 終了
```

左欄はディレクトリ名だけを出す（`C:\Users\project1` → `project1`）。同名のプロジェクトが並ぶこともあるため、
選択中の対象のフルパスは常にヘッダ 2 行目に表示する。

## 設計

`ai-harness-main`（PATH 上）を子プロセスとして実行し、その等幅テーブル出力を読むだけ。
名前付きパイプへ直接つながず、`lib/` の DLL も `common.yml` も読まない。したがって

- **TUI はどこに置いてもよい**。PATH で `ai-harness-main` が解決できればよく、インストールディレクトリを知らない。
- **daemon を起こさない**。`--project` は照会のみで、未起動なら `daemon: stopped` と空の一覧を表示する。
- ハーネスの内部構造が変わっても、CLI の 3 コマンドが保たれる限り追随不要。

出力のパースは `" | "` を列数上限で分割する。最終列（ログ本文）に区切り文字が含まれても壊れず、
本文中の改行・タブは `ai-harness-main` 側がエスケープするため 1 レコード＝1 行が保たれる。

## 操作

| キー | 動作 |
|---|---|
| `↑` `↓` / `k` `j` | 対象の選択 |
| `f` | ログの重大度フィルタを循環（`all` → `info+` → `warn+` → `error`） |
| `r` | 即時に取り直す |
| `q` / `Esc` | 終了 |

対象の先頭 `(ai-harness-main)` はハーネス実行体そのものを指す。選ぶと `lib/` のインストール済み
プラグイン（`enabled` の代わりに 1 行説明を表示）と、実行体自身のライフサイクルログ（`logs/`）が出る。

`ai-harness-main` の実行に失敗しても画面は落ちない。理由をヘッダに赤字で出し、直前の表示内容を
保持したまま次の周期で回復を試みる。

画面は**代替画面バッファ**（`nvim` や `less` と同じ）に描く。終了すると起動前の端末表示がそのまま戻り、
ダッシュボードの描画跡は残らない。代替画面を持たない端末（ANSI 非対応の旧 `cmd.exe` 等）では、
終了時に画面をクリアして代替とする。

## 実行

```sh
dotnet run --project ai-harness-tui -c Release
```

端末が必要（stdin/stdout をリダイレクトすると理由を出して終了する）。出力を機械的に取り込むなら
`ai-harness-main --project` / `--logs` / `--plugin` を直接使う。

## 構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | エントリ。コンソールを UTF-8 にして画面を起動 |
| `Harness/HarnessCli.cs` | `ai-harness-main` の子プロセス実行（PATH 解決・UTF-8 で読む） |
| `Harness/TableParser.cs` | 等幅テーブルの分解（列数上限つき分割） |
| `Harness/HarnessQuery.cs` | `--project` / `--plugin` / `--logs` を型付きで叩く |
| `Ui/DashboardState.cs` | 表示状態と取得の集約 |
| `Ui/Dashboard.cs` | レイアウト・`Live` ループ・キー処理 |

net10.0 / nullable 有効 / 暗黙 usings。描画は [Spectre.Console](https://github.com/spectreconsole/spectre.console)（マネージドのみ）。
