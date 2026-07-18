namespace ai_harness_tui;

/// <summary>LSP 1 言語ぶんの表示行。</summary>
/// <param name="Language">言語名。</param>
/// <param name="Server">サーバ名。カタログ表示では既定サーバに <c>(既定)</c> が付く。</param>
/// <param name="Status">
/// 稼働状況（<c>Running</c>／<c>Installing</c>／<c>Failed</c>／<c>未起動</c>）。
/// プロジェクト無指定のカタログ表示では得られないため <c>null</c>。
/// </param>
/// <param name="Error">直近のエラー（無ければ空文字）。カタログ表示では <c>null</c>。</param>
internal readonly record struct LspRow(string Language, string Server, string? Status, string? Error);
