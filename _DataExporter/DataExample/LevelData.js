// 계산으로 만드는 표 예제.
// 키 = 표 이름(= 클래스명 = JSON 파일명), 값 = 행 배열.
// 0행 컬럼명, 1행 자료형, 2행부터 데이터 — 엑셀과 규칙이 같다.

const BASE_EXP = 100;
const EXP_CURVE = 1.15;
const BASE_HP = 50;
const HP_PER_LEVEL = 12;

const rows = [
    ['Id',   'Level', 'RequiredExp', 'Hp'  ],
    ['int!', 'int!',  'int!',        'int!'],
];

for (let level = 1; level <= 20; level++) {
    rows.push([
        level,
        level,
        Math.floor(BASE_EXP * Math.pow(EXP_CURVE, level - 1)),
        BASE_HP + level * HP_PER_LEVEL,
    ]);
}

module.exports = { LevelData: rows };
