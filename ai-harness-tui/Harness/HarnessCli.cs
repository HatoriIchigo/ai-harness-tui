using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ai_harness_tui;

/// <summary>
/// <c>ai-harness-main</c> を子プロセスとして実行する。TUI はこの CLI だけを情報源とし、
/// 名前付きパイプ・<c>lib/</c> の走査・<c>common.yml</c> の解釈を一切持たない。
/// プラグインの有効化（<c>--plugin --enable</c>／<c>--disable</c>）も同じ CLI 越しに行う
/// ＝ TUI は <c>common.yml</c> を自分で書かない。
///
/// 実行体は PATH から解決する（<c>CreateProcess</c> が PATH を探索する）。したがって TUI 自身は
/// どこに置いてもよく、インストールディレクトリを知る必要もない。
/// </summary>
internal static class HarnessCli
{
    /// <summary>PATH 上の実行体名。拡張子は OS が補う。</summary>
    private const string Executable = "ai-harness-main";

    /// <summary>子プロセスの実行結果。</summary>
    /// <param name="Lines">stdout の行（末尾の空行は除く）。</param>
    /// <param name="Error">stderr の全文。</param>
    /// <param name="ExitCode">
    /// 終了コード。情報表示は 0＝成功 / 1＝引数エラー。<c>--enable</c>／<c>--disable</c> は
    /// 1＝拒否（フェイルクローズを招く有効化など）で、理由は <paramref name="Error"/> に入る。
    /// </param>
    internal readonly record struct Result(IReadOnlyList<string> Lines, string Error, int ExitCode);

    /// <summary>
    /// PATH で解決でき、実行できるかを確かめる。<c>--version</c> は daemon に触れず即座に返るため、
    /// 起動確認に使う（<c>--project</c> は daemon への接続を待つぶん遅い）。
    /// </summary>
    public static bool IsAvailable(out string version, out string error)
    {
        version = "";
        try
        {
            var result = Run("--version");
            version = result.Lines.FirstOrDefault() ?? Executable;
            error = "";
            return true;
        }
        catch (Win32Exception)
        {
            error = $"{Executable} が PATH 上に見つかりません。インストールディレクトリを PATH に通してください。";
            return false;
        }
    }

    /// <summary>
    /// 引数を渡して実行し、stdout／stderr／終了コードを取得する。stdout は UTF-8 固定
    /// （main は情報表示モードで UTF-8 を出す）。
    /// 情報表示では stderr は「daemon 未起動」等の注記であり、空一覧と併せて呼び出し側が解釈する。
    /// </summary>
    public static Result Run(params string[] args)
    {
        var startInfo = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{Executable} を起動できませんでした。");

        // 先に読み切ってから待つ（パイプが埋まって相手が止まるのを避ける）。
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToList();
        return new Result(lines, stderr.Trim(), process.ExitCode);
    }
}
