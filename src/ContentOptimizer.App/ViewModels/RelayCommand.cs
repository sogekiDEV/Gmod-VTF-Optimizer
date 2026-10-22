using System;
using System.Windows.Input;

namespace ContentOptimizer.App.ViewModels
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> execute;
        private readonly Func<bool>? canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute)
        {
        }

        public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => execute(parameter);
    }
}
