using Microsoft.Data.Sqlite;

var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macro Deck", "profiles.db");
if (!File.Exists(dbPath))
{
	Console.Error.WriteLine($"profiles.db not found at {dbPath}");
	return 1;
}

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

var tables = new List<string>();
using (var cmd = connection.CreateCommand())
{
	cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
	using var reader = cmd.ExecuteReader();
	while (reader.Read())
	{
		tables.Add(reader.GetString(0));
	}
}

Console.WriteLine("Tables:");
foreach (var table in tables)
{
	Console.WriteLine($"- {table}");
}

foreach (var table in tables)
{
	using var pragmaCmd = connection.CreateCommand();
	pragmaCmd.CommandText = $"PRAGMA table_info([{table}])";
	using var reader = pragmaCmd.ExecuteReader();
	var columns = new List<string>();
	while (reader.Read())
	{
		columns.Add(reader.GetString(1));
	}

	Console.WriteLine($"\n[{table}] Columns: {string.Join(", ", columns)}");
}

using (var cmd = connection.CreateCommand())
{
	cmd.CommandText = "SELECT * FROM ProfileJson LIMIT 1";
	using var reader = cmd.ExecuteReader();
	if (reader.Read())
	{
		Console.WriteLine("\nSample ProfileJson row:");
		for (var i = 0; i < reader.FieldCount; i++)
		{
			var name = reader.GetName(i);
			var value = reader.IsDBNull(i) ? "<null>" : reader.GetValue(i).ToString();
			if (value != null && value.Length > 500)
			{
				value = value.Substring(0, 500) + "...";
			}
			Console.WriteLine($"{name}: {value}");
		}
	}
}

return 0;
