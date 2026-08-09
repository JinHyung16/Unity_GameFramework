using System;
using System.Collections.Generic;

namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 키 클래스 - 윈도우를 식별하기 위한 키
    /// </summary>
    public class WindowKey
    {
        public string Path { get; private set; }

        public WindowKey(string path)
        {
            Path = path ?? string.Empty;
        }

        public override bool Equals(object obj)
        {
            if (obj is WindowKey other)
            {
                return Path == other.Path;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Path?.GetHashCode() ?? 0;
        }
    }

    /// <summary>
    /// 제네릭 윈도우 키
    /// </summary>
    public class WindowKey<T> : WindowKey where T : BaseWindow
    {
        public WindowKey(string path) : base(path)
        {
        }
    }

    /// <summary>
    /// WindowKey 비교자
    /// </summary>
    public class WindowKeyEqualityComparer : IEqualityComparer<WindowKey>
    {
        public static readonly WindowKeyEqualityComparer Default = new WindowKeyEqualityComparer();

        public bool Equals(WindowKey x, WindowKey y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Path == y.Path;
        }

        public int GetHashCode(WindowKey obj)
        {
            return obj?.Path?.GetHashCode() ?? 0;
        }
    }
}



