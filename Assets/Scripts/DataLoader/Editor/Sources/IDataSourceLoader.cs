using System.Collections.Generic;

namespace Game_DataLoader
{
    /// <summary>
    /// 원본에서 읽어낸 표 하나.
    /// Rows 는 0행 컬럼명, 1행 자료형, 2행부터 데이터다. 확장자와 무관하게 이 형태로 맞춘다.
    /// </summary>
    public sealed class SourceTable
    {
        public string Name;
        public string SourceFile;
        public List<IReadOnlyList<object>> Rows = new List<IReadOnlyList<object>>();

        /// <summary>Rows 인덱스를 사람이 보는 행 번호로. 엑셀 1행이 곧 Rows[0] 이다.</summary>
        public static int ToDisplayRow(int rowIndex)
        {
            return rowIndex + 1;
        }
    }

    /// <summary>
    /// 확장자 하나를 읽는 규칙.
    /// 이 인터페이스를 구현한 클래스를 Editor/Sources/ 에 넣으면 자동으로 목록에 잡힌다.
    /// (DataSourceRegistry 가 리플렉션으로 수집한다. 등록 코드를 따로 고칠 필요가 없다)
    /// </summary>
    public interface IDataSourceLoader
    {
        /// <summary>소문자 확장자. 점을 포함한다. 예: ".xlsx"</summary>
        string Extension { get; }

        /// <summary>설정 창에 보일 이름. 예: "Excel"</summary>
        string DisplayName { get; }

        /// <summary>목록 정렬 순서. 작을수록 위.</summary>
        int Order { get; }

        /// <summary>
        /// 지금 이 PC 에서 쓸 수 있는지. 외부 런타임(node·python 등)이 필요한 로더는
        /// 없을 때 false 와 함께 이유를 돌려준다. 설정 창이 그 이유를 그대로 보여준다.
        /// </summary>
        bool IsAvailable(out string reason);

        /// <summary>
        /// 파일 하나에서 표를 읽는다. 한 파일에 표가 여럿일 수 있다(엑셀 시트, js 객체 키).
        /// 문제는 예외로 던지지 말고 log 에 쌓는다 — 한 파일이 깨져도 나머지는 계속 변환한다.
        /// 복구 불가한 경우에만 빈 목록을 돌려준다.
        /// </summary>
        IEnumerable<SourceTable> Load(string filePath, DataIssueLog log);
    }
}
