using System;
using System.Reflection;
using System.Windows.Input;

namespace AudioRepeator
{
    public class RelayCommand : ICommand
    {
        private readonly WeakAction _execute;

        private readonly WeakFunc<bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }


        public RelayCommand(Action execute, bool keepTargetAlive = false)
            : this(execute, null, keepTargetAlive)
        {
        }

        public RelayCommand(Action execute, Func<bool> canExecute, bool keepTargetAlive = false)
        {
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }

            _execute = new WeakAction(execute, keepTargetAlive);
            if (canExecute != null)
            {
                _canExecute = new WeakFunc<bool>(canExecute, keepTargetAlive);
            }
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute != null)
            {
                if (_canExecute.IsStatic || _canExecute.IsAlive)
                {
                    return _canExecute.Execute();
                }

                return false;
            }

            return true;
        }

        public virtual void Execute(object parameter)
        {
            if (CanExecute(parameter) && _execute != null && (_execute.IsStatic || _execute.IsAlive))
            {
                _execute.Execute();
            }
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly WeakAction<T> _execute;

        private readonly WeakFunc<T, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<T> execute, bool keepTargetAlive = false)
            : this(execute, (Func<T, bool>)null, keepTargetAlive)
        {
        }

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute, bool keepTargetAlive = false)
        {
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }

            _execute = new WeakAction<T>(execute, keepTargetAlive);
            if (canExecute != null)
            {
                _canExecute = new WeakFunc<T, bool>(canExecute, keepTargetAlive);
            }
        }


        public bool CanExecute(object parameter)
        {
            if (_canExecute == null)
            {
                return true;
            }

            if (_canExecute.IsStatic || _canExecute.IsAlive)
            {
                if (parameter == null && typeof(T).GetTypeInfo().IsValueType)
                {
                    return _canExecute.Execute(default(T));
                }

                if (parameter == null || parameter is T)
                {
                    return _canExecute.Execute((T)parameter);
                }
            }

            return false;
        }

        public virtual void Execute(object parameter)
        {
            if (!CanExecute(parameter) || _execute == null || (!_execute.IsStatic && !_execute.IsAlive))
            {
                return;
            }

            if (parameter == null)
            {
                if (typeof(T).GetTypeInfo().IsValueType)
                {
                    _execute.Execute(default(T));
                }
                else
                {
                    _execute.Execute((T)parameter);
                }
            }
            else
            {
                _execute.Execute((T)parameter);
            }
        }
    }


    public class WeakAction
    {
        private Action _staticAction;

        protected MethodInfo Method { get; set; }

        public virtual string MethodName
        {
            get
            {
                if (_staticAction != null)
                {
                    return _staticAction.GetMethodInfo().Name;
                }

                return Method.Name;
            }
        }

        protected WeakReference ActionReference { get; set; }

        protected object LiveReference { get; set; }

        protected WeakReference Reference { get; set; }

        public bool IsStatic => _staticAction != null;

        public virtual bool IsAlive
        {
            get
            {
                if (_staticAction == null && Reference == null && LiveReference == null)
                {
                    return false;
                }

                if (_staticAction != null)
                {
                    if (Reference != null)
                    {
                        return Reference.IsAlive;
                    }

                    return true;
                }

                if (LiveReference != null)
                {
                    return true;
                }

                if (Reference != null)
                {
                    return Reference.IsAlive;
                }

                return false;
            }
        }

        public object Target
        {
            get
            {
                if (Reference == null)
                {
                    return null;
                }

                return Reference.Target;
            }
        }

        protected object ActionTarget
        {
            get
            {
                if (LiveReference != null)
                {
                    return LiveReference;
                }

                if (ActionReference == null)
                {
                    return null;
                }

                return ActionReference.Target;
            }
        }

        protected WeakAction()
        {
        }

        public WeakAction(Action action, bool keepTargetAlive = false)
            : this(action?.Target, action, keepTargetAlive)
        {
        }

        public WeakAction(object target, Action action, bool keepTargetAlive = false)
        {
            if (action.GetMethodInfo().IsStatic)
            {
                _staticAction = action;
                if (target != null)
                {
                    Reference = new WeakReference(target);
                }
            }
            else
            {
                Method = action.GetMethodInfo();
                ActionReference = new WeakReference(action.Target);
                LiveReference = (keepTargetAlive ? action.Target : null);
                Reference = new WeakReference(target);
            }
        }

        public void Execute()
        {
            if (_staticAction != null)
            {
                _staticAction();
                return;
            }

            object actionTarget = ActionTarget;
            if (IsAlive && (object)Method != null && (LiveReference != null || ActionReference != null) && actionTarget != null)
            {
                Method.Invoke(actionTarget, null);
            }
        }

        public void MarkForDeletion()
        {
            Reference = null;
            ActionReference = null;
            LiveReference = null;
            Method = null;
            _staticAction = null;
        }
    }

    public class WeakAction<T> : WeakAction
    {
        private Action<T> _staticAction;

        public override string MethodName
        {
            get
            {
                if (_staticAction != null)
                {
                    return _staticAction.GetMethodInfo().Name;
                }

                return base.Method.Name;
            }
        }

        public override bool IsAlive
        {
            get
            {
                if (_staticAction == null && base.Reference == null)
                {
                    return false;
                }

                if (_staticAction != null)
                {
                    if (base.Reference != null)
                    {
                        return base.Reference.IsAlive;
                    }

                    return true;
                }

                return base.Reference.IsAlive;
            }
        }

        public WeakAction(Action<T> action, bool keepTargetAlive = false)
            : this(action?.Target, action, keepTargetAlive)
        {
        }

        public WeakAction(object target, Action<T> action, bool keepTargetAlive = false)
        {
            if (action.GetMethodInfo().IsStatic)
            {
                _staticAction = action;
                if (target != null)
                {
                    base.Reference = new WeakReference(target);
                }
            }
            else
            {
                base.Method = action.GetMethodInfo();
                base.ActionReference = new WeakReference(action.Target);
                base.LiveReference = (keepTargetAlive ? action.Target : null);
                base.Reference = new WeakReference(target);
            }
        }

        public new void Execute()
        {
            Execute(default(T));
        }

        public void Execute(T parameter)
        {
            if (_staticAction != null)
            {
                _staticAction(parameter);
                return;
            }

            object actionTarget = base.ActionTarget;
            if (IsAlive && (object)base.Method != null && (base.LiveReference != null || base.ActionReference != null) && actionTarget != null)
            {
                base.Method.Invoke(actionTarget, new object[1] { parameter });
            }
        }

        public void ExecuteWithObject(object parameter)
        {
            T parameter2 = (T)parameter;
            Execute(parameter2);
        }

        public new void MarkForDeletion()
        {
            _staticAction = null;
            base.MarkForDeletion();
        }
    }


    public class WeakFunc<TResult>
    {
        private Func<TResult> _staticFunc;

        protected MethodInfo Method { get; set; }

        public bool IsStatic => _staticFunc != null;

        public virtual string MethodName
        {
            get
            {
                if (_staticFunc != null)
                {
                    return _staticFunc.GetMethodInfo().Name;
                }

                return Method.Name;
            }
        }

        protected WeakReference FuncReference { get; set; }

        protected object LiveReference { get; set; }

        protected WeakReference Reference { get; set; }

        public virtual bool IsAlive
        {
            get
            {
                if (_staticFunc == null && Reference == null && LiveReference == null)
                {
                    return false;
                }

                if (_staticFunc != null)
                {
                    if (Reference != null)
                    {
                        return Reference.IsAlive;
                    }

                    return true;
                }

                if (LiveReference != null)
                {
                    return true;
                }

                if (Reference != null)
                {
                    return Reference.IsAlive;
                }

                return false;
            }
        }

        public object Target
        {
            get
            {
                if (Reference == null)
                {
                    return null;
                }

                return Reference.Target;
            }
        }

        protected object FuncTarget
        {
            get
            {
                if (LiveReference != null)
                {
                    return LiveReference;
                }

                if (FuncReference == null)
                {
                    return null;
                }

                return FuncReference.Target;
            }
        }

        protected WeakFunc()
        {
        }

        public WeakFunc(Func<TResult> func, bool keepTargetAlive = false)
            : this(func?.Target, func, keepTargetAlive)
        {
        }

        public WeakFunc(object target, Func<TResult> func, bool keepTargetAlive = false)
        {
            if (func.GetMethodInfo().IsStatic)
            {
                _staticFunc = func;
                if (target != null)
                {
                    Reference = new WeakReference(target);
                }
            }
            else
            {
                Method = func.GetMethodInfo();
                FuncReference = new WeakReference(func.Target);
                LiveReference = (keepTargetAlive ? func.Target : null);
                Reference = new WeakReference(target);
            }
        }

        public TResult Execute()
        {
            if (_staticFunc != null)
            {
                return _staticFunc();
            }

            object funcTarget = FuncTarget;
            if (IsAlive && (object)Method != null && (LiveReference != null || FuncReference != null) && funcTarget != null)
            {
                return (TResult)Method.Invoke(funcTarget, null);
            }

            return default(TResult);
        }

        public void MarkForDeletion()
        {
            Reference = null;
            FuncReference = null;
            LiveReference = null;
            Method = null;
            _staticFunc = null;
        }
    }

    public class WeakFunc<T, TResult> : WeakFunc<TResult>
    {
        private Func<T, TResult> _staticFunc;

        public override string MethodName
        {
            get
            {
                if (_staticFunc != null)
                {
                    return _staticFunc.GetMethodInfo().Name;
                }

                return base.Method.Name;
            }
        }

        public override bool IsAlive
        {
            get
            {
                if (_staticFunc == null && base.Reference == null)
                {
                    return false;
                }

                if (_staticFunc != null)
                {
                    if (base.Reference != null)
                    {
                        return base.Reference.IsAlive;
                    }

                    return true;
                }

                return base.Reference.IsAlive;
            }
        }

        public WeakFunc(Func<T, TResult> func, bool keepTargetAlive = false)
            : this(func?.Target, func, keepTargetAlive)
        {
        }

        public WeakFunc(object target, Func<T, TResult> func, bool keepTargetAlive = false)
        {
            if (func.GetMethodInfo().IsStatic)
            {
                _staticFunc = func;
                if (target != null)
                {
                    base.Reference = new WeakReference(target);
                }
            }
            else
            {
                base.Method = func.GetMethodInfo();
                base.FuncReference = new WeakReference(func.Target);
                base.LiveReference = (keepTargetAlive ? func.Target : null);
                base.Reference = new WeakReference(target);
            }
        }

        public new TResult Execute()
        {
            return Execute(default(T));
        }

        public TResult Execute(T parameter)
        {
            if (_staticFunc != null)
            {
                return _staticFunc(parameter);
            }

            object funcTarget = base.FuncTarget;
            if (IsAlive && (object)base.Method != null && (base.LiveReference != null || base.FuncReference != null) && funcTarget != null)
            {
                return (TResult)base.Method.Invoke(funcTarget, new object[1] { parameter });
            }

            return default(TResult);
        }

        public object ExecuteWithObject(object parameter)
        {
            T parameter2 = (T)parameter;
            return Execute(parameter2);
        }

        public new void MarkForDeletion()
        {
            _staticFunc = null;
            base.MarkForDeletion();
        }
    }
}
