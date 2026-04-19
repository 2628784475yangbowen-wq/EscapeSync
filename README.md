# EscapeSync

Multiplayer cooperative escape-room puzzle game for 3 players.
CPS 3330 Spring 2026 final project — Alisha Patel, Bowen Yang Adam, Stone Xu Teng.

## Stack

| Layer | Tech |
|---|---|
| Language | C# (.NET 10) |
| Front-end | Blazor WebAssembly |
| Back-end | ASP.NET Core Web API |
| Real-time | SignalR |
| Database | SQLite (development) / PostgreSQL (production, optional) via Entity Framework Core |
| ORM | EF Core |

## Project layout

- `EscapeSync.Shared` — DTOs, enums, and hub-method/event name constants shared by client and server.
- `EscapeSync.GameLogic` — authoritative `Room` rules, puzzle state machines, and player model (no ASP.NET dependency; unit-tested).
- `EscapeSync.Server` — ASP.NET Core host, SignalR `GameHub`, in-memory `GameManager`, EF Core persistence for finished games.
- `EscapeSync.Client` — Blazor WebAssembly single-page app.
- `EscapeSync.GameLogic.Tests` — xUnit tests for puzzles and room lifecycle.

## Open in VS Code

```
code EscapeSync
```

On first open, VS Code will prompt to install the recommended extensions
(`.vscode/extensions.json`): C# Dev Kit, C# language support, .NET runtime,
and the Blazor WASM companion. Accept them.

- **Build:** `Ctrl+Shift+B` (runs the `build-solution` task)
- **Run/Debug:** open the *Run and Debug* sidebar → choose
  **Run Server + Client** → press ▶. This launches both projects together;
  the Blazor client opens in your browser.

## How to run from the command line

Open two terminals.

```
# Terminal 1 — back-end (http://localhost:5088)
cd EscapeSync.Server
dotnet run

# Terminal 2 — front-end (http://localhost:5132)
cd EscapeSync.Client
dotnet run
```

Open <http://localhost:5132> in **three separate browser windows** (incognito works
well so each window has its own SignalR connection). One player creates a room
and shares the room code; the other two join with that code. When **three** players
are in the lobby, the **host** must click **Start Game**; nothing begins until then.

The SQLite file `escapesync.db` is created next to the server binary on first
launch (EF Core `EnsureCreated`).

## Gameplay

- **Roles** are assigned by join order: Locksmith, Cryptographer, Operator.
- **Puzzle 1 — Combination Lock**
  - Locksmith sees only colored pips and colored buttons.
  - Cryptographer sees the target digit sequence and a color→digit cipher table.
  - Operator sees pips plus SUBMIT / CLEAR. The Cryptographer must describe the
    right sequence of colors to the Locksmith; the Operator submits.
- **Puzzle 2 — Timed Mechanism**
  - Locksmith sees the safe-zone bounds on a 0–100 scale.
  - Cryptographer sees the live needle position.
  - Operator sees only the ACTIVATE button.
- **Puzzle 3 — Final Door**
  - Locksmith sees the left two digits of the four-digit exit code.
  - Cryptographer sees the right two digits.
  - Operator enters all four digits in order on a keypad, then submits.
- **30-minute** countdown timer, 3 lives, 3 hints (hints show partial information to the whole team).
- In-room text chat runs over the same SignalR hub.

## Design patterns exercised

- **Repository pattern** — `IGameRecordRepository` hides EF Core from the game layer.
- **Observer / publish-subscribe** — SignalR `Clients.Group(room).SendAsync(...)` pushes
  authoritative state and per-role views to every observer when the room mutates.
- **MVC-style separation** — `Room` holds model state, `GameHub` is the controller,
  the Blazor pages are the views. `GameManager` is the service layer wrapping both.
- **Domain isolation** — pure game rules live in `EscapeSync.GameLogic` so they can be tested without hosting or SignalR.

## Tests

From the repository root:

```
dotnet test EscapeSync.slnx
```

Tests cover puzzle generators, needle bounds, door-code entry rules, lobby validation, timer loss, wrong guesses, hints, mid-game disconnect, chat trimming, and a full three-puzzle win path.

## Notes

- Live room state is kept in memory (`GameManager` + `Room`). Only finished-game
  records are persisted to SQLite — matching the demo scope for this project.
- A `GameTickerService` background host advances the countdown timer and the
  Puzzle 2 needle for every active room at 5 Hz (Puzzle 3 is turn-based only).

## Push to GitHub

The repo is already initialized with an initial commit on the `main` branch.

1. Create an empty repository on GitHub (no README, no license, no `.gitignore` —
   we already have them) — for example `https://github.com/<you>/EscapeSync`.
2. Add the remote and push:

   ```
   git remote add origin https://github.com/<you>/EscapeSync.git
   git push -u origin main
   ```

If you want to use SSH instead, swap the URL for
`git@github.com:<you>/EscapeSync.git`.
