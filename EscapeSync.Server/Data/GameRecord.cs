using System.ComponentModel.DataAnnotations;

namespace EscapeSync.Server.Data;

/// <summary>
/// Persistent record of a finished game session.
/// Written once when the room transitions to Won or Lost.
/// </summary>
public class GameRecord
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(16)]
    public string RoomCode { get; set; } = string.Empty;

    /// <summary>Comma-separated nicknames of the three players, in role order.</summary>
    [Required, MaxLength(256)]
    public string PlayerNicknames { get; set; } = string.Empty;

    public bool Won { get; set; }

    /// <summary>Seconds elapsed from game start to end. For losses this equals the full timer.</summary>
    public int DurationSeconds { get; set; }

    public int HintsUsed { get; set; }

    public int LivesLost { get; set; }

    public DateTime EndedAt { get; set; } = DateTime.UtcNow;
}
