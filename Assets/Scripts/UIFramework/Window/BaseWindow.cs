using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game_UIFramework
{
    /// <summary>
    /// 모든 윈도우의 기본 클래스
    /// 윈도우 열기/닫기, 상태 관리, 깊이 관리 등의 핵심 기능 제공
    /// </summary>
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

        /// <summary>
        /// 윈도우 타입 프로퍼티
        /// </summary>
        public WindowType WindowType
        {
            get => _windowType;
            set => _windowType = value;
        }

        /// <summary>
        /// 윈도우 열기 (내부 호출)
        /// </summary>
        public void OpenInternal(Action onOpenBefore = null, Action onOpenAfter = null)
        {
            if (IsOpen())
                return;

            SetState(WindowStateType.Opening);
            onOpenBefore?.Invoke();
            OnOpening();
            SetState(WindowStateType.Opened);
            SetEnable(true);
            onOpenAfter?.Invoke();
        }

        /// <summary>
        /// 윈도우 닫기
        /// </summary>
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

        /// <summary>
        /// 강제 닫기
        /// </summary>
        public void ForcedClose()
        {
            if (!IsOpen())
                return;

            BaseClose();
        }

        /// <summary>
        /// 옵저버 추가
        /// </summary>
        public void AddObserver(IWindowObserver observer)
        {
            if (observer != null && !_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
        }

        /// <summary>
        /// 옵저버 제거
        /// </summary>
        public void RemoveObserver(IWindowObserver observer)
        {
            _observers.Remove(observer);
        }

        /// <summary>
        /// 깊이 설정
        /// </summary>
        public void SetDepth(int depth)
        {
            if (_canvas == null)
                return;

            _currentDepth = depth;
            _canvas.sortingOrder = depth;
        }

        /// <summary>
        /// 상수 깊이 설정
        /// </summary>
        public void SetConstantDepth()
        {
            SetDepth(_constantDepth);
        }

        /// <summary>
        /// UI 카메라 바인딩 (ScreenSpaceCamera 렌더 모드)
        /// </summary>
        public void BindCamera(Camera uiCamera)
        {
            if (_canvas == null || uiCamera == null)
            {
                return;
            }
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = uiCamera;
        }

        /// <summary>
        /// 활성화/비활성화 설정
        /// </summary>
        public void SetEnable(bool enable)
        {
            gameObject.SetActive(enable);
        }

        /// <summary>
        /// Canvas 활성화/비활성화
        /// </summary>
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
        }

        /// <summary>
        /// 윈도우 열기 시 호출되는 가상 메서드
        /// </summary>
        protected virtual void OnOpening()
        {
        }

        /// <summary>
        /// 닫기 전 호출되는 가상 메서드
        /// </summary>
        protected virtual void BeforeClosed()
        {
        }

        /// <summary>
        /// 닫기 시 호출되는 가상 메서드
        /// </summary>
        protected virtual void OnClose()
        {
        }

        /// <summary>
        /// 닫을 수 있는지 확인하는 가상 메서드
        /// </summary>
        protected virtual bool HandleCanClose()
        {
            return true;
        }

        /// <summary>
        /// 상태 설정
        /// </summary>
        protected void SetState(WindowStateType state)
        {
            if (_windowState == state)
                return;

            _windowState = state;
            NotifyObservers();
        }

        /// <summary>
        /// Canvas 초기화
        /// </summary>
        private void InitCanvas()
        {
            if (_canvas == null)
            {
                _canvas = GetComponent<Canvas>();
            }
        }

        /// <summary>
        /// 기본 닫기 로직
        /// </summary>
        private void BaseClose()
        {
            BeforeClosed();
            SetState(WindowStateType.Closed);
            OnClose();
            SetEnable(false);
            StopAllCoroutines();
        }

        /// <summary>
        /// 옵저버에게 상태 변경 알림
        /// </summary>
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

