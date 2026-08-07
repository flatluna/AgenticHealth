using Microsoft.Data.SqlClient;

const string connectionString = "Server=tcp:flatsqlserver.database.windows.net,1433;Initial Catalog=PersonalAgentDB;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Authentication=Active Directory Default;";

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

int? personId = null;
await using (var cmd = new SqlCommand("SELECT Id FROM People WHERE Name = 'Usuario' AND AzureObjectId IS NULL", connection))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    if (await reader.ReadAsync())
    {
        personId = reader.GetInt32(0);
    }
}

if (personId is null)
{
    Console.WriteLine("No legacy 'Usuario' person found (nothing to delete).");
    return;
}

Console.WriteLine($"Legacy PersonId = {personId}");

async Task<int> CountAsync(string table)
{
    await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {table} WHERE PersonId = @id", connection);
    cmd.Parameters.AddWithValue("@id", personId);
    return (int)(await cmd.ExecuteScalarAsync())!;
}

Console.WriteLine($"WeightLogs: {await CountAsync("WeightLogs")}");
Console.WriteLine($"MealLogs: {await CountAsync("MealLogs")}");
Console.WriteLine($"ExerciseLogs: {await CountAsync("ExerciseLogs")}");
Console.WriteLine($"Goals: {await CountAsync("Goals")}");
Console.WriteLine($"GoalPlans: {await CountAsync("GoalPlans")}");
Console.WriteLine($"GoalPlanCheckIns: {await CountAsync("GoalPlanCheckIns")}");

await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

async Task<int> ExecAsync(string sql)
{
    await using var cmd = new SqlCommand(sql, connection, transaction);
    cmd.Parameters.AddWithValue("@id", personId);
    return await cmd.ExecuteNonQueryAsync();
}

await ExecAsync("DELETE FROM GoalPlanCheckIns WHERE PersonId = @id");
await ExecAsync("DELETE FROM Goals WHERE PersonId = @id");
await ExecAsync("DELETE FROM GoalPlans WHERE PersonId = @id");
await ExecAsync("DELETE FROM WeightLogs WHERE PersonId = @id");
await ExecAsync("DELETE FROM MealLogs WHERE PersonId = @id");
await ExecAsync("DELETE FROM ExerciseLogs WHERE PersonId = @id");
var deletedPeople = await ExecAsync("DELETE FROM People WHERE Id = @id");

await transaction.CommitAsync();

Console.WriteLine($"Deleted legacy Person row: {deletedPeople}");
