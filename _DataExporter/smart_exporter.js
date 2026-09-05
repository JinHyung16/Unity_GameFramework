const fs = require('fs');
const path = require('path');
const XLSX = require('xlsx');
const { execFileSync } = require('child_process');
const { normalizeEnumName } = require('./enum_name');

// ─────────────────────────────────────────────────────────────────────────────
// 이 부분에 Json 변환할 파일 확장자명 넣으세요.
//
// 넣을 수 있는 확장자 (아래 SOURCE_LOADERS에 로더가 있는 것만):
//
//   '.xlsx'   엑셀      시트 1장 = 표 1개
//   '.js'     Node      module.exports = { 표이름: [[컬럼명..], [자료형..], [값..]] }
//   '.py'     Python    TABLES      = { "표이름": [[컬럼명..], [자료형..], [값..]] }
//                       ※ 로컬에 python 필요. 없으면 .py만 건너뛰고 나머지는 그대로 변환된다
//
// 규칙:
//   - 여기서 뺀 확장자는 GameData/ 폴더에 파일이 있어도 아예 읽지 않는다.
//   - 목록에 없는 확장자(예: '.csv')를 적으면 시작할 때 경고가 뜨고 그 확장자는 무시된다.
//     새 포맷을 지원하려면 SOURCE_LOADERS에 한 줄 추가하고 로더 메서드를 만들어야 한다.
//   - 확장자와 무관하게 표 규칙은 같다: 1행 컬럼명 → 2행 자료형 → 3행부터 값.
// ─────────────────────────────────────────────────────────────────────────────
const SOURCE_EXTENSIONS = ['.xlsx', '.js', '.py'];
// ─────────────────────────────────────────────────────────────────────────────

// 확장자 → 로더 메서드. 지원 가능한 확장자의 단일 기준이다.
// 새 포맷을 붙일 때는 여기에 한 줄 추가하고, 그 메서드가
// { sheets: [{ name, rows }] } 형태만 돌려주면 나머지 파이프라인은 그대로 동작한다.
const SOURCE_LOADERS = {
    '.xlsx': 'loadXlsxWorkbook',
    '.js': 'loadJsWorkbook',
    '.py': 'loadPyWorkbook'
};

// .py 파일에서 표를 꺼내 JSON으로 넘겨받는 러너.
// 대상 파일을 모듈로 로드해 TABLES(또는 tables) 딕셔너리를 stdout에 직렬화한다.
const PYTHON_RUNNER = [
    'import importlib.util, json, pathlib, sys',
    'p = pathlib.Path(sys.argv[1])',
    'spec = importlib.util.spec_from_file_location(p.stem, str(p))',
    'm = importlib.util.module_from_spec(spec)',
    'spec.loader.exec_module(m)',
    'data = getattr(m, "TABLES", None) or getattr(m, "tables", None)',
    'if data is None: raise SystemExit("TABLES 딕셔너리를 찾을 수 없습니다")',
    'sys.stdout.write(json.dumps(data, ensure_ascii=False, default=str))'
].join('\n');

class SmartExcelExporter {
    constructor(options = {}) {
        // 프로젝트 루트 = _DataExporter 의 한 단계 위. config 의 상대 경로는 전부 이 기준이다.
        this.rootPath = options.rootPath || path.resolve(__dirname, '..');
        this.configPath = options.configPath || path.join(__dirname, 'config.json');
        this.verbose = options.verbose || false;

        // 시트별 컬럼 스키마(이름/자료형) — _Schema.json 출력용
        this.schemas = {};

        // 자료형 매핑 (배열은 '<자료형>array' 형태만 허용, json 금지)
        this.typeMap = {
            'int': 'number',
            'long': 'number',
            'float': 'number',
            'double': 'number',
            'number': 'number',
            'string': 'string',
            'text': 'string',
            'bool': 'boolean',
            'boolean': 'boolean',
            'intarray': 'intarray',
            'floatarray': 'floatarray',
            'stringarray': 'stringarray',
            'stringarray|': 'stringarray',
            'null': 'null'
        };

        // enum 정의(_Enum)에서 수집: enum명 -> 멤버(소문자) Set. 타입행에 enum명만 적으면 enum으로 인식한다.
        this.enumDefs = new Map();
        // 같은 정의를 원본 대소문자 + 등장 순서 그대로 보관한다. GameEnum.cs 코드젠용.
        // (enumDefs는 값 검증용이라 소문자로 눕혀놔서 그대로 못 쓴다)
        this.enumMemberOrder = new Map();

        // 제외 패턴
        this.excludePatterns = [
            /~/,            // 이름에 '~'가 포함된 파일은 제외 (Excel 임시 파일 ~$... 포함)
            /\.tmp$/,
            /^\.git/,
            /^Thumbs\.db$/,
            /^\.DS_Store$/
        ];

        this.loadConfig();
        this.resolvePaths(options);
        this.resolveActiveExtensions();
        this.initializeExcludePatterns();
        this.loadEnumDefinitions();
    }

    /**
     * enum 정의 파일(_Enum 등)에서 enum명/멤버를 미리 수집한다.
     * 타입행에 'CurrencyType'처럼 enum명만 적으면 enum으로 파싱하기 위한 사전 정보.
     */
    loadEnumDefinitions() {
        if (!fs.existsSync(this.gamedataPath)) {
            return;
        }

        const enumFiles = fs.readdirSync(this.gamedataPath)
            .filter(file => this.isSourceFile(file))
            .filter(file => !this.isExcluded(file))
            .filter(file => this.isEnumFile(file));

        for (const filename of enumFiles) {
            try {
                const workbook = this.loadWorkbook(path.join(this.gamedataPath, filename));

                for (const sheet of workbook.sheets) {
                    const sheetName = sheet.name;
                    if (sheetName.includes('~')) {
                        continue;
                    }
                    const rows = sheet.rows;
                    if (rows.length < 2) {
                        continue;
                    }
                    const headerRow = rows[0];
                    for (let col = 0; col < headerRow.length; col++) {
                        const rawEnumName = headerRow[col] == null ? '' : String(headerRow[col]).trim();
                        if (rawEnumName === '' || rawEnumName.startsWith('#')) {
                            continue;
                        }
                        const enumName = normalizeEnumName(rawEnumName);
                        if (!this.enumDefs.has(enumName)) {
                            this.enumDefs.set(enumName, new Set());
                        }
                        if (!this.enumMemberOrder.has(enumName)) {
                            this.enumMemberOrder.set(enumName, []);
                        }
                        const members = this.enumDefs.get(enumName);
                        const ordered = this.enumMemberOrder.get(enumName);
                        for (let row = 1; row < rows.length; row++) {
                            const cell = rows[row] ? rows[row][col] : null;
                            const member = cell == null ? '' : String(cell).trim();
                            if (member === '' || member.startsWith('#')) {
                                continue;
                            }
                            members.add(member.toLowerCase());
                            ordered.push(member);
                        }
                    }
                }
            } catch (error) {
                this.log(`enum 정의 로드 실패 (${filename}): ${error.message}`, 'warning');
            }
        }


        if (this.enumDefs.size > 0) {
            this.log(`enum 정의 로드 완료: ${this.enumDefs.size}개 (${[...this.enumDefs.keys()].join(', ')})`, 'debug');
        }
    }

    /**
     * 타입행 값이 enum 표기인지 판별해 enum명을 돌려준다 (아니면 null).
     * - 'CurrencyType'  : enum 정의에 있으면 enum (권장 표기)
     * - 'enum:XxxType'  : 폐기된 표기, 경고 후 호환 처리
     */
    resolveEnumType(baseType) {
        const t = String(baseType || '').trim();
        if (t.toLowerCase().startsWith('enum:')) {
            return normalizeEnumName(t.slice('enum:'.length));
        }
        if (this.enumDefs.has(t)) {
            return t;
        }
        const normalized = normalizeEnumName(t);
        if (/^E?[A-Z]/.test(t) && this.enumDefs.has(normalized)) {
            return normalized;
        }
        return null;
    }

