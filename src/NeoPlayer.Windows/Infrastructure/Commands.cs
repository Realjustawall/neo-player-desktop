using System.Windows.Input;
namespace NeoPlayer.Windows;
public sealed class RelayCommand(Action execute, Func<bool>? can=null) : ICommand
{
 public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p)=>can?.Invoke()??true; public void Execute(object? p)=>execute(); public void Raise()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
public sealed class RelayCommand<T>(Action<T?> execute, Func<T?,bool>? can=null) : ICommand
{
 public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p)=>can?.Invoke((T?)p)??true; public void Execute(object? p)=>execute((T?)p); public void Raise()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? can=null) : ICommand
{
 bool running; public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p)=>!running&&(can?.Invoke()??true); public async void Execute(object? p){ if(!CanExecute(p))return; running=true; CanExecuteChanged?.Invoke(this,EventArgs.Empty); try{await execute();}finally{running=false;CanExecuteChanged?.Invoke(this,EventArgs.Empty);} }
}
public sealed class AsyncRelayCommand<T>(Func<T?,Task> execute, Func<T?,bool>? can=null) : ICommand
{
 bool running; public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p)=>!running&&(can?.Invoke((T?)p)??true); public async void Execute(object? p){ if(!CanExecute(p))return; running=true; CanExecuteChanged?.Invoke(this,EventArgs.Empty); try{await execute((T?)p);}finally{running=false;CanExecuteChanged?.Invoke(this,EventArgs.Empty);} }
}