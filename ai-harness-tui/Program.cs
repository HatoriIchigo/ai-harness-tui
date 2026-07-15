using System.Text;

namespace ai_harness_tui;

/// <summary>
/// ai-harness-tui エントリポイント。
///
/// <c>ai-harness-main</c>（PATH 上）の情報表示サブコマンドを子プロセスとして叩き、その出力だけを描画する
/// 読み取り専用のダッシュボード。daemon へ直接つなぐことも、<c>lib/</c> や <c>common.yml</c> を読むこともない。
/// 起動しても daemon は起きない（<c>--project</c> は照会のみ）。
///
/// 例外的に、自己更新（<see cref="TuiSelfUpdater"/>）のための 2 つの非対話モードを持つ:
///   <c>--health</c>       … single-file 起動が成立することの確認（即 0 を返す）。
///   <c>--apply-update</c> … tmp の新バイナリから、インストール先の実行体を置換する applier。
/// いずれも画面を出さない。引数なしの通常起動はダッシュボード。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--apply-update":
                    return TuiSelfUpdater.ApplyUpdate(args);
                case TuiSelfUpdater.HealthMode:
                    return 0;
            }
        }

        // 日本語のログ本文と罫線を正しく出すため、コンソール出力を UTF-8 に切り替える。
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // 端末に接続していない場合は設定できない。既定のまま続行する。
        }

        return Dashboard.Run();
    }
}
