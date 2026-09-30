namespace NUTrade1.Core;

/// <summary>
/// Distinguishes a message typed by a participant from a system line the app or a
/// Function inserts (e.g. "Trade marked completed").
/// </summary>
public enum ChatMessageType
{
    Text = 0,
    System,
}
