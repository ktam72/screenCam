// REQ-007: View のボタン用コマンド。ViewModel は WPF の型を知らない
namespace ScreenCam.Ui;

using System;
using System.Windows.Input;

public sealed class RelayCommand : ICommand
{
    private readonly Action execute;

    public RelayCommand(Action execute) => this.execute = execute;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();

    // CanExecute は常に true。再クエリ不要なので WPF の event 契約だけ満たす (CS0067 回避)
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
