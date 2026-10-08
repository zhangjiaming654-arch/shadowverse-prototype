namespace Shadowverse.DeckEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 最后一道网：界面线程上漏出来的异常不该把程序打死。
        //
        // 这不是用来掩盖 bug 的 —— 它是为了**不让一个界面小错报废整局对战**，
        // 同时把原因显示出来。真正的修法永远是去修那个异常（见 MAINTENANCE.md §5.1）。
        //
        // 之所以需要它：`ReplayForm.HumanPlay` 里的 `RunOnUi` 只保护经它派发的回调，
        // **保护不到鼠标事件处理器**（点击、拖拽、菜单项）—— 而交互的 bug 恰恰都在那里。
        // 实测就是菜单项释放时抛 "Collection was modified"，直接弹未处理异常对话框。
        //
        // 调试时 VS 仍然会在异常处中断（默认行为），所以问题不会被藏住。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => ReportFailure(eventArgs.Exception);

        Application.Run(args.Contains("--replay", StringComparer.OrdinalIgnoreCase) ? new ReplayForm() : new DeckEditorForm());
    }

    private static void ReportFailure(Exception exception)
    {
        try
        {
            MessageBox.Show(
                exception.ToString(),
                "界面出错（已拦下，程序继续运行）",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // 连弹窗都失败就只能放弃 —— 但绝不能再抛出去，那又会变成未处理异常。
        }

        System.Diagnostics.Debug.WriteLine("[DeckEditor] 未处理的界面异常：" + exception);
    }
}
