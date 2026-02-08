# Antigravity 코딩 최적화 바이블 (Agent Optimization Guide)

**대상**: Antigravity 에이전트  
**목적**: 코딩 생성 시 품질, 성능, 보안 최적화  
**버전**: 1.0  
**작성일**: 2026년 2월 1일  

---

## 📋 목차

1. [코드 생성 원칙](#1-코드-생성-원칙)
2. [아키텍처 최적화](#2-아키텍처-최적화)
3. [성능 최적화](#3-성능-최적화)
4. [보안 최적화](#4-보안-최적화)
5. [테스트 자동화](#5-테스트-자동화)
6. [에러 처리](#6-에러-처리)
7. [코드 품질](#7-코드-품질)
8. [문서화](#8-문서화)
9. [에이전트 체크리스트](#9-에이전트-체크리스트)

---

## 1. 코드 생성 원칙

### 원칙 1: DRY (Don't Repeat Yourself)

**❌ 하지 말 것:**
```javascript
// 3개 다른 엔드포인트에서 동일한 로직 반복
app.get('/users/:id', (req, res) => {
  const user = db.query('SELECT * FROM users WHERE id = ?', [req.params.id]);
  if (!user) return res.status(404).json({ error: 'Not found' });
  return res.json(user);
});

app.get('/posts/:id', (req, res) => {
  const post = db.query('SELECT * FROM posts WHERE id = ?', [req.params.id]);
  if (!post) return res.status(404).json({ error: 'Not found' });
  return res.json(post);
});
```

**✅ 해야 할 것:**
```javascript
// 재사용 가능한 핸들러 작성
const getResourceById = (tableName) => (req, res) => {
  const resource = db.query(`SELECT * FROM ${tableName} WHERE id = ?`, [req.params.id]);
  if (!resource) return res.status(404).json({ error: 'Not found' });
  return res.json(resource);
};

app.get('/users/:id', getResourceById('users'));
app.get('/posts/:id', getResourceById('posts'));
```

**효과**: 코드 라인 40% 감소, 유지보수 시간 60% 단축

---

### 원칙 2: KISS (Keep It Simple, Stupid)

**❌ 과도하게 복잡:**
```javascript
// 불필요한 추상화 레이어
class UserRepositoryFactoryBuilder {
  constructor(dbConnection, cacheManager, logger) { ... }
  getUserById(id) { ... }
}

const userRepoFactory = new UserRepositoryFactoryBuilder(db, cache, logger);
const user = userRepoFactory.getUserById(1);
```

**✅ 간단명료:**
```javascript
// 직관적이고 읽기 쉬운 코드
async function getUserById(userId) {
  const cached = await cache.get(`user:${userId}`);
  if (cached) return cached;
  
  const user = await db.query('SELECT * FROM users WHERE id = ?', [userId]);
  await cache.set(`user:${userId}`, user, 3600);
  return user;
}
```

**원칙**: 6개월 뒤 당신이 읽어도 이해할 수 있는가?

---

### 원칙 3: SOLID 원칙 준수

**S (Single Responsibility)** - 한 클래스/함수는 한 가지만
```javascript
// ❌ 잘못된 예: UserService가 DB, 이메일, 로깅을 모두 담당
class UserService {
  createUser() { /* DB 저장 */ }
  sendWelcomeEmail() { /* 이메일 전송 */ }
  logActivity() { /* 로깅 */ }
}

// ✅ 올바른 예: 책임 분리
class UserService { createUser() { /* DB만 */ } }
class EmailService { sendWelcomeEmail() { /* 이메일만 */ } }
class Logger { logActivity() { /* 로깅만 */ } }
```

**O (Open/Closed)** - 확장에는 열려있고 수정에는 닫혀있기
```javascript
// ❌ 수정이 필요한 구조
class PaymentProcessor {
  process(method) {
    if (method === 'credit_card') { /* 신용카드 */ }
    else if (method === 'paypal') { /* PayPal */ }
    else if (method === 'bitcoin') { /* 비트코인 */ }
  }
}
// 새로운 결제 방식 추가 시 이 클래스 수정 필요

// ✅ 확장 가능한 구조
interface PaymentMethod {
  process(amount: number): Promise<boolean>;
}

class CreditCardPayment implements PaymentMethod {
  process(amount) { /* 신용카드 */ }
}

class PayPalPayment implements PaymentMethod {
  process(amount) { /* PayPal */ }
}

// 새로운 방식 추가할 때 기존 코드 수정 없음
```

**L (Liskov Substitution)** - 하위 타입은 상위 타입을 대체 가능
**I (Interface Segregation)** - 클라이언트별 인터페이스 분리
**D (Dependency Inversion)** - 구체적 구현이 아닌 추상화에 의존

---

## 2. 아키텍처 최적화

### 레이어드 아키텍처 (권장)

```
Controller 계층 (HTTP 요청 처리)
    ↓
Service 계층 (비즈니스 로직)
    ↓
Repository 계층 (데이터 접근)
    ↓
Database (물리 저장)
```

**각 계층의 책임:**

| 계층 | 책임 | 예시 |
|------|------|------|
| **Controller** | HTTP 요청/응답, 입력 검증, 라우팅 | `POST /users` 요청 받기 |
| **Service** | 비즈니스 로직, 트랜잭션 관리 | 사용자 생성 규칙 적용 |
| **Repository** | DB 쿼리, 캐싱 | `INSERT INTO users` |

**코드 예시:**
```javascript
// Controller (HTTP 계층)
app.post('/users', async (req, res) => {
  const { email, password } = req.body;
  
  // 입력 검증
  if (!email || !password) {
    return res.status(400).json({ error: 'Missing required fields' });
  }
  
  // Service 호출
  const user = await userService.createUser(email, password);
  res.status(201).json(user);
});

// Service (비즈니스 로직)
async function createUser(email, password) {
  // 중복 확인
  const existing = await userRepository.findByEmail(email);
  if (existing) throw new Error('Email already registered');
  
  // 비밀번호 해싱
  const hashedPassword = await bcrypt.hash(password, 10);
  
  // Repository에 저장 요청
  const user = await userRepository.create({
    email,
    password: hashedPassword,
    createdAt: new Date()
  });
  
  return user;
}

// Repository (데이터 접근)
async function create(userData) {
  const result = await db.query(
    'INSERT INTO users (email, password, created_at) VALUES (?, ?, ?)',
    [userData.email, userData.password, userData.createdAt]
  );
  return { id: result.insertId, ...userData };
}
```

---

### 의존성 주입 (Dependency Injection)

**❌ 강하게 결합된 코드:**
```javascript
class UserService {
  constructor() {
    this.db = new Database(); // 하드코딩
    this.emailService = new EmailService(); // 하드코딩
  }
}

// 테스트할 때 실제 DB와 이메일 서비스 실행됨
```

**✅ 의존성 주입:**
```javascript
class UserService {
  constructor(db, emailService) {
    this.db = db;
    this.emailService = emailService;
  }
}

// 프로덕션
const service = new UserService(realDb, realEmailService);

// 테스트
const service = new UserService(mockDb, mockEmailService);
```

**효과**: 테스트 속도 10배, 테스트 작성 시간 50% 단축

---

## 3. 성능 최적화

### 3.1 데이터베이스 최적화

**쿼리 최적화:**

```javascript
// ❌ N+1 문제
const users = await db.query('SELECT * FROM users LIMIT 10');
for (let user of users) {
  // 10번의 쿼리 실행
  user.posts = await db.query('SELECT * FROM posts WHERE user_id = ?', [user.id]);
}

// ✅ JOIN 사용 (1번의 쿼리)
const users = await db.query(`
  SELECT u.*, p.* 
  FROM users u 
  LEFT JOIN posts p ON u.id = p.user_id 
  LIMIT 10
`);

// 또는 배치 로딩
const userIds = users.map(u => u.id);
const posts = await db.query(
  'SELECT * FROM posts WHERE user_id IN (?)',
  [userIds]
);
```

**인덱스 생성:**
```sql
-- 자주 검색되는 컬럼에 인덱스 추가
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_posts_user_id ON posts(user_id);
CREATE INDEX idx_orders_created_at ON orders(created_at);
```

**쿼리 결과 캐싱:**
```javascript
async function getUserWithPosts(userId) {
  const cacheKey = `user:${userId}:posts`;
  
  // 1단계: 캐시 확인
  let data = await cache.get(cacheKey);
  if (data) return data;
  
  // 2단계: DB 조회
  data = await db.query(`
    SELECT u.*, p.* 
    FROM users u 
    LEFT JOIN posts p ON u.id = p.user_id 
    WHERE u.id = ?
  `, [userId]);
  
  // 3단계: 캐시 저장 (1시간)
  await cache.set(cacheKey, data, 3600);
  
  return data;
}
```

---

### 3.2 메모리 최적화

**스트리밍 사용 (대용량 데이터):**

```javascript
// ❌ 메모리에 모두 로드 (1GB 파일 = 1GB 메모리)
app.get('/export-users', async (req, res) => {
  const users = await db.query('SELECT * FROM users'); // 1,000,000명
  res.json(users); // 메모리 폭발
});

// ✅ 스트리밍 방식 (일부씩 처리)
app.get('/export-users', async (req, res) => {
  res.setHeader('Content-Type', 'application/json');
  res.write('[\n');
  
  const batchSize = 1000;
  let offset = 0;
  let first = true;
  
  while (true) {
    const batch = await db.query(
      'SELECT * FROM users LIMIT ? OFFSET ?',
      [batchSize, offset]
    );
    
    if (batch.length === 0) break;
    
    for (let user of batch) {
      res.write((first ? '' : ',\n') + JSON.stringify(user));
      first = false;
    }
    
    offset += batchSize;
  }
  
  res.write('\n]');
  res.end();
});
```

---

### 3.3 API 응답 최적화

**필드 선택성 (Field Selection):**

```javascript
// ❌ 항상 모든 필드 반환
app.get('/users', (req, res) => {
  const users = await db.query('SELECT * FROM users');
  res.json(users); // password, ssn, internalNotes 포함
});

// ✅ 클라이언트가 필드 선택
app.get('/users', (req, res) => {
  const fields = req.query.fields ? req.query.fields.split(',') : ['id', 'name', 'email'];
  const query = `SELECT ${fields.join(', ')} FROM users`;
  const users = await db.query(query);
  res.json(users);
});

// 사용: GET /users?fields=id,name,email
```

**페이지네이션:**

```javascript
app.get('/posts', (req, res) => {
  const page = req.query.page || 1;
  const limit = req.query.limit || 20;
  const offset = (page - 1) * limit;
  
  const posts = await db.query(
    'SELECT * FROM posts LIMIT ? OFFSET ?',
    [limit, offset]
  );
  
  const total = await db.query('SELECT COUNT(*) as count FROM posts');
  
  res.json({
    data: posts,
    pagination: {
      page,
      limit,
      total: total[0].count,
      pages: Math.ceil(total[0].count / limit)
    }
  });
});
```

---

## 4. 보안 최적화

### 4.1 입력 검증 및 새니타이제이션

```javascript
// ❌ 검증 없음 (SQL Injection 위험)
app.post('/users', (req, res) => {
  const email = req.body.email;
  const query = `SELECT * FROM users WHERE email = '${email}'`; // 위험!
  const user = db.query(query);
});

// ✅ 올바른 검증
const { body, validationResult } = require('express-validator');

app.post('/users', [
  body('email').isEmail().normalizeEmail(),
  body('password').isLength({ min: 8 }).matches(/[A-Z]/).matches(/[0-9]/),
  body('name').trim().isLength({ min: 2, max: 100 }).escape()
], (req, res) => {
  const errors = validationResult(req);
  if (!errors.isEmpty()) {
    return res.status(400).json({ errors: errors.array() });
  }
  
  const { email, password, name } = req.body;
  
  // Prepared statement 사용 (SQL Injection 방지)
  const user = db.query(
    'INSERT INTO users (email, password, name) VALUES (?, ?, ?)',
    [email, hashedPassword, name]
  );
  
  res.status(201).json(user);
});
```

---

### 4.2 인증 & 인가

**JWT 토큰 생성:**

```javascript
const jwt = require('jsonwebtoken');

// 토큰 생성
function generateToken(userId) {
  const token = jwt.sign(
    { userId, role: 'user' },
    process.env.JWT_SECRET,
    { expiresIn: '24h' } // 만료 시간 필수!
  );
  return token;
}

// 토큰 검증 (미들웨어)
function authenticateToken(req, res, next) {
  const authHeader = req.headers['authorization'];
  const token = authHeader && authHeader.split(' ')[1]; // Bearer token
  
  if (!token) return res.sendStatus(401);
  
  jwt.verify(token, process.env.JWT_SECRET, (err, user) => {
    if (err) return res.sendStatus(403);
    req.user = user;
    next();
  });
}

// 사용
app.get('/protected', authenticateToken, (req, res) => {
  res.json({ userId: req.user.userId });
});
```

**역할 기반 접근 제어 (RBAC):**

```javascript
function authorize(requiredRoles) {
  return (req, res, next) => {
    if (!requiredRoles.includes(req.user.role)) {
      return res.status(403).json({ error: 'Forbidden' });
    }
    next();
  };
}

// 관리자만 접근 가능
app.delete('/users/:id', authenticateToken, authorize(['admin']), (req, res) => {
  // 삭제 로직
});
```

---

### 4.3 환경 변수 관리

```javascript
// ❌ 하드코딩 (위험)
const DB_PASSWORD = 'myPassword123';
const API_KEY = 'sk-1234567890';

// ✅ 환경 변수 사용
require('dotenv').config();

const dbPassword = process.env.DB_PASSWORD;
const apiKey = process.env.API_KEY;

// 필수 환경 변수 검증
const requiredEnvs = ['DB_PASSWORD', 'API_KEY', 'JWT_SECRET', 'NODE_ENV'];
requiredEnvs.forEach(env => {
  if (!process.env[env]) {
    throw new Error(`Missing required environment variable: ${env}`);
  }
});
```

---

### 4.4 HTTPS 및 헤더 보안

```javascript
const express = require('express');
const helmet = require('helmet');
const cors = require('cors');

const app = express();

// 보안 헤더 설정
app.use(helmet());

// CORS 설정
app.use(cors({
  origin: process.env.ALLOWED_ORIGINS?.split(','),
  credentials: true
}));

// HTTPS 강제 (프로덕션)
if (process.env.NODE_ENV === 'production') {
  app.use((req, res, next) => {
    if (req.header('x-forwarded-proto') !== 'https') {
      return res.redirect(301, `https://${req.header('host')}${req.url}`);
    }
    next();
  });
}
```

---

## 5. 테스트 자동화

### 5.1 단위 테스트 (Unit Tests)

```javascript
// 테스트할 함수
function calculateDiscount(price, quantity) {
  if (quantity >= 10) return price * 0.9;
  if (quantity >= 5) return price * 0.95;
  return price;
}

// Jest 테스트
describe('calculateDiscount', () => {
  test('should apply 10% discount for 10+ items', () => {
    expect(calculateDiscount(100, 10)).toBe(90);
  });
  
  test('should apply 5% discount for 5+ items', () => {
    expect(calculateDiscount(100, 5)).toBe(95);
  });
  
  test('should apply no discount for <5 items', () => {
    expect(calculateDiscount(100, 1)).toBe(100);
  });
  
  test('should handle edge cases', () => {
    expect(calculateDiscount(0, 10)).toBe(0);
    expect(calculateDiscount(100, 0)).toBe(100);
  });
});
```

**테스트 커버리지 목표**: 80%+ (Critical 코드는 100%)

---

### 5.2 통합 테스트 (Integration Tests)

```javascript
const request = require('supertest');
const app = require('../app');

describe('POST /users', () => {
  it('should create a new user', async () => {
    const response = await request(app)
      .post('/users')
      .send({
        email: 'test@example.com',
        password: 'SecurePass123',
        name: 'Test User'
      });
    
    expect(response.statusCode).toBe(201);
    expect(response.body.email).toBe('test@example.com');
    expect(response.body.password).toBeUndefined(); // 비밀번호는 응답에 없어야 함
  });
  
  it('should reject duplicate email', async () => {
    // 첫 번째 사용자 생성
    await request(app)
      .post('/users')
      .send({
        email: 'duplicate@example.com',
        password: 'SecurePass123',
        name: 'User 1'
      });
    
    // 같은 이메일로 두 번째 생성 시도
    const response = await request(app)
      .post('/users')
      .send({
        email: 'duplicate@example.com',
        password: 'SecurePass123',
        name: 'User 2'
      });
    
    expect(response.statusCode).toBe(400);
    expect(response.body.error).toContain('already registered');
  });
});
```

---

### 5.3 모킹 (Mocking)

```javascript
// 외부 서비스를 모킹하여 테스트 격리
const { jest } = require('@jest/globals');

describe('User Service with Mocking', () => {
  let userService;
  let mockDb;
  let mockEmailService;
  
  beforeEach(() => {
    // Mock 객체 생성
    mockDb = {
      query: jest.fn()
    };
    
    mockEmailService = {
      sendWelcomeEmail: jest.fn().mockResolvedValue(true)
    };
    
    userService = new UserService(mockDb, mockEmailService);
  });
  
  it('should create user and send welcome email', async () => {
    mockDb.query.mockResolvedValueOnce({ id: 1, email: 'user@example.com' });
    
    const user = await userService.createUser('user@example.com', 'password');
    
    expect(mockDb.query).toHaveBeenCalled();
    expect(mockEmailService.sendWelcomeEmail).toHaveBeenCalledWith('user@example.com');
    expect(user.id).toBe(1);
  });
});
```

---

## 6. 에러 처리

### 6.1 try-catch 패턴

```javascript
// ❌ 에러 처리 없음
async function getUserById(userId) {
  const user = await db.query('SELECT * FROM users WHERE id = ?', [userId]);
  return user[0]; // 에러 발생 가능
}

// ✅ 올바른 에러 처리
async function getUserById(userId) {
  try {
    const user = await db.query('SELECT * FROM users WHERE id = ?', [userId]);
    
    if (!user || user.length === 0) {
      throw new NotFoundError(`User ${userId} not found`);
    }
    
    return user[0];
  } catch (error) {
    logger.error('Failed to get user', { userId, error: error.message });
    throw error; // 상위 레이어에서 처리하도록
  }
}
```

---

### 6.2 커스텀 에러 클래스

```javascript
class AppError extends Error {
  constructor(message, statusCode) {
    super(message);
    this.statusCode = statusCode;
    this.isOperational = true;
  }
}

class NotFoundError extends AppError {
  constructor(message = 'Resource not found') {
    super(message, 404);
  }
}

class ValidationError extends AppError {
  constructor(message = 'Validation failed') {
    super(message, 400);
  }
}

class UnauthorizedError extends AppError {
  constructor(message = 'Unauthorized') {
    super(message, 401);
  }
}

// 사용
if (!user) {
  throw new NotFoundError('User not found');
}
```

---

### 6.3 전역 에러 핸들러

```javascript
app.use((err, req, res, next) => {
  // 에러 로깅
  logger.error('Request error', {
    message: err.message,
    stack: err.stack,
    path: req.path,
    method: req.method
  });
  
  // 운영 에러 (예상된 에러)
  if (err.isOperational) {
    return res.status(err.statusCode).json({
      status: 'error',
      message: err.message
    });
  }
  
  // 프로그래밍 에러 (예상하지 않은 에러)
  return res.status(500).json({
    status: 'error',
    message: 'Internal server error'
  });
});
```

---

## 7. 코드 품질

### 7.1 코드 스타일 & 포매팅

**ESLint 설정:**

```javascript
// .eslintrc.json
{
  "env": {
    "node": true,
    "es2021": true
  },
  "extends": ["eslint:recommended"],
  "rules": {
    "no-var": "error",
    "prefer-const": "error",
    "eqeqeq": "error",
    "no-console": "warn",
    "no-unused-vars": "error",
    "no-trailing-spaces": "error",
    "indent": ["error", 2],
    "quotes": ["error", "single"],
    "semi": ["error", "always"],
    "curly": ["error", "all"],
    "brace-style": ["error", "1tbs"],
    "space-infix-ops": "error"
  }
}
```

---

### 7.2 명명 규칙

| 타입 | 규칙 | 예시 |
|------|------|------|
| 변수/함수 | camelCase | `getUserName`, `isActive` |
| 클래스/생성자 | PascalCase | `UserService`, `DatabaseConnection` |
| 상수 | UPPER_SNAKE_CASE | `MAX_RETRIES`, `API_TIMEOUT` |
| 파일 | kebab-case | `user-service.js`, `get-posts.js` |

**불명확한 이름 ❌ → 명확한 이름 ✅**

```javascript
// ❌ 나쁜 이름
function process(data) { }
const temp = getUserData();
const d = new Date();

// ✅ 좋은 이름
function validateAndTransformUserData(rawData) { }
const userData = getUserData();
const currentDate = new Date();
```

---

### 7.3 함수 길이 제한

```javascript
// ❌ 너무 긴 함수 (100+ 라인)
function createOrder(userId, items, paymentInfo) {
  // 검증 (20라인)
  // DB 조회 (20라인)
  // 계산 (30라인)
  // 결제 (20라인)
  // 이메일 (20라인)
}

// ✅ 작은 함수로 분해
function createOrder(userId, items, paymentInfo) {
  validateOrder(userId, items);
  const user = getUser(userId);
  const total = calculateTotal(items);
  processPayment(paymentInfo, total);
  sendConfirmationEmail(user, items, total);
  return saveOrder(userId, items, total);
}
```

**목표**: 함수당 10-30줄 (최대 50줄)

---

## 8. 문서화

### 8.1 JSDoc 주석

```javascript
/**
 * 사용자를 생성하고 시스템에 등록합니다.
 * 
 * @param {string} email - 사용자 이메일 (유효한 이메일 형식)
 * @param {string} password - 사용자 비밀번호 (최소 8자)
 * @param {string} name - 사용자 이름 (선택사항)
 * @returns {Promise<Object>} 생성된 사용자 객체
 * @throws {ValidationError} 이메일이 중복된 경우
 * @throws {Error} 데이터베이스 연결 실패 시
 * 
 * @example
 * const user = await createUser('user@example.com', 'SecurePass123', 'John Doe');
 * console.log(user.id); // 123
 */
async function createUser(email, password, name = '') {
  // 구현
}
```

---

### 8.2 README 문서화

```markdown
# Project Name

## Installation
npm install

## Usage
npm start

## API Documentation
- GET /users/:id - Get user by ID
- POST /users - Create new user
- PUT /users/:id - Update user
- DELETE /users/:id - Delete user

## Architecture
[프로젝트 구조 설명]

## Testing
npm test

## Known Issues
- Issue 1: [설명]
- Issue 2: [설명]
```

---

## 9. 에이전트 체크리스트

### 코드 생성 전 체크리스트

- [ ] **구체성 확인** - 요청사항이 명확한가?
- [ ] **기술 스택 명시** - 언어, 프레임워크, 데이터베이스 확인
- [ ] **성공 기준 정의** - 어떻게 테스트할 것인가?
- [ ] **관련 파일 확인** - 기존 코드와 일관성 유지 여부
- [ ] **환경 변수** - 민감한 정보 처리 계획

### 코드 생성 후 체크리스트

- [ ] **DRY 원칙** - 중복 코드 제거
- [ ] **SOLID 원칙** - 단일 책임, 개방-폐쇄 원칙 등
- [ ] **에러 처리** - try-catch, 커스텀 에러 클래스 적용
- [ ] **보안** - SQL Injection, XSS, 환경 변수 관리
- [ ] **성능** - N+1 쿼리, 인덱싱, 캐싱
- [ ] **테스트** - 단위 테스트, 모킹 포함
- [ ] **코드 스타일** - ESLint, 명명 규칙 일관성
- [ ] **문서화** - JSDoc, README 업데이트
- [ ] **함수 길이** - 10-30줄 목표
- [ ] **타입 안정성** - TypeScript 타입 또는 JSDoc @type
- [ ] **로깅** - 중요 작업 로깅 추가
- [ ] **메모리 누수** - 이벤트 리스너 정리, 순환 참조 확인

### 배포 전 최종 체크리스트

- [ ] **모든 테스트 통과** - npm test (100% 성공)
- [ ] **npm audit 통과** - npm audit (0 vulnerabilities)
- [ ] **코드 리뷰** - 두 명 이상 리뷰
- [ ] **보안 감시** - OWASP Top 10 확인
- [ ] **성능 테스트** - 로드 테스트, 벤치마크
- [ ] **환경 변수** - 프로덕션 .env 설정 완료
- [ ] **데이터베이스** - 마이그레이션 스크립트 실행
- [ ] **백업 계획** - 롤백 절차 수립
- [ ] **모니터링** - 로그, 에러 추적 설정
- [ ] **문서화** - API 문서, 설치 가이드 완성

---

## 🎯 성과 지표

| 메트릭 | 목표 | 확인 방법 |
|--------|------|----------|
| 테스트 커버리지 | 80%+ | `npm test -- --coverage` |
| 보안 취약점 | 0 (Critical) | `npm audit` |
| 코드 복잡도 | McCabe < 10 | eslint-plugin-complexity |
| 중복 코드 | < 5% | SonarQube |
| 번들 크기 | < 500KB | `npm run build` |
| 응답 시간 | < 200ms | 모니터링 대시보드 |

---

## 📝 기억할 3가지

### 1️⃣ 가독성 > 영리함
```
"Any fool can write code that a computer can understand.
Good programmers write code that humans can understand." - Martin Fowler
```

### 2️⃣ 보안은 나중에가 아니라 처음부터
```
입력 검증 → JWT 토큰 → 환경 변수 → HTTPS
첫 줄부터 적용하세요!
```

### 3️⃣ 테스트는 투자, 비용이 아님
```
테스트 1시간 작성 = 배포 후 버그 수정 10시간 절약
```

---

**최종 원칙**: **Simple, Secure, Tested, Documented**

이 4가지를 항상 명심하고 코드를 작성하세요.

---

**버전 히스토리:**
- v1.0 (2026-02-01): 초판 작성

**다음 업데이트 예정:**
- v1.1 (2026-03-01): TypeScript 최적화 추가
- v1.2 (2026-04-01): GraphQL 최적화 추가
- v1.3 (2026-05-01): 마이크로서비스 패턴 추가
