using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LockStepAI.Services
{
    public class DatabaseService
    {
        private readonly string _dbPath;

        public DatabaseService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "LockStepAI");
            Directory.CreateDirectory(folder);
            string legacyDbPath = Path.Combine(folder, "lockstep.db");
            _dbPath = Path.Combine(folder, "LockStepAI.db");
            if (!File.Exists(_dbPath) && File.Exists(legacyDbPath))
            {
                using var legacyConnection = new SqliteConnection($"Data Source={legacyDbPath};Mode=ReadOnly;Pooling=True");
                using var newConnection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
                legacyConnection.Open();
                newConnection.Open();
                legacyConnection.BackupDatabase(newConnection);
            }
            Initialize();
        }

        private void Initialize()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Chats (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS Messages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChatId INTEGER NOT NULL,
                    Role TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(ChatId) REFERENCES Chats(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS HallucinationReports (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChatId INTEGER NOT NULL,
                    Prompt TEXT NOT NULL,
                    Response TEXT NOT NULL,
                    Reason TEXT NOT NULL,
                    ReportedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );";
            command.ExecuteNonQuery();
        }

        public int CreateNewChat(string title)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Chats (Title) VALUES ($title); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$title", title);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void UpdateChatTitle(int chatId, string newTitle)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Chats SET Title = $title WHERE Id = $id;";
            command.Parameters.AddWithValue("$title", newTitle);
            command.Parameters.AddWithValue("$id", chatId);
            command.ExecuteNonQuery();
        }

        public void DeleteChat(int chatId)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
            connection.Open();
            var command = connection.CreateCommand();
            // Foreign key cascade will delete messages automatically if PRAGMA foreign_keys = ON;
            // Since SQLite has them off by default, we delete manually:
            command.CommandText = "DELETE FROM Messages WHERE ChatId = $id; DELETE FROM Chats WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", chatId);
            command.ExecuteNonQuery();
        }

        public void SaveMessage(int chatId, string role, string content)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Messages (ChatId, Role, Content) VALUES ($c, $r, $m);";
            command.Parameters.AddWithValue("$c", chatId);
            command.Parameters.AddWithValue("$r", role);
            command.Parameters.AddWithValue("$m", content);
            command.ExecuteNonQuery();
        }

        public void SaveHallucinationReport(int chatId, string prompt, string response, string reason)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO HallucinationReports (ChatId, Prompt, Response, Reason) VALUES ($chatId, $prompt, $response, $reason);";
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$prompt", prompt);
            command.Parameters.AddWithValue("$response", response);
            command.Parameters.AddWithValue("$reason", reason);
            command.ExecuteNonQuery();
        }

        public List<(int Id, string Title)> GetChats()
        {
            var list = new List<(int, string)>();
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Title FROM Chats ORDER BY CreatedAt DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) list.Add((reader.GetInt32(0), reader.GetString(1)));
            return list;
        }

        public List<(string Role, string Content)> GetMessages(int chatId)
        {
            var list = new List<(string, string)>();
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=True");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Role, Content
                FROM (
                    SELECT Role, Content, Id
                    FROM Messages
                    WHERE ChatId = $id
                    ORDER BY Id DESC
                    LIMIT 50
                )
                ORDER BY Id ASC;";
            command.Parameters.AddWithValue("$id", chatId);
            using var reader = command.ExecuteReader();
            while (reader.Read()) list.Add((reader.GetString(0), reader.GetString(1)));
            return list;
        }
    }
}