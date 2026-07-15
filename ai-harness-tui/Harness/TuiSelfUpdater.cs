using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ai_harness_tui;

/// <summary>
/// TUI（ai-harness-tui 自身）の自己更新。<c>u</c> の確認後、<see cref="Dashboard"/> が Live を抜けてから呼ぶ。
///
/// 稼働中の実行ファイルは自分自身では上書きできない（特に Windows はロック）。そこで
///   1. <see cref="Run"/>（稼働中の実行体）: 自リポジトリを tmp へ clone → self-contained single-file で publish →
///      新バイナリを <c>--health</c> で検証 → <c>--apply-update</c> モードで detached 起動し、自身は終了する。
///   2. <see cref="ApplyUpdate"/>（tmp の新バイナリ）: 旧プロセスの終了を待ち、インストール先の実行体を
///      退避（.bak）→ 新バイナリで上書き → 起動検証（失敗はロールバック）→ tmp 掃除。
///
/// applier は「置換対象 exe とは別ファイル（tmp の新バイナリ）」ゆえロックに縛られず置換できる。
/// ai-harness-main と違い daemon は持たないため、置換後の自動再起動はしない（ユーザーが再度 TUI を起動する）。
/// 取得元・ブランチは既定をハードコードし、環境変数（<c>AIH_TUI_REPO</c> / <c>AIH_TUI_BRANCH</c>）で上書きできる。
/// </summary>
internal static class TuiSelfUpdater
{
    private const string ApplyMode = "--apply-update";

    /// <summary>single-file 起動が成立することの確認モード。<see cref="Program"/> が即 0 を返す。</summary>
    public const string HealthMode = "--health";

    /// <summary>自リポジトリの既定 URL。</summary>
    public const string DefaultRepo = "https://github.com/HatoriIchigo/ai-harness-tui";

    /// <summary>既定ブランチ。</summary>
    public const string DefaultBranch = "main";

    /// <summary>取得元リポジトリ URL（<c>AIH_TUI_REPO</c> で上書き）。</summary>
    public static string Repo =>
        Environment.GetEnvironmentVariable("AIH_TUI_REPO") is { Length: > 0 } r ? r : DefaultRepo;

    /// <summary>取得ブランチ（<c>AIH_TUI_BRANCH</c> で上書き）。</summary>
    public static string Branch =>
        Environment.GetEnvironmentVariable("AIH_TUI_BRANCH") is { Length: > 0 } b ? b : DefaultBranch;

