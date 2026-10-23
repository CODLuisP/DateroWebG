namespace DateroWeb.Models
{
    public class ControlGPS
    {
        public string Codasig { get; set; }
        public string DeviceID { get; set; }
        public string Nom_control { get; set; }
        public string Hora_inicio { get; set; }
        public string Hora_estimada { get; set; }
        public string Hora_llegada { get; set; }
        public string Volado { get; set; }  // Diferencia en minutos como string
        public string Fecha { get; set; }  // Formato: YYYY-MM-DD
    }
}