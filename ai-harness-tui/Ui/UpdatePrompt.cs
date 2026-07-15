using Spectre.Console;
using Spectre.Console.Rendering;

namespace ai_harness_tui;

/// <summary>
/// <c>u</c> で開く self-update の確認モーダル。<see cref="ProjectPopup"/> と同じく、開いている間だけ
/// 本体の領域をこのモーダルで置き換え、中央に浮かせて見せる（キーもモーダルが先に食う）。
///
/// 確定すると <see cref="Dashboard"/> が Live を抜け、通常コンソールで <see cref="TuiSelfUpdater.Run"/>
/// （clone → publish → 検証 → applier ハンドオフ）を実行して TUI を終了する。
/// </summary>
internal static class UpdatePrompt
{
    public static IRenderable Render(DashboardState state)
    {
        _ = state;
        var rows = new List<IRenderable>
        {
            new Markup("[bold]ai-harness-tui を自己更新します。[/]"),
            new Markup($"[grey]取得元: {Markup.Escape(TuiSelfUpdater.Repo)} ({Markup.Escape(TuiSelfUpdater.Branch)})[/]"),
            new Markup("[grey]git clone → publish → 実行体を置換します。更新後、TUI は一度終了します。[/]"),
            new Markup(""),
            new Markup("[bold]Enter[/]/[bold]y[/] 実行   [bold]Esc[/]/[bold]n[/] 取消"),
        };

        var panel = new Panel(new Rows(rows))
            .Header("self-update")
            .Border(BoxBorder.Rounded)
            .Padding(1, 0);

        return new Align(panel, HorizontalAlignment.Center, VerticalAlignment.Middle);
    }
}