    /**
     * 설정 파일 로드
     */
    loadConfig() {
        try {
            if (fs.existsSync(this.configPath)) {
                const config = JSON.parse(fs.readFileSync(this.configPath, 'utf8'));
                this.config = config;
                this.log(`설정 파일 로드 완료: ${this.configPath}`, 'success');
            } else {
                this.config = {};
                this.log(`설정 파일이 없습니다. 기본 설정 사용: ${this.configPath}`, 'warning');
            }
        } catch (error) {
            this.log(`설정 파일 로드 실패: ${error.message}`, 'error');
            this.config = {};
        }
    }

    /**
     * 경로 확정 — config.json 의 Paths 가 기준이고, 환경변수가 있으면 그쪽이 이긴다.
     * config 의 상대 경로는 프로젝트 루트 기준으로 푼다. Unity 의 DbGenerator 도 같은 값을 읽는다.
     */
    resolvePaths(options) {
        const paths = this.config.Paths || {};

        const resolve = (value, fallback) => {
            const picked = value || fallback;
            return path.isAbsolute(picked) ? picked : path.resolve(this.rootPath, picked);
        };

        this.gamedataPath = options.gamedataPath
            || process.env.GAMEDATA_PATH
            || resolve(paths.SourceFolder, '_DataExporter/GameData');

        this.jsonOutputPath = options.jsonOutputPath
            || process.env.JSON_OUTPUT_PATH
            || resolve(paths.JsonOutput, 'Assets/GameData');

        // 스키마는 Unity 의 DB Generate 가 읽는다. 런타임에는 쓰이지 않으므로
        // Addressables 대상인 JSON 폴더 바깥에 둔다.
        this.schemaOutputPath = options.schemaOutputPath
            || process.env.SCHEMA_OUTPUT_PATH
            || resolve(paths.SchemaOutput, 'Assets/GameDataSchema/_Schema.json');

        this.log(`입력      : ${this.gamedataPath}`, 'debug');
        this.log(`JSON 출력 : ${this.jsonOutputPath}`, 'debug');
        this.log(`스키마    : ${this.schemaOutputPath}`, 'debug');
    }

    /**
     * 제외 패턴 초기화
     */
    initializeExcludePatterns() {
        // 컬럼 제외 패턴
        this.excludeColumnPrefix = this.config.ExcludeColumnPrefix || '_';
        this.excludeColumnPatterns = this.config.ExcludeColumnPatterns || ['_*'];

        // 시트 제외 패턴
        this.excludeSheetPrefix = this.config.ExcludeSheetPrefix || '_';
        this.excludeSheetPatterns = this.config.ExcludeSheetPatterns || ['_*'];

        // 여러 엑셀을 한 표로 합칠 때 쓰는 목록 파일 이름
        this.loadDataFileName = this.config.LoadFileDataFileName || 'LoadDataFile.xlsx';
        this.loadDataBaseName = this.baseNameOf(this.loadDataFileName);

        // enum 정의 파일 패턴 — '_' 접두사 제외 규칙의 예외.
        // 파일명에 패턴이 포함되면 enum 정의 파일로 취급한다 (JSON만 생성, CS 미생성).
        this.enumFilePatterns = this.config.EnumFilePatterns || ['_Enum'];

        this.log(`컬럼 제외 접두사: ${this.excludeColumnPrefix}`, 'debug');
        this.log(`시트 제외 접두사: ${this.excludeSheetPrefix}`, 'debug');
    }

    /**
     * 패턴 매칭 확인
     */
    isMatchPattern(name, patterns) {
        if (!name || !patterns) return false;

        return patterns.some(pattern => {
            // 와일드카드 패턴 처리
            if (pattern.includes('*')) {
                const regex = new RegExp('^' + pattern.replace(/\*/g, '.*') + '$', 'i');
                return regex.test(name);
            }
            // 정확한 매칭
            return name === pattern;
        });
    }

    /**
     * 컬럼 제외 여부 확인
     */
    isColumnExcluded(columnName) {
        if (!columnName) return true;

        // 이름에 '~'가 포함된 컬럼은 기본적으로 제외
        if (columnName.includes('~')) {
            return true;
        }

        // 접두사로 제외
        if (this.excludeColumnPrefix && columnName.startsWith(this.excludeColumnPrefix)) {
            return true;
        }

        // 패턴으로 제외
        if (this.isMatchPattern(columnName, this.excludeColumnPatterns)) {
            return true;
        }

        return false;
    }

    /**
     * 시트 제외 여부 확인
     */
    isSheetExcluded(sheetName) {
        if (!sheetName) return true;

        // 이름에 '~'가 포함된 시트는 기본적으로 제외
        if (sheetName.includes('~')) {
            return true;
        }

        // 접두사로 제외
        if (this.excludeSheetPrefix && sheetName.startsWith(this.excludeSheetPrefix)) {
            return true;
        }

        // 패턴으로 제외
        if (this.isMatchPattern(sheetName, this.excludeSheetPatterns)) {
            return true;
        }

        return false;
    }

    /**
     * enum 정의 파일 여부 확인 ('_' 접두사 제외 규칙의 예외)
     */
    isEnumFile(filename) {
        const baseName = this.baseNameOf(filename);
        return this.enumFilePatterns.some(pattern =>
            baseName.toLowerCase().includes(pattern.toLowerCase()));
    }

    /**
     * 로그 출력
     */
    log(message, type = 'info') {
        const prefix = {
            'info': '[INFO]',
            'success': '[OK]',
            'warning': '[WARN]',
            'error': '[ERROR]',
            'debug': '[DEBUG]'
        }[type] || '[INFO]';

        // verbose가 아닐 때 debug 로그는 숨김
        if (type === 'debug' && !this.verbose) return;

        console.log(`${prefix} ${message}`);
    }

    /**
     * 파일이 제외 패턴에 해당하는지 확인
     */
    isExcluded(filename) {
        return this.excludePatterns.some(pattern => pattern.test(filename));
    }

    // ── 소스 파일 로딩 ────────────────────────────────────────────────────────
    // .xlsx / .js / .py 를 모두 { sheets: [{ name, rows }] } 한 가지 형태로 읽는다.
    // rows는 2차원 배열이고 0행=컬럼명, 1행=자료형, 2행부터 데이터다.
    // 아래 파이프라인은 이 형태만 알면 되므로 확장자가 늘어도 로더만 추가하면 된다.

    /** 변환 대상 확장자인지 (SOURCE_EXTENSIONS ∩ 로더 있는 것) */
    isSourceFile(filename) {
        return this.activeExtensions.includes(path.extname(filename).toLowerCase());
    }

    /**
     * SOURCE_EXTENSIONS를 검증해 실제로 읽을 확장자 목록을 확정한다.
     * 로더가 없는 확장자(오타 / 미구현)는 경고하고 제외한다 — 조용히 무시되면 원인을 못 찾는다.
     */
    resolveActiveExtensions() {
        const normalized = SOURCE_EXTENSIONS.map(ext => String(ext).trim().toLowerCase());
        const unsupported = normalized.filter(ext => !SOURCE_LOADERS[ext]);

        if (unsupported.length > 0) {
            this.log(
                `SOURCE_EXTENSIONS에 로더가 없는 확장자가 있어 무시합니다: ${unsupported.join(', ')} ` +
                `(사용 가능: ${Object.keys(SOURCE_LOADERS).join(', ')})`, 'warning');
        }

        this.activeExtensions = normalized.filter(ext => !!SOURCE_LOADERS[ext]);

        if (this.activeExtensions.length === 0) {
            this.log('SOURCE_EXTENSIONS가 비어 있어 변환할 파일이 없습니다.', 'warning');
        }
    }

    /** 확장자를 뗀 이름 = 표/클래스/JSON 파일 이름의 기준 */
    baseNameOf(filename) {
        return path.basename(filename, path.extname(filename));
    }

    /** baseName으로 실제 소스 파일을 찾는다 (확장자를 모르는 경우) */
    findSourceFile(baseName) {
        for (const ext of this.activeExtensions) {
            const candidate = path.join(this.gamedataPath, baseName + ext);
            if (fs.existsSync(candidate)) {
                return candidate;
            }
        }
        return null;
    }

