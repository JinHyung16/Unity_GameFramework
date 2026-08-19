using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game_UIFramework
{
    public class BaseWindow
        : BaseComponent
        , IBaseWindow
    {
        [Header("Window Settings")]
        [SerializeField] protected WindowType _windowType = WindowType.Normal;
        [SerializeField] protected CloseType _closeType = CloseType.Close;
        [SerializeField] protected int _constantDepth = 0;
        [SerializeField] protected bool _hideBackground = false;

        [Header("Canvas")]
        [SerializeField] protected Canvas _canvas;

        protected WindowStateType _windowState = WindowStateType.Closed;
        private List<IWindowObserver> _observers = new List<IWindowObserver>();
        private int _currentDepth = 0;

        public WindowType WindowType
        {
            get => _windowType;
            set => _windowType = value;
        }

        public void OpenInternal(Action onOpenBefore = null, Action onOpenAfter = null)
        {
            if (IsOpen())
                return;

            SetState(WindowStateType.Opening);

            SetEnable(true);
            onOpenBefore?.Invoke();
            OnOpening();
            SetState(WindowStateType.Opened);
            onOpenAfter?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen())
                return;

            if (_windowType == WindowType.HUD)
                return;

            if (_closeType == CloseType.Handle && !HandleCanClose())
            {
                return;
            }

            BaseClose();
        }

        public void ForcedClose()
        {
            if (!IsOpen())
                return;

            BaseClose();
        }

        public void AddObserver(IWindowObserver observer)
        {
            if (observer != null && !_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
        }

        public void RemoveObserver(IWindowObserver observer)
        {
            _observers.Remove(observer);
        }

        public void SetDepth(int depth)
        {
            if (_canvas == null)
                return;

            _currentDepth = depth;
            _canvas.sortingOrder = depth;
        }

        public void SetConstantDepth()
        {
            SetDepth(_constantDepth);
        }

        public void BindCamera(Camera uiCamera)
        {
            if (_canvas == null || uiCamera == null)
            {
                return;
            }
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = uiCamera;
        }

        public void SetEnable(bool enable)
        {
            gameObject.SetActive(enable);
        }

        public bool CanvasEnable
        {
            get => _canvas != null && _canvas.enabled;
            set
            {
                if (_canvas != null)
                    _canvas.enabled = value;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            InitCanvas();
        }

        protected override void OnDestroy()
        {
            _observers.Clear();
            base.OnDestroy();
        }

        protected virtual void OnOpening()
        {
        }

        protected virtual void BeforeClosed()
        {
        }

        protected virtual void OnClose()
        {
        }

        protected virtual bool HandleCanClose()
        {
            return true;
        }

        protected void SetState(WindowStateType state)
        {
            if (_windowState == state)
                return;

            _windowState = state;
            NotifyObservers();
        }

        private void InitCanvas()
        {
            if (_canvas == null)
            {
                _canvas = GetComponent<Canvas>();
            }
        }

        private void BaseClose()
        {
            BeforeClosed();
            SetState(WindowStateType.Closed);
            OnClose();
            SetEnable(false);
            StopAllCoroutines();
        }

        private void NotifyObservers()
        {
            for (int i = _observers.Count - 1; i >= 0; i--)
            {
                if (_observers[i] == null)
                {
                    _observers.RemoveAt(i);
                    continue;
                }
                _observers[i].OnWindowStateChanged(this, _windowState);
            }
        }

        #region IBaseWindow 구현

        public bool IsOpen()
        {
            return _windowState != WindowStateType.Closed;
        }

        public int GetDepth()
        {
            return _canvas != null ? _canvas.sortingOrder : -1;
        }

        public string GetName()
        {
            return name;
        }

        public WindowType GetWindowType()
        {
            return _windowType;
        }

        public WindowStateType GetWindowState()
        {
            return _windowState;
        }

        #endregion
    }
}
