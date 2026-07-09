namespace ai_harness_tui;

/// <summary>
/// ログの重大度フィルタ。<c>f</c> キーで循環させる。
/// <c>ai-harness-main --filter</c> は「列挙したレベルのみ」を意味するため、
/// 「これ以上」を作るには下位を含めて全て渡す。
/// </summary>
internal enum LogFilter
{
    /// <summary>全レベル（<c>--filter</c> を付けない）。</summary>
    All,

    /// <summary><c>info</c> 以上。</summary>
    InfoUp,

    /// <summary><c>warn</c> 以上。</summary>
    WarnUp,

    /// <summary><c>error</c> のみ。</summary>
    ErrorOnly,
}

/// <summary><see cref="LogFilter"/> の表示名と <c>--filter</c> 引数への変換。</summary>
internal static class LogFilters
{
    /// <summary>次の候補へ循環する。</summary>
    public static LogFilter Next(this LogFilter filter) =>
        filter == LogFilter.ErrorOnly ? LogFilter.All : filter + 1;

    /// <summary>フッタに出す短い名前。</summary>
    public static string Label(this LogFilter filter) => filter switch
    {
        LogFilter.InfoUp => "info+",
        LogFilter.WarnUp => "warn+",
        LogFilter.ErrorOnly => "error",
        _ => "all",
    };

    /// <summary><c>--filter</c> に渡す値。<see cref="LogFilter.All"/> は <c>null</c>（オプションごと省く）。</summary>
    public static string? Argument(this LogFilter filter) => filter switch
    {
        LogFilter.InfoUp => "info,warn,error",
        LogFilter.WarnUp => "warn,error",
        LogFilter.ErrorOnly => "error",
        _ => null,
    };
}
