using Microsoft.Data.SqlClient;

const string connString = "Server=tcp:flatsqlserver.database.windows.net,1433;Initial Catalog=PersonalAgentDB;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Authentication=Active Directory Default;";

await using var conn = new SqlConnection(connString);
await conn.OpenAsync();

Console.WriteLine("=== AppUsers ===");
await using (var cmd = new SqlCommand("SELECT Id, AzureObjectId, Email, DisplayName FROM AppUsers ORDER BY Id", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"Id={reader["Id"]}, AzureObjectId={reader["AzureObjectId"]}, Email={reader["Email"]}, DisplayName={reader["DisplayName"]}");
    }
}

Console.WriteLine("\n=== People ===");
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
