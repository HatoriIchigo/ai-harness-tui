namespace ai_harness_tui;

/// <summary>ログ 1 行。<c>ai-harness-main --logs</c> の 3 列そのまま。</summary>
/// <param name="Time">記録時刻（<c>yyyy-MM-dd HH:mm:ss</c>）。</param>
/// <param name="Level">短縮レベル名（<c>trace</c>／<c>debug</c>／<c>info</c>／<c>warn</c>／<c>error</c>）。</param>
/// <param name="Contents">本文。プラグイン由来なら <c>&lt;PluginName&gt;: </c> が前置されている。</param>
internal readonly record struct LogRow(string Time, string Level, string Contents);
