# Antigravity Agent 코딩 최적화 바이블
## AI 에이전트를 위한 코드 생성 가이드

**버전**: 1.0  
**대상**: Antigravity AI 에이전트  
**목적**: 고품질 코드 자동 생성 시 따를 가이드라인  

---

## 목차

1. [핵심 원칙](#핵심-원칙)
2. [코드 구조 설계](#코드-구조-설계)
3. [에러 처리](#에러-처리)
4. [테스트 작성](#테스트-작성)
5. [성능 최적화](#성능-최적화)
6. [보안 최적화](#보안-최적화)
7. [문서화](#문서화)
8. [프레임워크별 가이드](#프레임워크별-가이드)
9. [체크리스트](#체크리스트)

---

## 핵심 원칙

### 원칙 1: 명확함 > 정교함

**생성해야 할 코드**:
- 읽기 쉬운 코드
- 명확한 변수명
- 한 가지 책임만 하는 함수
- 단계별 설명이 가능한 로직

**피해야 할 코드**:
- 한 줄짜리 복잡한 삼항연산자
- 의도가 불명확한 약자 (e.g., `usr`, `chk`, `val`)
- 중첩된 콜백 지옥
- 너무 긴 함수 (100줄 이상)

### 원칙 2: 견고함 (Robustness)

**모든 코드는 다음을 포함해야 함**:
1. 입력 검증
2. 에러 처리
3. 로깅
4. 단위 테스트

**예시**:
```javascript
// ❌ 위험: 검증 없음
function getUserById(id) {
  return db.query(`SELECT * FROM users WHERE id = ${id}`);
}

// ✅ 안전: 검증 + 에러 처리 + 로깅
function getUserById(id) {
  // 입력 검증
  if (!id || typeof id !== 'number' || id <= 0) {
    throw new ValidationError('Invalid user ID: must be positive number');
  }
  
  try {
    logger.debug(`Fetching user with ID: ${id}`);
    const user = db.query('SELECT * FROM users WHERE id = ?', [id]);
    
    if (!user) {
      logger.warn(`User not found: ${id}`);
      throw new NotFoundError(`User with ID ${id} not found`);
    }
    
    return user;
  } catch (error) {
    logger.error(`Error fetching user ${id}:`, error);
    throw error;
  }
}
```

### 원칙 3: 일관성

**같은 프로젝트 내에서**:
- 동일한 네이밍 규칙 유지
- 동일한 에러 처리 패턴
- 동일한 로깅 레벨 사용
- 동일한 폴더 구조

### 원칙 4: 유지보수성

**미래의 개발자를 생각하며**:
- 복잡한 로직에는 주석 작성
- 상수는 매직 넘버 대신 명명된 상수 사용
- 설정값은 환경 변수로 관리
- 변경 이유를 기록

### 원칙 5: 테스트 가능성

**코드를 작성할 때 테스트를 염두에**:
- 의존성은 주입 가능하게 (dependency injection)
- 함수는 순수함수로 (side effects 최소화)
- 외부 서비스는 모킹 가능하게
- 경계값(edge cases) 테스트 가능하게

---

## 코드 구조 설계

### 함수 설계 원칙

**좋은 함수의 특징**:

```javascript
/**
 * 사용자 인증을 수행합니다.
 * 
 * @param {string} email - 사용자 이메일
 * @param {string} password - 사용자 비밀번호
 * @returns {Promise<{token: string, user: Object}>} 인증 결과
 * @throws {ValidationError} 입력값 검증 실패
 * @throws {AuthenticationError} 인증 실패
 * @example
 * const {token, user} = await authenticateUser('user@example.com', 'password123');
 */
async function authenticateUser(email, password) {
  // 1. 입력 검증
  if (!email || !isValidEmail(email)) {
    throw new ValidationError('Invalid email format');
  }
  if (!password || password.length < 8) {
    throw new ValidationError('Password must be at least 8 characters');
  }

  // 2. 데이터 조회
  const user = await User.findByEmail(email);
  if (!user) {
    throw new AuthenticationError('Invalid email or password');
  }

  // 3. 비밀번호 검증
  const isPasswordValid = await bcrypt.compare(password, user.hashedPassword);
  if (!isPasswordValid) {
    throw new AuthenticationError('Invalid email or password');
  }

  // 4. 토큰 생성
  const token = jwt.sign(
    { userId: user.id, email: user.email },
    process.env.JWT_SECRET,
    { expiresIn: '24h' }
  );

  // 5. 결과 반환
  return {
    token,
    user: {
      id: user.id,
      email: user.email,
      name: user.name
    }
  };
}
```

**필수 요소**:
- ✅ JSDoc 주석 (목적, 파라미터, 반환값, 에러, 예시)
- ✅ 명확한 단계별 주석
- ✅ 입력 검증
- ✅ 에러 처리
- ✅ 최대 50-80줄 (길면 분해)

### 클래스/모듈 설계

**단일 책임 원칙**:

```javascript
// ✅ 좋은 구조: 관심사 분리
class UserService {
  constructor(userRepository, emailService, logger) {
    this.userRepository = userRepository;
    this.emailService = emailService;
    this.logger = logger;
  }

  async createUser(userData) {
    // 사용자 생성 로직만 담당
  }
}

class UserRepository {
  async findById(id) {
    // 데이터베이스 접근만 담당
  }
}

class EmailService {
  async sendVerificationEmail(email) {
    // 이메일 발송만 담당
  }
}

// ❌ 나쁜 구조: 모든 로직이 한 곳에
class User {
  async createAndSendEmail(data) {
    // 사용자 생성 + 이메일 발송 + DB 접근 + 검증 모두 함
  }
}
```

### 폴더 구조

**각 에이전트가 생성할 폴더 구조**:

```
src/
├── models/
│   ├── User.js
│   ├── Product.js
│   └── Order.js
├── services/
│   ├── UserService.js
│   ├── ProductService.js
│   └── OrderService.js
├── repositories/
│   ├── UserRepository.js
│   ├── ProductRepository.js
│   └── OrderRepository.js
├── routes/
│   ├── users.js
│   ├── products.js
│   └── orders.js
├── middlewares/
│   ├── authMiddleware.js
│   ├── errorHandler.js
│   └── requestLogger.js
├── utils/
│   ├── validators.js
│   ├── errorTypes.js
│   └── constants.js
├── tests/
│   ├── unit/
│   ├── integration/
│   └── fixtures/
├── config/
│   ├── database.js
│   └── logger.js
└── index.js
```

---

## 에러 처리

### 에러 처리 패턴

**생성할 모든 코드에 포함**:

```javascript
// 1. 커스텀 에러 클래스 정의 (한 번만)
class ValidationError extends Error {
  constructor(message) {
    super(message);
    this.name = 'ValidationError';
    this.statusCode = 400;
  }
}

class AuthenticationError extends Error {
  constructor(message) {
    super(message);
    this.name = 'AuthenticationError';
    this.statusCode = 401;
  }
}

class NotFoundError extends Error {
  constructor(message) {
    super(message);
    this.name = 'NotFoundError';
    this.statusCode = 404;
  }
}

// 2. 함수에서 사용
async function updateUser(id, data) {
  // 입력 검증
  if (!id || !Number.isInteger(id)) {
    throw new ValidationError('Invalid user ID');
  }

  try {
    const user = await User.findById(id);
    if (!user) {
      throw new NotFoundError(`User ${id} not found`);
    }

    // 비즈니스 로직
    const updated = await user.update(data);
    return updated;
  } catch (error) {
    // 알려진 에러는 그대로 던지기
    if (error instanceof ValidationError || 
        error instanceof NotFoundError) {
      throw error;
    }
    
    // 예상하지 못한 에러는 로깅 후 일반 에러로 변환
    logger.error('Unexpected error updating user:', error);
    throw new Error('Internal server error');
  }
}

// 3. Express 미들웨어에서 처리
app.use((err, req, res, next) => {
  const statusCode = err.statusCode || 500;
  const message = err.name === 'ValidationError' 
    ? err.message 
    : 'Internal server error';

  logger.error(`[${err.name}] ${err.message}`, {
    statusCode,
    path: req.path,
    method: req.method
  });

  res.status(statusCode).json({
    error: {
      name: err.name,
      message: message,
      statusCode: statusCode
    }
  });
});
```

### 콘솔/로깅

**절대 금지**:
```javascript
// ❌ 절대 하지 말 것
console.log(sensitiveData);  // 민감 정보
console.log('error:', err);   // 스택 트레이스 노출
console.error(password);      // 비밀번호 로그
```

**필수 사항**:
```javascript
// ✅ 항상 이렇게
const logger = require('./config/logger');

logger.debug('Starting user creation', { email });
logger.info('User created successfully', { userId });
logger.warn('Retry attempt 3 of 5', { service: 'payment' });
logger.error('Database connection failed', { error: err.message });
```

---

## 테스트 작성

### 필수 테스트 패턴

**모든 함수는 다음 테스트를 포함**:

```javascript
const { expect } = require('chai');
const sinon = require('sinon');
const { createUser } = require('../services/UserService');
const { ValidationError } = require('../utils/errorTypes');

describe('UserService.createUser', () => {
  // 1. 정상 케이스
  it('should create user with valid data', async () => {
    const userData = {
      email: 'test@example.com',
      password: 'SecurePass123',
      name: 'Test User'
    };

    const result = await createUser(userData);

    expect(result).to.have.property('id');
    expect(result.email).to.equal('test@example.com');
  });

  // 2. 입력 검증 실패
  it('should throw ValidationError for invalid email', async () => {
    const userData = {
      email: 'invalid-email',
      password: 'SecurePass123'
    };

    try {
      await createUser(userData);
      expect.fail('Should have thrown ValidationError');
    } catch (error) {
      expect(error).to.be.instanceOf(ValidationError);
      expect(error.message).to.include('Invalid email');
    }
  });

  // 3. 중복 확인
  it('should throw error for duplicate email', async () => {
    const userData = {
      email: 'existing@example.com',
      password: 'SecurePass123'
    };

    await createUser(userData);

    try {
      await createUser(userData);
      expect.fail('Should have thrown DuplicateError');
    } catch (error) {
      expect(error.message).to.include('already exists');
    }
  });

  // 4. 외부 의존성 모킹
  it('should call emailService for verification', async () => {
    const emailServiceStub = sinon.stub(emailService, 'send');

    const userData = {
      email: 'test@example.com',
      password: 'SecurePass123'
    };

    await createUser(userData);

    expect(emailServiceStub.called).to.be.true;
    emailServiceStub.restore();
  });

  // 5. 경계값 테스트
  it('should handle minimum password length', async () => {
    const userData = {
      email: 'test@example.com',
      password: '12345678' // 정확히 8자
    };

    const result = await createUser(userData);
    expect(result).to.exist;
  });

  it('should reject password shorter than minimum', async () => {
    const userData = {
      email: 'test@example.com',
      password: '1234567' // 7자 - 부족
    };

    try {
      await createUser(userData);
      expect.fail('Should have thrown ValidationError');
    } catch (error) {
      expect(error.statusCode).to.equal(400);
    }
  });
});
```

### 테스트 커버리지 목표

**생성하는 모든 코드**:
- 정상 경로(Happy Path): 필수 ✅
- 에러 케이스: 필수 ✅
- 경계값: 필수 ✅
- 의존성 모킹: 필수 ✅

**목표**:
- 일반 코드: 80%+ 커버리지
- 중요 비즈니스 로직: 95%+ 커버리지

---

## 성능 최적화

### 데이터베이스 쿼리

**좋은 패턴**:

```javascript
// ✅ 인덱스 사용
const user = await User.findById(id); // id는 인덱스

// ✅ 필요한 필드만 조회
const user = await User.findById(id, { 
  fields: ['id', 'name', 'email'] 
});

// ✅ 배치 조회로 N+1 문제 해결
const users = await User.find({ id: { $in: userIds } });

// ✅ Pagination으로 메모리 절약
const users = await User.find()
  .limit(20)
  .skip((page - 1) * 20)
  .lean(); // 몽고DB 최적화
```

**나쁜 패턴**:

```javascript
// ❌ 모든 필드 조회
const user = await User.findById(id);

// ❌ N+1 문제
for (const userId of userIds) {
  const user = await User.findById(userId); // 매번 쿼리
}

// ❌ 페이징 없이 모두 로드
const users = await User.find();
```

### 메모리 관리

**좋은 패턴**:

```javascript
// ✅ 스트림 처리로 대용량 파일
async function processLargeFile(filePath) {
  const stream = fs.createReadStream(filePath, {
    encoding: 'utf8',
    highWaterMark: 64 * 1024 // 64KB chunks
  });

  for await (const chunk of stream) {
    processChunk(chunk);
  }
}

// ✅ 캐싱으로 반복 계산 제거
const cache = new Map();
function getOrCompute(key, computeFn) {
  if (!cache.has(key)) {
    cache.set(key, computeFn());
  }
  return cache.get(key);
}

// ✅ 타임아웃으로 무한 대기 방지
const result = await Promise.race([
  operation(),
  new Promise((_, reject) => 
    setTimeout(() => reject(new Error('Timeout')), 5000)
  )
]);
```

**나쁜 패턴**:

```javascript
// ❌ 메모리에 전체 로드
const content = fs.readFileSync(largeFile, 'utf8');

// ❌ 무한 루프/재귀
while (true) { /* ... */ }
async function infinite() { return infinite(); }

// ❌ 캐시 정리 없음
for (let i = 0; i < 1000000; i++) {
  cache.set(`key_${i}`, expensiveComputation());
}
```

---

## 보안 최적화

### 입력 검증

**생성할 때마다 포함**:

```javascript
const Joi = require('joi');

// 1. 스키마 정의
const userSchema = Joi.object({
  email: Joi.string()
    .email()
    .required()
    .trim()
    .lowercase(),
  password: Joi.string()
    .min(8)
    .max(128)
    .required()
    .pattern(/[A-Z]/, '[a-z]', /[0-9]/, /[\W_]/) // 강력한 비밀번호
    .messages({
      'string.min': 'Password must be at least 8 characters',
      'string.pattern.base': 'Password must include uppercase, lowercase, number, and special character'
    }),
  name: Joi.string()
    .max(100)
    .trim()
    .required()
});

// 2. 검증 실행
async function createUser(data) {
  // 입력 검증
  const { error, value } = userSchema.validate(data, {
    abortEarly: false, // 모든 에러 반환
    stripUnknown: true // 알 수 없는 필드 제거
  });

  if (error) {
    const messages = error.details.map(d => d.message);
    throw new ValidationError(messages.join('; '));
  }

  // 검증된 데이터 사용
  return saveUser(value);
}
```

### SQL Injection 방지

**필수**:

```javascript
// ✅ Prepared Statements 항상 사용
const user = await db.query(
  'SELECT * FROM users WHERE id = ? AND email = ?',
  [id, email]
);

// ❌ 절대 금지: 문자열 연결
const user = await db.query(
  `SELECT * FROM users WHERE id = ${id} AND email = '${email}'`
);
```

### XSS 방지

**필수**:

```javascript
// ✅ 데이터베이스에서 조회한 데이터를 HTML에 렌더링할 때
const sanitizeHtml = require('sanitize-html');
const safeContent = sanitizeHtml(userContent, {
  allowedTags: ['b', 'i', 'a'],
  allowedAttributes: { a: ['href'] }
});

// ✅ 또는 템플릿 엔진 사용 (자동 이스케이프)
res.render('user', { name: unsafeUserName }); // 자동 이스케이프

// ❌ 절대 금지
res.send(`<h1>${userInput}</h1>`); // 위험
```

### 비밀번호 처리

**필수**:

```javascript
const bcrypt = require('bcrypt');

// ✅ 비밀번호 해싱 (저장 시)
async function hashPassword(password) {
  const salt = await bcrypt.genSalt(10);
  return await bcrypt.hash(password, salt);
}

// ✅ 비밀번호 검증 (로그인 시)
async function verifyPassword(password, hash) {
  return await bcrypt.compare(password, hash);
}

// ❌ 절대 금지
database.users.insert({ password: plainText }); // 평문 저장
```

### 민감 정보 로깅

**필수**:

```javascript
// ✅ 민감 정보 마스킹
function maskSensitiveData(obj) {
  const masked = { ...obj };
  const sensitiveFields = [
    'password', 'token', 'apiKey', 'secret',
    'ssn', 'creditCard'
  ];

  for (const field of sensitiveFields) {
    if (field in masked) {
      masked[field] = '***';
    }
  }

  return masked;
}

logger.info('User data:', maskSensitiveData(userData));

// ❌ 절대 금지
logger.info('User authenticated', { user, password }); // 비밀번호 노출
```

---

## 문서화

### JSDoc 표준

**생성하는 모든 함수에 포함**:

```javascript
/**
 * 사용자 인증을 수행합니다.
 * 
 * 이 함수는 제공된 이메일과 비밀번호를 사용하여
 * 데이터베이스에서 사용자를 찾고 검증합니다.
 * 
 * @param {string} email - 사용자 이메일 (필수, 유효한 이메일 형식)
 * @param {string} password - 사용자 비밀번호 (필수, 최소 8자)
 * @returns {Promise<Object>} 인증 성공 시 토큰과 사용자 정보
 * @returns {Promise<Object>} 반환값.token - JWT 토큰
 * @returns {Promise<Object>} 반환값.user - 사용자 객체
 * @returns {Promise<Object>} 반환값.user.id - 사용자 ID
 * @returns {Promise<Object>} 반환값.user.email - 사용자 이메일
 * 
 * @throws {ValidationError} 입력값이 유효하지 않을 때
 * @throws {AuthenticationError} 이메일 또는 비밀번호가 일치하지 않을 때
 * @throws {Error} 예상하지 못한 서버 에러
 * 
 * @example
 * // 기본 사용
 * const {token, user} = await authenticateUser(
 *   'user@example.com',
 *   'SecurePassword123'
 * );
 * console.log(token); // "eyJhbGc..."
 * 
 * @example
 * // 에러 처리
 * try {
 *   await authenticateUser('invalid-email', 'pass');
 * } catch (error) {
 *   if (error instanceof ValidationError) {
 *     console.log('입력값 검증 실패:', error.message);
 *   }
 * }
 */
async function authenticateUser(email, password) {
  // 구현
}
```

### README 구조

**생성하는 모든 모듈**:

```markdown
# Module Name

## 개요
[이 모듈이 무엇인가, 왜 필요한가]

## 설치
\`\`\`bash
npm install [module-name]
\`\`\`

## 기본 사용법
\`\`\`javascript
const { function1 } = require('./Module');
const result = await function1(params);
\`\`\`

## API 문서

### function1(param1, param2)
[설명]

**파라미터**:
- param1 (string): [설명]
- param2 (number): [설명]

**반환값**: Promise<Object>

**에러**:
- ValidationError: [언제 던져지는가]

**예시**:
\`\`\`javascript
\`\`\`

## 테스트
\`\`\`bash
npm test
\`\`\`
```

---

## 프레임워크별 가이드

### Express.js

**항상 포함**:

```javascript
// 1. 미들웨어 순서
app.use(express.json());
app.use(logger);
app.use(authMiddleware);
app.use(routes);
app.use(errorHandler);

// 2. 라우트 구조
router.post('/users', [
  validateInput, // 입력 검증
  authenticate,   // 인증 확인
  authorizeAdmin, // 권한 확인
  async (req, res, next) => {
    try {
      const result = await userService.create(req.body);
      res.status(201).json(result);
    } catch (error) {
      next(error); // 에러 핸들러로
    }
  }
]);

// 3. 에러 핸들러 (마지막)
app.use((err, req, res, next) => {
  logger.error(err);
  res.status(err.statusCode || 500).json({
    error: err.message
  });
});
```

### React

**항상 포함**:

```javascript
// 1. Props 검증
import PropTypes from 'prop-types';

function UserCard({ user, onDelete }) {
  return <div>{user.name}</div>;
}

UserCard.propTypes = {
  user: PropTypes.shape({
    id: PropTypes.number.required,
    name: PropTypes.string.required,
    email: PropTypes.string.required
  }).required,
  onDelete: PropTypes.func.required
};

// 2. 에러 바운더리
class ErrorBoundary extends React.Component {
  componentDidCatch(error, errorInfo) {
    logger.error('React error:', error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return <div>Something went wrong</div>;
    }

    return this.props.children;
  }
}

// 3. 커스텀 훅
function useAsync(asyncFunction, immediate = true) {
  const [state, setState] = React.useState({
    status: immediate ? 'pending' : 'idle',
    data: null,
    error: null
  });

  const execute = React.useCallback(async () => {
    setState({ status: 'pending', data: null, error: null });
    try {
      const response = await asyncFunction();
      setState({ status: 'success', data: response, error: null });
    } catch (error) {
      setState({ status: 'error', data: null, error });
    }
  }, [asyncFunction]);

  React.useEffect(() => {
    if (immediate) {
      execute();
    }
  }, [execute, immediate]);

  return { ...state, execute };
}
```

### MongoDB/Mongoose

**항상 포함**:

```javascript
// 1. 스키마 설계
const userSchema = new mongoose.Schema({
  email: {
    type: String,
    required: true,
    unique: true,
    trim: true,
    lowercase: true,
    match: /^[^\s@]+@[^\s@]+\.[^\s@]+$/ // 이메일 검증
  },
  password: {
    type: String,
    required: true,
    minlength: 8,
    select: false // 쿼리에서 기본 제외
  },
  createdAt: {
    type: Date,
    default: Date.now,
    index: true // 인덱스
  }
}, { 
  timestamps: true // createdAt, updatedAt 자동 관리
});

// 2. 인덱스 생성
userSchema.index({ email: 1 }); // 이메일로 검색 빠르게
userSchema.index({ createdAt: -1 }); // 최신순 정렬

// 3. 훅으로 자동 처리
userSchema.pre('save', async function(next) {
  if (this.isModified('password')) {
    this.password = await bcrypt.hash(this.password, 10);
  }
  next();
});

// 4. 메서드 추가
userSchema.methods.toJSON = function() {
  const { __v, password, ...rest } = this.toObject();
  return rest; // 민감 정보 제외
};

const User = mongoose.model('User', userSchema);
```

---

## 체크리스트

### 코드 생성 전 체크리스트

```
[ ] 요청을 명확히 이해했나?
[ ] 관련 기존 코드를 참고했나?
[ ] 에러 케이스를 생각했나?
[ ] 필요한 테스트를 계획했나?
[ ] 보안 문제가 없나?
```

### 코드 생성 후 체크리스트

```
[ ] 모든 함수에 JSDoc 주석이 있나?
[ ] 입력 검증이 있나?
[ ] 에러 처리가 있나?
[ ] 로깅이 있나?
[ ] 테스트가 작성되었나?
[ ] 코드 스타일이 일관적인가?
[ ] 성능 문제가 없나?
[ ] 보안 문제가 없나?
[ ] 의존성이 명확한가?
[ ] README/문서가 작성되었나?
```

### 배포 전 체크리스트

```
[ ] 모든 테스트 통과 (npm test)
[ ] 코드 리뷰 통과
[ ] npm audit 통과 (보안 취약점 0)
[ ] 성능 벤치마크 실행
[ ] 환경 변수 설정 확인
[ ] 로그 레벨 프로덕션으로 조정
[ ] 에러 메시지 일반화 (민감 정보 제외)
[ ] 롤백 계획 수립
```

---

## 성과 지표

### 코드 품질 메트릭

**생성하는 코드가 달성해야 할 목표**:

| 메트릭 | 목표 | 달성 방법 |
|--------|------|----------|
| **테스트 커버리지** | 80%+ | 모든 함수에 테스트 작성 |
| **보안 취약점** | 0 | 입력 검증, SQL 주입 방지, 암호화 |
| **에러 처리** | 100% | try-catch, 커스텀 에러 클래스 |
| **문서화** | 100% | JSDoc 주석, README |
| **성능** | <200ms (API) | DB 쿼리 최적화, 캐싱 |

### 런타임 메트릭

**배포 후 모니터링**:

```
- 에러율 < 0.1%
- 응답시간 < 200ms (p95)
- 메모리 누수 없음
- CPU 사용률 < 70%
```

---

## 최종 규칙

### THE 5 COMMANDMENTS

```
1. 명확함
   "정교한 코드" 보다 "읽기 쉬운 코드"

2. 견고함
   "먼저 동작하는 코드" 보다 "에러에 강한 코드"

3. 검증
   "사용자를 믿기" 보다 "모든 입력을 검증"

4. 테스트
   "수동 테스트" 보다 "자동 테스트"

5. 문서화
   "코드 자체가 문서" 보다 "명시적 주석 + JSDoc"
```

### 각 코드 라인을 작성할 때마다 자문

```
1. "이 코드가 실패하면 무슨 일이?"
   → 에러 처리 추가

2. "3개월 후 이 코드를 읽을 개발자가 이해할 수 있나?"
   → 명확한 변수명, 주석 추가

3. "악의적 사용자가 이 입력을 조작하면?"
   → 검증 추가

4. "이 함수가 얼마나 오래 걸릴까?"
   → 성능 고려, 캐싱 검토

5. "이 코드를 테스트할 수 있나?"
   → 테스트 가능하도록 리팩토링
```

---

**기억하세요**: 

**당신(Antigravity)이 생성하는 코드는 인간 개발자가 읽고 유지보수할 것입니다.**

**빠른 코드가 아니라 견고하고 명확한 코드를 생성하세요.**

---

**버전 히스토리**:
- v1.0 (2026-02-01): Antigravity 에이전트 최적화 바이블 초판

**다음 버전**:
- v1.1: 더 많은 예시 추가
- v1.2: 프레임워크별 심화 가이드