    loadWorkbook(filePath) {
        const ext = path.extname(filePath).toLowerCase();
        const loader = SOURCE_LOADERS[ext];

        if (!loader || typeof this[loader] !== 'function') {
            throw new Error(
                `로더가 없는 확장자입니다: ${ext} ` +
                `(사용 가능: ${Object.keys(SOURCE_LOADERS).join(' / ')})`);
        }

        return this[loader](filePath);
    }

    loadXlsxWorkbook(filePath) {
        const workbook = XLSX.readFile(filePath, {
            cellStyles: false,
            cellFormulas: false,
            cellDates: true,
            cellNF: false,
            sheetStubs: false
        });

        const sheets = [];
        for (const sheetName of workbook.SheetNames) {
            const rows = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], {
                header: 1,
                raw: false,
                defval: ''
            });
            sheets.push({ name: sheetName, rows: rows });
        }
        return { sheets: sheets };
    }

    loadJsWorkbook(filePath) {
        const abs = path.resolve(filePath);
        // 같은 프로세스에서 두 번 읽을 수 있으므로 캐시를 비운다
        delete require.cache[abs];

        const loaded = require(abs);
        const tables = (loaded && loaded.default) ? loaded.default : loaded;
        return this.normalizeTableMap(tables, filePath);
    }

    loadPyWorkbook(filePath) {
        const python = this.resolvePythonCommand();
        if (!python) {
            throw new Error('Python 실행 파일을 찾지 못했습니다 (python / python3 / py)');
        }

        const stdout = execFileSync(python, ['-c', PYTHON_RUNNER, path.resolve(filePath)], {
            encoding: 'utf8',
            maxBuffer: 64 * 1024 * 1024,
            // 한글 표가 Windows 기본 코드페이지에서 깨지지 않게 강제한다
            env: Object.assign({}, process.env, { PYTHONIOENCODING: 'utf-8' })
        });

        return this.normalizeTableMap(JSON.parse(stdout), filePath);
    }

    /** python 실행 명령을 한 번만 찾아 캐시한다. 없으면 null */
    resolvePythonCommand() {
        if (this._pythonCommand !== undefined) {
            return this._pythonCommand;
        }

        const candidates = process.env.PYTHON ? [process.env.PYTHON] : ['python', 'python3', 'py'];
        for (const candidate of candidates) {
            try {
                execFileSync(candidate, ['--version'], { stdio: 'ignore' });
                this._pythonCommand = candidate;
                return candidate;
            } catch (e) {
                // 다음 후보로
            }
        }

        this._pythonCommand = null;
        return null;
    }

    /**
     * { 표이름: 값 } 객체를 워크북 형태로 정규화한다.
     * - 값이 배열이면 그대로 행 배열로 본다 (일반 표)
     * - 값이 객체면 { enum명: [멤버...] } 로 보고 열 방향으로 눕힌다 (_Enum 규약)
     */
    normalizeTableMap(tables, filePath) {
        if (!tables || typeof tables !== 'object' || Array.isArray(tables)) {
            throw new Error(`{ 표이름: 행배열 } 형태를 내보내야 합니다: ${path.basename(filePath)}`);
        }

        const sheets = [];
        for (const [tableName, value] of Object.entries(tables)) {
            if (typeof value === 'function') {
                continue;
            }
            sheets.push({ name: tableName, rows: this.tableToRows(value, tableName, filePath) });
        }
        return { sheets: sheets };
    }

    tableToRows(value, tableName, filePath) {
        if (Array.isArray(value)) {
            return value.map(row => (Array.isArray(row) ? row : [row]));
        }

        if (value && typeof value === 'object') {
            // _Enum 규약: { StatType: ['Attack', ...] } → 헤더=enum명, 아래=멤버인 표로 변환
            const names = Object.keys(value);
            const columns = names.map(name => (Array.isArray(value[name]) ? value[name] : []));
            const height = columns.reduce((max, col) => Math.max(max, col.length), 0);

            const rows = [names];
            for (let i = 0; i < height; i++) {
                rows.push(columns.map(col => (i < col.length ? col[i] : null)));
            }
            return rows;
        }

        throw new Error(`표 '${tableName}'의 값이 배열이 아닙니다: ${path.basename(filePath)}`);
    }

    /**
     * 변환 대상 파일 목록 (LoadDataFile에 정의된 파일들 제외)
     */
    getSourceFiles() {
        if (!fs.existsSync(this.gamedataPath)) {
            this.log(`입력 폴더가 없습니다: ${this.gamedataPath}`, 'error');
            return [];
        }

        const files = fs.readdirSync(this.gamedataPath);
        const allSourceFiles = files
            .filter(file => this.isSourceFile(file))
            .filter(file => {
                // Python이 없는 환경이면 .py만 조용히 건너뛴다 (나머지 파이프라인은 그대로 돈다)
                if (path.extname(file).toLowerCase() !== '.py' || this.resolvePythonCommand()) {
                    return true;
                }
                if (!this._pythonWarned) {
                    this._pythonWarned = true;
                    this.log('Python을 찾지 못해 .py 파일을 건너뜁니다. python을 설치하거나 PYTHON 환경변수를 지정하세요.', 'warning');
                }
                return false;
            })
            .filter(file => !this.isExcluded(file))
            .filter(file => {
                // enum 정의 파일(_Enum 등)은 '_' 접두사 제외 규칙의 예외
                if (this.isEnumFile(file)) {
                    return true;
                }
                // 시트/컬럼과 동일하게 '_' 접두사 파일은 export 대상에서 제외
                if (this.excludeSheetPrefix && file.startsWith(this.excludeSheetPrefix)) {
                    this.log(`파일 제외됨 (접두사 '${this.excludeSheetPrefix}'): ${file}`, 'debug');
                    return false;
                }
                return true;
            });

        // LoadDataFile에 정의된 파일들을 제외하기 위해 먼저 LoadDataFile 정보 확인
        const loadDataFiles = this.getLoadDataFileList();

        return allSourceFiles.filter(file => {
            const baseName = this.baseNameOf(file);
            // 목록 파일 자체는 exportAll이 먼저 따로 처리한다. 여기서 빼지 않으면 두 번 처리된다.
            if (baseName === this.loadDataBaseName) {
                return false;
            }
            // LoadDataFile에 정의된 파일들은 개별 처리에서 제외
            return !loadDataFiles.includes(baseName);
        });
    }

    /**
     * LoadDataFile에 정의된 파일 목록 가져오기
     */
    getLoadDataFileList() {
        // 목록 파일도 .xlsx / .js / .py 아무거나 될 수 있다
        const loadDataFilePath = this.findSourceFile(this.loadDataBaseName);

        if (!loadDataFilePath) {
            return [];
        }

        try {
            const workbook = this.loadWorkbook(loadDataFilePath);

            const loadDataFiles = new Set();

            for (const sheet of workbook.sheets) {
                const sheetName = sheet.name;
                // 제외 패턴 시트는 건너뛰기
                if (this.isSheetExcluded(sheetName)) {
                    continue;
                }

                const data = this.processSheetRows(sheet.rows, sheetName);

                for (const row of data) {
                    const loadDataFileName = row.LoadDataFileName;

                    if (loadDataFileName) {
                        // 문자열 또는 배열 처리
                        let fileNameString = '';

                        if (typeof loadDataFileName === 'string') {
                            fileNameString = loadDataFileName;
                        } else if (Array.isArray(loadDataFileName)) {
                            fileNameString = loadDataFileName.join(',');
                        } else {
                            continue;
                        }

                        // 쉼표로 구분된 파일명들 추가
                        const fileNames = fileNameString.split(',').map(name => name.trim());
                        fileNames.forEach(fileName => {
                            if (fileName) {
                                loadDataFiles.add(fileName);
                            }
                        });
                    }
                }
            }

            return Array.from(loadDataFiles);

        } catch (error) {
            this.log(`${this.loadDataFileName} 파일 목록 추출 실패: ${error.message}`, 'warning');
            return [];
        }
    }

    /**
     * 자료형에 따라 값 변환
     */
    convertValue(value, type, columnName) {
        // 자료형에서 필수 여부(!) 확인
        const isRequired = type.endsWith('!');
        let baseType = type;

        if (isRequired) {
            baseType = type.slice(0, -1);
        }

        // 빈 값 처리
        const isEmpty = value === null || value === undefined || value === '';

        if (isEmpty) {
            if (isRequired) {
                this.log(`필수 컬럼에 빈 값 발견 (${columnName}): 자료형 '${type}'은 값이 필수입니다`, 'error');
                return this.getDefaultValueForType(baseType);
            }
            return null;
        }

        // 문자열로 변환 후 처리
        const strValue = String(value).trim();

        // 빈 문자열 재처리
        if (strValue === '') {
            if (isRequired) {
                this.log(`필수 컬럼에 빈 문자열 발견 (${columnName}): 자료형 '${type}'은 값이 필수입니다`, 'error');
                return this.getDefaultValueForType(baseType);
            }
            return null;
        }

        // enum 컬럼: 멤버 이름 문자열 그대로 내보냄 (C# 역직렬화 시 enum 파싱)
        // 타입행에는 enum명만 적는다 (예: 'CurrencyType'). 'enum:' 접두 표기는 폐기.
        const enumName = this.resolveEnumType(baseType);
        if (enumName !== null) {
            if (baseType.toLowerCase().startsWith('enum:')) {
                this.log(`'enum:' 접두 표기는 폐기되었습니다 (${columnName}): 자료형을 '${enumName}'로만 적으세요`, 'warning');
            }
            const members = this.enumDefs.get(enumName);
            if (members && members.size > 0 && members.has(strValue.toLowerCase()) === false) {
                this.log(`enum 멤버가 아닌 값 (${columnName}): '${strValue}' 는 ${enumName} 정의에 없습니다`, 'warning');
            }
            return strValue;
        }

        switch (baseType.toLowerCase()) {
            case 'int':
            case 'integer':
            case 'long': {
                // 천 단위 구분자(쉼표) 제거
                const cleanIntString = strValue.replace(/,/g, '');
                const intVal = parseInt(cleanIntString, 10);
                if (isNaN(intVal)) {
                    this.log(`정수 변환 실패 (${columnName}): '${strValue}' -> 기본값 0 사용`, 'warning');
                    return isRequired ? 0 : null;
                }
                return intVal;
            }

            case 'float':
            case 'double':
            case 'number': {
                // 천 단위 구분자(쉼표) 제거
                const cleanNumString = strValue.replace(/,/g, '');
                const numVal = parseFloat(cleanNumString);
                if (isNaN(numVal)) {
                    this.log(`숫자 변환 실패 (${columnName}): '${strValue}' -> 기본값 0 사용`, 'warning');
                    return isRequired ? 0 : null;
                }
                return numVal;
            }

            case 'bool':
            case 'boolean': {
                if (strValue.toLowerCase() === 'true' || strValue === '1') return true;
                if (strValue.toLowerCase() === 'false' || strValue === '0') return false;
                const boolVal = Boolean(strValue);
                if (isRequired && !boolVal) {
                    this.log(`필수 불린 컬럼에 거짓 값 (${columnName}): '${strValue}' -> true 사용`, 'warning');
                    return true;
                }
                return boolVal;
            }

            // 배열은 항상 '<자료형>array' 이름 + 쉼표 구분 셀만 허용.
            // 요소에 쉼표가 들어가야 하는 데이터는 배열이 아니라 1:N 시트로 정규화한다.
            case 'intarray': {
                const intArrayVal = strValue.split(',').map(item => {
                    const intVal = parseInt(item.trim(), 10);
                    if (isNaN(intVal)) {
                        this.log(`정수 배열 요소 변환 실패 (${columnName}): '${item.trim()}' -> 0 사용`, 'warning');
                        return 0;
                    }
                    return intVal;
                });
                return intArrayVal.length > 0 ? intArrayVal : (isRequired ? [] : null);
            }

            case 'floatarray': {
                const floatArrayVal = strValue.split(',').map(item => {
                    const numVal = parseFloat(item.trim());
                    if (isNaN(numVal)) {
                        this.log(`실수 배열 요소 변환 실패 (${columnName}): '${item.trim()}' -> 0 사용`, 'warning');
                        return 0;
                    }
                    return numVal;
                });
                return floatArrayVal.length > 0 ? floatArrayVal : (isRequired ? [] : null);
            }

            case 'stringarray': {
                const stringArrayVal = strValue.split(',').map(item => item.trim());
                return stringArrayVal.length > 0 ? stringArrayVal : (isRequired ? [] : null);
            }

            // '|' 구분 배열은 폐기됐다. 배열 자료형은 '<자료형>Array' + 쉼표 구분만 허용한다.
            case 'stringarray|':
            case 'intarray|':
            case 'floatarray|':
                throw new Error(`'|' 구분 배열 자료형은 사용할 수 없습니다 (${columnName}): 요소에 쉼표가 들어가는 데이터는 1:N 시트로 정규화하세요`);

            case 'array':
                throw new Error(`'array' 자료형은 사용할 수 없습니다 (${columnName}): intarray/floatarray/stringarray 중 하나를 사용하세요`);

            case 'json':
                throw new Error(`'json' 자료형은 사용할 수 없습니다 (${columnName}): 기본 자료형·enum으로 정규화하세요`);

            case 'null':
                return null;

            case 'string':
            case 'text':
            default:
                return strValue;
        }
    }

    /**
     * 자료형에 따른 기본값 반환
     */
    getDefaultValueForType(type) {
        switch (type.toLowerCase()) {
            case 'int':
            case 'integer':
            case 'long':
            case 'float':
            case 'double':
            case 'number':
                return 0;
            case 'bool':
            case 'boolean':
                return false;
            case 'intarray':
            case 'floatarray':
            case 'stringarray':
            case 'stringarray|':
                return [];
            case 'string':
            case 'text':
            default:
                return '';
        }
    }

    /**
     * 표 하나(행 배열)에서 데이터 추출 및 변환
     * rows: 0행=컬럼명, 1행=자료형, 2행부터 데이터. 출처가 xlsx/js/py 무엇이든 여기로 모인다.
     */
    processSheetRows(rows, sheetName) {
        const jsonData = rows || [];

        if (jsonData.length < 3) {
            this.log(`표에 충분한 데이터가 없습니다: ${sheetName}`, 'warning');
            return [];
        }

        // js/py에서는 셀에 숫자가 그대로 올 수 있다. 컬럼명·자료형 행은 문자열로 맞춰둔다.
        const toText = cell => (cell === null || cell === undefined ? '' : String(cell).trim());
        const headerRow = (jsonData[0] || []).map(toText);  // 첫 번째 행: 컬럼명
        const typeRow = (jsonData[1] || []).map(toText);    // 두 번째 행: 자료형
        let dataRows = jsonData.slice(2);                   // 세 번째 행부터: 실제 데이터

        // 자료형이 정의된 컬럼만 유효한 데이터 컬럼으로 처리
        const validColumnCount = this.getValidColumnCount(typeRow);

        // 테이블 범위 밖 데이터 제거
        dataRows = this.removeDataOutsideTable(dataRows, validColumnCount);

        // 컬럼 정보 구성 (자료형이 정의된 컬럼만 처리)
        const columns = [];
        let excludedColumnCount = 0;

        for (let i = 0; i < validColumnCount; i++) {
            const type = typeRow[i] || 'string';
            const name = headerRow[i] || `Column${i}`;

            // 자료형이 비어있으면 제외
            if (!type || type.trim() === '') {
                continue;
            }

            if (name && !this.isColumnExcluded(name)) {
                columns.push({
                    index: i,
                    name: name,
                    type: type
                });
            } else if (name && this.isColumnExcluded(name)) {
                excludedColumnCount++;
                this.log(`컬럼 제외됨: ${name}`, 'debug');
            }
        }

        if (excludedColumnCount > 0) {
            this.log(`${sheetName}: ${excludedColumnCount}개 컬럼이 제외되었습니다`, 'info');
        }

        this.schemas[sheetName] = columns;

        // 데이터 변환
        const result = [];
        for (let rowIndex = 0; rowIndex < dataRows.length; rowIndex++) {
            const row = dataRows[rowIndex];
            const item = {};
            let hasData = false;

            for (const column of columns) {
                const rawValue = row[column.index];
                const convertedValue = this.convertValue(rawValue, column.type, column.name);

                item[column.name] = convertedValue;

                if (convertedValue !== null && convertedValue !== undefined && convertedValue !== '') {
                    hasData = true;
                }
            }

            // 빈 행은 제외
            if (hasData) {
                result.push(item);
            }
        }

        this.log(`${sheetName}: ${result.length}개 행 처리 완료`, 'success');
        return result;
    }

    /**
     * 자료형이 정의된 유효한 컬럼 수 계산
     */
    getValidColumnCount(typeRow) {
        let validCount = 0;
        for (let i = 0; i < typeRow.length; i++) {
            const type = typeRow[i];
            if (type && type.trim() !== '') {
                validCount = i + 1; // 마지막 유효 컬럼 위치 + 1
            }
        }
        return validCount;
    }

    /**
     * 테이블 범위 밖 데이터 제거 (유효 컬럼 수 기준)
     */
    removeDataOutsideTable(dataRows, validColumnCount) {
        const result = [];
        let consecutiveEmptyRows = 0;
        const maxConsecutiveEmptyRows = 3; // 연속된 빈 행이 3개 이상이면 중단

        for (let i = 0; i < dataRows.length; i++) {
            const row = dataRows[i];

            // 유효한 컬럼 범위 내에서 데이터가 있는지 확인
            let hasValidData = false;
            for (let j = 0; j < validColumnCount; j++) {
                const cellValue = row[j];
                if (cellValue !== null && cellValue !== undefined && cellValue !== '') {
                    hasValidData = true;
                    break;
                }
            }

            if (hasValidData) {
                consecutiveEmptyRows = 0;
                result.push(row);
            } else {
                consecutiveEmptyRows++;
                if (consecutiveEmptyRows >= maxConsecutiveEmptyRows) {
                    this.log(`연속된 빈 행 ${consecutiveEmptyRows}개 발견, 테이블 범위 밖으로 판단하여 중단`, 'debug');
                    break;
                }
            }
        }

        return result;
    }

    /**
     * 단일 소스 파일(.xlsx / .js / .py) 처리
     */
    processSourceFile(filename) {
        const filePath = path.join(this.gamedataPath, filename);
        const baseName = this.baseNameOf(filename);

        this.log(`처리 시작: ${filename}`, 'info');

        try {
            const workbook = this.loadWorkbook(filePath);

            // 목록 파일 특별 처리
            if (baseName === this.loadDataBaseName) {
                return this.processLoadDataFile(workbook, filename);
            }

            // enum 정의 파일 특별 처리
            if (this.isEnumFile(filename)) {
                return this.processEnumFile(workbook, filename, baseName);
            }

            // 일반 파일 처리
            const results = {};
            let excludedSheetCount = 0;

            // 모든 표 처리
            for (const sheet of workbook.sheets) {
                const sheetName = sheet.name;
                // 설정에서 제외된 시트는 건너뛰기
                if (this.config.ExceptCheckEmptySheet &&
                    this.config.ExceptCheckEmptySheet.includes(sheetName)) {
                    this.log(`시트 제외됨 (설정): ${sheetName}`, 'debug');
                    continue;
                }

                // 제외 패턴에 해당하는 시트는 건너뛰기
                if (this.isSheetExcluded(sheetName)) {
                    excludedSheetCount++;
                    this.log(`시트 제외됨 (패턴): ${sheetName}`, 'debug');
                    continue;
                }

                const data = this.processSheetRows(sheet.rows, sheetName);

                if (data.length > 0) {
                    results[sheetName] = data;
                }
            }

            if (excludedSheetCount > 0) {
                this.log(`${filename}: ${excludedSheetCount}개 시트가 제외되었습니다`, 'info');
            }

            // JSON 파일 생성 (각 시트별로 개별 파일)
            this.createJsonFiles(baseName, results, false);

            return {
                success: true,
                filename: filename,
                sheets: Object.keys(results),
                totalRecords: Object.values(results).reduce((sum, data) => sum + data.length, 0)
            };

        } catch (error) {
            this.log(`파일 처리 실패 (${filename}): ${error.message}`, 'error');
            return {
                success: false,
                filename: filename,
                error: error.message
            };
        }
    }

    /**
     * LoadDataFile.xlsx 특별 처리
     */
    processLoadDataFile(workbook, filename) {
        this.log('LoadDataFile.xlsx 특별 처리 시작', 'info');

        try {
            // LoadDataFile에서 파일 정보 추출
            const loadDataInfo = this.extractLoadDataInfo(workbook);

            if (Object.keys(loadDataInfo).length === 0) {
                this.log('LoadDataFile.xlsx에서 유효한 데이터를 찾을 수 없습니다.', 'warning');
                return { success: true, filename: filename, sheets: [], totalRecords: 0 };
            }

            // FileType별로 파일들을 합쳐서 처리
            const results = {};

            for (const [fileType, info] of Object.entries(loadDataInfo)) {
                this.log(`FileType '${fileType}' 처리 시작 - 파일들: [${info.files.join(', ')}]`, 'info');

                const combinedData = [];
                let expectedColumns = null;
                // 합쳐진 결과는 FileType 이름으로 나가는데, 스키마는 원본 '시트명'으로 저장돼 있다.
                // 여기서 FileType 키로도 스키마를 달아주지 않으면 JSON만 나오고 CS 클래스가 조용히 안 나온다.
                let combinedSchema = null;

                // 각 파일을 순서대로 처리
                const allFilesToProcess = [...info.files]; // 기존 파일들

                // FileType과 같은 이름의 파일도 추가 (만약 존재한다면)
                if (!allFilesToProcess.includes(fileType)) {
                    if (this.findSourceFile(fileType)) {
                        allFilesToProcess.unshift(fileType); // 맨 앞에 추가 (메인 파일)
                        this.log(`FileType 메인 파일 추가: ${fileType}`, 'debug');
                    }
                }

                for (const fileName of allFilesToProcess) {
                    // 목록에는 확장자 없이 이름만 적으므로 지원 확장자를 순서대로 찾는다
                    const filePath = this.findSourceFile(fileName);

                    if (!filePath) {
                        this.log(`파일을 찾을 수 없습니다: ${fileName} (${this.activeExtensions.join(' / ')})`, 'warning');
                        continue;
                    }

                    this.log(`처리 중: ${path.basename(filePath)} (경로: ${filePath})`, 'info');

                    try {
                        const combineResult = this.processSourceFileForCombine(filePath, fileName);
                        const fileData = combineResult.data;

                        if (combinedSchema === null && combineResult.schema) {
                            combinedSchema = combineResult.schema;
                        }

                        if (fileData.length === 0) {
                            this.log(`${fileName}.xlsx에서 데이터를 찾을 수 없습니다.`, 'warning');
                            continue;
                        }

                        // 첫 번째 파일의 컬럼 구조를 기준으로 설정
                        if (expectedColumns === null) {
                            expectedColumns = Object.keys(fileData[0]);
                        } else {
                            // 컬럼 구조 검증
                            const currentColumns = Object.keys(fileData[0]);
                            if (!this.compareColumns(currentColumns, expectedColumns)) {
                                throw new Error(
                                    `FileType '${fileType}'의 파일들 간 컬럼 구조가 일치하지 않습니다.\n` +
                                    `기준 컬럼: [${expectedColumns.join(', ')}]\n` +
                                    `${fileName}.xlsx 컬럼: [${currentColumns.join(', ')}]\n` +
                                    `모든 파일의 컬럼명과 순서가 완전히 동일해야 합니다.`
                                );
                            }
                        }

                        // 데이터 합치기
                        combinedData.push(...fileData);
                        this.log(`${fileName}.xlsx: ${fileData.length}개 행 추가 (총 ${combinedData.length}개 행)`, 'info');

                        if (this.verbose && fileData.length > 0) {
                            this.log(`${fileName}.xlsx 샘플 데이터: ${JSON.stringify(fileData[0], null, 2)}`, 'debug');
                        }

                    } catch (error) {
                        this.log(`${fileName}.xlsx 처리 실패: ${error.message}`, 'error');
                        throw error;
                    }
                }

                if (combinedData.length > 0) {
                    results[fileType] = combinedData;
                    if (combinedSchema && !this.schemas[fileType]) {
                        this.schemas[fileType] = combinedSchema;
                    }
                    this.log(`FileType '${fileType}': 총 ${combinedData.length}개 행 합쳐짐`, 'success');
                } else {
                    this.log(`FileType '${fileType}': 합칠 데이터가 없습니다.`, 'warning');
                }
            }

            // JSON 파일 생성 (FileType별로 개별 파일)
            this.createJsonFiles(this.loadDataBaseName, results, true);

            return {
                success: true,
                filename: filename,
                sheets: Object.keys(results),
                totalRecords: Object.values(results).reduce((sum, data) => sum + data.length, 0)
            };

        } catch (error) {
            this.log(`LoadDataFile.xlsx 처리 실패: ${error.message}`, 'error');
            return {
                success: false,
                filename: filename,
                error: error.message
            };
        }
    }

    /**
     * 목록 파일에서 FileType별 파일 정보 추출
     */
    extractLoadDataInfo(workbook) {
        const loadDataInfo = {};

        for (const sheet of workbook.sheets) {
            const sheetName = sheet.name;
            // 설정에서 제외된 시트는 건너뛰기
            if (this.config.ExceptCheckEmptySheet &&
                this.config.ExceptCheckEmptySheet.includes(sheetName)) {
                this.log(`시트 제외됨: ${sheetName}`, 'debug');
                continue;
            }

            // 제외 패턴에 해당하는 시트는 건너뛰기
            if (this.isSheetExcluded(sheetName)) {
                this.log(`시트 제외됨 (패턴): ${sheetName}`, 'debug');
                continue;
            }

            const data = this.processSheetRows(sheet.rows, sheetName);

            for (const row of data) {
                const fileType = row.FileType;
                const loadDataFileName = row.LoadDataFileName;

                if (!fileType || !loadDataFileName) {
                    continue;
                }

                // loadDataFileName이 문자열 또는 배열인지 확인하여 처리
                let fileNameString = '';

                if (typeof loadDataFileName === 'string') {
                    fileNameString = loadDataFileName;
                } else if (Array.isArray(loadDataFileName)) {
                    // 배열인 경우 쉼표로 연결
                    fileNameString = loadDataFileName.join(',');
                    this.log(`LoadDataFileName이 배열로 읽힘 (FileType: ${fileType}): 문자열로 변환`, 'debug');
                } else {
                    this.log(`LoadDataFileName이 예상치 못한 타입 (FileType: ${fileType}): ${typeof loadDataFileName}`, 'warning');
                    continue;
                }

                if (!loadDataInfo[fileType]) {
                    loadDataInfo[fileType] = {
                        files: [],
                        sheets: []
                    };
                }

                // LoadDataFileName을 쉼표로 분리
                const fileNames = fileNameString.split(',').map(name => name.trim());

                for (const fileName of fileNames) {
                    if (fileName && !loadDataInfo[fileType].files.includes(fileName)) {
                        loadDataInfo[fileType].files.push(fileName);
                        this.log(`LoadDataFile에서 발견: FileType='${fileType}', LoadDataFileName='${fileName}'`, 'debug');
                    }
                }

                if (!loadDataInfo[fileType].sheets.includes(sheetName)) {
                    loadDataInfo[fileType].sheets.push(sheetName);
                }
            }
        }

        return loadDataInfo;
    }

    /**
     * 합치기를 위한 소스 파일 처리 (모든 표 처리 후 병합)
     */
    processSourceFileForCombine(filePath, fileName) {
        try {
            const workbook = this.loadWorkbook(filePath);

            let combinedData = [];
            let expectedColumns = null;
            let firstSchema = null;

            // 모든 표를 처리하고 병합
            for (const sheet of workbook.sheets) {
                const sheetName = sheet.name;
                // 제외 패턴에 해당하는 시트는 건너뛰기
                if (this.isSheetExcluded(sheetName)) {
                    this.log(`시트 제외됨 (패턴): ${sheetName}`, 'debug');
                    continue;
                }

                const sheetData = this.processSheetRows(sheet.rows, sheetName);

                if (sheetData.length === 0) {
                    this.log(`${fileName}의 ${sheetName} 시트에서 데이터를 찾을 수 없습니다.`, 'debug');
                    continue;
                }

                // 첫 번째 시트의 컬럼 구조를 기준으로 설정
                if (expectedColumns === null) {
                    expectedColumns = Object.keys(sheetData[0]);
                    firstSchema = this.schemas[sheetName] || null;
                    this.log(`${fileName}의 기준 컬럼 구조 설정: [${expectedColumns.join(', ')}]`, 'debug');
                } else {
                    // 컬럼 구조 검증 (선택적)
                    const currentColumns = Object.keys(sheetData[0]);
                    if (!this.compareColumns(currentColumns, expectedColumns)) {
                        this.log(
                            `${fileName}의 ${sheetName} 시트 컬럼 구조가 다릅니다.\n` +
                            `기준: [${expectedColumns.join(', ')}]\n` +
                            `현재: [${currentColumns.join(', ')}]\n` +
                            `데이터를 그대로 추가합니다.`,
                            'warning'
                        );
                    }
                }

                // 데이터 병합
                combinedData.push(...sheetData);
                this.log(`${fileName}의 ${sheetName} 시트: ${sheetData.length}개 행 추가`, 'debug');
            }

            this.log(`${fileName} 총 ${combinedData.length}개 행 병합 완료`, 'info');
            return { data: combinedData, schema: firstSchema };

        } catch (error) {
            this.log(`파일 읽기 실패 (${fileName}): ${error.message}`, 'error');
            throw error;
        }
    }

    /**
     * 컬럼 구조 비교 (순서와 이름 모두 동일해야 함)
     */
    compareColumns(columns1, columns2) {
        if (columns1.length !== columns2.length) {
            return false;
        }

        for (let i = 0; i < columns1.length; i++) {
            if (columns1[i] !== columns2[i]) {
                return false;
            }
        }

        return true;
    }

    /**
     * enum 정의 파일 처리
     * - 각 시트의 열 하나 = enum 하나 (헤더 셀 = enum 이름, 아래 셀들 = 멤버)
     * - {Enum, Value} 행 배열로 변환해 시트명이 아닌 "파일명" 기준 JSON 하나로 출력한다 (_Enum.json).
     *   이 JSON은 코드젠에 쓰이지 않고(=GameEnum.cs는 엑셀에서 바로 뽑는다)
     *   Live Data Editor로 enum 목록을 훑어보기 위한 참고용이다.
     * - 데이터 클래스(CS)는 생성하지 않는다
     */
    processEnumFile(workbook, filename, baseName) {
        const entries = [];
        const sheets = [];

        for (const sheet of workbook.sheets) {
            const sheetName = sheet.name;
            // 임시 시트만 제외 ('_' 접두사 시트 제외 규칙은 적용하지 않음)
            if (sheetName.includes('~')) {
                continue;
            }

            const rows = sheet.rows;
            if (rows.length < 2) {
                continue;
            }

            const headerRow = rows[0];
            let sheetEntryCount = 0;

            for (let col = 0; col < headerRow.length; col++) {
                const rawEnumName = headerRow[col] == null ? '' : String(headerRow[col]).trim();
                if (rawEnumName === '' || rawEnumName.startsWith('#')) {
                    continue;
                }

                // enum 네이밍 컨벤션 강제: E접두사 제거 + 'Type' 접미사 필수
                const enumName = normalizeEnumName(rawEnumName);
                if (enumName !== rawEnumName) {
                    this.log(`enum 이름 컨벤션 위반: '${rawEnumName}' -> '${enumName}' 로 정규화됨. ${filename}의 헤더를 수정하세요.`, 'warning');
                }

                for (let row = 1; row < rows.length; row++) {
                    const cell = rows[row] ? rows[row][col] : null;
                    const member = cell == null ? '' : String(cell).trim();
                    if (member === '' || member.startsWith('#')) {
                        continue;
                    }
                    entries.push({ Enum: enumName, Value: member });
                    sheetEntryCount++;
                }
            }

            if (sheetEntryCount > 0) {
                sheets.push(sheetName);
            }
        }

        if (entries.length === 0) {
            this.log(`${filename}: enum 정의를 찾지 못했습니다 (헤더 셀 = enum 이름, 아래 셀들 = 멤버)`, 'warning');
            return { success: true, filename: filename, sheets: [], totalRecords: 0 };
        }

        if (!fs.existsSync(this.jsonOutputPath)) {
            fs.mkdirSync(this.jsonOutputPath, { recursive: true });
        }

        const outputFileName = `${baseName}.json`;
        const outputPath = path.join(this.jsonOutputPath, outputFileName);
        fs.writeFileSync(outputPath, JSON.stringify(entries, null, 4), 'utf8');
        this.log(`JSON 생성 완료: ${outputFileName} (enum 멤버 ${entries.length}개)`, 'success');

        if (this.config.GenerateMetaFiles !== false) {
            fs.writeFileSync(outputPath + '.meta', this.generateMetaFile(), 'utf8');
        }

        return {
            success: true,
            filename: filename,
            sheets: sheets,
            totalRecords: entries.length
        };
    }

    /**
     * JSON 파일 생성
     */
    createJsonFiles(baseName, results, isLoadDataFile = false) {
        // 출력 디렉토리 확인/생성
        if (!fs.existsSync(this.jsonOutputPath)) {
            fs.mkdirSync(this.jsonOutputPath, { recursive: true });
        }

        // 이번에 처리된 시트들의 컨테이너 베이스를 Containers.Generated.cs 한 파일에 병합 생성
        const processedSchemas = {};

        for (const [key, data] of Object.entries(results)) {
            // 파일명 결정
            let outputFileName;
            if (isLoadDataFile) {
                // LoadDataFile의 경우 FileType 이름으로 파일 생성
                outputFileName = `${key}.json`;
            } else {
                // 일반 파일의 경우 시트명으로 파일 생성
                outputFileName = `${key}.json`;
            }

            const outputPath = path.join(this.jsonOutputPath, outputFileName);

            try {
                // JSON 파일 생성 (들여쓰기 4칸)
                fs.writeFileSync(outputPath, JSON.stringify(data, null, 4), 'utf8');
                this.log(`JSON 생성 완료: ${outputFileName} (${data.length}개 레코드)`, 'success');

                // Unity용 .meta 파일 생성 (옵션)
                if (this.config.GenerateMetaFiles !== false) {
                    const metaPath = outputPath + '.meta';
                    const metaContent = this.generateMetaFile();
                    fs.writeFileSync(metaPath, metaContent, 'utf8');
                }

            } catch (error) {
                this.log(`JSON 생성 실패 (${outputFileName}): ${error.message}`, 'error');
            }
        }

    }

    /**
     * _Schema.json 갱신 (표별 컬럼/자료형 + enum 정의)
     * Unity의 DB Generate가 이 파일만 읽어 C#을 만든다. C#은 여기서 만들지 않는다.
     * export 마지막에 항상 호출한다. 내용이 같으면 파일을 건드리지 않는다.
     */
    writeSchema() {
        if (!this.schemaOutputPath) {
            return;
        }

        const tables = {};
        for (const name of Object.keys(this.schemas).sort()) {
            const columns = this.schemas[name];
            if (!columns || columns.length === 0) {
                continue;
            }
            tables[name] = columns.map(c => ({ name: String(c.name), type: String(c.type) }));
        }

        const enums = {};
        for (const name of [...this.enumMemberOrder.keys()]) {
            const seen = new Set();
            const members = [];
            for (const raw of this.enumMemberOrder.get(name)) {
                const member = String(raw);
                if (seen.has(member)) {
                    continue;
                }
                seen.add(member);
                members.push(member);
            }
            if (members.length > 0) {
                enums[name] = members;
            }
        }

        if (Object.keys(tables).length === 0 && Object.keys(enums).length === 0) {
            return;
        }

        const content = JSON.stringify({ tables: tables, enums: enums }, null, 4);

        const dir = path.dirname(this.schemaOutputPath);
        if (!fs.existsSync(dir)) {
            fs.mkdirSync(dir, { recursive: true });
        }

        if (fs.existsSync(this.schemaOutputPath) && fs.readFileSync(this.schemaOutputPath, 'utf8') === content) {
            this.log(`_Schema.json 변경 없음 (표 ${Object.keys(tables).length}개)`, 'debug');
            return;
        }

        fs.writeFileSync(this.schemaOutputPath, content, 'utf8');
        this.log(`스키마 생성 완료: _Schema.json (표 ${Object.keys(tables).length}개, enum ${Object.keys(enums).length}개)`, 'success');
    }

    /**
     * Unity용 .meta 파일 생성
     */
    generateMetaFile() {
        const guid = this.generateGuid();
        return `fileFormatVersion: 2
guid: ${guid}
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
`;
    }

    /**
     * GUID 생성
     */
    generateGuid() {
        return 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx'.replace(/[x]/g, function () {
            return (Math.random() * 16 | 0).toString(16);
        });
    }

    /**
     * 모든 엑셀 파일 처리
     */
    exportAll() {
        const results = [];
        let successCount = 0;
        let errorCount = 0;

        // 1. 먼저 LoadDataFile.xlsx 처리
        const loadDataFilePath = this.findSourceFile(this.loadDataBaseName);
        if (loadDataFilePath) {
            this.log(`${path.basename(loadDataFilePath)} 처리 시작`, 'info');
            const result = this.processSourceFile(path.basename(loadDataFilePath));
            results.push(result);

            if (result.success) successCount++;
            else errorCount++;
        }

        // 2. 나머지 개별 파일들 처리 (LoadDataFile에 정의된 파일들 제외)
        const excelFiles = this.getSourceFiles();

        if (excelFiles.length === 0 && !loadDataFilePath) {
            this.log('처리할 엑셀 파일이 없습니다.', 'warning');
            return { success: true, results: [] };
        }

        this.log(`총 ${excelFiles.length}개 개별 파일 처리 시작`, 'info');

        let fileIndex = 0;
        for (const filename of excelFiles) {
            fileIndex++;
            console.log('');
            this.log(`[${fileIndex}/${excelFiles.length}] ${filename}`, 'info');
            const result = this.processSourceFile(filename);
            results.push(result);

            if (result.success) {
                successCount++;
                this.log(`[${fileIndex}/${excelFiles.length}] ${filename} 완료 - 시트 ${result.sheets.length}개 / ${result.totalRecords}개 레코드`, 'success');
            } else {
                errorCount++;
                this.log(`[${fileIndex}/${excelFiles.length}] ${filename} 실패 - ${result.error}`, 'error');
            }
        }

        this.writeSchema();

        // 결과 요약
        this.log('\n=== 처리 완료 ===', 'info');
        this.log(`성공: ${successCount}개`, 'success');
        this.log(`실패: ${errorCount}개`, errorCount > 0 ? 'error' : 'info');

        if (errorCount > 0) {
            this.log('\n실패한 파일:', 'error');
            results.filter(r => !r.success).forEach(r => {
                this.log(`  - ${r.filename}: ${r.error}`, 'error');
            });
        }

        return {
            success: errorCount === 0,
            results: results,
            summary: {
                total: results.length,
                success: successCount,
                error: errorCount
            }
        };
    }

    /**
     * 특정 파일들만 처리
     */
    exportFiles(filenames) {
        const results = [];
        let successCount = 0;
        let errorCount = 0;
        let fileIndex = 0;

        for (const filename of filenames) {
            if (!this.isSourceFile(filename)) {
                this.log(`변환 대상 확장자가 아닙니다: ${filename} (${this.activeExtensions.join(' / ')})`, 'warning');
                continue;
            }

            fileIndex++;
            console.log('');
            this.log(`[${fileIndex}/${filenames.length}] ${filename}`, 'info');

            const filePath = path.join(this.gamedataPath, filename);
            if (!fs.existsSync(filePath)) {
                this.log(`파일이 없습니다: ${filename}`, 'error');
                results.push({
                    success: false,
                    filename: filename,
                    error: '파일이 존재하지 않습니다'
                });
                errorCount++;
                continue;
            }

            const result = this.processSourceFile(filename);
            results.push(result);

            if (result.success) {
                successCount++;
                this.log(`[${fileIndex}/${filenames.length}] ${filename} 완료 - 시트 ${result.sheets.length}개 / ${result.totalRecords}개 레코드`, 'success');
            } else {
                errorCount++;
                this.log(`[${fileIndex}/${filenames.length}] ${filename} 실패 - ${result.error}`, 'error');
            }
        }

        this.writeSchema();

        console.log('');
        this.log('=== 처리 완료 ===', 'info');
        this.log(`성공: ${successCount}개`, 'success');
        this.log(`실패: ${errorCount}개`, errorCount > 0 ? 'error' : 'info');

        if (errorCount > 0) {
            this.log('실패한 파일:', 'error');
            results.filter(r => !r.success).forEach(r => {
                this.log(`  - ${r.filename}: ${r.error}`, 'error');
            });
        }

        return { success: errorCount === 0, results: results };
    }

    /**
     * (보강) 수정된 파일만 처리
     * - 기존 구현은 "baseName.json"만 비교했는데, 실제 출력은 시트명 기반 파일(예: Sheet1.json)이어서
     *   다중 시트에서 오동작할 수 있습니다.
     * - 여기서는 "해당 엑셀에서 생성될 수 있는 출력 파일들"의 mtime을 비교합니다.
     */
    exportModified() {
        const modifiedFiles = [];

        // LoadDataFile.xlsx도 modified 대상에 포함 (존재하는 경우)
        const loadDataFilePath = this.findSourceFile(this.loadDataBaseName);
        if (loadDataFilePath) {
            const excelStat = fs.statSync(loadDataFilePath);
            const shouldProcess = this.shouldProcessLoadDataFile(excelStat.mtime, loadDataFilePath);
            if (shouldProcess) {
                modifiedFiles.push(path.basename(loadDataFilePath));
            }
        }

        // 개별 엑셀 파일들 (LoadDataFile에 정의된 파일들 제외)
        const excelFiles = this.getSourceFiles();

        for (const filename of excelFiles) {
            const excelPath = path.join(this.gamedataPath, filename);
            const excelStat = fs.statSync(excelPath);

            const shouldProcess = this.shouldProcessWorkbook(filename, excelPath, excelStat.mtime);
            if (shouldProcess) {
                modifiedFiles.push(filename);
            }
        }

        if (modifiedFiles.length === 0) {
            // 엑셀 변경이 없어도 콘크리트 컨테이너 추가/삭제는 반영해야 한다
            this.writeSchema();
            this.log('수정된 파일이 없습니다.', 'success');
            return { success: true, results: [] };
        }

        this.log(`수정된 파일 ${modifiedFiles.length}개 처리`, 'info');
        return this.exportFiles(modifiedFiles);
    }

    shouldProcessWorkbook(filename, excelPath, excelMtime) {
        try {
            // enum 정의 파일은 파일명 기준 JSON 하나로 출력되므로 그 파일과 비교
            if (this.isEnumFile(filename)) {
                const enumJsonPath = path.join(this.jsonOutputPath, `${this.baseNameOf(filename)}.json`);
                if (!fs.existsSync(enumJsonPath)) {
                    return true;
                }
                return excelMtime > fs.statSync(enumJsonPath).mtime;
            }

            const workbook = this.loadWorkbook(excelPath);

            const candidateSheets = workbook.sheets
                .map(sheet => sheet.name)
                .filter(sheetName => {
                    if (this.config.ExceptCheckEmptySheet && this.config.ExceptCheckEmptySheet.includes(sheetName)) {
                        return false;
                    }
                    if (this.isSheetExcluded(sheetName)) return false;
                    return true;
                });

            if (candidateSheets.length === 0) {
                // 처리할 시트가 없으면, 변경 감지 의미가 없으니 스킵
                return false;
            }

            // 시트명 기반 출력 파일 비교
            for (const sheetName of candidateSheets) {
                const jsonPath = path.join(this.jsonOutputPath, `${sheetName}.json`);
                if (!fs.existsSync(jsonPath)) {
                    return true;
                }
                const jsonStat = fs.statSync(jsonPath);
                if (excelMtime > jsonStat.mtime) {
                    return true;
                }
            }

            return false;
        } catch (e) {
            // mtime 비교 과정에서 실패하면 안전하게 "처리 필요"로 간주
            this.log(`modified 체크 중 오류 (${filename}): ${e.message} -> 처리 대상으로 포함`, 'warning');
            return true;
        }
    }

    shouldProcessLoadDataFile(excelMtime, loadDataFilePath) {
        try {
            const workbook = this.loadWorkbook(loadDataFilePath);

            const loadDataInfo = this.extractLoadDataInfo(workbook);
            const fileTypes = Object.keys(loadDataInfo);
            if (fileTypes.length === 0) {
                return false;
            }

            for (const fileType of fileTypes) {
                const jsonPath = path.join(this.jsonOutputPath, `${fileType}.json`);
                if (!fs.existsSync(jsonPath)) {
                    return true;
                }
                const jsonStat = fs.statSync(jsonPath);
                if (excelMtime > jsonStat.mtime) {
                    return true;
                }
            }

            return false;
        } catch (e) {
            this.log(`LoadDataFile modified 체크 중 오류: ${e.message} -> 처리 대상으로 포함`, 'warning');
            return true;
        }
    }
}

