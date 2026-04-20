# EscapeSync — Contributions: Alisha Patel
**CPS 3330 Spring 2026 Final Project**

---

## Role

> Real-time communication layer · SignalR hub implementation · State management · Database design · Entity Framework Core integration · Testing

---

## Files Owned / Implemented

### 1. SignalR Hub — `EscapeSync.Server/Hubs/GameHub.cs`

The sole entry point between all clients and the server. Every action a player takes in the browser flows through this hub.

- Extends ASP.NET Core `Hub` and maps each client-callable method to the game engine
- Handles `CreateRoom`, `JoinRoom`, `StartGame`, `PressColor`, `ClearGuess`, `SubmitGuess`, `Activate`, `RequestHint`, `SendChat`, `LeaveRoom`
- Overrides `OnDisconnectedAsync` so a player crash/refresh is treated as a graceful leave — preventing the room from hanging
- Intentionally thin — delegates all logic to `GameManager`, keeping transport concerns separate from game rules

---

### 2. State Management — `EscapeSync.Server/Game/`

#### `GameManager.cs` (singleton — owns all live rooms)

- Maintains two `ConcurrentDictionary` maps: room code → `Room`, and connection ID → room code
- Every hub operation calls into the manager, which acquires the room's lock, mutates state, then broadcasts
- `BroadcastAsync` sends the shared `RoomStateDto` to the whole group **and** a per-player `RoleViewDto` to each connection individually (role-based information hiding)
- `PersistIfFinishedAsync` writes the final `GameRecord` to SQLite exactly once when a game ends (won or lost), using a scoped `IServiceScopeFactory` to safely access the DI-scoped `DbContext` from a singleton
- `TickAllAsync` is called by the background service every 200ms; advances countdown and Puzzle 2 needle for all active rooms

#### `Room.cs` (per-session model)

- Authoritative in-memory state: players, stage, lives, hints, timer, chat log, puzzle states
- All mutations guarded by a `SemaphoreSlim` to prevent races between simultaneous hub calls and the background ticker
- Encapsulates all game rules: role assignment by join order, puzzle transitions, life loss, hint text generation, chat trimming
- Exposes `ToDto()` for the shared room snapshot and `ViewForRole()` for the role-specific projections

#### `GameTickerService.cs` (background service)

- Implements `BackgroundService` with a `PeriodicTimer` at 200ms (5 Hz)
- Calls `GameManager.TickAllAsync` each tick, which in turn drives the countdown timer and Puzzle 2 needle position for every active room

#### `Puzzle1State.cs`

- Generates a random color→digit cipher (4 LockColors mapped to unique digits 1–9) and a 4-slot target sequence on each new game
- `Generate(Random?)` accepts a seeded RNG for deterministic testing

#### `Puzzle2State.cs`

- Generates a random safe zone (15-unit window within 10–90) and a bouncing needle
- `Advance(int deltaMs)` moves the needle at 35 units/second, reversing direction at the 0/100 boundaries
- `NeedleInSafeZone()` is the win condition predicate for Puzzle 2

---

### 3. Database Design — `EscapeSync.Server/Data/`

#### `GameRecord.cs` (EF Core entity)

- Designed the schema for persisting finished game sessions
- Fields: `RoomCode`, `PlayerNicknames` (comma-separated, role order), `Won`, `DurationSeconds`, `HintsUsed`, `LivesLost`, `EndedAt`
- Uses Data Annotations (`[Key]`, `[Required]`, `[MaxLength]`) for validation and schema constraints

#### `GameDbContext.cs`

- Minimal `DbContext` exposing a single `DbSet<GameRecord>`
- Configured for SQLite via `DbContextOptions` injected from DI (connection string from `appsettings.json`)
- Database is created on first launch via `EnsureCreated` in `Program.cs`

---

### 4. Repository Pattern — `EscapeSync.Server/Data/IGameRecordRepository.cs`

- Designed `IGameRecordRepository` interface to decouple the game engine from EF Core
- `GameRecordRepository` implements `AddAsync` (save + immediate commit) and `GetRecentAsync` (ordered by `EndedAt` desc, with a take limit)
- Registered as `Scoped` in DI; used by `GameManager` via a transient scope created at save time

---

### 5. Shared Contracts — `EscapeSync.Shared/`

*(Shared by all three team members; Alisha authored the real-time and persistence-related contracts)*

#### `HubEvents.cs`

