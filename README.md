# ai-harness-tui

> `ai-harness-main` の情報表示サブコマンドを叩いて描く、読み取り専用の TUI ダッシュボード。

上部のボタンで `plugins` と `log` を切り替える**単一表示**。上部はタブだけで、状態を語るのは下部の
ステータスライン（黄緑の帯）に一本化する。2 秒ごとに取り直して同じ領域を再描画する。

```
 plugins  log
┌ logs (newest first, warn+) ─────────────────────────────────────────────────────────────────┐
│  time            level   contents                                                           │
│  07-09 23:59:59  error   deny: ai-harness-deny  Bash("git push --force")                    │
│  07-09 23:59:41  warn    deny: ai-harness-file-rules  src/App.ts が 300 行を超えています     │
│                                                                                             │
└─────────────────────────────────────────────────────────────────────────────────────────────┘
 project1  dev  C:\Users\dev\project1     ai-harness-main 0.1.0  daemon: running  memory: 2 project(s)
 p 対象   Tab 切替   jk スクロール   f フィルタ: warn+   r 再取得   q 終了
```

ステータスラインは左に「いま何を見ているか」（プロジェクト名・git ブランチ・フルパス）、右に
「ハーネスがどうなっているか」（版・daemon・メモリ上のプロジェクト数）を出す。daemon が止まっていると
`stopped` だけが赤くなる。幅が足りないときはフルパス → 右群の順に落とし、プロジェクト名は必ず残す。

ログは 1 行おきに地色を変える（既定の地 ⇄ 濃い灰）。長い行を横に目で追うためで、レベルの色（`error` 赤・
`warn` 黄）は地色の上にそのまま乗る。

対象（ハーネス実行体＋daemon のメモリ上のプロジェクト）は画面に常駐させず、`p` のポップアップで選ぶ。
本体の 1 ビューに端末の幅と行数を丸ごと渡せる。

```
              ╭─ projects ─────────────────────────────╮
              │   * (ai-harness-main)                  │
              │     project1    C:\Users\dev\project1  │
              │     project2    C:\Users\dev\project2  │
              │   jk 選択   Enter 決定   Esc 取消      │
              ╰────────────────────────────────────────╯
```

ステータスラインはディレクトリ名だけを出す（`C:\Users\dev\project1` → `project1`）。同名のプロジェクトが
並ぶこともあるため、フルパスは右端とポップアップに添える。ブランチは `.git/HEAD` を読んで求めるので
（`git` は起動しない）、git 管理外なら単に出ない。

## 設計

`ai-harness-main`（PATH 上）を子プロセスとして実行し、その等幅テーブル出力を読むだけ。
名前付きパイプへ直接つながず、`lib/` の DLL も `common.yml` も読まない。したがって

- **TUI はどこに置いてもよい**。PATH で `ai-harness-main` が解決できればよく、インストールディレクトリを知らない。
- **daemon を起こさない**。`--project` は照会のみで、未起動なら `daemon: stopped` と空の一覧を表示する。
- ハーネスの内部構造が変わっても、CLI の 3 コマンドが保たれる限り追随不要。

出力のパースは `" | "` を列数上限で分割する。最終列（ログ本文）に区切り文字が含まれても壊れず、
本文中の改行・タブは `ai-harness-main` 側がエスケープするため 1 レコード＝1 行が保たれる。

ハーネス以外で唯一読むのが、ステータスラインに出す**プロジェクトの `.git/HEAD`**（ブランチ名）。
`git` を起動しないのは、2 秒ごとの再描画で外部プロセスを増やさないため。git が無い環境でも画面は壊れず、
ブランチが出ないだけになる。

## 操作

| キー | 動作 |
|---|---|
| `p` | プロジェクト選択ポップアップを開く（`j` `k` 選択・`Enter` 決定・`Esc` 取消） |
| `Tab` / `←` `→` / `h` `l` | `plugins` と `log` を切り替え |
| `1` / `2` | `plugins` / `log` を直接開く |
| `↑` `↓` / `k` `j` | ログを 1 行スクロール（`log` 表示中） |
| `f` | ログの重大度フィルタを循環（`all` → `info+` → `warn+` → `error`） |
| `r` | 即時に取り直す |
| `q` / `Esc` | 終了 |

ポップアップは開いている間キーを独占する（nvim のモーダルと同じ）。決定するまで表示中の対象は動かない。

対象の先頭 `(ai-harness-main)` はハーネス実行体そのものを指す。選ぶと `lib/` のインストール済み
プラグイン（`enabled` の代わりに 1 行説明を表示）と、実行体自身のライフサイクルログ（`logs/`）が出る。

ログのスクロールは「新しい順に何件読み飛ばすか」で、`--logs --n` の取得件数を増やして実現する。
これ以上古いログが無い方向へは進まない。

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
| `Harness/GitBranch.cs` | `.git/HEAD` からブランチ名を求める（`git` は起動しない） |
| `Ui/DashboardState.cs` | 表示状態と取得の集約（対象・ビュー・ポップアップ・スクロール） |
| `Ui/Dashboard.cs` | レイアウト・`Live` ループ・キー処理・タブ・本体のビュー |
| `Ui/StatusLine.cs` | 下部の帯（対象・ブランチ・版・daemon）とキー案内 |
| `Ui/ProjectPopup.cs` | `p` のプロジェクト選択ポップアップ |
| `Ui/Term.cs` | 端末サイズと文字列整形（切り詰め・ディレクトリ名） |

net10.0 / nullable 有効 / 暗黙 usings。描画は [Spectre.Console](https://github.com/spectreconsole/spectre.console)（マネージドのみ）。