// CLI 인터페이스
function main() {
    const args = process.argv.slice(2);
    const command = args[0] || 'all';

    const options = {
        configPath: process.env.CONFIG_PATH || null,
        verbose: args.includes('--verbose') || args.includes('-v')
    };

    const exporter = new SmartExcelExporter(options);

    switch (command) {
        case 'all': {
            const result = exporter.exportAll();
            if (!result.success) process.exitCode = 1;
            break;
        }

        case 'modified': {
            const result = exporter.exportModified();
            if (result && result.success === false) process.exitCode = 1;
            break;
        }

        case 'file': {
            const files = args.slice(1).filter(arg => !arg.startsWith('--'));
            if (files.length === 0) {
                console.log('처리할 파일을 지정해주세요.');
                process.exit(1);
            }
            const result = exporter.exportFiles(files);
            if (!result.success) process.exitCode = 1;
            break;
        }

        case 'help':
            console.log(`
Hugh Data Exporter (xlsx / js / py -> JSON + _Schema.json)

사용법:
  node smart_exporter.js [command] [files...] [options]

명령어:
  all        - 모든 소스 파일 처리 (기본값)
  modified   - 수정된 파일만 처리
  file       - 특정 파일들만 처리
  help       - 도움말 표시

지원 확장자: ${Object.keys(SOURCE_LOADERS).join(', ')}
  (smart_exporter.js 상단 SOURCE_EXTENSIONS에서 켜고 끕니다)

옵션:
  --verbose, -v  - 상세 로그 출력

기본 경로:
  입력:  ./gamedata
  출력:  ./jsondata

C# 은 여기서 만들지 않습니다.
  Unity 에서 Tools > GameData > DB Generate (Ctrl+G) 를 실행하세요.

환경변수:
  GAMEDATA_PATH       - 입력 폴더 경로
  JSON_OUTPUT_PATH    - JSON 출력 폴더 경로
  SCHEMA_OUTPUT_PATH  - _Schema.json 출력 파일 경로
  CONFIG_PATH         - 설정 파일 경로
            `);
            break;

        default:
            console.log(`알 수 없는 명령어: ${command}`);
            console.log('도움말을 보려면 "node smart_exporter.js help"를 실행하세요.');
            break;
    }
}

// 모듈로 사용할 때와 직접 실행할 때 구분
if (require.main === module) {
    main();
}

module.exports = SmartExcelExporter;

