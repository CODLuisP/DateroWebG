using Microsoft.Extensions.Configuration;

namespace DateroWeb.Config
{
    /// <summary>
    /// Configuración de la aplicación leída desde appsettings.json
    /// (y opcionalmente sobrescrita por variables de entorno con prefijo DATERO_).
    /// </summary>
    public class AppSettings
    {
        public string ConnectionString { get; init; } = "";
        public string Usuario { get; init; } = "";
        public int PuertoHttp { get; init; }
        public string ApiBaseUrl { get; init; } = "";
        public int HttpTimeoutSegundos { get; init; } = 10;

        public static AppSettings Cargar()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables(prefix: "DATERO_")
                .Build();

            var app = config.GetSection("App");

            var settings = new AppSettings
            {
                ConnectionString = config.GetConnectionString("MySql") ?? "",
                Usuario = app["Usuario"] ?? "",
                PuertoHttp = app.GetValue("PuertoHttp", 8080),
                ApiBaseUrl = (app["ApiBaseUrl"] ?? "").TrimEnd('/'),
                HttpTimeoutSegundos = app.GetValue("HttpTimeoutSegundos", 10)
            };

            if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                throw new InvalidOperationException("Falta 'ConnectionStrings:MySql' en appsettings.json");
            if (string.IsNullOrWhiteSpace(settings.Usuario))
                throw new InvalidOperationException("Falta 'App:Usuario' en appsettings.json");
            if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
                throw new InvalidOperationException("Falta 'App:ApiBaseUrl' en appsettings.json");

            return settings;
        }
    }
}
