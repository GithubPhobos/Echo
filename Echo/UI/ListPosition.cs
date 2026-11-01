namespace Echo.UI;

/// <summary>
///     Where a list is: the highlighted row and the first row in sight. The caller keeps it, so a list
///     shown again after one of its rows has changed opens exactly where it was left.
/// </summary>
/// <param name="current">The row highlighted at first.</param>
internal sealed class ListPosition(int current = 0)
{
    public int Current { get; set; } = current;

    public int First { get; set; }
}