using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game_UIFramework
{
    public enum CellSizeModeType
    {
        Free,
        Static,

        /// <summary>항목마다 길이가 다르다. 데이터 소스가 IRecyclableVariableSize를 구현해야 한다.</summary>
        PerItem,
    }

    public class RecyclableScrollView : ScrollRect, IRecyclableScrollView
    {
        [SerializeField] private CellSizeModeType _cellSizeMode = CellSizeModeType.Static;
        [SerializeField] private GameObject _cellPrefab;
        [SerializeField] private Vector2 _cellSize;
        [SerializeField] private float _spacing;
        [SerializeField] private RectOffset _padding = new RectOffset();
        [SerializeField] private bool _reverseArrangement;
        [SerializeField] private int _minPoolCount = 5;
        [SerializeField] private int _offscreenBuffer = 1;

        private RectTransform _viewportRect;
        private GameObject _activePrefab;
        private IRecyclableScrollDataSource _dataSource;
        private readonly Dictionary<int, IRecyclableItem> _activeCells = new();
        private readonly Queue<IRecyclableItem> _cellPool = new();
        private readonly List<int> _recycleIndexBuffer = new();
        private int _itemCount;
        private Vector2 _measuredCellSize;
        private bool _isInitialized;
        private int _visibleFirst = -1;
        private int _visibleLast = -1;

        // PerItem 모드에서 슬롯별 시작 위치와 길이를 미리 쌓아둔다 (매 프레임 재계산하지 않는다)
        private readonly List<float> _slotStart = new();
        private readonly List<float> _slotLength = new();
        private IRecyclableVariableSize _variableSize;
        private float _contentLength;

        private bool IsVertical => vertical;
        private bool IsPerItem => _cellSizeMode == CellSizeModeType.PerItem && _variableSize != null;
        private Vector2 CellSize => _cellSizeMode == CellSizeModeType.Static ? _cellSize : _measuredCellSize;
        private float CellLength => IsVertical ? CellSize.y : CellSize.x;
        private float Step => CellLength + _spacing;
        private float StartPad => IsVertical ? _padding.top : _padding.left;
        private float EndPad => IsVertical ? _padding.bottom : _padding.right;
        private float NearPad => IsVertical ? _padding.left : _padding.top;
        private float FarPad => IsVertical ? _padding.right : _padding.bottom;

        private int SlotOf(int index) => _reverseArrangement ? _itemCount - 1 - index : index;

        public void Initialize(IRecyclableScrollDataSource dataSource, GameObject cellPrefab = null)
        {
            var prefab = cellPrefab != null ? cellPrefab : _cellPrefab;
            if (prefab == null || prefab.GetComponent<IRecyclableItem>() == null)
            {
                Debug.LogError($"[RecyclableScrollView] IRecyclableItem을 구현한 셀 프리팹이 필요합니다. ({name})");
                return;
            }

            _dataSource = dataSource;
            _variableSize = dataSource as IRecyclableVariableSize;

            if (!_isInitialized)
            {
                SetupScrollView();
                _isInitialized = true;
            }

            if (_activePrefab != prefab)
            {
                DestroyAllCells();
                _activePrefab = prefab;
            }

            WarmUpPool();
            MeasureCellSize();
            Reload();
        }

        public void Reload()
        {
            if (!_isInitialized || _dataSource == null)
                return;

            _itemCount = _dataSource.GetItemCount();
            RebuildSlotMetrics();
            RecycleAllCells();
            InvalidateVisibleRange();
            ResizeContent();
            ClampContentPosition();
            UpdateVisibleCells();
        }

        public void RefreshVisibleCells()
        {
            foreach (var pair in _activeCells)
                _dataSource.CreateCell(pair.Value, pair.Key);
        }

        public void ScrollToIndex(int index)
        {
            if (!_isInitialized || _itemCount == 0)
                return;

            index = Mathf.Clamp(index, 0, _itemCount - 1);
            float offset = Mathf.Clamp(SlotStart(SlotOf(index)), 0f, MaxScrollOffset());

            StopMovement();
            content.anchoredPosition = IsVertical
                ? new Vector2(content.anchoredPosition.x, offset)
                : new Vector2(-offset, content.anchoredPosition.y);

            UpdateVisibleCells();
        }

        private void SetupScrollView()
        {
            _viewportRect = viewport != null ? viewport : (RectTransform)transform;
            ApplyContentAnchor();
            onValueChanged.AddListener(OnScrollValueChanged);
        }

        private void ApplyContentAnchor()
        {
            if (content == null)
                return;

            if (IsVertical)
            {
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
            }
            else
            {
                content.anchorMin = new Vector2(0f, 0f);
                content.anchorMax = new Vector2(0f, 1f);
                content.pivot = new Vector2(0f, 0.5f);
            }
        }

        private void OnScrollValueChanged(Vector2 _)
        {
            UpdateVisibleCells();
        }

        private void WarmUpPool()
        {
            while (_cellPool.Count + _activeCells.Count < _minPoolCount)
            {
                var cell = CreateCell();
                cell.RectTransform.gameObject.SetActive(false);
                _cellPool.Enqueue(cell);
            }
        }

        private void MeasureCellSize()
        {
            if (_cellSizeMode != CellSizeModeType.Free)
                return;

            if (_cellPool.Count == 0)
            {
                var created = CreateCell();
                created.RectTransform.gameObject.SetActive(false);
                _cellPool.Enqueue(created);
            }

            var sample = _cellPool.Peek();
            Vector2 measured = sample.RectTransform.rect.size;
            _measuredCellSize = new Vector2(
                measured.x > 0f ? measured.x : _cellSize.x,
                measured.y > 0f ? measured.y : _cellSize.y);
        }

        /// <summary>슬롯별 시작 위치·길이 표를 다시 쌓는다 (균일 모드는 계산식 그대로라 결과가 같다)</summary>
        private void RebuildSlotMetrics()
        {
            _slotStart.Clear();
            _slotLength.Clear();

            float cursor = StartPad;
            for (int slot = 0; slot < _itemCount; slot++)
            {
                float length = LengthOfSlot(slot);
                _slotStart.Add(cursor);
                _slotLength.Add(length);
                cursor += length + _spacing;
            }
            _contentLength = _itemCount > 0 ? cursor - _spacing + EndPad : 0f;
        }

        private float LengthOfSlot(int slot)
        {
            if (IsPerItem == false)
            {
                return CellLength;
            }

            float length = _variableSize.GetItemLength(IndexOfSlot(slot));
            return length > 0f ? length : CellLength;
        }

        private int IndexOfSlot(int slot)
        {
            return _reverseArrangement ? _itemCount - 1 - slot : slot;
        }

        private float SlotStart(int slot)
        {
            if (slot < 0 || slot >= _slotStart.Count)
            {
                return StartPad + Mathf.Max(0, slot) * Step;
            }
            return _slotStart[slot];
        }

        private void ResizeContent()
        {
            float length = _contentLength;

            var sizeDelta = content.sizeDelta;
            if (IsVertical)
                sizeDelta.y = length;
            else
                sizeDelta.x = length;
            content.sizeDelta = sizeDelta;
        }

        private void ClampContentPosition()
        {
            float max = MaxScrollOffset();
            var position = content.anchoredPosition;

            if (IsVertical)
                position.y = Mathf.Clamp(position.y, 0f, max);
            else
                position.x = Mathf.Clamp(position.x, -max, 0f);

            content.anchoredPosition = position;
        }

        private float MaxScrollOffset()
        {
            float contentLength = IsVertical ? content.sizeDelta.y : content.sizeDelta.x;
            float viewportLength = IsVertical ? _viewportRect.rect.height : _viewportRect.rect.width;
            return Mathf.Max(0f, contentLength - viewportLength);
        }

        private float CurrentScrollOffset()
        {
            return IsVertical ? content.anchoredPosition.y : -content.anchoredPosition.x;
        }

        private void UpdateVisibleCells()
        {
            if (_itemCount == 0)
                return;

            CalculateVisibleRange(out int first, out int last);
            if (first == _visibleFirst && last == _visibleLast)
                return;

            _visibleFirst = first;
            _visibleLast = last;
            RecycleOutOfRangeCells(first, last);

            for (int index = first; index <= last; index++)
            {
                if (_activeCells.ContainsKey(index))
                    continue;

                var cell = GetCellFromPool();
                PositionCell(cell, index);
                _dataSource.CreateCell(cell, index);
                _activeCells.Add(index, cell);
            }
        }

        private void CalculateVisibleRange(out int first, out int last)
        {
            float offset = CurrentScrollOffset();
            float viewportLength = IsVertical ? _viewportRect.rect.height : _viewportRect.rect.width;

            int firstSlot;
            int lastSlot;
            if (IsPerItem)
            {
                firstSlot = SlotAt(offset);
                lastSlot = SlotAt(offset + viewportLength);
            }
            else
            {
                firstSlot = Mathf.FloorToInt((offset - StartPad) / Step);
                lastSlot = Mathf.FloorToInt((offset - StartPad + viewportLength) / Step);
            }

            if (_reverseArrangement)
            {
                first = _itemCount - 1 - lastSlot;
                last = _itemCount - 1 - firstSlot;
            }
            else
            {
                first = firstSlot;
                last = lastSlot;
            }

            first = Mathf.Clamp(first - _offscreenBuffer, 0, _itemCount - 1);
            last = Mathf.Clamp(last + _offscreenBuffer, 0, _itemCount - 1);
        }

        /// <summary>스크롤 오프셋이 걸치는 슬롯을 이분 탐색으로 찾는다</summary>
        private int SlotAt(float offset)
        {
            int low = 0;
            int high = _slotStart.Count - 1;
            int found = 0;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (_slotStart[mid] <= offset)
                {
                    found = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return found;
        }

        private void RecycleOutOfRangeCells(int first, int last)
        {
            _recycleIndexBuffer.Clear();

            foreach (var pair in _activeCells)
            {
                if (pair.Key < first || pair.Key > last)
                    _recycleIndexBuffer.Add(pair.Key);
            }

            foreach (int index in _recycleIndexBuffer)
            {
                ReturnCellToPool(_activeCells[index]);
                _activeCells.Remove(index);
            }
        }

        private void InvalidateVisibleRange()
        {
            _visibleFirst = -1;
            _visibleLast = -1;
        }

        private void RecycleAllCells()
        {
            foreach (var pair in _activeCells)
                ReturnCellToPool(pair.Value);
            _activeCells.Clear();
        }

        private void DestroyAllCells()
        {
            foreach (var pair in _activeCells)
                Destroy(pair.Value.RectTransform.gameObject);
            _activeCells.Clear();

            while (_cellPool.Count > 0)
                Destroy(_cellPool.Dequeue().RectTransform.gameObject);
        }

        private IRecyclableItem CreateCell()
        {
            var instance = Instantiate(_activePrefab, content);
            var cell = instance.GetComponent<IRecyclableItem>();
            SetupCellAnchor(cell.RectTransform);
            return cell;
        }

        private IRecyclableItem GetCellFromPool()
        {
            if (_cellPool.Count > 0)
            {
                var cell = _cellPool.Dequeue();
                cell.RectTransform.gameObject.SetActive(true);
                return cell;
            }

            return CreateCell();
        }

        private void ReturnCellToPool(IRecyclableItem cell)
        {
            cell.RectTransform.gameObject.SetActive(false);
            _cellPool.Enqueue(cell);
        }

        private void SetupCellAnchor(RectTransform rect)
        {
            if (IsVertical)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
            }
        }

        private void PositionCell(IRecyclableItem cell, int index)
        {
            int slot = SlotOf(index);
            float length = slot < _slotLength.Count ? _slotLength[slot] : CellLength;
            LayoutCellRect(cell.RectTransform, slot, length, SlotStart(slot));
        }

        private void LayoutCellRect(RectTransform rect, int slot, float cellLength)
        {
            LayoutCellRect(rect, slot, cellLength, StartPad + slot * (cellLength + _spacing));
        }

        private void LayoutCellRect(RectTransform rect, int slot, float cellLength, float offset)
        {

            if (IsVertical)
            {
                rect.sizeDelta = new Vector2(-(NearPad + FarPad), cellLength);
                rect.anchoredPosition = new Vector2((NearPad - FarPad) * 0.5f, -offset);
            }
            else
            {
                rect.sizeDelta = new Vector2(cellLength, -(NearPad + FarPad));
                rect.anchoredPosition = new Vector2(offset, (FarPad - NearPad) * 0.5f);
            }
        }

        protected override void OnDestroy()
        {
            onValueChanged.RemoveListener(OnScrollValueChanged);
            base.OnDestroy();
        }

#if UNITY_EDITOR
        /// <summary> 편집 모드에서 Content 하위 셀 자식들을 현재 값으로 재배치한다 (LayoutGroup식 미리보기) </summary>
        public void RefreshEditorPreview()
        {
            if (Application.isPlaying || content == null)
                return;

            ApplyContentAnchor();

            float cellLength = PreviewCellLength();
            int total = CountActiveChildren();
            int visual = 0;

            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf)
                    continue;

                SetupCellAnchor(child);
                int slot = _reverseArrangement ? total - 1 - visual : visual;
                LayoutCellRect(child, slot, cellLength);
                visual++;
            }

            float length = total > 0 ? StartPad + EndPad + total * cellLength + (total - 1) * _spacing : 0f;
            var sizeDelta = content.sizeDelta;
            if (IsVertical)
                sizeDelta.y = length;
            else
                sizeDelta.x = length;
            content.sizeDelta = sizeDelta;
        }

        private int CountActiveChildren()
        {
            int total = 0;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child != null && child.gameObject.activeSelf)
                    total++;
            }

            return total;
        }

        private float PreviewCellLength()
        {
            if (_cellSizeMode == CellSizeModeType.Static)
                return IsVertical ? _cellSize.y : _cellSize.x;

            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child != null && child.gameObject.activeSelf)
                    return IsVertical ? child.rect.height : child.rect.width;
            }

            return IsVertical ? _cellSize.y : _cellSize.x;
        }
#endif
    }
}