    /// <summary>
    /// 自己更新できる状態か（single-file 実行体か・<c>git</c>／<c>dotnet</c> が PATH にあるか）を判定する。
    /// 不可なら <paramref name="reason"/> に理由を入れて <c>false</c>。<c>u</c> 押下時にこれで弾く。
    /// </summary>
    public static bool CanSelfUpdate(out string reason)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            reason = "実行体パスを特定できないため自己更新できません。";
            return false;
        }
        // dotnet ミュクサ経由（dotnet <dll>）だと置換すべき exe を特定できない。単一ファイル実行のみ対象。
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            reason = "`dotnet <dll>` 経由では自己更新できません（single-file 発行の実行体で実行してください）。";
            return false;
        }
        if (!CommandExists("git"))
        {
            reason = "git が見つかりません。git をインストールしてください。";
            return false;
        }
        if (!CommandExists("dotnet"))
        {
            reason = "dotnet が見つかりません。.NET SDK をインストールしてください。";
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>
    /// 通常コンソール（Live を抜けた後）から呼ぶ。自リポジトリを tmp へ clone → single-file publish →
    /// 新バイナリを <c>--health</c> で検証 → applier へハンドオフする。ハンドオフできたら <c>true</c>
    /// （呼び出し側は自プロセスを終了する）。clone／publish／検証に失敗したら例外。
    /// </summary>
    public static bool Run()
    {
        var installExe = Environment.ProcessPath!;
        var exeName = Path.GetFileName(installExe);

        var tmpRoot = Path.Combine(
            Path.GetTempPath(), "ai-harness-tui-selfupdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpRoot);

        var repoDir = Path.Combine(tmpRoot, "ai-harness-tui");
        Console.WriteLine($"clone: {Repo} ({Branch}) -> {repoDir}");
        CloneOrUpdate(Repo, Branch, repoDir);

        var csproj = FindCsproj(repoDir);
        var outDir = Path.Combine(tmpRoot, "out");
        var rid = RuntimeInformation.RuntimeIdentifier;
        Console.WriteLine($"publish: {csproj} (rid={rid})");
        RunOrThrow("dotnet",
        [
            "publish", csproj, "-c", "Release", "-r", rid, "--self-contained", "true",
            "-p:PublishSingleFile=true", "-o", outDir,
        ]);

        var newExe = Path.Combine(outDir, exeName);
        if (!File.Exists(newExe))
        {
            throw new InvalidOperationException($"publish 出力に実行体が無い: {newExe}");
        }

        // 発行直後の健全性検証（壊れた実行体で置換に進まない）。
        if (RunExe(newExe, [HealthMode]) != 0)
        {
            throw new InvalidOperationException("publish した新バイナリの起動検証に失敗。");
        }

        // 新バイナリを applier として detached 起動。自身は即終了し、実行体ロックを解放する。
        Console.WriteLine("新バイナリを検証しました。置換をバックグラウンドで適用します。");
        StartDetached(newExe,
        [
            ApplyMode,
            "--target", installExe,
            "--pid", Environment.ProcessId.ToString(),
            "--tmp", tmpRoot,
        ]);
        return true;
    }

    /// <summary>
    /// <c>--apply-update</c> モード。tmp の新バイナリから動き、インストール先の実行体を安全に置換する。
    /// 引数: <c>--target &lt;install exe&gt; --pid &lt;旧プロセス&gt; --tmp &lt;作業領域&gt;</c>。
    /// detached ゆえコンソールへは繋がらないため、結果は置換先ディレクトリの
    /// <c>.ai-harness-tui-update.log</c> に残す。
    /// </summary>
    public static int ApplyUpdate(string[] args)
    {
        var target = ArgValue(args, "--target");
        var pidText = ArgValue(args, "--pid");
        var tmpRoot = ArgValue(args, "--tmp");

        if (string.IsNullOrEmpty(target))
        {
            return 1; // 置換先不明。ログ先も定まらないため静かに失敗。
        }

        var logPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(target))!, ".ai-harness-tui-update.log");
        void Log(string message)
        {
            try
            {
                File.AppendAllText(logPath, $"{DateTime.Now:o} {message}{Environment.NewLine}");
            }
            catch
            {
                // ログ失敗は無視（更新本体は続ける）。
            }
        }

        var backup = target + ".bak";
        Log($"apply 開始 target={target}");
        try
        {
            // 1. 旧プロセスの終了を待つ（実行体ロック解放のため）。
            if (int.TryParse(pidText, out var pid))
            {
                WaitForProcessExit(pid, TimeSpan.FromSeconds(30));
            }

            // 2. 現行実行体を退避。
            File.Copy(target, backup, overwrite: true);

            // 3. 新バイナリで上書き（ロック解放前は失敗し得るためリトライ）。
            CopyWithRetry(Environment.ProcessPath!, target, TimeSpan.FromSeconds(30));
            Log("実行体を置換。起動検証中。");

            // 4. 置換後の起動検証。失敗ならロールバック。
            if (RunExe(target, [HealthMode]) != 0)
            {
                File.Copy(backup, target, overwrite: true);
                Log("置換後の起動検証に失敗。旧実行体へロールバックした。");
                return 1;
            }

            Log("自己更新に成功。");
            TryDelete(backup);
            return 0;
        }
        catch (Exception ex)
        {
            Log($"自己更新に失敗: {ex.Message}");
            try
            {
                if (File.Exists(backup))
                {
                    File.Copy(backup, target, overwrite: true);
                    Log("旧実行体へロールバックした。");
                }
            }
            catch (Exception rollbackEx)
            {
                Log($"ロールバックにも失敗: {rollbackEx.Message}");
            }
            return 1;
        }
        finally
        {
            // tmp 掃除（自分自身の exe を含むため Windows では消せないことがある。無害）。
            if (!string.IsNullOrEmpty(tmpRoot))
            {
                TryDeleteDirectory(tmpRoot);
            }
        }
    }

    // ---- ヘルパ ----

    /// <summary>
    /// <paramref name="repoDir"/> にリポジトリを用意する。既存（<c>.git</c> あり）なら fetch → reset、
    /// 無ければ shallow clone。
    /// </summary>
    private static void CloneOrUpdate(string url, string branch, string repoDir)
    {
        if (Directory.Exists(Path.Combine(repoDir, ".git")))
        {
            RunOrThrow("git", ["-C", repoDir, "fetch", "--depth", "1", "origin", branch]);
            RunOrThrow("git", ["-C", repoDir, "reset", "--hard", "FETCH_HEAD"]);
        }
        else
        {
            RunOrThrow("git", ["clone", "--depth", "1", "--branch", branch, url, repoDir]);
        }
    }

    /// <summary>リポジトリ内の csproj を特定する（bin／obj 配下は除外）。名前一致優先、単一ならそれ。</summary>
    private static string FindCsproj(string repoDir)
    {
        var all = Directory.EnumerateFiles(repoDir, "*.csproj", SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(repoDir, p);
                var segs = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !segs.Any(s =>
                    string.Equals(s, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, "obj", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        if (all.Count == 0)
        {
            throw new InvalidOperationException($"csproj が見つからない: {repoDir}");
        }
        var match = all.FirstOrDefault(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p), "ai-harness-tui", StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }
        if (all.Count == 1)
        {
            return all[0];
        }
        throw new InvalidOperationException(
            $"csproj が複数ありリポジトリ名と一致するものが無い: {string.Join(", ", all.Select(Path.GetFileName))}");
    }

    /// <summary><paramref name="file"/> を実行し、非 0 終了なら例外。出力は親コンソールへそのまま流す。</summary>
    private static void RunOrThrow(string file, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"プロセス起動に失敗: {file}");
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{file} {string.Join(' ', args)} が終了コード {p.ExitCode} で失敗");
        }
    }

    /// <summary><paramref name="file"/> を静かに実行して終了コードを返す（検証用）。起動失敗は -1。</summary>
    private static int RunExe(string file, IReadOnlyList<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
            using var p = Process.Start(psi);
            if (p is null)
            {
                return -1;
            }
            p.WaitForExit();
            return p.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>端末から切り離して detached 起動する（applier の起動）。</summary>
    private static void StartDetached(string exe, IReadOnlyList<string> args)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
        }
        else
        {
            psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
        }
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        Process.Start(psi);
    }

    private static void WaitForProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // 既に終了済み。
        }
    }

    private static void CopyWithRetry(string source, string dest, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                File.Copy(source, dest, overwrite: true);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);   // 置換対象がまだロック中（プロセス終了待ち）。
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static string? ArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    /// <summary><paramref name="cmd"/> が PATH で実行可能か（<c>--version</c> を静かに実行して判定）。</summary>
    private static bool CommandExists(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo(cmd)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }
            p.WaitForExit();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort。
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 自分自身の exe を含む tmp は Windows で消せないことがある。無害。
        }
    }
}
