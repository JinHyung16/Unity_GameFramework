using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game_UIFramework
{
    /// <summary>
    /// UI 윈도우를 관리하는 Management의 기본 클래스
    /// WindowManagement와 1대1로 소통하여 윈도우를 등록하고 제어하는 역할
    /// 각 콘텐츠(상점, 이벤트, 길드 등)별로 Management를 만들 때 상속받아 사용
    /// </summary>
    public abstract class BaseManagement : MonoBehaviour
    {
        protected IWindowRegistry _windowRegistry;
        protected IWindowController _windowController;
        protected WindowManagement _windowManagement;

        /// <summary>
        /// 타입별 Management 조회표.
        /// 한 화면의 버튼이 다른 화면의 팝업을 열어야 할 때(격납고 노심 슬롯 → 노심 장착 팝업 등)
        /// Management끼리 직접 참조할 길이 없어 여기서 찾는다.
        /// </summary>
        private static readonly Dictionary<Type, BaseManagement> _instances = new Dictionary<Type, BaseManagement>(16);

        /// <summary>같은 씬에 올라온 다른 Management를 가져온다. 없으면 null이다</summary>
        public static T Get<T>() where T : BaseManagement
        {
            if (_instances.TryGetValue(typeof(T), out BaseManagement management) && management != null)
            {
                return (T)management;
            }

            // 파괴됐거나 아직 Awake 전이면 씬에서 한 번 찾아 캐시한다
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

        /// <summary>
        /// WindowManagement 초기화
        /// </summary>
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

        /// <summary>
        /// 컴포넌트 초기화 (필요시 오버라이드)
        /// </summary>
        protected virtual void InitializeComponents()
        {
            // 서브 클래스에서 필요한 초기화 작업 수행
        }

        /// <summary>
        /// 윈도우 등록 메서드
        /// 서브 클래스에서 이 메서드를 오버라이드하여 필요한 윈도우들을 등록
        /// </summary>
        protected abstract void AddWindows();

        /// <summary>
        /// 윈도우 등록 헬퍼 메서드
        /// </summary>
        protected void RegisterWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow
        {
            _windowRegistry?.AddWindow(key, windowType);
        }

        /// <summary>
        /// 윈도우 열기 헬퍼 메서드
        /// </summary>
        protected T OpenWindow<T>(WindowKey<T> key, System.Action<T> onOpenBefore = null, System.Action<T> onOpenAfter = null) where T : BaseWindow
        {
            return _windowController?.OpenWindow(key, onOpenBefore, onOpenAfter);
        }

        /// <summary>
        /// 윈도우 닫기 헬퍼 메서드
        /// </summary>
        protected void CloseWindow<T>(WindowKey<T> key) where T : BaseWindow
        {
            _windowController?.CloseWindow(key);
        }

        /// <summary>
        /// 윈도우 강제 닫기 헬퍼 메서드
        /// </summary>
        protected void ForceCloseWindow<T>(WindowKey<T> key) where T : BaseWindow
        {
            _windowController?.ForceCloseWindow(key);
        }

        /// <summary>
        /// 윈도우 가져오기 헬퍼 메서드
        /// </summary>
        protected T GetWindow<T>(WindowKey<T> key, bool createIfNotExists = true) where T : BaseWindow
        {
            return _windowController?.GetWindow(key, createIfNotExists);
        }

        /// <summary>
        /// 윈도우가 열려있는지 확인 헬퍼 메서드
        /// </summary>
        protected bool IsWindowOpen<T>(WindowKey<T> key) where T : BaseWindow
        {
            return _windowController != null && _windowController.IsWindowOpen(key);
        }
    }
}

