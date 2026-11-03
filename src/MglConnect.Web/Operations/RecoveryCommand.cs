using System.Text.Json;
namespace MglConnect.Web.Operations;

public static class RecoveryCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            object result = args switch {
                ["--operations","backup-sqlite",var source,var destination] => await SqliteRecovery.BackupAsync(source,destination),
                ["--operations","backup-sqlite",var source,var destination,"--keys",var keys] => await SqliteRecovery.BackupAsync(source,destination,keys),
                ["--operations","verify",var package] => await SqliteRecovery.VerifyAsync(package),
                ["--operations","restore",var package,var destination] => await Restore(package,destination),
                _ => throw new ArgumentException("Uso: --operations backup-sqlite BANCO DESTINO [--keys DIRETORIO] | verify PACOTE | restore PACOTE DESTINO_NOVO")
            };
            Console.WriteLine(JsonSerializer.Serialize(new {success=true,result}));return 0;
        }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {
            // Paths, SQL parameters, key material and database contents never reach console logs.
            Console.Error.WriteLine(JsonSerializer.Serialize(new{success=false,errorType=ex.GetType().Name,
                message="Operação não confirmada. Origem preservada; destino final não publicado. Confira os parâmetros e o pacote incompleto."}));return 1;
        }
    }
    private static async Task<object> Restore(string package,string destination)
    {await SqliteRecovery.RestoreAsync(package,destination);return new{restored=true};}
}
