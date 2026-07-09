using System.Text;

namespace ai_harness_tui;

/// <summary>
/// ai-harness-tui エントリポイント。
///
/// <c>ai-harness-main</c>（PATH 上）の情報表示サブコマンドを子プロセスとして叩き、その出力だけを描画する
/// 読み取り専用のダッシュボード。daemon へ直接つなぐことも、<c>lib/</c> や <c>common.yml</c> を読むこともない。
/// 起動しても daemon は起きない（<c>--project</c> は照会のみ）。
/// </summary>
public static class Program
{
    public static int Main()
    {
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
