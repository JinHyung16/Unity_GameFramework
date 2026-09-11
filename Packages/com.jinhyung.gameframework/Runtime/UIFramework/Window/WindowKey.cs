using System;
using System.Collections.Generic;

namespace Game_UIFramework
{
    public class WindowKey
    {
        public string Path { get; private set; }

        private readonly int _hash;

        public WindowKey(string path)
        {
            Path = path ?? string.Empty;
            _hash = Path.GetHashCode();
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
            return _hash;
        }
    }

    public class WindowKey<T> : WindowKey where T : BaseWindow
    {
        public WindowKey(string path) : base(path)
        {
        }
    }

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
            return obj?.GetHashCode() ?? 0;
        }
    }
}
