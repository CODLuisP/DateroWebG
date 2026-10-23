using MySqlConnector;
using DateroWeb.Config;
using DateroWeb.Models;

namespace DateroWeb.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly string _accountId;

        public DatabaseService(AppSettings settings)
        {
            _connectionString = settings.ConnectionString;
            _accountId = settings.Usuario;
        }

        public async Task<List<DeviceEspera>> GetDevicesEsperaAsync()
        {
            var lista = new List<DeviceEspera>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                const string query = @"
                    SELECT deviceID, rutaact, codconductoract
                    FROM device
                    WHERE accountID = @AccountId";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@AccountId", _accountId);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                    lista.Add(LeerDevice(reader));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error al obtener dispositivos en espera: {ex.Message}");
            }

            return lista;
        }

        public async Task<DeviceEspera?> GetDeviceAsync(string placa)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                const string query = @"
                    SELECT deviceID, rutaact, codconductoract
                    FROM device
                    WHERE accountID = @AccountId AND deviceID = @Placa";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@AccountId", _accountId);
                command.Parameters.AddWithValue("@Placa", placa);
                using var reader = await command.ExecuteReaderAsync();

                return await reader.ReadAsync() ? LeerDevice(reader) : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error al obtener dispositivo {placa}: {ex.Message}");
                return null;
            }
        }

        private static DeviceEspera LeerDevice(MySqlDataReader reader) => new DeviceEspera
        {
            Placa = reader.IsDBNull(0) ? null : reader.GetString(0).Trim(),
            RutaAct = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
            CodConductorAct = reader.IsDBNull(2) ? "0" : reader.GetString(2).Trim()
        };
    }
}
