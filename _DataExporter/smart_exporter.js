const fs = require('fs');
const path = require('path');
const XLSX = require('xlsx');
const { generateDataClass, generateContainersFile, generateGameRootFile, generateGameEnumFile, normalizeEnumName, setKnownEnums } = require('./class_generator');

class SmartExcelExporter {
    constructor(options = {}) {
        // Side-project friendly defaults
        this.gamedataPath = options.gamedataPath || path.join(process.cwd(), 'gamedata');
        this.jsonOutputPath = options.jsonOutputPath || path.join(process.cwd(), 'jsondata');
        this.configPath = options.configPath || path.join(process.cwd(), 'config.json');
        this.csOutputPath = options.csOutputPath || null;
        this.verbose = options.verbose || false;

        // GameRoot 코드젠 경로 — 미지정 시 csOutputPath(DataLoader/Generated) 기준으로 유도
        this.containersPath = options.containersPath
            || (this.csOutputPath ? path.resolve(this.csOutputPath, '..', 'Containers') : null);
        this.gameRootOutputPath = options.gameRootOutputPath
            || (this.csOutputPath ? path.resolve(this.csOutputPath, '..', '..', 'Game', 'Core', 'GameRoot.Generated.cs') : null);

        // GameEnum.cs — 데이터 클래스가 Game.GameEnum.XxxType을 참조하므로 같은 실행에서 같이 뽑아야
        // 컴파일이 한 번에 통과한다. DataLoader/Generated 기준 한 단계 위(DataLoader/)에 놓는다.
        this.gameEnumOutputPath = options.gameEnumOutputPath
            || (this.csOutputPath ? path.resolve(this.csOutputPath, '..', 'GameEnum.cs') : null);

        // 시트별 컬럼 스키마(이름/자료형) — C# 클래스 생성용
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
            .filter(file => file.endsWith('.xlsx'))
            .filter(file => !this.isExcluded(file))
            .filter(file => this.isEnumFile(file));

        for (const filename of enumFiles) {
            try {
                const workbook = XLSX.readFile(path.join(this.gamedataPath, filename), {
                    cellStyles: false,
                    cellFormulas: false,
                    sheetStubs: false
                });

                for (const sheetName of workbook.SheetNames) {
                    if (sheetName.includes('~')) {
                        continue;
                    }
                    const rows = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], { header: 1, defval: null });
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

        setKnownEnums([...this.enumDefs.keys()]);
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
        this.loadDataBaseName = path.basename(this.loadDataFileName, '.xlsx');

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
        const baseName = path.basename(filename, '.xlsx');
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

    /**
     * 엑셀 파일 목록 가져오기 (LoadDataFile에 정의된 파일들 제외)
     */
    getExcelFiles() {
        if (!fs.existsSync(this.gamedataPath)) {
            this.log(`입력 폴더가 없습니다: ${this.gamedataPath}`, 'error');
            return [];
        }

        const files = fs.readdirSync(this.gamedataPath);
        const allExcelFiles = files
            .filter(file => file.endsWith('.xlsx'))
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

        return allExcelFiles.filter(file => {
            const baseName = path.basename(file, '.xlsx');
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
        const loadDataFilePath = path.join(this.gamedataPath, this.loadDataFileName);

        if (!fs.existsSync(loadDataFilePath)) {
            return [];
        }

        try {
            const workbook = XLSX.readFile(loadDataFilePath, {
                cellStyles: false,
                cellFormulas: false,
                cellDates: true,
                cellNF: false,
                sheetStubs: false
            });

            const loadDataFiles = new Set();

            for (const sheetName of workbook.SheetNames) {
                // 제외 패턴 시트는 건너뛰기
                if (this.isSheetExcluded(sheetName)) {
                    continue;
                }

                const worksheet = workbook.Sheets[sheetName];
                const data = this.processWorksheet(worksheet, sheetName);

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
            this.log(`LoadDataFile.xlsx 파일 목록 추출 실패: ${error.message}`, 'warning');
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
     * 워크시트에서 데이터 추출 및 변환
     */
    processWorksheet(worksheet, sheetName) {
        const jsonData = XLSX.utils.sheet_to_json(worksheet, {
            header: 1,
            raw: false,
            defval: ''
        });

        if (jsonData.length < 3) {
            this.log(`시트에 충분한 데이터가 없습니다: ${sheetName}`, 'warning');
            return [];
        }

        const headerRow = jsonData[0];    // 첫 번째 행: 컬럼명
        const typeRow = jsonData[1];      // 두 번째 행: 자료형
        let dataRows = jsonData.slice(2); // 세 번째 행부터: 실제 데이터

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
     * 단일 엑셀 파일 처리
     */
    processExcelFile(filename) {
        const filePath = path.join(this.gamedataPath, filename);
        const baseName = path.basename(filename, '.xlsx');

        this.log(`처리 시작: ${filename}`, 'info');

        try {
            // 엑셀 파일 읽기
            const workbook = XLSX.readFile(filePath, {
                cellStyles: false,
                cellFormulas: false,
                cellDates: true,
                cellNF: false,
                sheetStubs: false
            });

            // LoadDataFile.xlsx 특별 처리
            if (baseName === this.loadDataBaseName) {
                return this.processLoadDataFile(workbook, filename);
            }

            // enum 정의 파일 특별 처리
            if (this.isEnumFile(filename)) {
                return this.processEnumFile(workbook, filename, baseName);
            }

            // 일반 엑셀 파일 처리
            const results = {};
            let excludedSheetCount = 0;

            // 모든 시트 처리
            for (const sheetName of workbook.SheetNames) {
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

                const worksheet = workbook.Sheets[sheetName];
                const data = this.processWorksheet(worksheet, sheetName);

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
                    const fileTypePath = path.join(this.gamedataPath, `${fileType}.xlsx`);
                    if (fs.existsSync(fileTypePath)) {
                        allFilesToProcess.unshift(fileType); // 맨 앞에 추가 (메인 파일)
                        this.log(`FileType 메인 파일 추가: ${fileType}.xlsx`, 'debug');
                    }
                }

                for (const fileName of allFilesToProcess) {
                    const filePath = path.join(this.gamedataPath, `${fileName}.xlsx`);

                    if (!fs.existsSync(filePath)) {
                        this.log(`파일을 찾을 수 없습니다: ${fileName}.xlsx`, 'warning');
                        continue;
                    }

                    this.log(`처리 중: ${fileName}.xlsx (경로: ${filePath})`, 'info');

                    try {
                        const combineResult = this.processExcelFileForCombine(filePath, fileName);
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
     * LoadDataFile.xlsx에서 파일 정보 추출
     */
    extractLoadDataInfo(workbook) {
        const loadDataInfo = {};

        for (const sheetName of workbook.SheetNames) {
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

            const worksheet = workbook.Sheets[sheetName];
            const data = this.processWorksheet(worksheet, sheetName);

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
     * 합치기를 위한 엑셀 파일 처리 (모든 시트 처리 후 병합)
     */
    processExcelFileForCombine(filePath, fileName) {
        try {
            const workbook = XLSX.readFile(filePath, {
                cellStyles: false,
                cellFormulas: false,
                cellDates: true,
                cellNF: false,
                sheetStubs: false
            });

            let combinedData = [];
            let expectedColumns = null;
            let firstSchema = null;

            // 모든 시트를 처리하고 병합
            for (const sheetName of workbook.SheetNames) {
                // 제외 패턴에 해당하는 시트는 건너뛰기
                if (this.isSheetExcluded(sheetName)) {
                    this.log(`시트 제외됨 (패턴): ${sheetName}`, 'debug');
                    continue;
                }

                const worksheet = workbook.Sheets[sheetName];
                const sheetData = this.processWorksheet(worksheet, sheetName);

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

        for (const sheetName of workbook.SheetNames) {
            // 임시 시트만 제외 ('_' 접두사 시트 제외 규칙은 적용하지 않음)
            if (sheetName.includes('~')) {
                continue;
            }

            const rows = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], { header: 1, defval: null });
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
        this.log(`JSON 생성 완료: ${outputFileName} (enum 멤버 ${entries.length}개, CS 생성 안 함)`, 'success');

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

                if (this.csOutputPath && this.schemas[key]) {
                    if (generateDataClass(key, this.schemas[key], this.csOutputPath)) {
                        processedSchemas[key] = this.schemas[key];
                        this.log(`CS 생성 완료: ${key}.cs`, 'success');
                    }
                }

            } catch (error) {
                this.log(`JSON 생성 실패 (${outputFileName}): ${error.message}`, 'error');
            }
        }

        if (this.csOutputPath && generateContainersFile(this.csOutputPath, processedSchemas)) {
            this.log(`CS 생성 완료: Containers.Generated.cs (${Object.keys(processedSchemas).length}개 테이블 갱신)`, 'success');
        }
    }

    /**
     * GameEnum.cs 갱신 (_Enum 엑셀 → Game.GameEnum 중첩 enum)
     * export 마지막에 항상 호출한다. 내용이 같으면 파일을 건드리지 않는다.
     */
    generateGameEnum() {
        if (!this.gameEnumOutputPath) {
            return;
        }
        if (this.enumMemberOrder.size === 0) {
            // _Enum 엑셀이 없는 프로젝트다. 기존 파일이 있어도 지우지 않는다.
            return;
        }

        const result = generateGameEnumFile(this.enumMemberOrder, this.gameEnumOutputPath);
        if (!result) {
            return;
        }

        if (result.written) {
            this.log(`CS 생성 완료: GameEnum.cs (enum ${result.count}개, 멤버 ${result.memberCount}개)`, 'success');
        } else {
            this.log(`GameEnum.cs 변경 없음 (enum ${result.count}개)`, 'debug');
        }
    }

    /**
     * GameRoot.Generated.cs 갱신 (콘크리트 컨테이너 스캔 → GameRoot 프로퍼티)
     * export 마지막에 항상 호출한다. 내용이 같으면 파일을 건드리지 않는다.
     */
    generateGameRoot() {
        if (!this.containersPath || !this.gameRootOutputPath) {
            return;
        }

        const result = generateGameRootFile(this.containersPath, this.gameRootOutputPath);
        if (!result) {
            return;
        }

        if (result.written) {
            this.log(`CS 생성 완료: GameRoot.Generated.cs (컨테이너 ${result.count}개)`, 'success');
        } else {
            this.log(`GameRoot.Generated.cs 변경 없음 (컨테이너 ${result.count}개)`, 'debug');
        }
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
        const loadDataFilePath = path.join(this.gamedataPath, this.loadDataFileName);
        if (fs.existsSync(loadDataFilePath)) {
            this.log(`${this.loadDataFileName} 처리 시작`, 'info');
            const result = this.processExcelFile(this.loadDataFileName);
            results.push(result);

            if (result.success) successCount++;
            else errorCount++;
        }

        // 2. 나머지 개별 파일들 처리 (LoadDataFile에 정의된 파일들 제외)
        const excelFiles = this.getExcelFiles();

        if (excelFiles.length === 0 && !fs.existsSync(loadDataFilePath)) {
            this.log('처리할 엑셀 파일이 없습니다.', 'warning');
            return { success: true, results: [] };
        }

        this.log(`총 ${excelFiles.length}개 개별 파일 처리 시작`, 'info');

        let fileIndex = 0;
        for (const filename of excelFiles) {
            fileIndex++;
            console.log('');
            this.log(`[${fileIndex}/${excelFiles.length}] ${filename}`, 'info');
            const result = this.processExcelFile(filename);
            results.push(result);

            if (result.success) {
                successCount++;
                this.log(`[${fileIndex}/${excelFiles.length}] ${filename} 완료 - 시트 ${result.sheets.length}개 / ${result.totalRecords}개 레코드`, 'success');
            } else {
                errorCount++;
                this.log(`[${fileIndex}/${excelFiles.length}] ${filename} 실패 - ${result.error}`, 'error');
            }
        }

        this.generateGameEnum();
        this.generateGameRoot();

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
            if (!filename.endsWith('.xlsx')) {
                this.log(`엑셀 파일이 아닙니다: ${filename}`, 'warning');
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

            const result = this.processExcelFile(filename);
            results.push(result);

            if (result.success) {
                successCount++;
                this.log(`[${fileIndex}/${filenames.length}] ${filename} 완료 - 시트 ${result.sheets.length}개 / ${result.totalRecords}개 레코드`, 'success');
            } else {
                errorCount++;
                this.log(`[${fileIndex}/${filenames.length}] ${filename} 실패 - ${result.error}`, 'error');
            }
        }

        this.generateGameEnum();
        this.generateGameRoot();

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
        const loadDataFileName = this.loadDataFileName;
        const loadDataFilePath = path.join(this.gamedataPath, loadDataFileName);
        if (fs.existsSync(loadDataFilePath)) {
            const excelStat = fs.statSync(loadDataFilePath);
            const shouldProcess = this.shouldProcessLoadDataFile(excelStat.mtime, loadDataFilePath);
            if (shouldProcess) {
                modifiedFiles.push(loadDataFileName);
            }
        }

        // 개별 엑셀 파일들 (LoadDataFile에 정의된 파일들 제외)
        const excelFiles = this.getExcelFiles();

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
            this.generateGameEnum();
        this.generateGameRoot();
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
                const enumJsonPath = path.join(this.jsonOutputPath, `${path.basename(filename, '.xlsx')}.json`);
                if (!fs.existsSync(enumJsonPath)) {
                    return true;
                }
                return excelMtime > fs.statSync(enumJsonPath).mtime;
            }

            const workbook = XLSX.readFile(excelPath, {
                cellStyles: false,
                cellFormulas: false,
                cellDates: true,
                cellNF: false,
                sheetStubs: false
            });

            const candidateSheets = workbook.SheetNames
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
            const workbook = XLSX.readFile(loadDataFilePath, {
                cellStyles: false,
                cellFormulas: false,
                cellDates: true,
                cellNF: false,
                sheetStubs: false
            });

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
        gamedataPath: process.env.GAMEDATA_PATH || path.join(process.cwd(), 'gamedata'),
        jsonOutputPath: process.env.JSON_OUTPUT_PATH || path.join(process.cwd(), 'jsondata'),
        configPath: process.env.CONFIG_PATH || path.join(process.cwd(), 'config.json'),
        csOutputPath: process.env.CS_OUTPUT_PATH || null,
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
Hugh Excel to JSON Exporter

사용법:
  node smart_exporter.js [command] [files...] [options]

명령어:
  all        - 모든 엑셀 파일 처리 (기본값)
  modified   - 수정된 파일만 처리
  file       - 특정 파일들만 처리
  help       - 도움말 표시

옵션:
  --verbose, -v  - 상세 로그 출력

기본 경로:
  입력:  ./gamedata
  출력:  ./jsondata

환경변수:
  GAMEDATA_PATH     - 입력 폴더 경로
  JSON_OUTPUT_PATH  - 출력 폴더 경로
  CONFIG_PATH       - 설정 파일 경로
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

