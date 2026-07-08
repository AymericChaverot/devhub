using System.Text.Json;
using DevHub.Core.Models;
using Microsoft.Data.Sqlite;

namespace DevHub.Core.Services;

public sealed class SqliteProjectStore : IProjectStore
{
    private readonly string _connectionString;
    private bool _initialized;

    /// <param name="databasePath">Override for tests; defaults to %LOCALAPPDATA%\DevHub\devhub.db.</param>
    public SqliteProjectStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevHub", "devhub.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<Project>>(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT Name, Path, GitRemoteUrl, IsGitRepository, LastScannedAt, Languages, Frameworks FROM Projects ORDER BY Name";

            var projects = new List<Project>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                projects.Add(new Project
                {
                    Name = reader.GetString(0),
                    Path = reader.GetString(1),
                    GitRemoteUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsGitRepository = reader.GetBoolean(3),
                    LastScannedAt = DateTime.Parse(reader.GetString(4)).ToUniversalTime(),
                    Languages = reader.IsDBNull(5)
                        ? []
                        : JsonSerializer.Deserialize<List<DetectedLanguage>>(reader.GetString(5)) ?? [],
                    Frameworks = reader.IsDBNull(6)
                        ? []
                        : JsonSerializer.Deserialize<List<DetectedFramework>>(reader.GetString(6)) ?? [],
                });
            }

            return projects;
        }, cancellationToken);
    }

    public Task ReplaceAllAsync(IReadOnlyList<Project> projects, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            using (var delete = connection.CreateCommand())
            {
                delete.CommandText = "DELETE FROM Projects";
                delete.ExecuteNonQuery();
            }

            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = """
                    INSERT INTO Projects (Name, Path, GitRemoteUrl, IsGitRepository, LastScannedAt, Languages, Frameworks)
                    VALUES ($name, $path, $remote, $isGit, $scannedAt, $languages, $frameworks)
                    """;
                var name = insert.Parameters.Add("$name", SqliteType.Text);
                var path = insert.Parameters.Add("$path", SqliteType.Text);
                var remote = insert.Parameters.Add("$remote", SqliteType.Text);
                var isGit = insert.Parameters.Add("$isGit", SqliteType.Integer);
                var scannedAt = insert.Parameters.Add("$scannedAt", SqliteType.Text);
                var languages = insert.Parameters.Add("$languages", SqliteType.Text);
                var frameworks = insert.Parameters.Add("$frameworks", SqliteType.Text);

                foreach (var project in projects)
                {
                    name.Value = project.Name;
                    path.Value = project.Path;
                    remote.Value = (object?)project.GitRemoteUrl ?? DBNull.Value;
                    isGit.Value = project.IsGitRepository;
                    scannedAt.Value = project.LastScannedAt.ToString("O");
                    languages.Value = JsonSerializer.Serialize(project.Languages);
                    frameworks.Value = JsonSerializer.Serialize(project.Frameworks);
                    insert.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }, cancellationToken);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        if (!_initialized)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Projects (
                    Path TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    GitRemoteUrl TEXT NULL,
                    IsGitRepository INTEGER NOT NULL,
                    LastScannedAt TEXT NOT NULL,
                    Languages TEXT NULL,
                    Frameworks TEXT NULL
                )
                """;
            command.ExecuteNonQuery();

            // Migrations for databases created before these columns existed.
            foreach (var column in new[] { "Languages", "Frameworks" })
            {
                using var check = connection.CreateCommand();
                check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Projects') WHERE name = '{column}'";
                if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                {
                    using var alter = connection.CreateCommand();
                    alter.CommandText = $"ALTER TABLE Projects ADD COLUMN {column} TEXT NULL";
                    alter.ExecuteNonQuery();
                }
            }

            _initialized = true;
        }

        return connection;
    }
}
