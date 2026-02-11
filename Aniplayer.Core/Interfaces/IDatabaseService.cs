using System.Data;

namespace Aniplayer.Core.Interfaces;

public interface IDatabaseService
{
    IDbConnection CreateConnection();
    Task InitializeAsync();
}
