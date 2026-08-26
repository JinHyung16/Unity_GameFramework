// enum 네이밍 컨벤션: E 접두사 제거 + 'Type' 접미사 강제
// (예: EStatType -> StatType, ECurrency -> CurrencyType)
// 익스포터와 Unity의 DB Generate가 같은 규칙을 써야 하므로 여기 한 곳에만 둔다.
function normalizeEnumName(name) {
    let n = String(name || '').trim();
    if (/^E[A-Z]/.test(n)) {
        n = n.slice(1);
    }
    if (!n.endsWith('Type')) {
        n += 'Type';
    }
    return n;
}

module.exports = { normalizeEnumName };
