namespace Icod.Pty;

/// <summary>Terminal dimensions in character cells, each between 1 and 32767.</summary>
public readonly record struct PtySize {
	/// <summary>Creates validated terminal dimensions.</summary>
	public PtySize(int columns, int rows) {
		if (columns is < 1 or > 32767) throw new ArgumentOutOfRangeException(nameof(columns));
		if (rows is < 1 or > 32767) throw new ArgumentOutOfRangeException(nameof(rows));
		Columns = columns;
		Rows = rows;
	}
	/// <summary>Gets the number of columns.</summary>
	public int Columns { get; }
	/// <summary>Gets the number of rows.</summary>
	public int Rows { get; }
}
