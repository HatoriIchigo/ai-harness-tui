namespace ai_harness_tui;

/// <summary>
/// プロジェクトルートの現在ブランチを求める。
///
/// <c>git</c> を起動せず <c>.git/HEAD</c> を読むだけにする。TUI は情報表示のためだけに 2 秒ごとに回るので、
/// 描画のたびに外部プロセスを増やしたくない。git が入っていない環境でも画面は壊れず、単にブランチが出ない。
///
/// ハーネスのプロジェクトルート（<c>.claude</c> を含む階層）は git のルートと一致するとは限らないため、
/// <c>.git</c> が見つかるまで親へ遡る。見つからなければ <c>null</c>（git 管理下でない）。
/// </summary>
internal static class GitBranch
{
    private const string RefPrefix = "ref: refs/heads/";
    private const string GitDirPrefix = "gitdir:";

    /// <summary>detached HEAD のとき出す短縮 SHA の桁数。</summary>
    private const int ShortShaLength = 7;

    /// <summary>
    /// <paramref name="projectRoot"/> のブランチ名。detached HEAD なら短縮 SHA。
    /// 対象がハーネス実行体自身（<c>null</c>）・git 管理外・読めない場合は <c>null</c>。
    /// </summary>
    public static string? Resolve(string? projectRoot)
    {
        if (projectRoot is null)
        {
            return null;
        }

        try
        {
            var gitDir = FindGitDir(projectRoot);
            if (gitDir is null)
            {
                return null;
            }

            var head = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(head))
            {
                return null;
            }

            var content = File.ReadAllText(head).Trim();
            if (content.StartsWith(RefPrefix, StringComparison.Ordinal))
            {
                return content[RefPrefix.Length..];
            }
            // detached HEAD は SHA が直接書かれている。
            return content.Length >= ShortShaLength ? content[..ShortShaLength] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ブランチは付加情報でしかない。読めなければ黙って出さない。
            return null;
        }
    }

    /// <summary>
    /// <paramref name="start"/> から親へ遡って git ディレクトリを探す。
    /// worktree・submodule では <c>.git</c> がファイルで、本体の場所を <c>gitdir:</c> で指す。
    /// </summary>
    private static string? FindGitDir(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            if (File.Exists(candidate))
            {
                return ResolveGitFile(candidate, dir.FullName);
            }
        }
        return null;
    }

    /// <summary><c>.git</c> ファイルの <c>gitdir: &lt;パス&gt;</c> を解決する（相対なら基準ディレクトリから）。</summary>
    private static string? ResolveGitFile(string gitFile, string baseDir)
    {
        var text = File.ReadAllText(gitFile).Trim();
        if (!text.StartsWith(GitDirPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var path = text[GitDirPrefix.Length..].Trim();
        return Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);
    }
}
