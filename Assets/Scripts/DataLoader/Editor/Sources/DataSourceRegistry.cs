using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game_DataLoader
{
    /// <summary>
    /// IDataSourceLoader 구현을 모아둔다.
    /// Editor/Sources/ 에 cs 파일 하나를 추가하면 그대로 목록에 잡힌다.
    /// </summary>
    public static class DataSourceRegistry
    {
        private static List<IDataSourceLoader> _loaders;

        public static IReadOnlyList<IDataSourceLoader> All
        {
            get
            {
                if (_loaders == null)
                {
                    Collect();
                }
                return _loaders;
            }
        }

        /// <summary>컴파일 후 목록을 다시 만든다.</summary>
        public static void Invalidate()
        {
            _loaders = null;
        }

        public static IDataSourceLoader Find(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return null;
            }
            string want = extension.ToLowerInvariant();
            foreach (IDataSourceLoader loader in All)
            {
                if (loader.Extension == want)
                {
                    return loader;
                }
            }
            return null;
        }

        /// <summary>지금 PC 에서 실제로 쓸 수 있는 확장자만.</summary>
        public static List<string> AvailableExtensions()
        {
            var result = new List<string>();
            foreach (IDataSourceLoader loader in All)
            {
                if (loader.IsAvailable(out _))
                {
                    result.Add(loader.Extension);
                }
            }
            return result;
        }

        private static void Collect()
        {
            var found = new List<IDataSourceLoader>();
            var seen = new Dictionary<string, IDataSourceLoader>();

            Type contract = typeof(IDataSourceLoader);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface)
                    {
                        continue;
                    }
                    if (contract.IsAssignableFrom(type) == false)
                    {
                        continue;
                    }
                    if (type.GetConstructor(Type.EmptyTypes) == null)
                    {
                        Debug.LogWarning($"[DataSourceRegistry] {type.Name} 은 기본 생성자가 없어 건너뜁니다.");
                        continue;
                    }

                    IDataSourceLoader loader;
                    try
                    {
                        loader = (IDataSourceLoader)Activator.CreateInstance(type);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[DataSourceRegistry] {type.Name} 생성 실패: {e.Message}");
                        continue;
                    }

                    string ext = loader.Extension;
                    if (string.IsNullOrEmpty(ext) || ext.StartsWith(".") == false)
                    {
                        Debug.LogWarning($"[DataSourceRegistry] {type.Name} 의 Extension 이 올바르지 않습니다: '{ext}'");
                        continue;
                    }

                    if (seen.TryGetValue(ext, out IDataSourceLoader other))
                    {
                        Debug.LogWarning(
                            $"[DataSourceRegistry] 확장자 '{ext}' 를 {other.GetType().Name} 와 {type.Name} 가 함께 다룹니다. " +
                            $"{other.GetType().Name} 를 씁니다.");
                        continue;
                    }

                    seen[ext] = loader;
                    found.Add(loader);
                }
            }

            _loaders = found.OrderBy(l => l.Order).ThenBy(l => l.Extension, StringComparer.Ordinal).ToList();
        }
    }
}