- `HubEvents` — string constants for server→client push events (`RoomState`, `RoleView`, `JoinResult`, `Kicked`)
- `HubMethods` — string constants for client→server hub calls, preventing magic strings on both ends

#### `Dtos.cs` (persistence and real-time DTOs)

- `RoomStateDto` — full room snapshot broadcast to all players every tick/action
- `RoleViewDto` — per-player projection containing only the information that role is allowed to see
- `PlayerDto`, `ChatMessageDto` — player list and chat entries within `RoomStateDto`
- `JoinResult` — return value from `CreateRoom`/`JoinRoom` carrying the assigned role
- Role-specific view records: `Puzzle1LocksmithView`, `Puzzle1CryptographerView`, `Puzzle1OperatorView`, `Puzzle2LocksmithView`, `Puzzle2CryptographerView`, `Puzzle2OperatorView`

---

### 6. Test Suite — `EscapeSync.Server.Tests/`

Built an xUnit test project with **25 passing tests** covering all layers of the server.

#### `RoomTests.cs` — 13 tests

Core game-model tests:
- Role assignment follows join order (Locksmith → Cryptographer → Operator), first player is host
- Fourth player rejected when room is full
- `Start` transitions to `Puzzle1` and initializes the countdown timer
- Timer reaching zero triggers `Lost` stage
- Correct Puzzle 1 guess transitions to `Puzzle2`
- Wrong Puzzle 1 guess costs a life and clears the guess
- Puzzle 2 activation in the safe zone wins the game
- Puzzle 2 activation outside the safe zone costs a life
- `RequestHint` decrements `HintsRemaining`, increments `HintsUsedTotal`, and sets a hint containing the first target digit
- `RequestHint` with zero hints remaining sets the "No hints remaining" message without incrementing the counter
- Depleting all lives via wrong Puzzle 1 guesses transitions the room to `Lost` with `EndedAt` set
- Chat messages are stored in `ChatLog`; the 51st message evicts the oldest, keeping the log at 50
- A player disconnecting mid-game immediately transitions the room to `Lost`

#### `PuzzleStateTests.cs` — 4 tests

- `Puzzle1State.Generate` cipher covers all 4 `LockColor` values with unique digits in range 1–9
- Puzzle 1 target digits are all resolvable through the cipher
- `Puzzle2State.Generate` safe zone is a 15-unit window within valid bounds
- Needle stays within 0–100 after 1,000 bouncing ticks

#### `GameRecordRepositoryTests.cs` — 3 tests

- Uses in-memory SQLite (real `DbContext`, no mocking) via `SqliteConnection("DataSource=:memory:")`
- `AddAsync` persists a record with auto-increment ID
- `GetRecentAsync` returns newest-first and respects the count limit
- All entity fields round-trip correctly

#### `GameManagerTests.cs` — 3 tests

- Mocked `IHubContext<GameHub>` (Moq) + real SQLite `DbContext` via `IServiceScopeFactory`
- `JoinAsync` on a valid room calls `AddToGroupAsync` and broadcasts `RoomStateDto`
- `LeaveAsync` removes the connection from the group and cleans up empty rooms
- Full win sequence (Puzzle 1 + Puzzle 2) persists a `GameRecord` to SQLite

#### `GameHubTests.cs` — 2 tests

- `CreateRoom` returns a 6-character room code with the `Locksmith` role
- `JoinRoom` with an invalid code returns an error containing "not found"

---

## Design Patterns Applied

| Pattern | Where |
|---|---|
| **Repository** | `IGameRecordRepository` decouples EF Core from game logic |
| **Observer / pub-sub** | SignalR `Clients.Group()` pushes state to all players after every mutation |
| **Singleton + Scoped DI** | `GameManager` is singleton; uses `IServiceScopeFactory` to safely create scoped `DbContext` |
| **Semaphore-based locking** | `SemaphoreSlim` in `Room` prevents concurrent mutations between hub calls and the ticker |
| **Information hiding via DTOs** | `RoleViewDto` ensures each player only receives their role's data |

---

## Technologies Used

- **C# / .NET 10**
- **ASP.NET Core SignalR** — real-time bidirectional communication
- **Entity Framework Core 10** — ORM for database access
- **SQLite** — lightweight embedded relational database
- **xUnit** — test framework
- **Moq** — mock library for isolating SignalR dependencies in tests
- **Microsoft.Data.Sqlite (in-memory)** — fast, isolated DB for repository tests
