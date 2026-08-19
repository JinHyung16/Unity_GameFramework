using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game_UIFramework
{
    public abstract class BaseManagement : MonoBehaviour
    {
        protected IWindowRegistry _windowRegistry;
        protected IWindowController _windowController;
        protected WindowManagement _windowManagement;

        private static readonly Dictionary<Type, BaseManagement> _instances = new Dictionary<Type, BaseManagement>(16);

        public static T Get<T>() where T : BaseManagement
        {
            if (_instances.TryGetValue(typeof(T), out BaseManagement management) && management != null)
            {
                return (T)management;
            }

            var found = FindAnyObjectByType<T>();
            if (found != null)
            {
                _instances[typeof(T)] = found;
            }
            return found;
        }

        protected virtual void Awake()
        {
            _instances[GetType()] = this;
            InitializeWindowManagement();
            InitializeComponents();
            AddWindows();
        }

        protected virtual void InitializeWindowManagement()
        {
            _windowManagement = WindowManagement.Instance;
            if (_windowManagement == null)
            {
                Debug.LogError($"[{GetType().Name}] WindowManagement.Instance is null!");
                return;
            }

            _windowRegistry = new WindowRegistry(_windowManagement);
            _windowController = new WindowController(_windowManagement);
        }

        protected virtual void InitializeComponents()
        {
        }

        protected abstract void AddWindows();

        protected void RegisterWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow
        {
            _windowRegistry?.AddWindow(key, windowType);
        }

        protected T OpenWindow<T>(WindowKey<T> key, System.Action<T> onOpenBefore = null, System.Action<T> onOpenAfter = null) where T : BaseWindow
        {
            return _windowController?.OpenWindow(key, onOpenBefore, onOpenAfter);
        }

        protected void CloseWindow<T>(WindowKey<T> key) where T : BaseWindow
        {
            _windowController?.CloseWindow(key);
        }

        protected void ForceCloseWindow<T>(WindowKey<T> key) where T : BaseWindow
        {
            _windowController?.ForceCloseWindow(key);
        }

        protected T GetWindow<T>(WindowKey<T> key, bool createIfNotExists = true) where T : BaseWindow
        {
            return _windowController?.GetWindow(key, createIfNotExists);
        }

        protected bool IsWindowOpen<T>(WindowKey<T> key) where T : BaseWindow
        {
            return _windowController != null && _windowController.IsWindowOpen(key);
        }
    }
}
