using Microsoft.Data.SqlClient;

const string connString = "Server=tcp:flatsqlserver.database.windows.net,1433;Initial Catalog=PersonalAgentDB;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Authentication=Active Directory Default;";

await using var conn = new SqlConnection(connString);
await conn.OpenAsync();

const int legacyPersonId = 2;

await using var transaction = (SqlTransaction)await conn.BeginTransactionAsync();

async Task<int> DeleteFromAsync(string table, string column, int personId)
{
    await using var cmd = new SqlCommand($"DELETE FROM {table} WHERE {column} = @personId", conn, transaction);
    cmd.Parameters.AddWithValue("@personId", personId);
    return await cmd.ExecuteNonQueryAsync();
}

Console.WriteLine($"Deleting all data for legacy shared PersonId={legacyPersonId}...");
Console.WriteLine($"GoalPlanCheckIns: {await DeleteFromAsync("GoalPlanCheckIns", "PersonId", legacyPersonId)}");
Console.WriteLine($"GoalPlans: {await DeleteFromAsync("GoalPlans", "PersonId", legacyPersonId)}");
Console.WriteLine($"Goals: {await DeleteFromAsync("Goals", "PersonId", legacyPersonId)}");
Console.WriteLine($"ExerciseLogs: {await DeleteFromAsync("ExerciseLogs", "PersonId", legacyPersonId)}");
Console.WriteLine($"MealLogs: {await DeleteFromAsync("MealLogs", "PersonId", legacyPersonId)}");
Console.WriteLine($"WeightLogs: {await DeleteFromAsync("WeightLogs", "PersonId", legacyPersonId)}");

await using (var cmd = new SqlCommand("DELETE FROM People WHERE Id = @personId", conn, transaction))
{
    cmd.Parameters.AddWithValue("@personId", legacyPersonId);
    Console.WriteLine($"Deleted legacy Person row: {await cmd.ExecuteNonQueryAsync()}");
}

await transaction.CommitAsync();

Console.WriteLine("\n=== People (after delete) ===");
await using (var cmd = new SqlCommand("SELECT Id, Name, AzureObjectId FROM People ORDER BY Id", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    var any = false;
    while (await reader.ReadAsync())
    {
        any = true;
        Console.WriteLine($"Id={reader["Id"]}, Name={reader["Name"]}, AzureObjectId={reader["AzureObjectId"]}");
    }
    if (!any) Console.WriteLine("(empty)");
}
