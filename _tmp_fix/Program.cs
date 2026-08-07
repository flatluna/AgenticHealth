using Microsoft.Data.SqlClient;

const string connString = "Server=tcp:flatsqlserver.database.windows.net,1433;Initial Catalog=PersonalAgentDB;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Authentication=Active Directory Default;";
const string azureObjectId = "bb245e09-41c2-4cb5-8511-785cb480dde1.0e9c8663-a4ff-440e-af94-be25e63a1a6a";

await using var conn = new SqlConnection(connString);
await conn.OpenAsync();

Console.WriteLine("=== Before ===");
await using (var cmd = new SqlCommand("SELECT Id, Name, HeightCm, CurrentWeightKg, ActivityLevel FROM People WHERE AzureObjectId = @id", conn))
{
    cmd.Parameters.AddWithValue("@id", azureObjectId);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"Id={reader["Id"]}, Name={reader["Name"]}, HeightCm={reader["HeightCm"]}, CurrentWeightKg={reader["CurrentWeightKg"]}, ActivityLevel={reader["ActivityLevel"]}");
    }
}

await using var transaction = (SqlTransaction)await conn.BeginTransactionAsync();

// Delete the test GoalPlan (planId=13) created by the diagnostic curl call.
await using (var cmd = new SqlCommand("DELETE FROM GoalPlans WHERE Id = 13 AND PersonId = (SELECT Id FROM People WHERE AzureObjectId = @id)", conn, transaction))
{
    cmd.Parameters.AddWithValue("@id", azureObjectId);
    Console.WriteLine($"Deleted test GoalPlans: {await cmd.ExecuteNonQueryAsync()}");
}

// Delete the WeightLog snapshot the diagnostic call created (81.6 kg).
await using (var cmd = new SqlCommand("DELETE FROM WeightLogs WHERE PersonId = (SELECT Id FROM People WHERE AzureObjectId = @id) AND WeightKg = 81.6", conn, transaction))
{
    cmd.Parameters.AddWithValue("@id", azureObjectId);
    Console.WriteLine($"Deleted test WeightLogs: {await cmd.ExecuteNonQueryAsync()}");
}

// Clear the contaminated profile fields so the user can re-enter their real values.
await using (var cmd = new SqlCommand("UPDATE People SET HeightCm = 0, CurrentWeightKg = NULL WHERE AzureObjectId = @id", conn, transaction))
{
    cmd.Parameters.AddWithValue("@id", azureObjectId);
    Console.WriteLine($"Reset People rows: {await cmd.ExecuteNonQueryAsync()}");
}

await transaction.CommitAsync();

Console.WriteLine("\n=== After ===");
await using (var cmd = new SqlCommand("SELECT Id, Name, HeightCm, CurrentWeightKg, ActivityLevel FROM People WHERE AzureObjectId = @id", conn))
{
    cmd.Parameters.AddWithValue("@id", azureObjectId);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"Id={reader["Id"]}, Name={reader["Name"]}, HeightCm={reader["HeightCm"]}, CurrentWeightKg={reader["CurrentWeightKg"]}, ActivityLevel={reader["ActivityLevel"]}");
    }
}

